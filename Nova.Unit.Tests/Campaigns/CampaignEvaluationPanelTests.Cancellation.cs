using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Campaigns.Components;
using Nova.UI.Features.Campaigns.Services;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignEvaluationPanelTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvaluationDisposalPropagatesIdentityAndFinderCancellationAsync(bool finder)
    {
        var entered = new TaskCompletionSource<CancellationToken>();
        if (finder)
        {
            _placements.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
                .Returns(call => CanceledQueryAsync<CampaignEffectivePlacementsResult>(entered, call.Arg<CancellationToken>()));
        }
        else
        {
            _participants.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
                .Returns(call => CanceledQueryAsync<CampaignParticipantDetailDto>(entered, call.Arg<CancellationToken>()));
        }
        var cut = CancellationPanel(finder ? new() { Search = "42" } : new() { ParticipantId = 301 });
        var token = await entered.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        token.IsCancellationRequested.ShouldBeFalse();

        await ((IAsyncDisposable)cut.Instance).DisposeAsync();

        var canceled = await cut.Instance.ParameterCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);
        canceled.ShouldBe(token);
        canceled.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task FinderSupersessionConsumesOnlyObsoleteCancellationAndPreservesNewPendingSearchAsync()
    {
        var firstEntered = new TaskCompletionSource<CancellationToken>();
        var second = new TaskCompletionSource<ServiceResult<CampaignEffectivePlacementsResult>>();
        _placements.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(call => string.Equals(call.Arg<GetCampaignEffectivePlacementsInput>().Search, "old", StringComparison.Ordinal)
                ? CanceledQueryAsync<CampaignEffectivePlacementsResult>(firstEntered, call.Arg<CancellationToken>())
                : second.Task);
        var cut = CancellationPanel(new() { Search = "old" });
        var oldToken = await firstEntered.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);

        cut.Render(parameters => parameters.Add(component => component.State, new CampaignWorkspaceEvaluationState { Search = "new" }));

        await cut.Instance.FirstParametersFinished.Task.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);
        oldToken.IsCancellationRequested.ShouldBeTrue();
        cut.Instance.ParameterCancellation.Task.IsCompleted.ShouldBeFalse();
        cut.Markup.ShouldContain("Finding players");
        cut.Markup.ShouldNotContain("Retry search");
        second.Task.IsCompleted.ShouldBeFalse();
        second.SetResult(new ServiceResult<CampaignEffectivePlacementsResult>(Results()));
        await cut.WaitForAssertionAsync(() => cut.FindAll("a[data-eval-result]").Count.ShouldBe(1));
        cut.Find("#evaluation-search").GetAttribute("value").ShouldBe("new");
        _ = _placements.Received(2).GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void EvaluationUnrelatedIdentityAndFinderCancellationOffersSuccessfulRetry(bool finder)
    {
        if (finder)
        {
            _placements.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>()).Returns(
                Task.FromException<ServiceResult<CampaignEffectivePlacementsResult>>(new OperationCanceledException("Transport timeout")),
                Task.FromResult(new ServiceResult<CampaignEffectivePlacementsResult>(Results())));
        }
        else
        {
            _participants.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>()).Returns(
                Task.FromException<ServiceResult<CampaignParticipantDetailDto>>(new OperationCanceledException("Transport timeout")),
                Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(Identity(301))));
        }
        var cut = Panel(finder ? new() { Search = "42" } : new() { ParticipantId = 301 });
        Button(cut, finder ? "Retry search" : "Retry identity").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain(finder ? "Retry search" : "Retry identity"));
        if (finder) { cut.FindAll("a[data-eval-result]").Count.ShouldBe(1); }
        else { cut.Find("#evaluation-player-heading").TextContent.ShouldContain("Jordan Lee"); }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvaluationDisposalDuringDispatchOrReplayPreservesExactOperationForReloadAsync(bool replay)
    {
        var entered = new TaskCompletionSource<CancellationToken>();
        var inputs = new List<AddEvaluationNoteInput>();
        _notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            inputs.Add(call.Arg<AddEvaluationNoteInput>());
            return inputs.Count == 1 ? CanceledQueryAsync<EvaluationNoteMutationSuccess>(entered, call.Arg<CancellationToken>())
                : Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(Success(inputs[^1])));
        });
        if (replay) { _storage.ReadSnapshot = PendingAddSnapshot(); }
        var cut = CancellationPanel(new() { ParticipantId = 301 });
        await cut.WaitForAssertionAsync(() => (replay
            ? cut.FindAll("button").Single(element => string.Equals(element.TextContent.Trim(), "Retry original operation", StringComparison.Ordinal))
            : cut.Find(".evaluation-save")).HasAttribute("disabled").ShouldBeFalse());
        if (!replay) { await cut.Find("#evaluation-note").InputAsync(new ChangeEventArgs { Value = "Original cancellation evidence" }); }
        var button = replay ? cut.FindAll("button").Single(element => string.Equals(element.TextContent.Trim(), "Retry original operation", StringComparison.Ordinal)) : cut.Find(".evaluation-save");
        var click = button.ClickAsync(new MouseEventArgs());
        var token = await entered.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        var pending = _storage.Writes[^1];
        var writes = _storage.Writes.Count;

        await ((IAsyncDisposable)cut.Instance).DisposeAsync();
        var canceled = await cut.Instance.EventCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);
        canceled.ShouldBe(token);
        await ObserveCanceledClickAsync(click);

        _storage.Writes.Count.ShouldBe(writes);
        _storage.Writes[^1].ShouldBe(pending);
        _storage.ReadSnapshot = pending;
        var restored = Panel(new() { ParticipantId = 301 });
        await restored.WaitForAssertionAsync(() => restored.Markup.ShouldContain("previous submission needs its receipt"));
        restored.Find("#evaluation-note").GetAttribute("value").ShouldBe(inputs[0].Content);
        await Button(restored, "Retry original operation").ClickAsync(new MouseEventArgs());
        await restored.WaitForAssertionAsync(() => restored.Markup.ShouldContain("Note saved."));
        inputs.Count.ShouldBe(2);
        inputs[1].ShouldBe(inputs[0]);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void EvaluationUnrelatedDispatchOrReplayCancellationRetainsOriginalRecovery(bool replay)
    {
        var inputs = new List<AddEvaluationNoteInput>();
        _notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            inputs.Add(call.Arg<AddEvaluationNoteInput>());
            return inputs.Count == 1 ? Task.FromException<ServiceResult<EvaluationNoteMutationSuccess>>(new OperationCanceledException("Transport timeout"))
                : Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(Success(inputs[^1])));
        });
        if (replay) { _storage.ReadSnapshot = PendingAddSnapshot(); }
        var cut = Panel(new() { ParticipantId = 301 });
        if (!replay)
        {
            cut.WaitForAssertion(() => cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse());
            cut.Find("#evaluation-note").Input("Original cancellation evidence");
        }
        (replay ? Button(cut, "Retry original operation") : cut.Find(".evaluation-save")).Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("outcome is not yet known"));
        using var snapshot = JsonDocument.Parse(_storage.Writes[^1]);
        snapshot.RootElement.GetProperty("Pending").GetProperty("OperationId").GetGuid().ShouldBe(inputs[0].OperationId);
        Button(cut, "Retry original operation").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Note saved."));
        inputs.Count.ShouldBe(2);
        inputs[1].ShouldBe(inputs[0]);
    }

    private static string PendingAddSnapshot() => JsonSerializer.Serialize(new
    {
        Revision = 1,
        Draft = "Original cancellation evidence",
        EditingNoteId = (long?)null,
        EditContent = string.Empty,
        EditOriginal = string.Empty,
        EditVersion = Guid.Empty,
        Pending = new { Kind = "add", OperationId = Guid.CreateVersion7(), AssignmentId = 301L, SubjectId = (long?)null, Version = Guid.Empty, Text = "Original cancellation evidence" }
    });

    private IRenderedComponent<CancellationEvaluationPanel> CancellationPanel(CampaignWorkspaceEvaluationState state) => Render<CancellationEvaluationPanel>(parameters => parameters
        .Add(component => component.CampaignId, 10).Add(component => component.Status, CampaignStatus.Active)
        .Add(component => component.AuthorityScope, "coach:club-1:False").Add(component => component.CaptureScope, "coach:club-1")
        .Add(component => component.State, state).Add(component => component.RosterState, new()));

    private static async Task<ServiceResult<T>> CanceledQueryAsync<T>(TaskCompletionSource<CancellationToken> entered, CancellationToken token)
    {
        var pending = new TaskCompletionSource<ServiceResult<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => pending.TrySetCanceled(token));
        entered.TrySetResult(token);
        return await pending.Task;
    }

    private static async Task ObserveCanceledClickAsync(Task click)
    {
        try { await click; }
        catch (OperationCanceledException) { /* The explicit observation above proves cancellation escaped the production callback. */ }
    }

