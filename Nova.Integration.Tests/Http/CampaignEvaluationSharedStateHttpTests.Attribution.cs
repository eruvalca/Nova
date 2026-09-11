using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.SharedKernel.Features.Campaigns;
using Shouldly;

namespace Nova.Integration.Tests.Http;

public sealed partial class CampaignEvaluationSharedStateHttpTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedEvidencePreservesOriginalActorAfterRenameAndDepartureOrDeletionAsync(bool deleteAuthor)
    {
        var token = TestContext.Current.CancellationToken;
        var (authorClient, observerClient, _, campaignId, assignmentId) = await SeedTwoMemberClubWithCampaignAsync("historical-actor", token);
        using var author = authorClient;
        using var observer = observerClient;
        using var addResponse = await author.PostAsJsonAsync(CampaignEndpoints.AddEvaluationNote,
            new AddEvaluationNoteInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = assignmentId, Content = "Original evidence" }, token);
        addResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var added = await addResponse.Content.ReadFromJsonAsync<EvaluationNoteMutationSuccess>(token);
        using var applyResponse = await author.PostAsJsonAsync(CampaignEndpoints.CreateAndApplyCampaignTag,
            new CreateAndApplyCampaignTagInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = assignmentId, Label = "Strong vision" }, token);
        applyResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var applied = await applyResponse.Content.ReadFromJsonAsync<CampaignTagApplicationMutationSuccess>(token);
        await AssertSnapshotsAndRenameActorAsync(added.NoteId, applied.CampaignTagApplicationId, token);
        using var editResponse = await author.PutAsJsonAsync(CampaignEndpoints.EditEvaluationNoteUrl(added.NoteId),
            new PutEvaluationNoteInput { OperationId = Guid.CreateVersion7(), ExpectedVersion = added.Version, Content = "Revised evidence" }, token);
        editResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var db = fixture.CreateAdminContext())
        {
            var actorId = await db.Notes.Where(note => note.NoteId == added.NoteId).Select(note => note.CreatedById).SingleAsync(token);
            var actor = await db.Users.SingleAsync(user => user.Id == actorId, token);
            if (deleteAuthor) { db.Users.Remove(actor); }
            else { actor.ClubId = null; }
            await db.SaveChangesAsync(token);
        }
        using var duplicateResponse = await observer.PostAsJsonAsync(CampaignEndpoints.CreateAndApplyCampaignTag,
            new CreateAndApplyCampaignTagInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = assignmentId, Label = "STRONG VISION" }, token);
        duplicateResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var duplicate = await duplicateResponse.Content.ReadFromJsonAsync<CampaignTagApplicationMutationSuccess>(token);
        duplicate.AlreadyApplied.ShouldBeTrue();
        duplicate.CampaignTagApplicationId.ShouldBe(applied.CampaignTagApplicationId);
        using var detailResponse = await observer.GetAsync(new Uri(CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId), UriKind.Relative), token);
        var detail = await EvaluationEvidenceHttpTestSupport.ReadEvaluationEvidenceAsync(detailResponse, observer, token);
        var projectedNote = detail.Notes.ShouldHaveSingleItem();
        projectedNote.AuthorDisplayName.ShouldBe("Alice Author");
        projectedNote.Content.ShouldBe("Revised evidence");
        projectedNote.CanEdit.ShouldBeFalse();
        projectedNote.CanDelete.ShouldBeFalse();
        var projectedApplication = detail.AppliedTags.ShouldHaveSingleItem();
        projectedApplication.ActorDisplayName.ShouldBe("Alice Author");
        projectedApplication.CanRemove.ShouldBeFalse();
    }

    private async Task AssertSnapshotsAndRenameActorAsync(long noteId, long applicationId, CancellationToken token)
    {
        using var db = fixture.CreateAdminContext();
        var note = await db.Notes.SingleAsync(row => row.NoteId == noteId, token);
        var application = await db.CampaignTagApplications.SingleAsync(row => row.CampaignTagApplicationId == applicationId, token);
        note.AuthorDisplayName.ShouldBe("Alice Author");
        application.AuthorDisplayName.ShouldBe("Alice Author");
        var actor = await db.Users.SingleAsync(user => user.Id == note.CreatedById, token);
        actor.FirstName = "Changed";
        actor.LastName = "Name";
        await db.SaveChangesAsync(token);
    }
}
