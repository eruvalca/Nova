using Microsoft.EntityFrameworkCore;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Features.Campaigns;

public sealed partial class EvaluationNoteServiceTests
{
    [Theory]
    [InlineData(-25, false)]
    [InlineData(-24, false)]
    [InlineData(-23, true)]
    public async Task RecoveryWindowAcceptsOnlyOperationsYoungerThanTwentyFourHoursAsync(int hoursAgo, bool accepted)
    {
        ActAs(ClubAMember1Id, ClubAId);
        var input = new AddEvaluationNoteInput { OperationId = Guid.CreateVersion7(DateTimeOffset.UtcNow.AddHours(hoursAgo)), PlayerCampaignAssignmentId = _assignmentId, Content = "Boundary observation" };
        var result = await CreateService().AddAsync(input, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBe(accepted);
        if (!accepted)
        {
            result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        }
        await using var db = _harness.CreateAdminContext();
        (await db.Notes.CountAsync(note => note.CreationOperationId == input.OperationId, TestContext.Current.CancellationToken)).ShouldBe(accepted ? 1 : 0);
    }

    [Fact]
    public async Task CommittedReceiptSurvivesParticipantAggregateDeletionAsync()
    {
        ActAs(ClubAMember1Id, ClubAId);
        var service = CreateService();
        var input = new AddEvaluationNoteInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = _assignmentId, Content = "Original observation" };
        var added = await service.AddAsync(input, TestContext.Current.CancellationToken);
        added.IsSuccess.ShouldBeTrue();
        await using (var db = _harness.CreateAdminContext())
        {
            var assignment = await db.PlayerCampaignAssignments.SingleAsync(assignment => assignment.PlayerCampaignAssignmentId == _assignmentId, TestContext.Current.CancellationToken);
            db.PlayerCampaignAssignments.Remove(assignment);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var replayed = await service.AddAsync(input, TestContext.Current.CancellationToken);
        replayed.IsSuccess.ShouldBeTrue();
        replayed.Value.ShouldBe(added.Value);
        var rejectedId = Guid.CreateVersion7();
        var newRequest = await service.AddAsync(input with { OperationId = rejectedId }, TestContext.Current.CancellationToken);
        newRequest.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
        EvaluationMutationRejection.IsNotCommitted(newRequest.Problem, rejectedId).ShouldBeTrue();
        await using var verify = _harness.CreateAdminContext();
        (await verify.Notes.AnyAsync(note => note.NoteId == added.Value.NoteId, TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await verify.EvaluationMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task ReplayReturnsOriginalReceiptAfterNoteEditedAndCampaignClosedAsync()
    {
        ActAs(ClubAMember1Id, ClubAId);
        var service = CreateService();
        var input = new AddEvaluationNoteInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = _assignmentId, Content = "Original evidence" };
        var added = await service.AddAsync(input, TestContext.Current.CancellationToken);
        added.IsSuccess.ShouldBeTrue();
        var edited = await service.EditAsync(new EditEvaluationNoteInput
        {
            OperationId = Guid.CreateVersion7(),
            NoteId = added.Value.NoteId,
            ExpectedVersion = added.Value.Version,
            Content = "Later evidence"
        }, TestContext.Current.CancellationToken);
        edited.IsSuccess.ShouldBeTrue();
        using (var db = _harness.CreateAdminContext())
        {
            var campaign = (await db.PlayerCampaignAssignments.Where(assignment => assignment.PlayerCampaignAssignmentId == _assignmentId).Select(assignment => assignment.Campaign).SingleAsync(TestContext.Current.CancellationToken));
            campaign.Status = Nova.SharedKernel.Enums.CampaignStatus.Closed;
            campaign.ClosedAt = DateTimeOffset.UtcNow;
            campaign.ClosedById = ClubAMember1Id;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var replayed = await service.AddAsync(input, TestContext.Current.CancellationToken);

        replayed.IsSuccess.ShouldBeTrue();
        replayed.Value.ShouldBe(added.Value);
        using var verify = _harness.CreateAdminContext();
        (await verify.Notes.SingleAsync(note => note.NoteId == added.Value.NoteId, TestContext.Current.CancellationToken)).Content.ShouldBe("Later evidence");
        (await verify.Notes.CountAsync(note => note.CreationOperationId == input.OperationId, TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleVersionRejectsEditAndDeleteWithoutChangingNewerNoteAsync(bool delete)
    {
        ActAs(ClubAMember1Id, ClubAId);
        var service = CreateService();
        var originalVersion = await NoteVersionAsync(_existingNoteId);
        var newer = await service.EditAsync(new EditEvaluationNoteInput
        {
            OperationId = Guid.CreateVersion7(),
            NoteId = _existingNoteId,
            ExpectedVersion = originalVersion,
            Content = "Newer shared evidence"
        }, TestContext.Current.CancellationToken);
        newer.IsSuccess.ShouldBeTrue();
        newer.Value.Version.ShouldNotBe(originalVersion);

        var rejectedId = Guid.CreateVersion7();
        var stale = delete
            ? await service.DeleteAsync(new DeleteEvaluationNoteInput { OperationId = rejectedId, NoteId = _existingNoteId, ExpectedVersion = originalVersion }, TestContext.Current.CancellationToken)
            : await service.EditAsync(new EditEvaluationNoteInput { OperationId = rejectedId, NoteId = _existingNoteId, ExpectedVersion = originalVersion, Content = "Stale replacement" }, TestContext.Current.CancellationToken);

        stale.IsProblem.ShouldBeTrue();
        stale.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        EvaluationMutationRejection.IsNotCommitted(stale.Problem, rejectedId).ShouldBeTrue();
        using var verify = _harness.CreateAdminContext();
        var persisted = (await verify.Notes.SingleAsync(note => note.NoteId == _existingNoteId, TestContext.Current.CancellationToken));
        persisted.Content.ShouldBe("Newer shared evidence");
        persisted.Version.ShouldBe(newer.Value.Version);
        (await verify.EvaluationMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyExpectedVersionRejectsEditAndDeleteBeforeWritingAsync(bool delete)
    {
        ActAs(ClubAMember1Id, ClubAId);
        var service = CreateService();
        var result = delete
            ? await service.DeleteAsync(new DeleteEvaluationNoteInput { OperationId = Guid.CreateVersion7(), NoteId = _existingNoteId, ExpectedVersion = Guid.Empty }, TestContext.Current.CancellationToken)
            : await service.EditAsync(new EditEvaluationNoteInput { OperationId = Guid.CreateVersion7(), NoteId = _existingNoteId, ExpectedVersion = Guid.Empty, Content = "Replacement" }, TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.ShouldNotBeNull();
        result.Problem.Errors.ShouldContainKey(nameof(EditEvaluationNoteInput.ExpectedVersion));
        using var verify = _harness.CreateAdminContext();
        (await verify.Notes.SingleAsync(note => note.NoteId == _existingNoteId, TestContext.Current.CancellationToken)).Content.ShouldBe("Initial note.");
        (await verify.EvaluationMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task ReusingOperationWithChangedPayloadOrDifferentActorReturnsConflictAsync()
    {
        ActAs(ClubAMember1Id, ClubAId);
        var service = CreateService();
        var input = new AddEvaluationNoteInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = _assignmentId, Content = "Original" };
        (await service.AddAsync(input, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        var changed = await service.AddAsync(input with { Content = "Changed" }, TestContext.Current.CancellationToken);
        changed.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        ActAs(ClubAMember2Id, ClubAId);
        var otherActor = await service.AddAsync(input, TestContext.Current.CancellationToken);
        otherActor.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        using var verify = _harness.CreateAdminContext();
        (await verify.Notes.CountAsync(note => note.CreationOperationId == input.OperationId, TestContext.Current.CancellationToken)).ShouldBe(1);
        (await verify.Notes.SingleAsync(note => note.CreationOperationId == input.OperationId, TestContext.Current.CancellationToken)).CreatedById.ShouldBe(ClubAMember1Id);
    }

    [Theory]
    [InlineData("ffffffff-ffff-7000-8000-000000000000")]
    [InlineData("00000000-0000-7000-8000-000000000001")]
    [InlineData("00000000-0000-4000-8000-000000000001")]
    public async Task InvalidOrExpiredOperationIdentityReturnsConflictWithoutDispatchAsync(string operationId)
    {
        ActAs(ClubAMember1Id, ClubAId);
        var input = new AddEvaluationNoteInput { OperationId = Guid.Parse(operationId), PlayerCampaignAssignmentId = _assignmentId, Content = "Expired" };
        var result = await CreateService().AddAsync(input, TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldNotBeNull();
        result.Problem.Detail.ShouldContain("24-hour");
        using var verify = _harness.CreateAdminContext();
        (await verify.Notes.AnyAsync(note => note.CreationOperationId == input.OperationId, TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await verify.EvaluationMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task RemovedMembershipRejectsReplayEvenWithStaleAuthenticatedScopeAsync()
    {
        ActAs(ClubAMember1Id, ClubAId);
        var service = CreateService();
        var input = new AddEvaluationNoteInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = _assignmentId, Content = "Original" };
        (await service.AddAsync(input, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        using (var db = _harness.CreateAdminContext())
        {
            (await db.Users.SingleAsync(user => user.Id == ClubAMember1Id, TestContext.Current.CancellationToken)).ClubId = null;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var result = await service.AddAsync(input, TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task DeleteReplayRecoversOriginalReceiptAfterAggregateDeletionAsync()
    {
        ActAs(ClubAMember1Id, ClubAId);
        var input = new DeleteEvaluationNoteInput { OperationId = Guid.CreateVersion7(), NoteId = _existingNoteId, ExpectedVersion = await NoteVersionAsync(_existingNoteId) };
        var service = CreateService();
        var deleted = await service.DeleteAsync(input, TestContext.Current.CancellationToken);
        deleted.IsSuccess.ShouldBeTrue();
        var replay = await service.DeleteAsync(input, TestContext.Current.CancellationToken);
        replay.IsSuccess.ShouldBeTrue();
        replay.Value.ShouldBe(deleted.Value);
        using var verify = _harness.CreateAdminContext();
        (await verify.Notes.AnyAsync(note => note.NoteId == _existingNoteId, TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await verify.EvaluationMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }
    private async Task<Guid> NoteVersionAsync(long noteId)
    {
        await using var db = _harness.CreateAdminContext();
        return await db.Notes.Where(note => note.NoteId == noteId).Select(note => note.Version).SingleAsync(TestContext.Current.CancellationToken);
    }

}
