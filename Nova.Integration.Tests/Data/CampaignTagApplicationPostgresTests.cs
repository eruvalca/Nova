using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Shared.Enums;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies campaign tag application migration application and PostgreSQL uniqueness/tenant-integrity constraints.
/// </summary>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignTagApplicationPostgresTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies the clean Aspire database applied the campaign tag application migration.
    /// </summary>
    [Fact]
    public async Task Migration_AppliesCampaignTagApplicationSchema()
    {
        await using var db = fixture.CreateTenantContext();

        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        appliedMigrations.ShouldContain(
            migration => migration.EndsWith("_AddCampaignTagApplications", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies PostgreSQL rejects duplicate participation/tag applications.
    /// </summary>
    [Fact]
    public async Task UniqueApplication_RejectsDuplicateParticipationTagPair()
    {
        var seed = await SeedAsync();
        ActAs(seed.ActorUserId, seed.ClubAId, isClubAdmin: true);
        await using var db = fixture.CreateTenantContext();
        db.CampaignTagApplications.Add(new CampaignTagApplicationEntity
        {
            PlayerCampaignAssignmentId = seed.ClubAAssignmentId,
            PlayerTagId = seed.ClubATagId,
            ClubId = default,
            CreatedById = default
        });

        await Should.ThrowAsync<DbUpdateException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies composite same-club foreign keys reject mismatched club references.
    /// </summary>
    [Fact]
    public async Task CompositeTenantForeignKeys_RejectCrossTenantAssignmentTagMix()
    {
        var seed = await SeedAsync();
        ActAs(userId: null, clubId: null);
        await using var db = fixture.CreateAdminContext();
        db.CampaignTagApplications.Add(new CampaignTagApplicationEntity
        {
            PlayerCampaignAssignmentId = seed.ClubAAssignmentId,
            PlayerTagId = seed.ClubATagId,
            ClubId = seed.ClubBId,
            CreatedById = seed.ActorUserId
        });

        await Should.ThrowAsync<DbUpdateException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Seeds two clubs with one campaign participation and tag definition each.
    /// </summary>
    /// <returns>Database-generated and deterministic identifiers used in assertions.</returns>
    private async Task<CampaignTagApplicationSeed> SeedAsync()
    {
        ActAs(userId: null, clubId: null);
        await using var db = fixture.CreateAdminContext();
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");

        var clubA = new ClubEntity
        {
            Name = $"Tag App Club A {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        var clubB = new ClubEntity
        {
            Name = $"Tag App Club B {suffix}",
            City = "Boston",
            State = "MA",
            CreatedById = actorUserId
        };
        db.Clubs.AddRange(clubA, clubB);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var seasonA = new SeasonEntity
        {
            Name = $"Season A {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = clubA.ClubId,
            CreatedById = actorUserId
        };
        var seasonB = new SeasonEntity
        {
            Name = $"Season B {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = clubB.ClubId,
            CreatedById = actorUserId
        };
        db.Seasons.AddRange(seasonA, seasonB);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var campaignA = new CampaignEntity
        {
            Name = $"Campaign A {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            SeasonId = seasonA.SeasonId,
            ClubId = clubA.ClubId,
            CreatedById = actorUserId
        };
        var campaignB = new CampaignEntity
        {
            Name = $"Campaign B {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Closed,
            ClosedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ClosedById = actorUserId,
            SeasonId = seasonB.SeasonId,
            ClubId = clubB.ClubId,
            CreatedById = actorUserId
        };
        db.Campaigns.AddRange(campaignA, campaignB);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var playerA = new PlayerEntity
        {
            FirstName = "Player",
            LastName = $"A{suffix}",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            ClubId = clubA.ClubId,
            CreatedById = actorUserId
        };
        var playerB = new PlayerEntity
        {
            FirstName = "Player",
            LastName = $"B{suffix}",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            ClubId = clubB.ClubId,
            CreatedById = actorUserId
        };
        db.Players.AddRange(playerA, playerB);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var assignmentA = new PlayerCampaignAssignmentEntity
        {
            PlayerId = playerA.PlayerId,
            CampaignId = campaignA.CampaignId,
            ClubId = clubA.ClubId,
            CreatedById = actorUserId
        };
        var assignmentB = new PlayerCampaignAssignmentEntity
        {
            PlayerId = playerB.PlayerId,
            CampaignId = campaignB.CampaignId,
            ClubId = clubB.ClubId,
            CreatedById = actorUserId
        };
        db.PlayerCampaignAssignments.AddRange(assignmentA, assignmentB);

        var tagA = new PlayerTagEntity
        {
            Name = $"Tag A {suffix}",
            Color = "#00CC00",
            ClubId = clubA.ClubId,
            CreatedById = actorUserId
        };
        var tagB = new PlayerTagEntity
        {
            Name = $"Tag B {suffix}",
            Color = "#0000CC",
            ClubId = clubB.ClubId,
            CreatedById = actorUserId
        };
        db.PlayerTags.AddRange(tagA, tagB);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.CampaignTagApplications.Add(new CampaignTagApplicationEntity
        {
            PlayerCampaignAssignmentId = assignmentA.PlayerCampaignAssignmentId,
            PlayerTagId = tagA.PlayerTagId,
            ClubId = clubA.ClubId,
            CreatedById = actorUserId
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new CampaignTagApplicationSeed(
            actorUserId,
            clubA.ClubId,
            clubB.ClubId,
            assignmentA.PlayerCampaignAssignmentId,
            tagA.PlayerTagId);
    }

    /// <summary>
    /// Sets the simulated current user for subsequent contexts.
    /// </summary>
    /// <param name="userId">The current user identifier, or null for anonymous.</param>
    /// <param name="clubId">The current club identifier, or null for no club.</param>
    /// <param name="isClubAdmin">Whether the user is a club administrator.</param>
    private void ActAs(long? userId, long? clubId, bool isClubAdmin = false)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = isClubAdmin;
    }

    /// <summary>
    /// Carries identifiers for a seeded campaign-tag-application graph.
    /// </summary>
    /// <param name="ActorUserId">The simulated acting user identifier.</param>
    /// <param name="ClubAId">The first club identifier.</param>
    /// <param name="ClubBId">The second club identifier.</param>
    /// <param name="ClubAAssignmentId">The first club's participation identifier.</param>
    /// <param name="ClubATagId">The first club's tag-definition identifier.</param>
    private sealed record CampaignTagApplicationSeed(
        long ActorUserId,
        long ClubAId,
        long ClubBId,
        long ClubAAssignmentId,
        long ClubATagId);
}
