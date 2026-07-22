using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies campaign lifecycle migration application and PostgreSQL status/event integrity constraints.
/// </summary>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignLifecyclePostgresTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies the clean Aspire database applied the campaign lifecycle migration.
    /// </summary>
    [Fact]
    public async Task Migration_AppliesCampaignLifecyclePersistenceSchema()
    {
        await using var db = fixture.CreateTenantContext();

        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        appliedMigrations.ShouldContain(
            migration => migration.EndsWith("_AddCampaignLifecyclePersistence", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies campaign status metadata constraints reject partial closure provenance.
    /// </summary>
    [Fact]
    public async Task StatusMetadataConstraint_RejectsPartialClosureProvenance()
    {
        var seed = await SeedCampaignAsync();

        await using var db = fixture.CreateAdminContext();
        var campaign = await db.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == seed.CampaignId, TestContext.Current.CancellationToken);
        campaign.Status = CampaignStatus.Closed;
        campaign.ClosedAt = DateTimeOffset.UtcNow;
        campaign.ClosedById = null;
        db.Update(campaign);

        await Should.ThrowAsync<DbUpdateException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies campaign status metadata constraints reject closure provenance while status is active.
    /// </summary>
    [Fact]
    public async Task StatusMetadataConstraint_RejectsClosureProvenance_ForActiveStatus()
    {
        var seed = await SeedCampaignAsync();

        await using var db = fixture.CreateAdminContext();
        var campaign = await db.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == seed.CampaignId, TestContext.Current.CancellationToken);
        campaign.Status = CampaignStatus.Active;
        campaign.ClosedAt = DateTimeOffset.UtcNow;
        campaign.ClosedById = Random.Shared.NextInt64(1, long.MaxValue);
        db.Update(campaign);

        await Should.ThrowAsync<DbUpdateException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies campaign status constraint rejects undefined enum values.
    /// </summary>
    [Fact]
    public async Task StatusMetadataConstraint_RejectsUndefinedStatus()
    {
        var seed = await SeedCampaignAsync();

        await using var db = fixture.CreateAdminContext();
        var campaign = await db.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == seed.CampaignId, TestContext.Current.CancellationToken);
        campaign.Status = (CampaignStatus)99;
        db.Update(campaign);

        await Should.ThrowAsync<DbUpdateException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies lifecycle event constraints reject undefined event type values.
    /// </summary>
    [Fact]
    public async Task LifecycleEventConstraint_RejectsUndefinedEventType()
    {
        var seed = await SeedCampaignAsync();
        await using var db = fixture.CreateAdminContext();
        db.CampaignLifecycleEvents.Add(new CampaignLifecycleEventEntity
        {
            CampaignId = seed.CampaignId,
            EventType = (CampaignLifecycleEventType)99,
            ClubId = seed.ClubId,
            CreatedById = seed.ActorUserId
        });

        await Should.ThrowAsync<DbUpdateException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies the composite campaign foreign key rejects lifecycle events whose club differs from the campaign owner.
    /// </summary>
    [Fact]
    public async Task LifecycleEventCampaignForeignKey_RejectsCrossTenantCampaignReference()
    {
        var first = await SeedCampaignAsync();
        var second = await SeedCampaignAsync();
        await using var db = fixture.CreateAdminContext();
        db.CampaignLifecycleEvents.Add(new CampaignLifecycleEventEntity
        {
            CampaignId = second.CampaignId,
            EventType = CampaignLifecycleEventType.Closed,
            ClubId = first.ClubId,
            CreatedById = first.ActorUserId
        });

        await Should.ThrowAsync<DbUpdateException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies campaign status concurrency prevents stale lifecycle transitions.
    /// </summary>
    [Fact]
    public async Task StatusConcurrency_RejectsStaleLifecycleTransition()
    {
        var seeded = await SeedCampaignAsync();
        await using var first = fixture.CreateAdminContext();
        await using var stale = fixture.CreateAdminContext();

        var firstCopy = await first.Campaigns
            .SingleAsync(campaign => campaign.CampaignId == seeded.CampaignId, TestContext.Current.CancellationToken);
        var staleCopy = await stale.Campaigns
            .SingleAsync(campaign => campaign.CampaignId == seeded.CampaignId, TestContext.Current.CancellationToken);

        firstCopy.Status = CampaignStatus.Closed;
        firstCopy.ClosedAt = DateTimeOffset.UtcNow;
        firstCopy.ClosedById = seeded.ActorUserId;
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);

        staleCopy.Status = CampaignStatus.Closed;
        staleCopy.ClosedAt = DateTimeOffset.UtcNow;
        staleCopy.ClosedById = seeded.ActorUserId;

        await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => stale.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies a placement waiting behind campaign closure reloads status after the advisory lock and is rejected.
    /// </summary>
    [Fact]
    public async Task PlacementConcurrency_RejectsMutation_WhenCampaignClosesWhileWaitingForLock()
    {
        var seed = await SeedPlacementCampaignAsync();
        fixture.CurrentUser.UserId = seed.ActorUserId;
        fixture.CurrentUser.ClubId = seed.ClubId;
        fixture.CurrentUser.IsClubAdmin = true;
        var service = new CampaignPlacementService(
            new FixtureDbContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<CampaignPlacementService>.Instance);
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var closeContext = fixture.CreateAdminContext();
        await using var transaction = await closeContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = long.MinValue + seed.CampaignId;
        await closeContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);

        var placementTask = service.UpdatePlacementAsync(
            new UpdateCampaignPlacementInput(
                seed.AssignmentId,
                PlacementOutcome.NotSelected,
                TeamId: null,
                seed.ConcurrencyToken),
            cancellationToken);

        await WaitForAdvisoryLockWaiterAsync(closeContext, cancellationToken);

        var campaign = await closeContext.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == seed.CampaignId, cancellationToken);
        campaign.Status = CampaignStatus.Closed;
        campaign.ClosedAt = DateTimeOffset.UtcNow;
        campaign.ClosedById = seed.ActorUserId;
        await closeContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var result = await placementTask;

        result.IsT4.ShouldBeTrue();
        await using var verify = fixture.CreateAdminContext();
        var assignment = await verify.PlayerCampaignAssignments
            .SingleAsync(candidate => candidate.PlayerCampaignAssignmentId == seed.AssignmentId, cancellationToken);
        assignment.PlacementOutcome.ShouldBe(PlacementOutcome.Undecided);
        assignment.ConcurrencyToken.ShouldBe(seed.ConcurrencyToken);
    }

    /// <summary>
    /// Waits until PostgreSQL reports a session blocked on an advisory lock.
    /// </summary>
    /// <param name="db">The context holding the campaign advisory lock.</param>
    /// <param name="cancellationToken">A token that cancels polling.</param>
    /// <returns>A task representing the polling operation.</returns>
    private static async Task WaitForAdvisoryLockWaiterAsync(
        NovaAdminDbContext db,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var hasWaiter = await db.Database
                .SqlQueryRaw<bool>(
                    """
                    SELECT EXISTS (
                        SELECT 1
                        FROM pg_stat_activity
                        WHERE wait_event_type = 'Lock'
                          AND wait_event = 'advisory'
                    ) AS "Value"
                    """)
                .SingleAsync(cancellationToken);
            if (hasWaiter)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }

        throw new TimeoutException("The placement mutation did not wait for the campaign advisory lock.");
    }

    /// <summary>
    /// Seeds one active campaign with an undecided participation for placement concurrency testing.
    /// </summary>
    /// <returns>The seeded campaign and participation identifiers.</returns>
    private async Task<CampaignPlacementSeed> SeedPlacementCampaignAsync()
    {
        var campaignSeed = await SeedCampaignAsync();
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var player = new PlayerEntity
        {
            FirstName = "Concurrent",
            LastName = suffix,
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            ClubId = campaignSeed.ClubId,
            CreatedById = campaignSeed.ActorUserId
        };
        db.Players.Add(player);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var concurrencyToken = Guid.NewGuid();
        var assignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaignSeed.CampaignId,
            PlacementOutcome = PlacementOutcome.Undecided,
            ConcurrencyToken = concurrencyToken,
            ClubId = campaignSeed.ClubId,
            CreatedById = campaignSeed.ActorUserId
        };
        db.PlayerCampaignAssignments.Add(assignment);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new CampaignPlacementSeed(
            campaignSeed.CampaignId,
            campaignSeed.ClubId,
            campaignSeed.ActorUserId,
            assignment.PlayerCampaignAssignmentId,
            concurrencyToken);
    }

    /// <summary>
    /// Seeds one campaign in a unique club and returns it detached for invalid-state mutation.
    /// </summary>
    /// <returns>The seeded campaign metadata.</returns>
    private async Task<CampaignLifecycleSeed> SeedCampaignAsync()
    {
        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;
        fixture.CurrentUser.IsClubAdmin = false;

        await using var db = fixture.CreateAdminContext();
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var club = new ClubEntity
        {
            Name = $"Campaign Lifecycle Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var season = new SeasonEntity
        {
            Name = $"Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        db.Seasons.Add(season);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var campaign = new CampaignEntity
        {
            Name = $"Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            SeasonId = season.SeasonId,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Entry(campaign).State = EntityState.Detached;
        return new CampaignLifecycleSeed(campaign.CampaignId, club.ClubId, actorUserId);
    }

    /// <summary>
    /// Carries identifiers for one seeded campaign lifecycle graph.
    /// </summary>
    /// <param name="CampaignId">The seeded campaign identifier.</param>
    /// <param name="ClubId">The seeded club identifier.</param>
    /// <param name="ActorUserId">The simulated acting user identifier.</param>
    private sealed record CampaignLifecycleSeed(long CampaignId, long ClubId, long ActorUserId);

    /// <summary>
    /// Carries identifiers for one seeded placement concurrency graph.
    /// </summary>
    /// <param name="CampaignId">The seeded campaign identifier.</param>
    /// <param name="ClubId">The seeded club identifier.</param>
    /// <param name="ActorUserId">The simulated acting administrator identifier.</param>
    /// <param name="AssignmentId">The seeded participation identifier.</param>
    /// <param name="ConcurrencyToken">The participation concurrency token.</param>
    private sealed record CampaignPlacementSeed(
        long CampaignId,
        long ClubId,
        long ActorUserId,
        long AssignmentId,
        Guid ConcurrencyToken);

    /// <summary>
    /// Creates tenant contexts against the live Aspire PostgreSQL database.
    /// </summary>
    /// <param name="fixture">The shared AppHost fixture.</param>
    private sealed class FixtureDbContextFactory(NovaAppHostFixture fixture) : IDbContextFactory<NovaDbContext>
    {
        /// <summary>
        /// Creates a tenant context synchronously.
        /// </summary>
        /// <returns>A new tenant context.</returns>
        public NovaDbContext CreateDbContext() => fixture.CreateTenantContext();

        /// <summary>
        /// Creates a tenant context asynchronously.
        /// </summary>
        /// <param name="cancellationToken">A token that cancels context creation.</param>
        /// <returns>A new tenant context.</returns>
        public ValueTask<NovaDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(fixture.CreateTenantContext());
    }
}
