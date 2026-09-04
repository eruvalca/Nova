using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies campaign lifecycle mutations remain correct when Npgsql retries a failed transaction.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignLifecycleRetryTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies opening lets a provider failure wrapped by EF in <see cref="DbUpdateException"/>
    /// reach the execution strategy, which retries with a fresh context and commits one aggregate.
    /// </summary>
    [Fact]
    public async Task CampaignOpen_RetriesWithFreshContext_AfterTransientSaveFailure()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, campaignId) = await SeedClubAndCampaignAsync(
            actorUserId,
            suffix,
            draft: true,
            activePlayerCount: 2);
        var operationId = Guid.CreateVersion7();
        ActAsAdmin(actorUserId, clubId);

        var failureInterceptor = new FailFirstCampaignWriteCommandInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = CreateService(factory);

        var result = await service.OpenAsync(
            campaignId,
            new OpenCampaignInput { OperationId = operationId },
            TestContext.Current.CancellationToken);

        var receipt = result.Value.ShouldBeOfType<OpenCampaignResult>();
        receipt.OperationId.ShouldBe(operationId);
        receipt.EnrolledPlayerCount.ShouldBe(2);
        failureInterceptor.FailureCount.ShouldBe(1);
        factory.CreatedContextCount.ShouldBe(4);

        await using var verify = fixture.CreateAdminContext();
        var campaign = await verify.Campaigns.SingleAsync(
            candidate => candidate.CampaignId == campaignId,
            TestContext.Current.CancellationToken);
        campaign.Status.ShouldBe(CampaignStatus.Active);
        campaign.OpeningOperationId.ShouldBe(operationId);
        (await verify.PlayerCampaignAssignments.CountAsync(
            assignment => assignment.CampaignId == campaignId,
            TestContext.Current.CancellationToken)).ShouldBe(2);
        (await verify.ActivityEvents.CountAsync(
            activity => activity.CampaignId == campaignId
                && activity.EventKind == ActivityEventKind.CampaignOpened,
            TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    /// <summary>Verifies opening reconstructs its immutable receipt after a lost commit acknowledgement.</summary>
    [Fact]
    public async Task CampaignOpen_ReturnsPersistedReceipt_WhenCommitAcknowledgementIsLost()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, campaignId) = await SeedClubAndCampaignAsync(
            actorUserId,
            suffix,
            draft: true,
            activePlayerCount: 1);
        var operationId = Guid.CreateVersion7();
        ActAsAdmin(actorUserId, clubId);

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = CreateService(factory);

        var result = await service.OpenAsync(
            campaignId,
            new OpenCampaignInput { OperationId = operationId },
            TestContext.Current.CancellationToken);

        var receipt = result.Value.ShouldBeOfType<OpenCampaignResult>();
        receipt.OperationId.ShouldBe(operationId);
        receipt.CampaignId.ShouldBe(campaignId);
        receipt.EnrolledPlayerCount.ShouldBe(1);
        receipt.ActiveTeamCount.ShouldBe(0);
        receipt.Warnings.ShouldBe([CampaignOpeningWarning.NoActiveTeams]);
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.Campaigns.SingleAsync(
            candidate => candidate.CampaignId == campaignId,
            TestContext.Current.CancellationToken);
        receipt.OpenedAt.ShouldBe(persisted.OpenedAt!.Value);
        receipt.OpenedByUserId.ShouldBe(persisted.OpenedById!.Value);
        (await verify.ActivityEvents.CountAsync(
            activity => activity.CampaignId == campaignId
                && activity.EventKind == ActivityEventKind.CampaignOpened,
            TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    /// <summary>Verifies Draft deletion retries a rolled-back attempt and writes one tombstone.</summary>
    [Fact]
    public async Task CampaignDraftDelete_RetriesWithFreshContext_AfterTransientSaveFailure()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, campaignId) = await SeedClubAndCampaignAsync(actorUserId, suffix, draft: true);
        ActAsAdmin(actorUserId, clubId);

        var failureInterceptor = new FailFirstSaveChangesInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var result = await CreateService(factory).DeleteDraftAsync(
            campaignId,
            TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);
        factory.CreatedContextCount.ShouldBe(3);
        await AssertDeletedWithOneTombstoneAsync(campaignId);
    }

    /// <summary>Verifies a deletion tombstone proves success after a lost commit acknowledgement.</summary>
    [Fact]
    public async Task CampaignDraftDelete_ReturnsSuccess_WhenCommitAcknowledgementIsLost()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, campaignId) = await SeedClubAndCampaignAsync(actorUserId, suffix, draft: true);
        ActAsAdmin(actorUserId, clubId);

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var result = await CreateService(factory).DeleteDraftAsync(
            campaignId,
            TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);
        await AssertDeletedWithOneTombstoneAsync(campaignId);
    }

    /// <summary>Verifies two opens serialize on one Draft and commit exactly one opening.</summary>
    [Fact]
    public async Task CampaignOpenConcurrency_SameDraftYieldsOneWinner()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, campaignId) = await SeedClubAndCampaignAsync(
            actorUserId,
            suffix,
            draft: true,
            activePlayerCount: 1);
        ActAsAdmin(actorUserId, clubId);
        var cancellationToken = TestContext.Current.CancellationToken;
        var gate = new AdvisoryLockGateInterceptor(advisoryLocksToSkip: 2);
        var firstService = CreateService(new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            gate));
        var secondService = CreateService(new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            new NoOpInterceptor()));

        var firstTask = firstService.OpenAsync(
            campaignId,
            new OpenCampaignInput { OperationId = Guid.CreateVersion7() },
            cancellationToken);
        await gate.WaitForAcquiredAsync(cancellationToken);
        var secondTask = secondService.OpenAsync(
            campaignId,
            new OpenCampaignInput { OperationId = Guid.CreateVersion7() },
            cancellationToken);

        await using var probe = fixture.CreateAdminContext();
        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
            probe,
            (long.MinValue / 16) + clubId,
            cancellationToken);
        gate.Release();

        var results = await Task.WhenAll(firstTask, secondTask);
        results.Count(result => result.IsSuccess).ShouldBe(1);
        results.Count(result => result.IsProblem).ShouldBe(1);
        results.Single(result => result.IsProblem).Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);

        await using var verify = fixture.CreateAdminContext();
        (await verify.PlayerCampaignAssignments.CountAsync(
            assignment => assignment.CampaignId == campaignId,
            cancellationToken)).ShouldBe(1);
        (await verify.ActivityEvents.CountAsync(
            activity => activity.CampaignId == campaignId
                && activity.EventKind == ActivityEventKind.CampaignOpened,
            cancellationToken)).ShouldBe(1);
    }

    /// <summary>Verifies opening and deletion serialize and cannot both commit for one Draft.</summary>
    [Fact]
    public async Task CampaignOpenDeleteConcurrency_OpenWinnerMakesDeleteConflict()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, campaignId) = await SeedClubAndCampaignAsync(
            actorUserId,
            suffix,
            draft: true,
            activePlayerCount: 1);
        ActAsAdmin(actorUserId, clubId);
        var cancellationToken = TestContext.Current.CancellationToken;
        var gate = new AdvisoryLockGateInterceptor(advisoryLocksToSkip: 2);
        var openService = CreateService(new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            gate));
        var deleteService = CreateService(new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            new NoOpInterceptor()));

        var openTask = openService.OpenAsync(
            campaignId,
            new OpenCampaignInput { OperationId = Guid.CreateVersion7() },
            cancellationToken);
        await gate.WaitForAcquiredAsync(cancellationToken);
        var deleteTask = deleteService.DeleteDraftAsync(campaignId, cancellationToken);

        await using var probe = fixture.CreateAdminContext();
        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
            probe,
            long.MinValue + campaignId,
            cancellationToken);
        gate.Release();

        var openResult = await openTask;
        var deleteResult = await deleteTask;
        openResult.IsSuccess.ShouldBeTrue();
        deleteResult.IsProblem.ShouldBeTrue();
        deleteResult.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);

        await using var verify = fixture.CreateAdminContext();
        (await verify.Campaigns.AnyAsync(
            campaign => campaign.CampaignId == campaignId
                && campaign.Status == CampaignStatus.Active,
            cancellationToken)).ShouldBeTrue();
        (await verify.ActivityEvents.CountAsync(
            activity => activity.CampaignId == campaignId
                && activity.EventKind == ActivityEventKind.CampaignOpened,
            cancellationToken)).ShouldBe(1);
        (await verify.ActivityEvents.CountAsync(
            activity => activity.CampaignId == campaignId
                && activity.EventKind == ActivityEventKind.CampaignDraftDeleted,
            cancellationToken)).ShouldBe(0);
    }

    /// <summary>
    /// Verifies a transient post-save failure rolls back and retries with database state loaded by a fresh context.
    /// </summary>
    [Fact]
    public async Task CampaignClose_RetriesWithFreshContext_AfterTransientSaveFailure()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, campaignId) = await SeedClubAndCampaignAsync(actorUserId, suffix);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstSaveChangesInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new CampaignLifecycleService(
            factory,
            fixture.CurrentUser,
            NullLogger<CampaignLifecycleService>.Instance);

        var result = await service.CloseAsync(campaignId, TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);

        // One context for execution-strategy setup and one per mutation attempt. The save failed
        // before the commit, so verification short-circuits without creating a context.
        factory.CreatedContextCount.ShouldBe(3);

        await using var verify = fixture.CreateAdminContext();
        var campaign = await verify.Campaigns
            .Where(candidate => candidate.CampaignId == campaignId)
            .Select(candidate => new { candidate.Status, candidate.ClosedById, candidate.ClosedAt })
            .SingleAsync(TestContext.Current.CancellationToken);
        campaign.Status.ShouldBe(CampaignStatus.Closed);
        campaign.ClosedById.ShouldBe(actorUserId);
        campaign.ClosedAt.ShouldNotBeNull();
    }

    /// <summary>
    /// Verifies a reopen replayed by the execution strategy leaves the campaign active exactly once.
    /// </summary>
    [Fact]
    public async Task CampaignReopen_RetriesWithFreshContext_AfterTransientSaveFailure()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, campaignId) = await SeedClubAndCampaignAsync(actorUserId, suffix, closed: true);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstSaveChangesInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new CampaignLifecycleService(
            factory,
            fixture.CurrentUser,
            NullLogger<CampaignLifecycleService>.Instance);

        var result = await service.ReopenAsync(campaignId, TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var campaign = await verify.Campaigns
            .Where(candidate => candidate.CampaignId == campaignId)
            .Select(candidate => new { candidate.Status, candidate.ClosedAt, candidate.ClosedById })
            .SingleAsync(TestContext.Current.CancellationToken);
        campaign.Status.ShouldBe(CampaignStatus.Active);
        campaign.ClosedAt.ShouldBeNull();
        campaign.ClosedById.ShouldBeNull();
    }

    /// <summary>
    /// Verifies a close whose commit reached the database but surfaced a transient failure is
    /// reported as success rather than replayed into a spurious "already closed" conflict.
    /// </summary>
    [Fact]
    public async Task CampaignClose_ReportsSuccess_WhenCommitSucceedsButTransientFailureSurfaces()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, campaignId) = await SeedClubAndCampaignAsync(actorUserId, suffix);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new CampaignLifecycleService(
            factory,
            fixture.CurrentUser,
            NullLogger<CampaignLifecycleService>.Instance);

        var result = await service.CloseAsync(campaignId, TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var campaign = await verify.Campaigns
            .Where(candidate => candidate.CampaignId == campaignId)
            .Select(candidate => new { candidate.Status, candidate.ClosedById })
            .SingleAsync(TestContext.Current.CancellationToken);
        campaign.Status.ShouldBe(CampaignStatus.Closed);
        campaign.ClosedById.ShouldBe(actorUserId);
    }

    /// <summary>
    /// Verifies the same ambiguous-commit protection applies to reopen.
    /// </summary>
    [Fact]
    public async Task CampaignReopen_ReportsSuccess_WhenCommitSucceedsButTransientFailureSurfaces()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, campaignId) = await SeedClubAndCampaignAsync(actorUserId, suffix, closed: true);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new CampaignLifecycleService(
            factory,
            fixture.CurrentUser,
            NullLogger<CampaignLifecycleService>.Instance);

        var result = await service.ReopenAsync(campaignId, TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var campaign = await verify.Campaigns
            .Where(candidate => candidate.CampaignId == campaignId)
            .Select(candidate => new { candidate.Status, candidate.ClosedAt, candidate.ClosedById })
            .SingleAsync(TestContext.Current.CancellationToken);
        campaign.Status.ShouldBe(CampaignStatus.Active);
        campaign.ClosedAt.ShouldBeNull();
        campaign.ClosedById.ShouldBeNull();
    }

    /// <summary>
    /// Verifies a transient failure raised before any commit does not let commit verification
    /// convert a genuine "already closed" conflict into a success.
    /// </summary>
    /// <remarks>
    /// The campaign is closed before the request runs, so the correct answer is a conflict. The
    /// injected failure interrupts the read, which means no commit was attempted and the applied
    /// status belongs to an earlier request rather than this one's ambiguous commit.
    /// </remarks>
    [Fact]
    public async Task CampaignClose_ReportsConflict_WhenTransientFailurePrecedesCommitOnClosedCampaign()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, campaignId) = await SeedClubAndCampaignAsync(actorUserId, suffix, closed: true);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstCampaignReadInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new CampaignLifecycleService(
            factory,
            fixture.CurrentUser,
            NullLogger<CampaignLifecycleService>.Instance);

        var result = await service.CloseAsync(campaignId, TestContext.Current.CancellationToken);

        failureInterceptor.FailureCount.ShouldBe(1);
        result.IsT4.ShouldBeTrue("an already-closed campaign must still conflict after a pre-commit retry");
    }

    /// <summary>
    /// Verifies a transient failure raised before any commit does not let commit verification
    /// convert a genuine "already active" conflict into a success.
    /// </summary>
    /// <remarks>
    /// The campaign is active before the request runs, so the correct answer is a conflict. The
    /// injected failure interrupts the read, which means no commit was attempted and the applied
    /// status belongs to an earlier request rather than this one's ambiguous commit.
    /// </remarks>
    [Fact]
    public async Task CampaignReopen_ReportsConflict_WhenTransientFailurePrecedesCommitOnActiveCampaign()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, campaignId) = await SeedClubAndCampaignAsync(actorUserId, suffix);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstCampaignReadInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new CampaignLifecycleService(
            factory,
            fixture.CurrentUser,
            NullLogger<CampaignLifecycleService>.Instance);

        var result = await service.ReopenAsync(campaignId, TestContext.Current.CancellationToken);

        failureInterceptor.FailureCount.ShouldBe(1);
        result.IsT3.ShouldBeTrue("an already-active campaign must still conflict after a pre-commit retry");
    }

    /// <summary>
    /// Seeds one club and one campaign owned by it, bypassing tenant filters.
    /// </summary>
    /// <param name="actorUserId">The creating user identifier.</param>
    /// <param name="suffix">A unique suffix for generated names.</param>
    /// <param name="closed">Whether the seeded campaign starts closed.</param>
    /// <returns>The seeded club and campaign identifiers.</returns>
    private async Task<(long ClubId, long CampaignId)> SeedClubAndCampaignAsync(
        long actorUserId,
        string suffix,
        bool closed = false,
        bool draft = false,
        int activePlayerCount = 0)
    {
        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;
        fixture.CurrentUser.IsClubAdmin = false;

        await using var seed = fixture.CreateAdminContext();
        var club = new ClubEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Campaign Retry Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        seed.Clubs.Add(club);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var season = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        seed.Seasons.Add(season);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        club.CurrentSeasonId = season.SeasonId;
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            SeasonId = season.SeasonId,
            ClubId = club.ClubId,
            CreatedById = actorUserId,
            Status = draft ? CampaignStatus.Draft : closed ? CampaignStatus.Closed : CampaignStatus.Active,
            ClosedAt = closed ? DateTimeOffset.UtcNow : null,
            ClosedById = closed ? actorUserId : null
        };
        seed.Campaigns.Add(campaign);

        for (var index = 0; index < activePlayerCount; index++)
        {
            seed.Players.Add(new PlayerEntity
            {
                CreationOperationId = Guid.CreateVersion7(),
                FirstName = "Opening",
                LastName = $"Player {index} {suffix}",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = club.ClubId,
                CreatedById = actorUserId
            });
        }

        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (club.ClubId, campaign.CampaignId);
    }

    /// <summary>Sets the flow-local simulated actor to a club administrator.</summary>
    private void ActAsAdmin(long actorUserId, long clubId)
    {
        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;
    }

    /// <summary>Creates the lifecycle service with the supplied live-database factory.</summary>
    private CampaignLifecycleService CreateService(IDbContextFactory<Nova.Data.NovaDbContext> factory) =>
        new(factory, fixture.CurrentUser, NullLogger<CampaignLifecycleService>.Instance);

    /// <summary>Asserts durable, exactly-once proof for a deleted Draft.</summary>
    private async Task AssertDeletedWithOneTombstoneAsync(long campaignId)
    {
        await using var verify = fixture.CreateAdminContext();
        (await verify.Campaigns.AnyAsync(
            campaign => campaign.CampaignId == campaignId,
            TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await verify.ActivityEvents.CountAsync(
            activity => activity.CampaignId == campaignId
                && activity.EventKind == ActivityEventKind.CampaignDraftDeleted,
            TestContext.Current.CancellationToken)).ShouldBe(1);
    }
}
