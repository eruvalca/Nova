using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignEvaluationPanelTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("forbidden")]
    [InlineData("conflict")]
    [InlineData("wrong-marker")]
    [InlineData("malformed-marker")]
    public async Task EvaluationUncertainRejectionAfterLostResponseRetainsOriginalOperationAcrossReloadAsync(string rejection)
    {
        var inputs = new List<AddEvaluationNoteInput>();
        _notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var input = call.Arg<AddEvaluationNoteInput>();
            inputs.Add(input);
            if (inputs.Count == 1) { return Task.FromException<ServiceResult<EvaluationNoteMutationSuccess>>(new HttpRequestException("Lost committed response")); }
            return Task.FromResult(inputs.Count == 2
                ? new ServiceResult<EvaluationNoteMutationSuccess>(RecoveryRejectionCases.Problem(rejection, input.OperationId))
                : new ServiceResult<EvaluationNoteMutationSuccess>(Success(input)));
        });
        var cut = Panel(new() { ParticipantId = 301 });
        await cut.WaitForAssertionAsync(() => cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse());
        await cut.Find("#evaluation-note").InputAsync(new ChangeEventArgs { Value = "Original potentially committed evidence" });
        await Button(cut, "Save note").ClickAsync(new MouseEventArgs());
        await Button(cut, "Retry original operation").ClickAsync(new MouseEventArgs());

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain(RecoveryRejectionCases.Detail(rejection)));
        Button(cut, "Retry original operation").HasAttribute("disabled").ShouldBeFalse();
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe(inputs[0].Content);
        cut.Find("#evaluation-note").HasAttribute("readonly").ShouldBeTrue();
        cut.Find("#evaluation-note").HasAttribute("disabled").ShouldBeFalse();
        _storage.ReadSnapshot = _storage.Writes[^1];
        cut.Dispose();
        var restored = Panel(new() { ParticipantId = 301 });
        await restored.WaitForAssertionAsync(() => Button(restored, "Retry original operation").ShouldNotBeNull());
        await Button(restored, "Retry original operation").ClickAsync(new MouseEventArgs());

        await restored.WaitForAssertionAsync(() => restored.Markup.ShouldContain("Note saved."));
        inputs.Count.ShouldBe(3);
        inputs[0].OperationId.ShouldNotBe(Guid.Empty);
        inputs[1].ShouldBe(inputs[0]);
        inputs[2].ShouldBe(inputs[0]);
    }

    [Fact]
    public async Task EvaluationExpiredOperationRemainsCopyableAndCannotBecomeFreshSubmissionAsync()
    {
        var runtime = PrepareEvaluationStorageRecovery();
        var original = new AddEvaluationNoteInput { OperationId = Guid.CreateVersion7(DateTimeOffset.UtcNow.AddDays(-2)), PlayerCampaignAssignmentId = 301, Content = "Expired potentially committed evidence" };
        runtime.Module.ReadJson = EvaluationRecoverySnapshot(original);
        _notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(
            new ServiceResult<EvaluationNoteMutationSuccess>(RecoveryRejectionCases.Problem("expired", original.OperationId))));
        var cut = Panel(new() { ParticipantId = 301 });
        await Button(cut, "Retry original operation").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("24-hour recovery window has expired"));
        runtime.Module.ClearCalls.ShouldBe(0);
        _storage.Writes.ShouldAllBe(snapshot => System.Text.Json.JsonSerializer.Serialize(snapshot).Contains(original.OperationId.ToString("D"), StringComparison.Ordinal));
        cut.Dispose();
        var restored = Panel(new() { ParticipantId = 301 });
        await Button(restored, "Retry original operation").ClickAsync(new MouseEventArgs());

        await restored.WaitForAssertionAsync(() => restored.Markup.ShouldContain("24-hour recovery window has expired"));
        restored.Find("#evaluation-note").GetAttribute("value").ShouldBe(original.Content);
        restored.Find("#evaluation-note").HasAttribute("readonly").ShouldBeTrue();
        restored.Find("#evaluation-note").HasAttribute("disabled").ShouldBeFalse();
        restored.Find(".evaluation-save").HasAttribute("disabled").ShouldBeTrue();
        runtime.Module.ClearCalls.ShouldBe(0);
        _ = _notes.Received(2).AddAsync(Arg.Is<AddEvaluationNoteInput>(input => input == original), Arg.Any<CancellationToken>());
        _notes.ReceivedCalls().Count().ShouldBe(2);
    }
}
