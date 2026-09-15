using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignPlacementServiceTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlacementCleanupRemovesAtMostFiveHundredExpiredReceiptsPerPassAsync(bool throughMembershipCleanup)
    {
        var now = DateTimeOffset.UtcNow.AddSeconds(-1);
        var retained = new List<Guid>();
        using (var seed = _harness.CreateAdminContext())
        {
            for (var index = 0; index < 503; index++)
            {
                var operation = Guid.CreateVersion7();
                if (index == 0 || index > 500) { retained.Add(operation); }
                seed.PlacementMutationReceipts.Add(new PlacementMutationReceiptEntity
                {
                    OperationId = operation,
                    PlayerCampaignAssignmentId = ClubAAssignmentId,
                    ConcurrencyToken = Guid.NewGuid(),
                    ClubId = ClubAId,
                    CreatedById = ClubAAdminId,
                    ActorUserId = ClubAAdminId,
                    RequestSha256 = new string('A', 64),
                    ResultJson = "{}",
                    RecoveryExpiresAt = index > 500 ? now.AddHours(1) : now.AddMinutes(-index)
                });
            }
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        using (var cleanup = _harness.CreateAdminContext())
        {
            if (throughMembershipCleanup)
            {
                await Nova.Features.Account.ClubMembershipMutationReceipts.PruneExpiredAsync(cleanup, TestContext.Current.CancellationToken);
            }
            else
            {
                await Nova.Features.Campaigns.PlacementReceiptCleanupService.PruneAsync(cleanup, now, TestContext.Current.CancellationToken);
            }
            cleanup.ChangeTracker.Entries<PlacementMutationReceiptEntity>().ShouldBeEmpty();
        }

        using var verify = _harness.CreateAdminContext();
        (await verify.PlacementMutationReceipts.Select(receipt => receipt.OperationId).ToListAsync(TestContext.Current.CancellationToken))
            .ShouldBe(retained, ignoreOrder: true);
    }
    /// <summary>Checks placement receipts are visible only within their club through write and read contexts.</summary>
    [Fact]
    public void PlacementMutationReceiptsFilterByOwningTenant()
    {
        using (var seed = _harness.CreateAdminContext())
        {
            seed.PlacementMutationReceipts.AddRange(
                new PlacementMutationReceiptEntity { RequestSha256 = new string('A', 64), ResultJson = "{}", RecoveryExpiresAt = DateTimeOffset.UtcNow.AddHours(24), OperationId = Guid.NewGuid(), PlayerCampaignAssignmentId = ClubAAssignmentId, ConcurrencyToken = Guid.NewGuid(), ClubId = ClubAId, CreatedById = ClubAAdminId },
                new PlacementMutationReceiptEntity { RequestSha256 = new string('A', 64), ResultJson = "{}", RecoveryExpiresAt = DateTimeOffset.UtcNow.AddHours(24), OperationId = Guid.NewGuid(), PlayerCampaignAssignmentId = ClubBAssignmentId, ConcurrencyToken = Guid.NewGuid(), ClubId = ClubBId, CreatedById = ClubBAdminId });
            seed.SaveChanges();
        }
        ActAs(ClubAMemberId, ClubAId);
        using var tenant = _harness.CreateTenantContext();
        tenant.PlacementMutationReceipts.Single().PlayerCampaignAssignmentId.ShouldBe(ClubAAssignmentId);
        using var read = _harness.CreateReadContext();
        read.PlacementMutationReceipts.Single().PlayerCampaignAssignmentId.ShouldBe(ClubAAssignmentId);
        ActAs(ClubBAdminId, ClubBId, isClubAdmin: true);
        using var other = _harness.CreateTenantContext();
        other.PlacementMutationReceipts.Single().PlayerCampaignAssignmentId.ShouldBe(ClubBAssignmentId);
    }

    /// <summary>Checks global expiration removes expired evidence in every tenant and retains unexpired receipts.</summary>
    [Fact]
    public async Task PlacementMutationReceiptsPruneExpiredEvidenceAcrossTenantsAsync()
    {
        var expiredOperation = Guid.NewGuid();
        var recentOperation = Guid.NewGuid();
        var otherOperation = Guid.NewGuid();
        using (var seed = _harness.CreateAdminContext())
        {
            var expired = new PlacementMutationReceiptEntity { RequestSha256 = new string('A', 64), ResultJson = "{}", RecoveryExpiresAt = DateTimeOffset.UtcNow.AddHours(24), OperationId = expiredOperation, PlayerCampaignAssignmentId = ClubAAssignmentId, ConcurrencyToken = Guid.NewGuid(), ClubId = ClubAId, CreatedById = ClubAAdminId };
            var recent = new PlacementMutationReceiptEntity { RequestSha256 = new string('A', 64), ResultJson = "{}", RecoveryExpiresAt = DateTimeOffset.UtcNow.AddHours(24), OperationId = recentOperation, PlayerCampaignAssignmentId = ClubAAssignmentId, ConcurrencyToken = Guid.NewGuid(), ClubId = ClubAId, CreatedById = ClubAAdminId };
            var other = new PlacementMutationReceiptEntity { RequestSha256 = new string('A', 64), ResultJson = "{}", RecoveryExpiresAt = DateTimeOffset.UtcNow.AddHours(24), OperationId = otherOperation, PlayerCampaignAssignmentId = ClubBAssignmentId, ConcurrencyToken = Guid.NewGuid(), ClubId = ClubBId, CreatedById = ClubBAdminId };
            seed.PlacementMutationReceipts.AddRange(expired, recent, other);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            await seed.PlacementMutationReceipts.Where(receipt => receipt.OperationId == expiredOperation || receipt.OperationId == otherOperation).ExecuteUpdateAsync(setters => setters.SetProperty(receipt => receipt.RecoveryExpiresAt, DateTimeOffset.UtcNow.AddDays(-2)), TestContext.Current.CancellationToken);
            await seed.PlacementMutationReceipts.Where(receipt => receipt.OperationId == recentOperation).ExecuteUpdateAsync(setters => setters.SetProperty(receipt => receipt.RecoveryExpiresAt, DateTimeOffset.UtcNow.AddHours(12)), TestContext.Current.CancellationToken);
        }
        ActAs(ClubAMemberId, ClubAId);

        (await SaveAsync(Nova.SharedKernel.Enums.PlacementOutcome.NotSelected, _clubAConcurrencyToken)).Value
            .ShouldBeOfType<Nova.SharedKernel.Features.Campaigns.PlacementMutationSuccess>();

        using (var cleanup = _harness.CreateAdminContext())
        {
            await Nova.Features.Campaigns.PlacementReceiptCleanupService.PruneAsync(cleanup, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        }
        using var verify = _harness.CreateAdminContext();
        var operations = (await verify.PlacementMutationReceipts.Select(receipt => receipt.OperationId).ToListAsync(TestContext.Current.CancellationToken));
        operations.ShouldNotContain(expiredOperation);
        operations.ShouldContain(recentOperation);
        operations.ShouldNotContain(otherOperation);
        operations.Count.ShouldBe(2);
    }
    /// <summary>Checks committed receipts cannot be rewritten to claim a different mutation result.</summary>
    [Fact]
    public async Task PlacementMutationReceiptsRejectChangesToCommittedReceiptAsync()
    {
        ActAs(ClubAMemberId, ClubAId);
        var saved = (await SaveAsync(Nova.SharedKernel.Enums.PlacementOutcome.NotSelected, _clubAConcurrencyToken)).Value
            .ShouldBeOfType<Nova.SharedKernel.Features.Campaigns.PlacementMutationSuccess>();
        using var tenant = _harness.CreateTenantContext();
        var receipt = (await tenant.PlacementMutationReceipts.SingleAsync(TestContext.Current.CancellationToken));
        var operationId = receipt.OperationId;
        receipt.ConcurrencyToken = Guid.NewGuid();

        Should.Throw<InvalidOperationException>(() => tenant.SaveChanges());

        using var verify = _harness.CreateAdminContext();
        var persisted = (await verify.PlacementMutationReceipts.SingleAsync(TestContext.Current.CancellationToken));
        persisted.OperationId.ShouldBe(operationId);
        persisted.ConcurrencyToken.ShouldBe(saved.ConcurrencyToken);
    }
    /// <summary>Checks immutable receipt evidence survives deletion of its former owning club.</summary>
    [Fact]
    public void PlacementMutationReceiptsSurviveOwningClubDeletion()
    {
        var operationId = Guid.NewGuid();
        var token = Guid.NewGuid();
        using (var seed = _harness.CreateAdminContext())
        {
            seed.PlacementMutationReceipts.Add(new PlacementMutationReceiptEntity
            {
                RequestSha256 = new string('A', 64),
                ResultJson = "{}",
                RecoveryExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
                OperationId = operationId,
                PlayerCampaignAssignmentId = ClubBAssignmentId,
                ConcurrencyToken = token,
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });
            seed.SaveChanges();
        }
        using (var delete = _harness.CreateAdminContext())
        {
            delete.Clubs.Remove(delete.Clubs.Single(club => club.ClubId == ClubBId));
            delete.SaveChanges();
        }

        using var verify = _harness.CreateAdminContext();
        verify.Clubs.Any(club => club.ClubId == ClubBId).ShouldBeFalse();
        var receipt = verify.PlacementMutationReceipts.Single();
        receipt.ClubId.ShouldBe(ClubBId);
        receipt.OperationId.ShouldBe(operationId);
        receipt.ConcurrencyToken.ShouldBe(token);
        receipt.PlayerCampaignAssignmentId.ShouldBe(ClubBAssignmentId);
    }

    /// <summary>Checks global cleanup removes expired orphan receipts while retaining fresh evidence.</summary>
    [Fact]
    public async Task PlacementMutationReceiptsGlobalCleanupRemovesExpiredDeletedClubEvidenceAsync()
    {
        var expiredOperation = Guid.NewGuid();
        var freshOperation = Guid.NewGuid();
        using (var seed = _harness.CreateAdminContext())
        {
            seed.PlacementMutationReceipts.AddRange(
                new PlacementMutationReceiptEntity { RequestSha256 = new string('A', 64), ResultJson = "{}", RecoveryExpiresAt = DateTimeOffset.UtcNow.AddHours(24), OperationId = expiredOperation, PlayerCampaignAssignmentId = ClubBAssignmentId, ConcurrencyToken = Guid.NewGuid(), ClubId = ClubBId, CreatedById = ClubBAdminId },
                new PlacementMutationReceiptEntity { RequestSha256 = new string('A', 64), ResultJson = "{}", RecoveryExpiresAt = DateTimeOffset.UtcNow.AddHours(24), OperationId = freshOperation, PlayerCampaignAssignmentId = ClubAAssignmentId, ConcurrencyToken = Guid.NewGuid(), ClubId = ClubAId, CreatedById = ClubAAdminId });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            await seed.PlacementMutationReceipts.Where(receipt => receipt.OperationId == expiredOperation).ExecuteUpdateAsync(setters => setters.SetProperty(receipt => receipt.RecoveryExpiresAt, DateTimeOffset.UtcNow.AddDays(-2)), TestContext.Current.CancellationToken);
        }
        using (var delete = _harness.CreateAdminContext())
        {
            delete.Clubs.Remove((await delete.Clubs.SingleAsync(club => club.ClubId == ClubBId, TestContext.Current.CancellationToken)));
            await delete.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        ActAs(ClubAMemberId, ClubAId);
        using (var cleanup = _harness.CreateAdminContext())
        {
            (await cleanup.PlacementMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
            await Nova.Features.Account.ClubMembershipMutationReceipts.PruneExpiredAsync(cleanup, TestContext.Current.CancellationToken);
            await cleanup.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var verify = _harness.CreateAdminContext();
        (await verify.PlacementMutationReceipts.SingleAsync(TestContext.Current.CancellationToken)).OperationId.ShouldBe(freshOperation);
        (await verify.Clubs.AnyAsync(club => club.ClubId == ClubBId, TestContext.Current.CancellationToken)).ShouldBeFalse();
    }
    /// <summary>Checks explicit receipt writes for another club are rejected before persistence.</summary>
    [Fact]
    public void PlacementMutationReceiptsRejectCrossTenantWrites()
    {
        ActAs(ClubAMemberId, ClubAId);
        using var tenant = _harness.CreateTenantContext();
        tenant.PlacementMutationReceipts.Add(new PlacementMutationReceiptEntity
        {
            RequestSha256 = new string('A', 64),
            ResultJson = "{}",
            RecoveryExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
            OperationId = Guid.NewGuid(),
            PlayerCampaignAssignmentId = ClubBAssignmentId,
            ConcurrencyToken = Guid.NewGuid(),
            ClubId = ClubBId,
            CreatedById = ClubAMemberId
        });
        Should.Throw<InvalidOperationException>(() => tenant.SaveChanges()).Message.ShouldContain("Cross-tenant");
        using var verify = _harness.CreateAdminContext();
        verify.PlacementMutationReceipts.Count().ShouldBe(0);
    }
}
