using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Integration.Tests.Http;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Tags;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>Real persisted actors, evidence, and prior-campaign placement for the locked notebook scene.</summary>
internal static class CampaignEvaluationNotebookSeed
{
    /// <summary>Prepares the existing 60-player campaign without altering any rendered content.</summary>
    /// <param name="fixture">The isolated provider fixture.</param>
    /// <param name="seed">The registered administrator and approved evaluator's campaign.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>Jordan Lee's existing participation identity.</returns>
    public static async Task<long> PrepareAsync(NovaAppHostFixture fixture, SeededEvaluationWorkspace seed, CancellationToken cancellationToken)
    {
        var samId = await PrepareAuthorsAsync(fixture, seed, cancellationToken);
        var participantId = seed.AssignmentIds[41];
        await PrepareCampaignAndPriorPlacementAsync(fixture, seed, participantId, cancellationToken);
        await PrepareEvidenceAsync(fixture, seed, participantId, samId, cancellationToken);
        await using var db = fixture.CreateAdminContext();
        (await db.PlayerCampaignAssignments.CountAsync(item => item.CampaignId == seed.CampaignId, cancellationToken)).ShouldBe(60);
        return participantId;
    }

    private static async Task<long> PrepareAuthorsAsync(NovaAppHostFixture fixture, SeededEvaluationWorkspace seed, CancellationToken token)
    {
        await SeedingHelpers.UpdateUserAsync(fixture, seed.EvaluatorEmail, seed.ClubId, token, firstName: "Alex", lastName: "Morgan");
        using var client = fixture.CreateNovaHttpClient();
        var samEmail = SeedingHelpers.UniqueEmail("notebook-sam");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, samEmail, EvaluationSeed.Password, token);
        await SeedingHelpers.UpdateUserAsync(fixture, samEmail, seed.ClubId, token, firstName: "Sam", lastName: "Chen");
        await using var db = fixture.CreateAdminContext();
        return await db.Users.Where(user => user.Email == samEmail).Select(user => user.Id).SingleAsync(token);
    }

    private static async Task PrepareCampaignAndPriorPlacementAsync(NovaAppHostFixture fixture, SeededEvaluationWorkspace seed, long participantId, CancellationToken token)
    {
        var teamId = await SeedingHelpers.InsertTeamAsync(fixture, seed.ClubId, seed.AdminEmail, "North U16", 2030, token);
        await using var db = fixture.CreateAdminContext();
        var campaign = await db.Campaigns.Include(item => item.Season).SingleAsync(item => item.CampaignId == seed.CampaignId, token);
        var priorSequence = campaign.SeasonOpeningSequence!.Value;
        campaign.Name = "Fall evaluation";
        campaign.Season.Name = "2026–27 Season";
        campaign.Season.StartDate = new(2026, 8, 1);
        campaign.Season.EndDate = new(2027, 7, 31);
        campaign.StartDate = new(2026, 9, 12);
        campaign.EndDate = new(2026, 9, 26);
        campaign.SeasonOpeningSequence = priorSequence + 1;
        campaign.InitialEnrolledPlayerCount = 60;
        campaign.InitialActiveTeamCount = 1;
        var selected = await db.PlayerCampaignAssignments.Include(item => item.Player).SingleAsync(item => item.PlayerCampaignAssignmentId == participantId, token);
        selected.Player.FirstName = "Jordan";
        selected.Player.LastName = "Lee";
        selected.Player.GraduationYear = 2030;
        ClearDecision(selected);
        // Another participant still needs placement, making the campaign's readiness honestly incomplete.
        ClearDecision(await db.PlayerCampaignAssignments.SingleAsync(item => item.PlayerCampaignAssignmentId == seed.AssignmentIds[59], token));
        await db.SaveChangesAsync(token);
        var prior = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Early season placement",
            ClubId = seed.ClubId,
            SeasonId = campaign.SeasonId,
            CreatedById = seed.AdminUserId,
            Status = CampaignStatus.Closed,
            StartDate = new(2026, 9, 1),
            EndDate = new(2026, 9, 10),
            OpenedAt = new(2026, 9, 1, 15, 0, 0, TimeSpan.Zero),
            ClosedAt = new(2026, 9, 10, 15, 0, 0, TimeSpan.Zero),
            ClosedById = seed.AdminUserId,
            SeasonOpeningSequence = priorSequence,
            InitialEnrolledPlayerCount = 1,
            InitialActiveTeamCount = 1,
        };
        db.Campaigns.Add(prior);
        await db.SaveChangesAsync(token);
        db.PlayerCampaignAssignments.Add(new()
        {
            PlayerId = selected.PlayerId,
            CampaignId = prior.CampaignId,
            ClubId = seed.ClubId,
            CreatedById = seed.AdminUserId,
            TeamId = teamId,
            PlacementOutcome = PlacementOutcome.Assigned,
            DecisionRecordedAt = prior.ClosedAt,
            DecisionRecordedById = seed.AdminUserId,
            DecisionActorDisplayName = "Alice Author",
        });
        await db.SaveChangesAsync(token);
    }

    private static void ClearDecision(PlayerCampaignAssignmentEntity participant)
    {
        participant.PlacementOutcome = PlacementOutcome.Undecided;
        participant.TeamId = null;
        participant.DecisionRecordedAt = null;
        participant.DecisionRecordedById = null;
        participant.DecisionActorDisplayName = null;
    }

    private static async Task PrepareEvidenceAsync(NovaAppHostFixture fixture, SeededEvaluationWorkspace seed, long participantId, long samId, CancellationToken token)
    {
        await using var db = fixture.CreateAdminContext();
        var tags = await db.PlayerTags.Where(tag => tag.ClubId == seed.ClubId && tag.LifecycleStatus == LifecycleStatus.Active).OrderBy(tag => tag.PlayerTagId).ToListAsync(token);
        tags.Count.ShouldBe(2);
        for (var index = 0; index < tags.Count; index++)
        {
            tags[index].Name = index == 0 ? "Strong" : "Good awareness";
            tags[index].NormalizedName = CollaborativeTagPolicy.NormalizeKey(tags[index].Name);
            tags[index].Color = CollaborativeTagPolicy.DefaultColor;
            db.CampaignTagApplications.Add(new() { ClubId = seed.ClubId, CreatedById = seed.EvaluatorUserId, AuthorDisplayName = "Alex Morgan", PlayerCampaignAssignmentId = participantId, PlayerTagId = tags[index].PlayerTagId, CreationOperationId = Guid.NewGuid() });
        }
        var alex = Note(seed.ClubId, participantId, seed.EvaluatorUserId, "Alex Morgan", "Keeps looking for passing options.");
        var sam = Note(seed.ClubId, participantId, samId, "Sam Chen", "Confident receiving under pressure.");
        var older = Note(seed.ClubId, participantId, seed.EvaluatorUserId, "Alex Morgan", "Earlier observation: checks space before receiving.");
        db.Notes.AddRange(alex, sam, older);
        await db.SaveChangesAsync(token);
        // Audit stamping runs at insert; historical fixture timestamps are set afterward as real stored data.
        // Store UTC instants for the requested 10:42 and 10:37 local browser/server display times.
        var latestTime = ObservationTime(42);
        var previousTime = ObservationTime(37);
        var oldestTime = ObservationTime(20);
        await db.Notes.Where(note => note.NoteId == alex.NoteId).ExecuteUpdateAsync(set => set.SetProperty(note => note.CreatedAt, latestTime), token);
        await db.Notes.Where(note => note.NoteId == sam.NoteId).ExecuteUpdateAsync(set => set.SetProperty(note => note.CreatedAt, previousTime), token);
        await db.Notes.Where(note => note.NoteId == older.NoteId).ExecuteUpdateAsync(set => set.SetProperty(note => note.CreatedAt, oldestTime), token);
    }

    private static NoteEntity Note(long clubId, long participantId, long authorId, string authorDisplayName, string content) => new()
    {
        ClubId = clubId,
        PlayerCampaignAssignmentId = participantId,
        CreatedById = authorId,
        AuthorDisplayName = authorDisplayName,
        Content = content,
        CreationOperationId = Guid.NewGuid(),
        Version = Guid.NewGuid(),
    };

    private static DateTimeOffset ObservationTime(int minute) => new DateTimeOffset(new DateTime(2026, 9, 10, 10, minute, 0, DateTimeKind.Local)).ToUniversalTime();
}
