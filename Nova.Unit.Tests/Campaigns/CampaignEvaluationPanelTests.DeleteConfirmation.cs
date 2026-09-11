using Bunit;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignEvaluationPanelTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DeleteConfirmationRetainsReviewedVersionUntilCanceledAndReopened(bool older, bool cancelAndReopen)
    {
        var history = ConfigureDeleteHistory(older);
        var inputs = new List<DeleteEvaluationNoteInput>();
        _notes.DeleteAsync(Arg.Any<DeleteEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var input = call.Arg<DeleteEvaluationNoteInput>();
            inputs.Add(input);
            history.Deleted = input.ExpectedVersion == history.CurrentVersion;
            return Task.FromResult(history.Deleted ? DeleteReceipt(input) : DeleteConflict());
        });
        var cut = Panel(new() { ParticipantId = 301 });
        OpenEvaluationDeleteAndRefreshHistory(cut, older);
        if (cancelAndReopen)
        {
            Button(cut, "Keep note").Click();
            Button(cut, "Delete").Click();
        }

        Button(cut, "Delete note").Click();

        cut.WaitForAssertion(() => inputs.Count.ShouldBe(1));
        inputs[0].NoteId.ShouldBe(1);
        inputs[0].ExpectedVersion.ShouldBe(cancelAndReopen ? history.CurrentVersion : history.ReviewedVersion);
        cut.Markup.ShouldNotContain("Delete this shared note?");
        if (cancelAndReopen)
        {
            cut.Markup.ShouldContain("Note deleted.");
            cut.Markup.ShouldNotContain("Concurrent revision V2");
        }
        else
        {
            cut.Markup.ShouldContain("The note changed after confirmation opened.");
            cut.Markup.ShouldContain("Concurrent revision V2");
            history.Deleted.ShouldBeFalse();
        }
    }

    [Fact]
    public void EvaluationOwnerChangeInvalidatesDeleteConfirmationBeforeAnotherDelete()
    {
        var history = ConfigureDeleteHistory(false);
        _notes.DeleteAsync(Arg.Any<DeleteEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(DeleteConflict()));
        var cut = Panel(new() { ParticipantId = 301 });
        Button(cut, "Delete").Click();
        cut.Markup.ShouldContain("Delete this shared note?");

        cut.Render(parameters => parameters.Add(component => component.AuthorityScope, "other:club-2:False")
            .Add(component => component.CaptureScope, "other:club-2"));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Concurrent revision V2"));
        cut.Markup.ShouldNotContain("Delete this shared note?");
        _ = _notes.DidNotReceive().DeleteAsync(Arg.Any<DeleteEvaluationNoteInput>(), Arg.Any<CancellationToken>());
        Button(cut, "Delete").Click();
        Button(cut, "Delete note").Click();
        _ = _notes.Received(1).DeleteAsync(Arg.Is<DeleteEvaluationNoteInput>(input => input.NoteId == 1 && input.ExpectedVersion == history.CurrentVersion), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void EvaluationClosingAndReopeningRequiresNewDeleteConfirmation()
    {
        var history = ConfigureDeleteHistory(false);
        _notes.DeleteAsync(Arg.Any<DeleteEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(DeleteConflict()));
        var cut = Panel(new() { ParticipantId = 301 });
        Button(cut, "Delete").Click();
        cut.Markup.ShouldContain("Delete this shared note?");

        cut.Render(parameters => parameters.Add(component => component.Status, CampaignStatus.Closed));
        cut.Markup.ShouldNotContain("Delete this shared note?");
        cut.Render(parameters => parameters.Add(component => component.Status, CampaignStatus.Active));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Concurrent revision V2"));
        cut.Markup.ShouldNotContain("Delete this shared note?");
        _ = _notes.DidNotReceive().DeleteAsync(Arg.Any<DeleteEvaluationNoteInput>(), Arg.Any<CancellationToken>());
        Button(cut, "Delete").Click();
        Button(cut, "Delete note").Click();
        _ = _notes.Received(1).DeleteAsync(Arg.Is<DeleteEvaluationNoteInput>(input => input.ExpectedVersion == history.CurrentVersion), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbiguousDeleteRetriesAndReloadKeepOriginalReviewedVersionAndOperation(bool older)
    {
        var history = ConfigureDeleteHistory(older);
        var inputs = new List<DeleteEvaluationNoteInput>();
        _notes.DeleteAsync(Arg.Any<DeleteEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            inputs.Add(call.Arg<DeleteEvaluationNoteInput>());
            return inputs.Count < 3 ? Task.FromException<ServiceResult<EvaluationNoteMutationSuccess>>(new HttpRequestException("Lost acknowledgement"))
                : Task.FromResult(DeleteConflict());
        });
        var cut = Panel(new() { ParticipantId = 301 });
        OpenEvaluationDeleteAndRefreshHistory(cut, older);
        Button(cut, "Delete note").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("outcome is not yet known"));
        Button(cut, "Retry original operation").Click();
        cut.WaitForAssertion(() => inputs.Count.ShouldBe(2));
        _storage.ReadSnapshot = _storage.Writes[^1];
        cut.Dispose();

        var restored = Panel(new() { ParticipantId = 301 });
        restored.WaitForAssertion(() => restored.Markup.ShouldContain("previous submission needs its receipt"));
        Button(restored, "Retry original operation").Click();

        restored.WaitForAssertion(() => inputs.Count.ShouldBe(3));
        inputs[0].ExpectedVersion.ShouldBe(history.ReviewedVersion);
        inputs[0].OperationId.ShouldNotBe(Guid.Empty);
        inputs[1].ShouldBe(inputs[0]);
        inputs[2].ShouldBe(inputs[0]);
        restored.Markup.ShouldContain("The note changed after confirmation opened.");
        restored.Markup.ShouldNotContain("Delete this shared note?");
        if (older) { Button(restored, "Show older notes").Click(); }
        restored.Markup.ShouldContain("Concurrent revision V2");
        history.Deleted.ShouldBeFalse();
    }

    private DeleteConfirmationHistory ConfigureDeleteHistory(bool older)
    {
        var history = new DeleteConfirmationHistory(older);
        _evidence.GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(history.Read(call.Arg<GetEvaluationHistoryInput>())));
        return history;
    }

    private static void OpenEvaluationDeleteAndRefreshHistory(Bunit.IRenderedComponent<Nova.UI.Features.Campaigns.Components.CampaignEvaluationPanel> cut, bool older)
    {
        cut.WaitForAssertion(() => cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse());
        Button(cut, "Show older notes").Click();
        cut.Find(older ? ".evaluation-older-notes" : ".evaluation-observations").TextContent.ShouldContain("Reviewed revision V1");
        Button(cut, "Delete").Click();
        Button(cut, "Load older notes").Click();
        Button(cut, "Retry notes").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Concurrent revision V2"));
        cut.Markup.ShouldContain("Delete this shared note?");
    }

    private static ServiceResult<EvaluationNoteMutationSuccess> DeleteConflict() => new(ServiceProblem.Conflict("The note changed after confirmation opened."));

    private static ServiceResult<EvaluationNoteMutationSuccess> DeleteReceipt(DeleteEvaluationNoteInput input) => new(new EvaluationNoteMutationSuccess(input.NoteId, input.ExpectedVersion,
        new(input.OperationId, 301, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24))));
}

