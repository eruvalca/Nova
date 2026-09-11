using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Shouldly;

namespace Nova.Integration.Tests.Http;

internal sealed record EvaluationEvidenceSnapshot(CampaignParticipantDetailDto Identity,
    IReadOnlyList<CampaignParticipantNoteDto> Notes, IReadOnlyList<CampaignParticipantTagApplicationDto> AppliedTags)
{
    public long PlayerCampaignAssignmentId => Identity.PlayerCampaignAssignmentId;
    public long PlayerId => Identity.PlayerId;
    public string DisplayName => Identity.DisplayName;
    public int GraduationYear => Identity.GraduationYear;
    public int? TryoutNumber => Identity.TryoutNumber;
    public PlacementOutcome PlacementOutcome => Identity.PlacementOutcome;
    public CampaignParticipantTeamSummaryDto? Team => Identity.Team;
    public DateTimeOffset CreatedAt => Identity.CreatedAt;
    public DateTimeOffset? ModifiedAt => Identity.ModifiedAt;
    public CampaignStatus CampaignStatus => Identity.CampaignStatus;
    public Guid ConcurrencyToken => Identity.ConcurrencyToken;
    public CampaignParticipantCapabilitiesDto Capabilities => Identity.Capabilities;
}

internal static class EvaluationEvidenceHttpTestSupport
{
    public static async Task<HttpResponseMessage> DeleteEvaluationNoteForTestAsync(HttpClient client, NovaAppHostFixture fixture, Uri url, CancellationToken token)
    {
        var noteId = long.Parse(url.OriginalString.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);
        await using var db = fixture.CreateAdminContext();
        var version = await db.Notes.Where(note => note.NoteId == noteId).Select(note => (Guid?)note.Version).SingleOrDefaultAsync(token) ?? Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Delete, url)
        {
            Content = JsonContent.Create(new DeleteEvaluationNoteInput { OperationId = Guid.CreateVersion7(), NoteId = noteId, ExpectedVersion = version })
        };
        return await client.SendAsync(request, token);
    }

    public static async Task<HttpResponseMessage> RemoveEvaluationTagForTestAsync(HttpClient client, Uri url, CancellationToken token)
    {
        var applicationId = long.Parse(url.OriginalString.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Delete, url)
        {
            Content = JsonContent.Create(new RemoveCampaignTagApplicationInput { OperationId = Guid.CreateVersion7(), CampaignTagApplicationId = applicationId })
        };
        return await client.SendAsync(request, token);
    }

    /// <summary>Reads identity and each independently bounded evidence endpoint; never aggregates continuation pages.</summary>
    public static async Task<EvaluationEvidenceSnapshot> ReadEvaluationEvidenceAsync(HttpResponseMessage response, HttpClient client, CancellationToken token)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var identity = await response.Content.ReadFromJsonAsync<CampaignParticipantDetailDto>(token);
        identity.ShouldNotBeNull();
        var segments = response.RequestMessage!.RequestUri!.AbsolutePath.Split('/');
        var participantIndex = Array.IndexOf(segments, "participants");
        var input = new GetEvaluationHistoryInput { CampaignId = long.Parse(segments[participantIndex - 1], System.Globalization.CultureInfo.InvariantCulture), PlayerCampaignAssignmentId = identity.PlayerCampaignAssignmentId };
        using var notesResponse = await client.GetAsync(new Uri(CampaignEndpoints.EvaluationNotesUrl(input), UriKind.Relative), token);
        using var applicationsResponse = await client.GetAsync(new Uri(CampaignEndpoints.EvaluationApplicationsUrl(input), UriKind.Relative), token);
        notesResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        applicationsResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var notes = await notesResponse.Content.ReadFromJsonAsync<EvaluationHistoryPage<CampaignParticipantNoteDto>>(token);
        var applications = await applicationsResponse.Content.ReadFromJsonAsync<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>(token);
        notes.ShouldNotBeNull();
        applications.ShouldNotBeNull();
        notes.Items.Count.ShouldBeLessThanOrEqualTo(20);
        applications.Items.Count.ShouldBeLessThanOrEqualTo(20);
        return new EvaluationEvidenceSnapshot(identity, notes.Items, applications.Items);
    }
}
