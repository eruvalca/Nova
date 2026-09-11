using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignParticipantDrawerTests
{
    [Fact]
    public async Task DrawerConfirmedNoncommitAllowsDeliberatelyReopenedCorrectedEditAsync()
    {
        var original = CreateNote(canEdit: true);
        var current = original with { Version = Guid.NewGuid(), Content = "Another session's revision" };
        var inputs = new List<EditEvaluationNoteInput>();
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        notes.EditAsync(Arg.Any<EditEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var input = call.Arg<EditEvaluationNoteInput>();
            inputs.Add(input);
            return Task.FromResult(inputs.Count == 1
                ? new ServiceResult<EvaluationNoteMutationSuccess>(EvaluationMutationRejection.NotCommitted(ServiceProblem.Conflict("Review the newer note before retrying."), input.OperationId))
                : new ServiceResult<EvaluationNoteMutationSuccess>(new EvaluationNoteMutationSuccess(input.NoteId, Guid.NewGuid(), new(input.OperationId, 301, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24)))));
        });
        var query = Substitute.For<ICampaignParticipantQueryService>();
        query.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>()).Returns(_ =>
            Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(notes: [inputs.Count == 0 ? original : current]))));
        RegisterServices(query, notes);
        var cut = DeleteDrawer();
        await FindButtonByText(cut, "Edit").ClickAsync(new MouseEventArgs());
        await cut.Find("textarea").InputAsync(new ChangeEventArgs { Value = "My first edit" });
        await FindButtonByText(cut, "Save").ClickAsync(new MouseEventArgs());

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Review the newer note before retrying."));
        cut.Find("textarea").GetAttribute("value").ShouldBe("My first edit");
        cut.FindAll("button").ShouldNotContain(button => string.Equals(button.TextContent.Trim(), "Recover original operation", StringComparison.Ordinal));
        await FindButtonByText(cut, "Cancel").ClickAsync(new MouseEventArgs());
        await FindButtonByText(cut, "Edit").ClickAsync(new MouseEventArgs());
        cut.Find("textarea").GetAttribute("value").ShouldBe(current.Content);
        await cut.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Corrected after reviewing current note" });
        await FindButtonByText(cut, "Save").ClickAsync(new MouseEventArgs());

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Note updated."));
        inputs.Count.ShouldBe(2);
        inputs[0].ExpectedVersion.ShouldBe(original.Version);
        inputs[1].ExpectedVersion.ShouldBe(current.Version);
        inputs[1].Content.ShouldBe("Corrected after reviewing current note");
        inputs[1].OperationId.ShouldNotBe(inputs[0].OperationId);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("forbidden")]
    [InlineData("conflict")]
    [InlineData("wrong-marker")]
    [InlineData("malformed-marker")]
    public async Task DrawerUncertainRejectionAfterLostResponseRetainsOriginalOperationAcrossReloadAsync(string rejection)
    {
        var inputs = new List<AddEvaluationNoteInput>();
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var input = call.Arg<AddEvaluationNoteInput>();
            inputs.Add(input);
            if (inputs.Count == 1) { return Task.FromException<ServiceResult<EvaluationNoteMutationSuccess>>(new HttpRequestException("Lost committed response")); }
            return Task.FromResult(inputs.Count == 2
                ? new ServiceResult<EvaluationNoteMutationSuccess>(RecoveryRejectionCases.Problem(rejection, input.OperationId))
                : new ServiceResult<EvaluationNoteMutationSuccess>(DrawerCancellationSuccess(input)));
        });
        RegisterMutableDrawer(notes);
        var module = JSInterop.SetupModule(DrawerModulePath);
        var read = module.Setup<string?>("readOperation", _ => true);
        read.SetResult(null);
        var write = module.SetupVoid("writeOperation", _ => true);
        write.SetVoidResult();
        var clear = module.SetupVoid("clearOperation", _ => true);
        clear.SetVoidResult();
        var cut = DeleteDrawer();
        await FindButtonByText(cut, "Add note").ClickAsync(new MouseEventArgs());
        await cut.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Original potentially committed evidence" });
        await FindButtonByText(cut, "Save note").ClickAsync(new MouseEventArgs());
        await FindButtonByText(cut, "Recover original operation").ClickAsync(new MouseEventArgs());

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain(RecoveryRejectionCases.Detail(rejection)));
        clear.Invocations.ShouldBeEmpty();
        FindButtonByText(cut, "Recover original operation").HasAttribute("disabled").ShouldBeFalse();
        cut.Find("textarea").GetAttribute("value").ShouldBe(inputs[0].Content);
        cut.Find("textarea").HasAttribute("readonly").ShouldBeTrue();
        cut.Find("textarea").HasAttribute("disabled").ShouldBeFalse();
        read.SetResult((string)write.Invocations.Last().Arguments[2]!);
        cut.Dispose();
        var restored = DeleteDrawer();
        await restored.WaitForAssertionAsync(() => FindButtonByText(restored, "Recover original operation").ShouldNotBeNull());
        await FindButtonByText(restored, "Recover original operation").ClickAsync(new MouseEventArgs());

        await restored.WaitForAssertionAsync(() => restored.Markup.ShouldContain("Note saved."));
        inputs.Count.ShouldBe(3);
        inputs[0].OperationId.ShouldNotBe(Guid.Empty);
        inputs[1].ShouldBe(inputs[0]);
        inputs[2].ShouldBe(inputs[0]);
        clear.Invocations.Count.ShouldBe(1);
    }

    [Fact]
    public async Task DrawerExpiredOperationRemainsCopyableAndCannotBecomeFreshSubmissionAsync()
    {
        var original = new AddEvaluationNoteInput { OperationId = Guid.CreateVersion7(DateTimeOffset.UtcNow.AddDays(-2)), PlayerCampaignAssignmentId = 301, Content = "Expired potentially committed evidence" };
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(
            new ServiceResult<EvaluationNoteMutationSuccess>(RecoveryRejectionCases.Problem("expired", original.OperationId))));
        var runtime = PrepareDrawerStorageRecovery(notes);
        runtime.Module.ReadJson = StoredDrawerAdd(original);
        var cut = DeleteDrawer();
        await FindButtonByText(cut, "Recover original operation").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("24-hour recovery window has expired"));
        runtime.Module.ClearCalls.ShouldBe(0);
        cut.Dispose();
        var restored = DeleteDrawer();
        await FindButtonByText(restored, "Recover original operation").ClickAsync(new MouseEventArgs());

        await restored.WaitForAssertionAsync(() => restored.Markup.ShouldContain("24-hour recovery window has expired"));
        restored.Find("textarea").GetAttribute("value").ShouldBe(original.Content);
        restored.Find("textarea").HasAttribute("readonly").ShouldBeTrue();
        restored.Find("textarea").HasAttribute("disabled").ShouldBeFalse();
        FindButtonByText(restored, "Save note").HasAttribute("disabled").ShouldBeTrue();
        runtime.Module.ClearCalls.ShouldBe(0);
        _ = notes.Received(2).AddAsync(Arg.Is<AddEvaluationNoteInput>(input => input == original), Arg.Any<CancellationToken>());
        notes.ReceivedCalls().Count().ShouldBe(2);
    }
}