#pragma warning disable CA1812 // bUnit constructs this lifecycle/event-observing component through reflection.
    private sealed class CancellationEvaluationPanel(ICampaignParticipantQueryService participants, IEffectivePlacementQueryService placements,
        ICampaignEvaluationQueryService evidence, ICampaignEvaluationNoteService notes, ICampaignTagApplicationService tags,
        NavigationManager navigation, IJSRuntime js) : CampaignEvaluationPanel(participants, placements, evidence, notes, tags, navigation, js), IHandleEvent
#pragma warning restore CA1812
    {
        public TaskCompletionSource<CancellationToken> ParameterCancellation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<CancellationToken> EventCancellation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstParametersFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task OnParametersSetAsync()
        {
            try { await base.OnParametersSetAsync(); }
            catch (OperationCanceledException exception) { ParameterCancellation.TrySetResult(exception.CancellationToken); throw; }
            finally { FirstParametersFinished.TrySetResult(); }
        }
        async Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem item, object? arg)
        {
            try { var task = item.InvokeAsync(arg); StateHasChanged(); await task; }
            catch (OperationCanceledException exception) { EventCancellation.TrySetResult(exception.CancellationToken); throw; }
            finally { if (!ComponentCancellationToken.IsCancellationRequested) { StateHasChanged(); } }
        }
    }
}
