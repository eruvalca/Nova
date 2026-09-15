using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Shouldly;

namespace Nova.Integration.Tests.Data;

public sealed partial class CampaignPlacementRetryTests
{
    [Fact]
    public async Task GlobalPlacementCleanupDeletesOnlyFiveHundredOldestExpiredReceiptsInPostgresAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var cutoff = DateTimeOffset.UnixEpoch.AddYears(30);
        await using var db = fixture.CreateAdminContext();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var receipts = Enumerable.Range(0, 503).Select(index => new Nova.Entities.PlacementMutationReceiptEntity
        {
            ClubId = long.MaxValue,
            CreatedById = 1,
            ActorUserId = 1,
            PlayerCampaignAssignmentId = 1,
            OperationId = Guid.CreateVersion7(),
            ConcurrencyToken = Guid.NewGuid(),
            RequestSha256 = new string('A', 64),
            ResultJson = "{}",
            RecoveryExpiresAt = index > 500 ? cutoff.AddDays(1) : cutoff.AddMinutes(-index)
        }).ToArray();
        // FK-less snapshots remain eligible even when the owning club is absent.
        db.PlacementMutationReceipts.AddRange(receipts);
        await db.SaveChangesAsync(cancellationToken);
        var ids = receipts.Select(receipt => receipt.PlacementMutationReceiptId).ToArray();
        var retained = new[] { ids[0], ids[501], ids[502] };
        db.ChangeTracker.Clear();

        await PlacementReceiptCleanupService.PruneAsync(db, cutoff, cancellationToken);

        db.ChangeTracker.Entries<Nova.Entities.PlacementMutationReceiptEntity>().ShouldBeEmpty();
        (await db.PlacementMutationReceipts.Where(receipt => ids.Contains(receipt.PlacementMutationReceiptId))
            .Select(receipt => receipt.PlacementMutationReceiptId).ToListAsync(cancellationToken)).ShouldBe(retained, ignoreOrder: true);
        await transaction.RollbackAsync(cancellationToken);
    }

    [Fact]
    public async Task ConcurrentExactOperationsReturnOneReceiptAndOnePlacementEventAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
#pragma warning disable CA5394 // Random identifiers isolate test fixtures; they are not security tokens.
        var actor = Random.Shared.NextInt64(1, long.MaxValue);
#pragma warning restore CA5394
        var seed = await SeedPlacementDataAsync(actor, Guid.NewGuid().ToString("N"));
        fixture.CurrentUser.UserId = actor;
        fixture.CurrentUser.ClubId = seed.ClubId;
        ICampaignPlacementService service = new CampaignPlacementService(fixture.CreateTenantContextFactory(),
            fixture.CurrentUser, NullLogger<CampaignPlacementService>.Instance);
        var input = new UpdateCampaignPlacementInput(seed.AssignmentId, PlacementOutcome.Assigned,
            seed.TeamId, seed.ConcurrencyToken, Guid.CreateVersion7());
        await using var gate = fixture.CreateAdminContext();
        await using var transaction = await gate.Database.BeginTransactionAsync(cancellationToken);
        var membershipKey = (long.MinValue / 64) + actor;
        await gate.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({membershipKey})", cancellationToken);
        var first = service.UpdatePlacementAsync(input, cancellationToken);
        var second = service.UpdatePlacementAsync(input, cancellationToken);
        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(gate, membershipKey, 2, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var results = await Task.WhenAll(first, second);

        results.ShouldAllBe(result => result.IsSuccess);
        results[0].Value.ShouldBe(results[1].Value);
        results[0].Value.Receipt.OperationId.ShouldBe(input.OperationId);
        await using var verify = fixture.CreateAdminContext();
        (await verify.PlacementMutationReceipts.CountAsync(row => row.ClubId == seed.ClubId, cancellationToken)).ShouldBe(1);
        (await verify.ActivityEvents.CountAsync(row => row.ClubId == seed.ClubId, cancellationToken)).ShouldBe(1);
        var assignment = await verify.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == seed.AssignmentId, cancellationToken);
        assignment.ConcurrencyToken.ShouldBe(results[0].Value.ConcurrencyToken);
        assignment.TeamId.ShouldBe(seed.TeamId);
        assignment.DecisionRecordedById.ShouldBe(actor);
    }
}
