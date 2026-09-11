using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Campaigns.Components;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignEvaluationPanelTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void EvaluationRendersIndependentCurrentIdentityCapabilities(bool add, bool apply, bool place)
    {
        SetEvaluationCapabilities(add, apply, place);
        var cut = Panel(new() { ParticipantId = 301 });

        cut.FindAll(".evaluation-save").Count.ShouldBe(add ? 1 : 0);
        cut.FindAll("button").Any(button => string.Equals(button.TextContent.Trim(), "Add a trait", StringComparison.Ordinal)).ShouldBe(apply);
        cut.FindAll(".evaluation-place").Count.ShouldBe(place ? 1 : 0);
        _notes.ReceivedCalls().ShouldBeEmpty();
        _tags.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OwnedNoteMutationDoesNotRequireAddNoteCapabilityAsync(bool delete)
    {
        SetEvaluationCapabilities(false, false, false);
        var note = Note() with { CanEdit = true, CanDelete = true };
        SetCapabilityNote(note);
        _notes.EditAsync(Arg.Any<EditEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(new EvaluationNoteMutationSuccess(1, Guid.NewGuid(),
                new(call.Arg<EditEvaluationNoteInput>().OperationId, 301, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24))))));
        _notes.DeleteAsync(Arg.Any<DeleteEvaluationNoteInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(DeleteReceipt(call.Arg<DeleteEvaluationNoteInput>())));
        var cut = Panel(new() { ParticipantId = 301 });

        if (delete)
        {
            await Button(cut, "Delete").ClickAsync(new MouseEventArgs());
            await Button(cut, "Delete note").ClickAsync(new MouseEventArgs());
            await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Note deleted."));
            _ = _notes.Received(1).DeleteAsync(Arg.Is<DeleteEvaluationNoteInput>(input => input.ExpectedVersion == note.Version), Arg.Any<CancellationToken>());
        }
        else
        {
            await Button(cut, "Edit").ClickAsync(new MouseEventArgs());
            await cut.Find("#edit-note-1").InputAsync(new ChangeEventArgs { Value = "Corrected original observation" });
            await Button(cut, "Save changes").ClickAsync(new MouseEventArgs());
            await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Note updated."));
            _ = _notes.Received(1).EditAsync(Arg.Is<EditEvaluationNoteInput>(input => input.ExpectedVersion == note.Version && string.Equals(input.Content, "Corrected original observation", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
        }
        cut.FindAll(".evaluation-save").ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CapturedNoteCallbacksCannotMutateAfterLifecycleOrNoteCapabilityRevocationAsync(bool delete, bool close)
    {
        var note = Note() with { CanEdit = true, CanDelete = true };
        SetCapabilityNote(note);
        var cut = Panel(new() { ParticipantId = 301 });
        var item = cut.FindComponent<EvaluationNoteItem>().Instance;
        var begin = delete ? item.OnDelete : item.OnEdit;
        await cut.InvokeAsync(() => begin.InvokeAsync());
        if (!delete) { await cut.Find("#edit-note-1").InputAsync(new ChangeEventArgs { Value = "Retain after revocation" }); }
        var submit = delete ? item.OnConfirmDelete : item.OnSave;
        var change = item.OnDraftChanged;
        var review = item.OnReviewVersion;
        SetCapabilityNote(note with { CanEdit = false, CanDelete = false });

        // A lifecycle refresh loads authoritative evidence while preserving an unsubmitted edit.
        await cut.InvokeAsync(() => cut.Render(parameters => parameters.Add(component => component.Status, CampaignStatus.Closed)));
        if (!close) { await cut.InvokeAsync(() => cut.Render(parameters => parameters.Add(component => component.Status, CampaignStatus.Active))); }
        var writesBeforeStaleCallbacks = _storage.Writes.Count;
        await cut.InvokeAsync(() => begin.InvokeAsync());
        await cut.InvokeAsync(() => change.InvokeAsync(new ChangeEventArgs { Value = "Stale unauthorized replacement" }));
        await cut.InvokeAsync(() => review.InvokeAsync());
        await cut.InvokeAsync(() => submit.InvokeAsync());

        _notes.ReceivedCalls().ShouldBeEmpty();
        _storage.Writes.Count.ShouldBe(writesBeforeStaleCallbacks);
        cut.FindAll("button").Any(button => string.Equals(button.TextContent.Trim(), "Save changes", StringComparison.Ordinal)).ShouldBeFalse();
        cut.FindAll("button").Any(button => string.Equals(button.TextContent.Trim(), "Delete note", StringComparison.Ordinal)).ShouldBeFalse();
        if (!delete) { cut.Find("#edit-note-1").GetAttribute("value").ShouldBe("Retain after revocation"); }
    }

    [Fact]
    public async Task ApplicationOwnerCanRemoveWithoutApplyCapabilityAsync()
    {
        SetEvaluationCapabilities(false, false, false);
        _evidence.GetApplicationsAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>(new EvaluationHistoryPage<CampaignParticipantTagApplicationDto>(
                [new(1, 11, "Strong", "#65743A", false, "Coach Rivera", DateTimeOffset.UtcNow, true)], null))));
        _tags.RemoveAsync(Arg.Any<RemoveCampaignTagApplicationInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<CampaignTagApplicationMutationSuccess>(new CampaignTagApplicationMutationSuccess(1, 11, false,
                new(call.Arg<RemoveCampaignTagApplicationInput>().OperationId, 301, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24))))));
        var cut = Panel(new() { ParticipantId = 301 });

        await Button(cut, "Remove").ClickAsync(new MouseEventArgs());
        await Button(cut, "Confirm removal").ClickAsync(new MouseEventArgs());

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Trait removed."));
        _ = _tags.Received(1).RemoveAsync(Arg.Is<RemoveCampaignTagApplicationInput>(input => input.CampaignTagApplicationId == 1), Arg.Any<CancellationToken>());
    }

    private void SetEvaluationCapabilities(bool add, bool apply, bool place) =>
        _participants.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(Identity(call.Arg<GetCampaignParticipantDetailInput>().PlayerCampaignAssignmentId)
                with
            { Capabilities = new(place, add, apply, false) })));

    private void SetCapabilityNote(CampaignParticipantNoteDto note) =>
        _evidence.GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(new EvaluationHistoryPage<CampaignParticipantNoteDto>([note], null))));
}
