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
    /// Verifies two concurrent removals in the same club on different campaigns both succeed when
    /// their prunes overlap the same expired receipts. Removals on different campaigns are not
    /// serialized by the campaign advisory lock, so without a set-based prune the second delete would
    /// affect zero rows and surface a DbUpdateConcurrencyException as a spurious not-found.
    /// </summary>
    [Fact]
    public async Task RemoveCampaignTagApplication_ConcurrentSameClubPrunes_BothSucceed()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, _, _, _, applicationId) = await SeedTagApplicationDataAsync(actorUserId, suffix, applied: true);
        var (_, _, _, secondApplicationId) = await SeedSecondTagApplicationDataAsync(actorUserId, clubId, suffix);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        // Backdate a receipt so both removals race to prune it.
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

        var firstGate = new GateReceiptDeleteInterceptor();
        var firstFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            firstGate);
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
            // The first removal pauses after its prune has decided which expired receipts to delete;
            // the second removal then prunes the same expired receipt and commits, leaving zero rows
            // for the paused delete to replay.
            firstRemove = ((ICampaignTagApplicationService)firstService).RemoveAsync(
                new RemoveCampaignTagApplicationInput { CampaignTagApplicationId = applicationId },
                TestContext.Current.CancellationToken);
            await firstGate.WaitForDeleteAttemptAsync(TestContext.Current.CancellationToken);

            var secondResult = await ((ICampaignTagApplicationService)secondService).RemoveAsync(
                new RemoveCampaignTagApplicationInput { CampaignTagApplicationId = secondApplicationId },
                TestContext.Current.CancellationToken);
            secondResult.IsSuccess.ShouldBeTrue("the competing removal must not be blocked by the paused one");

            firstGate.Release();
            var firstResult = await firstRemove;
            firstResult.IsSuccess.ShouldBeTrue("the paused removal must tolerate the expired receipt already being pruned");
        }
        finally
        {
            firstGate.Release();
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
    /// Seeds a second campaign, player, tag, participation, and application in the existing club so a
    /// removal can race with one in a different campaign without sharing an advisory-lock key.
    /// </summary>
    /// <param name="actorUserId">The creating user identifier.</param>
    /// <param name="clubId">The club shared with the first seeded application.</param>
    /// <param name="suffix">A unique suffix for generated names.</param>
    /// <returns>The seeded campaign, tag, participation, and application identifiers.</returns>
    private async Task<(long CampaignId, long TagId, long AssignmentId, long ApplicationId)>
        SeedSecondTagApplicationDataAsync(long actorUserId, long clubId, string suffix)
    {
        await using var seed = fixture.CreateAdminContext();
        var season = new SeasonEntity
        {
            Name = $"Tag Retry Season 2 {suffix}",
            StartDate = new DateOnly(2026, 2, 1),
            ClubId = clubId,
            CreatedById = actorUserId
        };
        var campaign = new CampaignEntity
        {
            Name = $"Tag Retry Campaign 2 {suffix}",
            StartDate = new DateOnly(2026, 7, 1),
            Status = CampaignStatus.Active,
            Season = season,
            SeasonId = 0,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        var player = new PlayerEntity
        {
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
            Name = $"Tag Retry Tag 2 {suffix}",
            NormalizedName = $"TAG RETRY TAG 2 {suffix}".ToUpperInvariant(),
            Color = "#00CC00",
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = clubId,
            CreatedById = actorUserId
        };

        seed.AddRange(season, campaign, player, playerTag);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var assignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            ClubId = clubId,
            CreatedById = actorUserId,
            PlacementOutcome = PlacementOutcome.Undecided,
            TryoutNumber = 7
        };
        seed.Add(assignment);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var application = new CampaignTagApplicationEntity
        {
            PlayerCampaignAssignmentId = assignment.PlayerCampaignAssignmentId,
            PlayerTagId = playerTag.PlayerTagId,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        seed.Add(application);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (campaign.CampaignId, playerTag.PlayerTagId, assignment.PlayerCampaignAssignmentId, application.CampaignTagApplicationId);
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
            Name = $"Tag Retry Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        seed.Clubs.Add(club);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var season = new SeasonEntity
        {
            Name = $"Tag Retry Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        var campaign = new CampaignEntity
        {
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
            Name = $"Tag Retry Tag {suffix}",
            NormalizedName = $"TAG RETRY TAG {suffix}".ToUpperInvariant(),
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
