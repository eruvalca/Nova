using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Features.Campaigns;
using Nova.Unit.Tests.Account;
using Shouldly;

namespace Nova.Unit.Tests.Features.Campaigns;

public sealed partial class EvaluationNoteServiceTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoteCreationSnapshotsAuthorAndEditAndMembershipChangesPreserveItAsync(bool deleteAuthor)
    {
        var token = TestContext.Current.CancellationToken;
        ActAs(ClubAMember1Id, ClubAId);
        var service = CreateService();
        var created = await service.AddAsync(new AddEvaluationNoteInput
        {
            OperationId = Guid.CreateVersion7(),
            PlayerCampaignAssignmentId = _assignmentId,
            Content = "Original observation"
        }, token);
        created.IsSuccess.ShouldBeTrue();
        using (var db = _harness.CreateAdminContext())
        {
            var saved = await db.Notes.SingleAsync(note => note.NoteId == created.Value.NoteId, token);
            saved.AuthorDisplayName.ShouldBe("Alice A");
            var author = await db.Users.SingleAsync(user => user.Id == ClubAMember1Id, token);
            author.FirstName = "Changed";
            author.LastName = "Name";
            await db.SaveChangesAsync(token);
        }
        var edited = await service.EditAsync(new EditEvaluationNoteInput
        {
            OperationId = Guid.CreateVersion7(),
            NoteId = created.Value.NoteId,
            ExpectedVersion = created.Value.Version,
            Content = "Edited observation"
        }, token);
        edited.IsSuccess.ShouldBeTrue();
        using (var db = _harness.CreateAdminContext())
        {
            var saved = await db.Notes.SingleAsync(note => note.NoteId == created.Value.NoteId, token);
            saved.AuthorDisplayName.ShouldBe("Alice A");
            saved.CreatedById.ShouldBe(ClubAMember1Id);
            var author = await db.Users.SingleAsync(user => user.Id == ClubAMember1Id, token);
            if (deleteAuthor) { db.Users.Remove(author); }
            else { author.ClubId = ClubBId; }
            await db.SaveChangesAsync(token);
        }
        ActAs(ClubAMember2Id, ClubAId);
        var queries = new CampaignEvaluationQueryService(new TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()), _harness.CurrentUser);
        using var metadata = _harness.CreateAdminContext();
        var campaignId = await metadata.PlayerCampaignAssignments.Where(row => row.PlayerCampaignAssignmentId == _assignmentId).Select(row => row.CampaignId).SingleAsync(token);
        var history = await queries.GetNotesAsync(new GetEvaluationHistoryInput { CampaignId = campaignId, PlayerCampaignAssignmentId = _assignmentId }, token);
        history.IsSuccess.ShouldBeTrue();
        var projected = history.Value.Items.Single(note => note.NoteId == created.Value.NoteId);
        projected.AuthorDisplayName.ShouldBe("Alice A");
        projected.Content.ShouldBe("Edited observation");
        projected.CanEdit.ShouldBeFalse();
        projected.CanDelete.ShouldBeFalse();
    }
}
