using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using OneOf.Types;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies campaign tag application mutations remain correct when Npgsql retries a failed transaction.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignTagApplicationRetryTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies a transient failure raised before any commit does not let remove verification
    /// convert a genuine not-found into a success.
    /// </summary>
    /// <remarks>
    /// The application is missing before the request runs, so the correct answer is not-found. The
    /// injected failure interrupts the initial lookup, which means no commit was attempted and the
    /// absent row belongs to no request rather than this one's ambiguous commit.
    /// </remarks>
    [Fact]
    public async Task RemoveCampaignTagApplication_ReportsNotFound_WhenTransientFailurePrecedesCommitOnDeletedApplication()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, _, _, _, _) = await SeedTagApplicationDataAsync(actorUserId, suffix);
        var missingApplicationId = Random.Shared.NextInt64(1, long.MaxValue);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstCampaignTagApplicationReadInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new CampaignTagApplicationService(
            factory,
            fixture.CurrentUser,
            NullLogger<CampaignTagApplicationService>.Instance);

        var result = await ((ICampaignTagApplicationService)service).RemoveAsync(
            new RemoveCampaignTagApplicationInput { CampaignTagApplicationId = missingApplicationId },
            TestContext.Current.CancellationToken);

        failureInterceptor.FailureCount.ShouldBe(1);
        result.IsProblem.ShouldBeTrue("a missing application must stay not-found after a pre-commit retry");
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies a transient failure raised before the duplicate probe does not let apply verification
    /// credit a concurrently created row to a request that never reached its commit.
    /// </summary>
    /// <remarks>
    /// The pair is already applied before the request runs, so the correct answer is a conflict. The
    /// injected failure interrupts the duplicate probe, which means no commit was attempted and the
    /// existing row belongs to an earlier request rather than this one's ambiguous commit.
    /// </remarks>
    [Fact]
    public async Task ApplyCampaignTagApplication_ReportsConflict_WhenTransientFailurePrecedesCommitOnAppliedPair()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, _, tagId, assignmentId, _) = await SeedTagApplicationDataAsync(actorUserId, suffix, applied: true);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstCampaignTagApplicationReadInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new CampaignTagApplicationService(
            factory,
            fixture.CurrentUser,
            NullLogger<CampaignTagApplicationService>.Instance);

        var result = await ((ICampaignTagApplicationService)service).ApplyAsync(
            new ApplyCampaignTagApplicationInput
            {
                PlayerCampaignAssignmentId = assignmentId,
                PlayerTagId = tagId
            },
            TestContext.Current.CancellationToken);

        failureInterceptor.FailureCount.ShouldBe(1);
        result.IsProblem.ShouldBeTrue("an already-applied pair must still conflict after a pre-commit retry");
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    /// <summary>
    /// Verifies an apply whose commit reached the database but surfaced a transient failure is
    /// reported as success rather than replayed into a spurious conflict.
    /// </summary>
    [Fact]
    public async Task ApplyCampaignTagApplication_ReportsSuccess_WhenCommitSucceedsButTransientFailureSurfaces()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, _, tagId, assignmentId, _) = await SeedTagApplicationDataAsync(actorUserId, suffix);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new CampaignTagApplicationService(
            factory,
            fixture.CurrentUser,
            NullLogger<CampaignTagApplicationService>.Instance);

        var result = await ((ICampaignTagApplicationService)service).ApplyAsync(
            new ApplyCampaignTagApplicationInput
            {
                PlayerCampaignAssignmentId = assignmentId,
                PlayerTagId = tagId
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.CampaignTagApplications
            .SingleOrDefaultAsync(
                candidate => candidate.CampaignTagApplicationId == result.Value.CampaignTagApplicationId,
                TestContext.Current.CancellationToken);
        persisted.ShouldNotBeNull();
        persisted.PlayerCampaignAssignmentId.ShouldBe(assignmentId);
        persisted.PlayerTagId.ShouldBe(tagId);
    }

    /// <summary>
    /// Verifies a remove whose commit reached the database but surfaced a transient failure is
    /// reported as success rather than replayed into a spurious not-found.
    /// </summary>
    [Fact]
    public async Task RemoveCampaignTagApplication_ReportsSuccess_WhenCommitSucceedsButTransientFailureSurfaces()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, _, _, _, applicationId) = await SeedTagApplicationDataAsync(actorUserId, suffix, applied: true);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new CampaignTagApplicationService(
            factory,
            fixture.CurrentUser,
            NullLogger<CampaignTagApplicationService>.Instance);

        var result = await ((ICampaignTagApplicationService)service).RemoveAsync(
            new RemoveCampaignTagApplicationInput { CampaignTagApplicationId = applicationId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.CampaignTagApplications
            .SingleOrDefaultAsync(
                candidate => candidate.CampaignTagApplicationId == applicationId,
                TestContext.Current.CancellationToken);
        persisted.ShouldBeNull();
    }

    /// <summary>
    /// Verifies a removal prunes receipts older than the retention window so the durable verification
    /// artifact does not accumulate unboundedly, while the current operation's receipt is retained.
    /// </summary>
    [Fact]
    public async Task RemoveCampaignTagApplication_PrunesExpiredRemovalReceipts()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, _, _, _, applicationId) = await SeedTagApplicationDataAsync(actorUserId, suffix, applied: true);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        // Backdate a receipt from an earlier removal beyond the retention window.
        var staleOperationId = Guid.CreateVersion7();
        await using (var seed = fixture.CreateAdminContext())
        {
            var staleReceipt = new CampaignTagApplicationRemovalReceiptEntity
            {
                RemovalOperationId = staleOperationId,
                CampaignTagApplicationId = applicationId,
                ClubId = clubId,
                CreatedById = actorUserId
            };
            seed.CampaignTagApplicationRemovalReceipts.Add(staleReceipt);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            staleReceipt.CreatedAt = DateTimeOffset.UtcNow.AddDays(-2);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            new NoOpInterceptor());
        var service = new CampaignTagApplicationService(
            factory,
            fixture.CurrentUser,
            NullLogger<CampaignTagApplicationService>.Instance);

        var result = await ((ICampaignTagApplicationService)service).RemoveAsync(
            new RemoveCampaignTagApplicationInput { CampaignTagApplicationId = applicationId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();

        await using var verify = fixture.CreateAdminContext();
        var staleRemains = await verify.CampaignTagApplicationRemovalReceipts
            .AnyAsync(receipt => receipt.RemovalOperationId == staleOperationId, TestContext.Current.CancellationToken);
        staleRemains.ShouldBeFalse("expired receipts must be pruned during removal");
        var freshReceiptCount = await verify.CampaignTagApplicationRemovalReceipts
            .CountAsync(receipt => receipt.CampaignTagApplicationId == applicationId, TestContext.Current.CancellationToken);
        freshReceiptCount.ShouldBe(1, "the current removal's receipt must survive for verification");
    }

    /// <summary>
    /// Verifies two concurrent removals in the same club and its sole Active campaign both succeed
    /// while pruning an expired receipt. The campaign advisory lock serializes these valid mutations,
    /// and each removal must preserve its own durable idempotency receipt.
    /// </summary>
    [Fact]
    public async Task RemoveCampaignTagApplication_ConcurrentSameActiveCampaignPrunes_BothSucceed()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, campaignId, _, _, applicationId) = await SeedTagApplicationDataAsync(actorUserId, suffix, applied: true);
        var (_, _, secondApplicationId) = await SeedSecondTagApplicationInCampaignAsync(
            actorUserId,
            clubId,
            campaignId,
            suffix);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        // Backdate a receipt so both removals would be eligible to prune it.
        var staleOperationId = Guid.CreateVersion7();
        await using (var seed = fixture.CreateAdminContext())
        {
            var staleReceipt = new CampaignTagApplicationRemovalReceiptEntity
            {
                RemovalOperationId = staleOperationId,
                CampaignTagApplicationId = applicationId,
                ClubId = clubId,
                CreatedById = actorUserId
            };
            seed.CampaignTagApplicationRemovalReceipts.Add(staleReceipt);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            staleReceipt.CreatedAt = DateTimeOffset.UtcNow.AddDays(-2);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var firstLockGate = new AdvisoryLockGateInterceptor();
        var firstPruneGate = new GateReceiptDeleteInterceptor();
        var firstFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            firstLockGate,
            firstPruneGate);
        var secondFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            new NoOpInterceptor());
        var firstService = new CampaignTagApplicationService(
            firstFactory,
            fixture.CurrentUser,
            NullLogger<CampaignTagApplicationService>.Instance);
        var secondService = new CampaignTagApplicationService(
            secondFactory,
            fixture.CurrentUser,
            NullLogger<CampaignTagApplicationService>.Instance);

        Task<ServiceResult<Success>> firstRemove;
        try
        {
            // First prove the removal acquired its campaign lock, then let it advance to the receipt
            // prune gate. It remains paused there with the transaction-scoped campaign lock held.
            firstRemove = ((ICampaignTagApplicationService)firstService).RemoveAsync(
                new RemoveCampaignTagApplicationInput { CampaignTagApplicationId = applicationId },
                TestContext.Current.CancellationToken);
            await firstLockGate.WaitForAcquiredAsync(TestContext.Current.CancellationToken);
            firstLockGate.Release();
            await firstPruneGate.WaitForDeleteAttemptAsync(TestContext.Current.CancellationToken);

            var secondRemove = ((ICampaignTagApplicationService)secondService).RemoveAsync(
                new RemoveCampaignTagApplicationInput { CampaignTagApplicationId = secondApplicationId },
                TestContext.Current.CancellationToken);

            // Do not release the first removal until PostgreSQL confirms that the second transaction
            // has reached and is blocked on the same campaign advisory-lock key.
            await using var lockProbe = fixture.CreateAdminContext();
            await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
                lockProbe,
                long.MinValue + campaignId,
                TestContext.Current.CancellationToken);
            secondRemove.IsCompleted.ShouldBeFalse();

            firstPruneGate.Release();
            var results = await Task.WhenAll(firstRemove, secondRemove);
            results.ShouldAllBe(result => result.IsSuccess);
        }
        finally
        {
            firstLockGate.Release();
            firstPruneGate.Release();
        }

        await using var verify = fixture.CreateAdminContext();
        var staleRemains = await verify.CampaignTagApplicationRemovalReceipts
            .AnyAsync(receipt => receipt.RemovalOperationId == staleOperationId, TestContext.Current.CancellationToken);
        staleRemains.ShouldBeFalse("the expired receipt must be pruned exactly once");
        var receiptCount = await verify.CampaignTagApplicationRemovalReceipts
            .CountAsync(receipt => receipt.ClubId == clubId, TestContext.Current.CancellationToken);
        receiptCount.ShouldBe(2, "each removal keeps its own durable receipt");
        var applicationsRemain = await verify.CampaignTagApplications
            .AnyAsync(
                application => application.CampaignTagApplicationId == applicationId
                    || application.CampaignTagApplicationId == secondApplicationId,
                TestContext.Current.CancellationToken);
        applicationsRemain.ShouldBeFalse("both applications must be removed");
    }

    /// <summary>
    /// Seeds a second player, tag, participation, and application in the existing Active campaign so
    /// both removal attempts remain valid under the one-Active-campaign-per-club invariant.
    /// </summary>
    /// <param name="actorUserId">The creating user identifier.</param>
    /// <param name="clubId">The club shared with the first seeded application.</param>
    /// <param name="campaignId">The sole Active campaign shared with the first seeded application.</param>
    /// <param name="suffix">A unique suffix for generated names.</param>
    /// <returns>The seeded tag, participation, and application identifiers.</returns>
    private async Task<(long TagId, long AssignmentId, long ApplicationId)>
        SeedSecondTagApplicationInCampaignAsync(long actorUserId, long clubId, long campaignId, string suffix)
    {
        await using var seed = fixture.CreateAdminContext();
        var player = new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            FirstName = "Tag",
            LastName = $"Retry Player 2 {suffix}",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        var playerTag = new PlayerTagEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Tag Retry Tag 2 {suffix}",
            NormalizedName = $"Tag Retry Tag 2 {suffix}".Trim().ToUpperInvariant(),
            Color = "#00CC00",
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = clubId,
            CreatedById = actorUserId
        };

        seed.AddRange(player, playerTag);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var assignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaignId,
            ClubId = clubId,
            CreatedById = actorUserId,
            PlacementOutcome = PlacementOutcome.Undecided,
            TryoutNumber = 8
        };
        seed.Add(assignment);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var application = new CampaignTagApplicationEntity
        {
            CreationOperationId = Guid.NewGuid(),
            PlayerCampaignAssignmentId = assignment.PlayerCampaignAssignmentId,
            PlayerTagId = playerTag.PlayerTagId,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        seed.Add(application);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (playerTag.PlayerTagId, assignment.PlayerCampaignAssignmentId, application.CampaignTagApplicationId);
    }

    /// <summary>
    /// Seeds one club, campaign, player, tag, participation, and optional tag application owned by it.
    /// </summary>
    /// <param name="actorUserId">The creating user identifier.</param>
    /// <param name="suffix">A unique suffix for generated names.</param>
    /// <param name="applied">Whether the seeded tag application row should already exist.</param>
    /// <returns>The seeded club, campaign, tag, participation, and application identifiers.</returns>
    private async Task<(long ClubId, long CampaignId, long TagId, long AssignmentId, long ApplicationId)> SeedTagApplicationDataAsync(
        long actorUserId,
        string suffix,
        bool applied = false)
    {
        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;
        fixture.CurrentUser.IsClubAdmin = false;

        await using var seed = fixture.CreateAdminContext();
        var club = new ClubEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Tag Retry Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        seed.Clubs.Add(club);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var season = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Tag Retry Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Tag Retry Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            Season = season,
            SeasonId = 0,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        var player = new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            FirstName = "Tag",
            LastName = $"Retry Player {suffix}",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        var playerTag = new PlayerTagEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Tag Retry Tag {suffix}",
            NormalizedName = $"Tag Retry Tag {suffix}".Trim().ToUpperInvariant(),
            Color = "#00CC00",
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };

        seed.AddRange(season, campaign, player, playerTag);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var assignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            ClubId = club.ClubId,
            CreatedById = actorUserId,
            PlacementOutcome = PlacementOutcome.Undecided,
            TryoutNumber = 7
        };
        seed.Add(assignment);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        long applicationId = 0;
        if (applied)
        {
            var application = new CampaignTagApplicationEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerCampaignAssignmentId = assignment.PlayerCampaignAssignmentId,
                PlayerTagId = playerTag.PlayerTagId,
                ClubId = club.ClubId,
                CreatedById = actorUserId
            };
            seed.Add(application);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            applicationId = application.CampaignTagApplicationId;
        }

        return (club.ClubId, campaign.CampaignId, playerTag.PlayerTagId, assignment.PlayerCampaignAssignmentId, applicationId);
    }
}
