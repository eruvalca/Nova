using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.SharedKernel.Features.Campaigns;
using Shouldly;

namespace Nova.Integration.Tests.Http;

public sealed partial class EvaluationNoteHttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvaluationHistoryRoutesAcceptEquivalentOffsetCursorsAsync(bool applications)
    {
        var token = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("history-offset");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, token);
        await UpdateUserAsync(email, clubId: null, token);
        var club = await CreateClubAsync(client, token);
        await RefreshClubMembershipCookieAsync(client, token);
        var (campaignId, assignmentId) = await SeedEvaluationNoteDataAsync(club.ClubId, email, token);
        await SeedBoundedEvidenceAsync(club.ClubId, assignmentId, token);
        var input = new GetEvaluationHistoryInput { CampaignId = campaignId, PlayerCampaignAssignmentId = assignmentId };
        using var firstResponse = await client.GetAsync(new Uri(HistoryRoute(input, applications), UriKind.Relative), token);
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var first = await firstResponse.Content.ReadFromJsonAsync<EvaluationHistoryPage<System.Text.Json.JsonElement>>(token);
        first.ShouldNotBeNull();
        first.Items.Count.ShouldBe(20);
        first.Next.ShouldNotBeNull();
        var continuation = input with { BeforeCreatedAt = first.Next.CreatedAt, BeforeId = first.Next.Id };
        using var utcResponse = await client.GetAsync(new Uri(HistoryRoute(continuation, applications), UriKind.Relative), token);
        using var offsetResponse = await client.GetAsync(new Uri(HistoryRoute(continuation with { BeforeCreatedAt = first.Next.CreatedAt.ToOffset(TimeSpan.FromMinutes(330)) }, applications), UriKind.Relative), token);
        utcResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        offsetResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var utc = await utcResponse.Content.ReadFromJsonAsync<EvaluationHistoryPage<System.Text.Json.JsonElement>>(token);
        var offset = await offsetResponse.Content.ReadFromJsonAsync<EvaluationHistoryPage<System.Text.Json.JsonElement>>(token);
        utc.ShouldNotBeNull();
        offset.ShouldNotBeNull();
        utc.Items.Count.ShouldBe(5);
        offset.Items.Select(item => item.GetRawText()).ShouldBe(utc.Items.Select(item => item.GetRawText()));
        offset.Next.ShouldBeNull();
    }

    private static string HistoryRoute(GetEvaluationHistoryInput input, bool applications) => applications
        ? CampaignEndpoints.EvaluationApplicationsUrl(input) : CampaignEndpoints.EvaluationNotesUrl(input);

    private async Task SeedBoundedEvidenceAsync(long clubId, long assignmentId, CancellationToken token)
    {
        await using var db = fixture.CreateAdminContext();
        var actor = await db.PlayerCampaignAssignments.Where(assignment => assignment.PlayerCampaignAssignmentId == assignmentId).Select(assignment => assignment.CreatedById).SingleAsync(token);
        for (var index = 0; index < 25; index++)
        {
            db.Notes.Add(new NoteEntity { CreationOperationId = Guid.NewGuid(), Content = $"Observation {index}", PlayerCampaignAssignmentId = assignmentId, ClubId = clubId, CreatedById = actor });
            var tag = new PlayerTagEntity { CreationOperationId = Guid.NewGuid(), Name = $"Trait {index}", NormalizedName = $"TRAIT {index}", Color = "#006B6B", ClubId = clubId, CreatedById = actor };
            db.CampaignTagApplications.Add(new CampaignTagApplicationEntity { CreationOperationId = Guid.NewGuid(), PlayerTag = tag, PlayerTagId = 0, PlayerCampaignAssignmentId = assignmentId, ClubId = clubId, CreatedById = actor });
        }
        await db.SaveChangesAsync(token);
    }
}
