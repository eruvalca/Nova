using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Shared.Enums;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies the evaluation-note campaign-association migration, FK cascade behavior, and
/// tenant filtering against a real PostgreSQL database.
/// </summary>
[Collection(NovaAppHostCollection.Name)]
public sealed class EvaluationNotePostgresTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies the live database has the evaluation note association migration applied.
    /// </summary>
    [Fact]
    public async Task Migration_AppliesEvaluationNoteCampaignAssociation()
    {
        await using var db = fixture.CreateTenantContext();

        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        appliedMigrations.ShouldContain(
            migration => migration.EndsWith("_AddEvaluationNoteCampaignAssociation", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that deleting a campaign participation cascades to its notes.
    /// </summary>
    [Fact]
    public async Task CascadeDelete_RemovesNotesWhenParticipationDeleted()
    {
        var data = await SeedAsync();
        ActAs(data.ActorUserId, data.ClubId, isClubAdmin: true);

        // Add a note to the participation.
        await using (var db = fixture.CreateTenantContext())
        {
            db.Notes.Add(new NoteEntity
            {
                Content = "Cascade test note.",
                PlayerCampaignAssignmentId = data.AssignmentId,
                ClubId = default,
                CreatedById = default
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Delete the participation — the note should cascade.
        await using (var db = fixture.CreateTenantContext())
        {
            var participation = await db.PlayerCampaignAssignments
                .SingleAsync(
                    a => a.PlayerCampaignAssignmentId == data.AssignmentId,
                    TestContext.Current.CancellationToken);
            db.PlayerCampaignAssignments.Remove(participation);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // The note must be gone.
        await using var verify = fixture.CreateAdminContext();
        var orphanedNotes = await verify.Notes
            .Where(n => n.PlayerCampaignAssignmentId == data.AssignmentId)
            .CountAsync(TestContext.Current.CancellationToken);

        orphanedNotes.ShouldBe(0);
    }

    /// <summary>
    /// Verifies that notes are filtered to the owning club and invisible to another club.
    /// </summary>
    [Fact]
    public async Task Notes_TenantFilter_HidesOtherClubNotes()
    {
        var data = await SeedAsync();
        ActAs(data.ActorUserId, data.ClubId, isClubAdmin: true);

        // Seed a note for this club's assignment.
        await using (var db = fixture.CreateTenantContext())
        {
            db.Notes.Add(new NoteEntity
            {
                Content = "Tenant filter test.",
                PlayerCampaignAssignmentId = data.AssignmentId,
                ClubId = default,
                CreatedById = default
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // A different (anonymous/no-club) user must not see the note.
        ActAs(userId: null, clubId: null);
        await using var db2 = fixture.CreateTenantContext();
        var visibleNotes = await db2.Notes
            .Where(n => n.PlayerCampaignAssignmentId == data.AssignmentId)
            .CountAsync(TestContext.Current.CancellationToken);

        visibleNotes.ShouldBe(0);
    }

    /// <summary>Seeds one club, season, campaign, player, and participation for isolation.</summary>
    private async Task<EvaluationNoteSeed> SeedAsync()
    {
        ActAs(userId: null, clubId: null);
        await using var db = fixture.CreateAdminContext();

        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");

        var club = new ClubEntity
        {
            Name = $"Note Club {suffix}",
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

        var player = new PlayerEntity
        {
            FirstName = "Note",
            LastName = suffix,
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        db.Players.Add(player);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var campaign = new CampaignEntity
        {
            Name = $"Note Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            SeasonId = season.SeasonId,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var assignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        db.PlayerCampaignAssignments.Add(assignment);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new EvaluationNoteSeed(club.ClubId, actorUserId, assignment.PlayerCampaignAssignmentId);
    }

    /// <summary>Sets the simulated current user for subsequent contexts.</summary>
    private void ActAs(long? userId, long? clubId, bool isClubAdmin = false)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = isClubAdmin;
    }

    /// <summary>
    /// Carries identifiers for a seeded evaluation-note test graph.
    /// </summary>
    /// <param name="ClubId">The seeded club identifier.</param>
    /// <param name="ActorUserId">The simulated acting user identifier.</param>
    /// <param name="AssignmentId">The seeded participation identifier.</param>
    private sealed record EvaluationNoteSeed(long ClubId, long ActorUserId, long AssignmentId);
}
