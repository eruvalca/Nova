using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
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
        bool closed = false)
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

        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            SeasonId = season.SeasonId,
            ClubId = club.ClubId,
            CreatedById = actorUserId,
            Status = closed ? CampaignStatus.Closed : CampaignStatus.Active,
            ClosedAt = closed ? DateTimeOffset.UtcNow : null,
            ClosedById = closed ? actorUserId : null
        };
        seed.Campaigns.Add(campaign);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (club.ClubId, campaign.CampaignId);
    }
}
