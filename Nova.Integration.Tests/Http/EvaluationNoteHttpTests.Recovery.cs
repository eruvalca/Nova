using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Integration.Tests.Http;

public sealed partial class EvaluationNoteHttpTests
{
    [Fact]
    public async Task VersionedHttpEditAndDeleteRejectStaleContentAndRecoverOriginalReceiptsAsync()
    {
        var token = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("note-version-replay");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, token);
        await UpdateUserAsync(email, clubId: null, token);
        var club = await CreateClubAsync(client, token);
        await RefreshClubMembershipCookieAsync(client, token);
        var (_, assignmentId) = await SeedEvaluationNoteDataAsync(club.ClubId, email, token);
        var add = ValidAddInput(assignmentId, "Original shared observation");
        using var addedResponse = await client.PostAsJsonAsync(CampaignEndpoints.AddEvaluationNote, add, token);
        addedResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var added = await addedResponse.Content.ReadFromJsonAsync<EvaluationNoteMutationSuccess>(token);
        added.Version.ShouldNotBe(Guid.Empty);
        added.Receipt.OperationId.ShouldBe(add.OperationId);
        var edit = ValidEditInput(added.Version, "Newer shared observation");
        using var editedResponse = await client.PutAsJsonAsync(CampaignEndpoints.EditEvaluationNoteUrl(added.NoteId), edit, token);
        editedResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var edited = await editedResponse.Content.ReadFromJsonAsync<EvaluationNoteMutationSuccess>(token);
        edited.Version.ShouldNotBe(added.Version);
        var staleInput = ValidEditInput(added.Version, "Stale overwrite");
        using var staleEdit = await client.PutAsJsonAsync(CampaignEndpoints.EditEvaluationNoteUrl(added.NoteId), staleInput, token);
        staleEdit.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var staleProblem = await JsonDocument.ParseAsync(await staleEdit.Content.ReadAsStreamAsync(token), cancellationToken: token);
        staleProblem.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        staleProblem.RootElement.GetProperty(EvaluationMutationRejection.OperationIdExtension).GetGuid().ShouldBe(staleInput.OperationId);
        using var staleRequest = new HttpRequestMessage(HttpMethod.Delete, CampaignEndpoints.DeleteEvaluationNoteUrl(added.NoteId))
        {
            Content = JsonContent.Create(new DeleteEvaluationNoteInput { OperationId = Guid.CreateVersion7(), NoteId = added.NoteId, ExpectedVersion = added.Version })
        };
        using var staleDelete = await client.SendAsync(staleRequest, token);
        staleDelete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var replayResponse = await client.PutAsJsonAsync(CampaignEndpoints.EditEvaluationNoteUrl(added.NoteId), edit, token);
        replayResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await replayResponse.Content.ReadFromJsonAsync<EvaluationNoteMutationSuccess>(token)).ShouldBe(edited);
        using var mismatchResponse = await client.PutAsJsonAsync(CampaignEndpoints.EditEvaluationNoteUrl(added.NoteId), edit with { Content = "Different payload" }, token);
        mismatchResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        EvaluationMutationRejection.IsNotCommitted(await mismatchResponse.ToServiceProblemAsync(token), edit.OperationId).ShouldBeFalse();
        await using var verify = fixture.CreateAdminContext();
        var note = await verify.Notes.SingleAsync(note => note.NoteId == added.NoteId, token);
        note.Content.ShouldBe("Newer shared observation");
        note.Version.ShouldBe(edited.Version);
        (await verify.EvaluationMutationReceipts.CountAsync(receipt => receipt.ClubId == club.ClubId, token)).ShouldBe(4);
    }

    [Fact]
    public async Task CommittedHttpOperationSurvivesUnmarkedMembershipDenialAndRecoversOriginalReceiptAsync()
    {
        var token = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("note-membership-recovery");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, token);
        await UpdateUserAsync(email, clubId: null, token);
        var club = await CreateClubAsync(client, token);
        await RefreshClubMembershipCookieAsync(client, token);
        var (_, assignment) = await SeedEvaluationNoteDataAsync(club.ClubId, email, token);
        var input = ValidAddInput(assignment, "Retain original operation while membership is unavailable");
        using var committedResponse = await client.PostAsJsonAsync(CampaignEndpoints.AddEvaluationNote, input, token);
        committedResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var original = await committedResponse.Content.ReadFromJsonAsync<EvaluationNoteMutationSuccess>(token);

        // Deliberately retain the existing authenticated cookie to exercise the persisted membership boundary.
        await UpdateUserAsync(email, clubId: null, token);
        using var denied = await client.PostAsJsonAsync(CampaignEndpoints.AddEvaluationNote, input, token);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var denial = await denied.ToServiceProblemAsync(token);
        EvaluationMutationRejection.IsNotCommitted(denial, input.OperationId).ShouldBeFalse();
        denial.Extensions!.ContainsKey(EvaluationMutationRejection.OperationIdExtension).ShouldBeFalse();

        await UpdateUserAsync(email, club.ClubId, token);
        using var recovered = await client.PostAsJsonAsync(CampaignEndpoints.AddEvaluationNote, input, token);
        recovered.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await recovered.Content.ReadFromJsonAsync<EvaluationNoteMutationSuccess>(token)).ShouldBe(original);
        await using var verify = fixture.CreateAdminContext();
        (await verify.Notes.CountAsync(note => note.PlayerCampaignAssignmentId == assignment, token)).ShouldBe(1);
        (await verify.EvaluationMutationReceipts.CountAsync(receipt => receipt.ClubId == club.ClubId && receipt.OperationId == input.OperationId, token)).ShouldBe(1);
    }
}
