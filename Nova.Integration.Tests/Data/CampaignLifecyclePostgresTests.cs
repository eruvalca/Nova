using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
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
        appliedMigrations.ShouldContain(
            migration => migration.EndsWith("_AddCampaignDraftLifecycle", StringComparison.Ordinal));
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

    /// <summary>Verifies Draft campaigns cannot carry closure provenance.</summary>
    [Fact]
    public async Task StatusMetadataConstraint_RejectsClosureProvenance_ForDraftStatus()
    {
        var seed = await SeedCampaignAsync();
        await using var db = fixture.CreateAdminContext();
        var campaign = await db.Campaigns.SingleAsync(
            candidate => candidate.CampaignId == seed.CampaignId,
            TestContext.Current.CancellationToken);
        campaign.Status = CampaignStatus.Draft;
        campaign.ClosedAt = DateTimeOffset.UtcNow;
        campaign.ClosedById = seed.ActorUserId;

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
                teamId: null,
                seed.ConcurrencyToken),
            cancellationToken);

        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
            closeContext,
            lockKey,
            cancellationToken);

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
    /// Verifies a close that waits behind an in-flight close is rejected after the lock is released
    /// and the campaign reloads as already closed, with the winner's closure and single event preserved.
    /// </summary>
    [Fact]
    public async Task CloseConcurrency_RejectsSecondClose_WhenCampaignClosesWhileWaitingForLock()
    {
        var seed = await SeedCampaignAsync();
        fixture.CurrentUser.UserId = seed.ActorUserId;
        fixture.CurrentUser.ClubId = seed.ClubId;
        fixture.CurrentUser.IsClubAdmin = true;
        var service = new CampaignLifecycleService(
            new FixtureDbContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<CampaignLifecycleService>.Instance);
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var closeContext = fixture.CreateAdminContext();
        await using var transaction = await closeContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = long.MinValue + seed.CampaignId;
        await closeContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);

        var closeTask = service.CloseAsync(seed.CampaignId, cancellationToken);

        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
            closeContext,
            lockKey,
            cancellationToken);

        var campaign = await closeContext.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == seed.CampaignId, cancellationToken);
        campaign.Status = CampaignStatus.Closed;
        campaign.ClosedAt = DateTimeOffset.UtcNow;
        campaign.ClosedById = seed.ActorUserId;
        closeContext.ActivityEvents.Add(new ActivityEventEntity
        {
            CampaignId = seed.CampaignId,
            ClubId = seed.ClubId,
            EventKind = ActivityEventKind.CampaignClosed,
            ActorUserId = seed.ActorUserId,
            ActorDisplayName = "Club Admin",
            PayloadJson = JsonSerializer.Serialize(new { campaignId = seed.CampaignId, campaignName = "Excluded" }),
            CreatedById = seed.ActorUserId
        });
        await closeContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var result = await closeTask;

        result.IsT4.ShouldBeTrue();
        result.AsT4.Detail.ShouldBe("The campaign is already closed.");

        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == seed.CampaignId, cancellationToken);
        persisted.Status.ShouldBe(CampaignStatus.Closed);
        persisted.ClosedById.ShouldBe(seed.ActorUserId);

        var closedEvents = await verify.ActivityEvents
            .Where(candidate => candidate.CampaignId == seed.CampaignId
                && candidate.EventKind == ActivityEventKind.CampaignClosed)
            .ToListAsync(cancellationToken);
        closedEvents.Count.ShouldBe(1);
    }

    /// <summary>
    /// Verifies a reopen that waits behind an in-flight reopen is rejected after the lock is released
    /// and the campaign reloads as already active, with the winner's transition and single event preserved.
    /// </summary>
    [Fact]
    public async Task ReopenConcurrency_RejectsSecondReopen_WhenCampaignReopensWhileWaitingForLock()
    {
        var seed = await SeedCampaignAsync(closed: true);
        fixture.CurrentUser.UserId = seed.ActorUserId;
        fixture.CurrentUser.ClubId = seed.ClubId;
        fixture.CurrentUser.IsClubAdmin = true;
        var service = new CampaignLifecycleService(
            new FixtureDbContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<CampaignLifecycleService>.Instance);
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var reopenContext = fixture.CreateAdminContext();
        await using var transaction = await reopenContext.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = long.MinValue + seed.CampaignId;
        await reopenContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);

        var reopenTask = service.ReopenAsync(seed.CampaignId, cancellationToken);

        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
            reopenContext,
            lockKey,
            cancellationToken);

        var campaign = await reopenContext.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == seed.CampaignId, cancellationToken);
        campaign.Status = CampaignStatus.Active;
        campaign.ClosedAt = null;
        campaign.ClosedById = null;
        reopenContext.ActivityEvents.Add(new ActivityEventEntity
        {
            CampaignId = seed.CampaignId,
            ClubId = seed.ClubId,
            EventKind = ActivityEventKind.CampaignReopened,
            ActorUserId = seed.ActorUserId,
            ActorDisplayName = "Club Admin",
            PayloadJson = JsonSerializer.Serialize(new { campaignId = seed.CampaignId, campaignName = "Excluded" }),
            CreatedById = seed.ActorUserId
        });
        await reopenContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var result = await reopenTask;

        result.IsT3.ShouldBeTrue();
        result.AsT3.Detail.ShouldBe("The campaign is already active.");

        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == seed.CampaignId, cancellationToken);
        persisted.Status.ShouldBe(CampaignStatus.Active);
        persisted.ClosedAt.ShouldBeNull();
        persisted.ClosedById.ShouldBeNull();

        var reopenedEvents = await verify.ActivityEvents
            .Where(candidate => candidate.CampaignId == seed.CampaignId
                && candidate.EventKind == ActivityEventKind.CampaignReopened)
            .ToListAsync(cancellationToken);
        reopenedEvents.Count.ShouldBe(1);
    }

    /// <summary>Verifies concurrent reopens of different campaigns yield one Active winner.</summary>
    [Fact]
    public async Task ReopenConcurrency_DifferentClosedCampaignsYieldOneWinner()
    {
        var first = await SeedCampaignAsync(closed: true);
        long secondCampaignId;
        await using (var seed = fixture.CreateAdminContext())
        {
            var seasonId = await seed.Campaigns
                .Where(campaign => campaign.CampaignId == first.CampaignId)
                .Select(campaign => campaign.SeasonId)
                .SingleAsync(TestContext.Current.CancellationToken);
            var second = new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = $"Second Closed {Guid.NewGuid():N}",
                StartDate = new DateOnly(2026, 7, 1),
                Status = CampaignStatus.Closed,
                ClosedAt = DateTimeOffset.UtcNow,
                ClosedById = first.ActorUserId,
                SeasonId = seasonId,
                ClubId = first.ClubId,
                CreatedById = first.ActorUserId
            };
            seed.Campaigns.Add(second);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            secondCampaignId = second.CampaignId;
        }

        fixture.CurrentUser.UserId = first.ActorUserId;
        fixture.CurrentUser.ClubId = first.ClubId;
        fixture.CurrentUser.IsClubAdmin = true;
        var service = new CampaignLifecycleService(
            new FixtureDbContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<CampaignLifecycleService>.Instance);

        var results = await Task.WhenAll(
            service.ReopenAsync(first.CampaignId, TestContext.Current.CancellationToken),
            service.ReopenAsync(secondCampaignId, TestContext.Current.CancellationToken));

        results.Count(result => result.IsT0).ShouldBe(1);
        results.Count(result => result.IsT3).ShouldBe(1);
        await using var verify = fixture.CreateAdminContext();
        (await verify.Campaigns.CountAsync(campaign => campaign.ClubId == first.ClubId
            && campaign.Status == CampaignStatus.Active,
            TestContext.Current.CancellationToken)).ShouldBe(1);
        (await verify.ActivityEvents.CountAsync(activity => activity.ClubId == first.ClubId
            && activity.EventKind == ActivityEventKind.CampaignReopened,
            TestContext.Current.CancellationToken)).ShouldBe(1);
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
            CreationOperationId = Guid.NewGuid(),
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
    /// <param name="closed">Whether the campaign should be seeded as closed with closure provenance.</param>
    /// <returns>The seeded campaign metadata.</returns>
    private async Task<CampaignLifecycleSeed> SeedCampaignAsync(bool closed = false)
    {
        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;
        fixture.CurrentUser.IsClubAdmin = false;

        await using var db = fixture.CreateAdminContext();
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var club = new ClubEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Campaign Lifecycle Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var season = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        db.Seasons.Add(season);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        club.CurrentSeasonId = season.SeasonId;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = closed ? CampaignStatus.Closed : CampaignStatus.Active,
            ClosedAt = closed ? DateTimeOffset.UtcNow : null,
            ClosedById = closed ? actorUserId : null,
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
        /// <param name="_">A token that cancels context creation.</param>
        /// <returns>A new tenant context.</returns>
        public ValueTask<NovaDbContext> CreateDbContextAsync(CancellationToken _ = default)
            => ValueTask.FromResult(fixture.CreateTenantContext());
    }
}
