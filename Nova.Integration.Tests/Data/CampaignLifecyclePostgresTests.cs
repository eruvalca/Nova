using Microsoft.EntityFrameworkCore;
using Nova.Entities;
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
}