/// <summary>Refreshes through the history retry UI while preserving the same target note identity.</summary>
internal sealed class DeleteConfirmationHistory(bool older)
{
    public Guid ReviewedVersion { get; } = Guid.NewGuid();
    public Guid CurrentVersion { get; } = Guid.NewGuid();
    public bool Deleted { get; set; }
    private int _reads;

    public ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>> Read(GetEvaluationHistoryInput input)
    {
        if (input.BeforeId is not null) { return ServiceProblem.ServerError("History continuation failed."); }
        var first = ++_reads == 1;
        var now = DateTimeOffset.UtcNow;
        var target = new CampaignParticipantNoteDto(1, first ? "Reviewed revision V1" : "Concurrent revision V2", "Original author", now, first ? null : now, false, true,
            first ? ReviewedVersion : CurrentVersion);
        CampaignParticipantNoteDto[] fillers = [new(2, "Neighbor two", "Other author", now, null, false, false, Guid.NewGuid()), new(3, "Neighbor three", "Other author", now, null, false, false, Guid.NewGuid())];
        IReadOnlyList<CampaignParticipantNoteDto> rows = fillers;
        if (!Deleted) { rows = older ? [.. fillers, target] : [target, .. fillers]; }
        return new EvaluationHistoryPage<CampaignParticipantNoteDto>(rows, first ? new(now.AddMinutes(-1), 3) : null);
    }
}
