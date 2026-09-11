using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using NSubstitute;
using Shouldly;
using CampaignParticipantDrawerComponent = Nova.UI.Features.Campaigns.Components.CampaignParticipantDrawer;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignParticipantDrawerTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawerDeleteConfirmationRetainsReviewedVersionUntilCanceledAndReopened(bool cancelAndReopen)
    {
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        var history = ConfigureDrawerDeleteHistory(notes);
        var inputs = new List<DeleteEvaluationNoteInput>();
        notes.DeleteAsync(Arg.Any<DeleteEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var input = call.Arg<DeleteEvaluationNoteInput>();
            inputs.Add(input);
            history.Deleted = input.ExpectedVersion == history.CurrentVersion;
            return Task.FromResult(history.Deleted ? DrawerDeleteReceipt(input) : DrawerDeleteConflict());
        });
        var cut = DeleteDrawer();
        OpenDrawerDeleteAndRefreshHistory(cut);
        if (cancelAndReopen)
        {
            FindButtonByText(cut, "Cancel").Click();
            FindButtonByText(cut, "Delete").Click();
        }
        ConfirmDrawerDelete(cut);

        cut.WaitForAssertion(() => inputs.Count.ShouldBe(1));
        inputs[0].NoteId.ShouldBe(1);
        inputs[0].ExpectedVersion.ShouldBe(cancelAndReopen ? history.CurrentVersion : history.ReviewedVersion);
        cut.FindAll("#participant-drawer-note-delete-confirm-1").ShouldBeEmpty();
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

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawerOwnerOrLifecycleChangeRequiresNewDeleteConfirmation(bool lifecycle)
    {
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        var history = ConfigureDrawerDeleteHistory(notes);
        notes.DeleteAsync(Arg.Any<DeleteEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(DrawerDeleteConflict()));
        var cut = DeleteDrawer();
        FindButtonByText(cut, "Delete").Click();
        cut.Find("#participant-drawer-note-delete-confirm-1").Change(true);
        if (lifecycle)
        {
            cut.Render(parameters => parameters.Add(component => component.AuthorizedStatus, CampaignStatus.Closed));
            cut.FindAll("#participant-drawer-note-delete-confirm-1").ShouldBeEmpty();
            cut.Render(parameters => parameters.Add(component => component.AuthorizedStatus, CampaignStatus.Active));
        }
        else
        {
            cut.Render(parameters => parameters.Add(component => component.AuthorityScope, "other:club-2:False"));
        }

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Concurrent revision V2"));
        cut.FindAll("#participant-drawer-note-delete-confirm-1").ShouldBeEmpty();
        _ = notes.DidNotReceive().DeleteAsync(Arg.Any<DeleteEvaluationNoteInput>(), Arg.Any<CancellationToken>());
        FindButtonByText(cut, "Delete").Click();
        FindButtonByText(cut, "Delete").HasAttribute("disabled").ShouldBeTrue();
        ConfirmDrawerDelete(cut);
        _ = notes.Received(1).DeleteAsync(Arg.Is<DeleteEvaluationNoteInput>(input => input.NoteId == 1 && input.ExpectedVersion == history.CurrentVersion), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void DrawerAmbiguousDeleteRetriesAndReloadKeepOriginalReviewedVersionAndOperation()
    {
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        var history = ConfigureDrawerDeleteHistory(notes);
        var inputs = new List<DeleteEvaluationNoteInput>();
        notes.DeleteAsync(Arg.Any<DeleteEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            inputs.Add(call.Arg<DeleteEvaluationNoteInput>());
            return inputs.Count < 3 ? Task.FromException<ServiceResult<EvaluationNoteMutationSuccess>>(new HttpRequestException("Lost acknowledgement"))
                : Task.FromResult(DrawerDeleteConflict());
        });
        var module = JSInterop.SetupModule(DrawerModulePath);
        var read = module.Setup<string?>("readOperation", _ => true);
        read.SetResult(null);
        var write = module.SetupVoid("writeOperation", _ => true);
        write.SetVoidResult();
        var cut = DeleteDrawer();
        OpenDrawerDeleteAndRefreshHistory(cut);
        ConfirmDrawerDelete(cut);
        cut.WaitForAssertion(() => FindButtonByText(cut, "Recover original operation").HasAttribute("disabled").ShouldBeFalse());
        FindButtonByText(cut, "Recover original operation").Click();
        cut.WaitForAssertion(() => inputs.Count.ShouldBe(2));
        read.SetResult((string)write.Invocations.Last().Arguments[2]!);
        cut.Dispose();

        var restored = DeleteDrawer();
        restored.WaitForAssertion(() => restored.Markup.ShouldContain("Recover original operation"));
        FindButtonByText(restored, "Recover original operation").Click();

        restored.WaitForAssertion(() => inputs.Count.ShouldBe(3));
        inputs[0].ExpectedVersion.ShouldBe(history.ReviewedVersion);
        inputs[0].OperationId.ShouldNotBe(Guid.Empty);
        inputs[1].ShouldBe(inputs[0]);
        inputs[2].ShouldBe(inputs[0]);
        restored.Markup.ShouldContain("The note changed after confirmation opened.");
        restored.FindAll("#participant-drawer-note-delete-confirm-1").ShouldBeEmpty();
        restored.Markup.ShouldContain("Concurrent revision V2");
        history.Deleted.ShouldBeFalse();
    }

    private DeleteConfirmationHistory ConfigureDrawerDeleteHistory(ICampaignEvaluationNoteService notes)
    {
        RegisterMutableDrawer(notes);
        var history = new DeleteConfirmationHistory(false);
        Services.GetRequiredService<ICampaignEvaluationQueryService>().GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(history.Read(call.Arg<GetEvaluationHistoryInput>())));
        return history;
    }

    private IRenderedComponent<CampaignParticipantDrawerComponent> DeleteDrawer() => Render<CampaignParticipantDrawerComponent>(parameters => parameters
        .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301).Add(component => component.AuthorizedStatus, CampaignStatus.Active));

    private static void OpenDrawerDeleteAndRefreshHistory(IRenderedComponent<CampaignParticipantDrawerComponent> cut)
    {
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Reviewed revision V1"));
        FindButtonByText(cut, "Delete").Click();
        FindButtonByText(cut, "Load older notes").Click();
        FindButtonByText(cut, "Retry notes").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Concurrent revision V2"));
        cut.Find("#participant-drawer-note-delete-confirm-1").ShouldNotBeNull();
    }

    private static void ConfirmDrawerDelete(IRenderedComponent<CampaignParticipantDrawerComponent> cut)
    {
        cut.Find("#participant-drawer-note-delete-confirm-1").Change(true);
        FindButtonByText(cut, "Delete").Click();
    }

    private static ServiceResult<EvaluationNoteMutationSuccess> DrawerDeleteConflict() => new(ServiceProblem.Conflict("The note changed after confirmation opened."));

    private static ServiceResult<EvaluationNoteMutationSuccess> DrawerDeleteReceipt(DeleteEvaluationNoteInput input) => new(new EvaluationNoteMutationSuccess(input.NoteId, input.ExpectedVersion,
        new(input.OperationId, 301, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24))));
}
