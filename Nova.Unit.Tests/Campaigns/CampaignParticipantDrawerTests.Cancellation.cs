using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Tags;
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
    public async Task DrawerDisposalDuringMutationOrReplayPreservesOriginalStoredPayloadAsync(bool replay)
    {
        var inputs = new List<AddEvaluationNoteInput>();
        var entered = new TaskCompletionSource<CancellationToken>();
        var notes = CancellableDrawerNoteService(inputs, entered);
        RegisterMutableDrawer(notes);
        var module = JSInterop.SetupModule(DrawerModulePath);
        var original = new AddEvaluationNoteInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = 301, Content = "Original drawer evidence" };
        var read = module.Setup<string?>("readOperation", _ => true);
        read.SetResult(replay ? StoredDrawerAdd(original) : null);
        var write = module.SetupVoid("writeOperation", _ => true);
        write.SetVoidResult();
        var clear = module.SetupVoid("clearOperation", _ => true);
        clear.SetVoidResult();
        var cut = Render<CancellationDrawer>(parameters => parameters.Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain(replay ? "Recover original operation" : "Add note"));
        if (!replay)
        {
            await FindButtonByText(cut, "Add note").ClickAsync(new MouseEventArgs());
            await cut.Find("textarea").InputAsync(new ChangeEventArgs { Value = original.Content });
        }
        var click = FindButtonByText(cut, replay ? "Recover original operation" : "Save note").ClickAsync(new MouseEventArgs());
        var token = await entered.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        var pending = (string)write.Invocations.Single().Arguments[2]!;

        await ((IAsyncDisposable)cut.Instance).DisposeAsync();
        var canceled = await cut.Instance.EventCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);
        canceled.ShouldBe(token);
        try { await click; }
        catch (OperationCanceledException) { /* EventCancellation independently proves the production callback propagated. */ }

        clear.Invocations.ShouldBeEmpty();
        pending.ShouldBe(StoredDrawerAdd(inputs[0]));
        read.SetResult(pending);
        var restored = Render<CampaignParticipantDrawerComponent>(parameters => parameters.Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301));
        await restored.WaitForAssertionAsync(() => restored.Markup.ShouldContain("Recover original operation"));
        restored.Find("textarea").GetAttribute("value").ShouldBe(inputs[0].Content);
        await FindButtonByText(restored, "Recover original operation").ClickAsync(new MouseEventArgs());
        await restored.WaitForAssertionAsync(() => restored.Markup.ShouldContain("Note saved."));
        inputs.Count.ShouldBe(2);
        inputs[1].ShouldBe(inputs[0]);
        clear.Invocations.Count.ShouldBe(1);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawerUnrelatedMutationOrReplayCancellationKeepsRecoveryAvailable(bool replay)
    {
        var inputs = new List<AddEvaluationNoteInput>();
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            inputs.Add(call.Arg<AddEvaluationNoteInput>());
            return inputs.Count == 1 ? Task.FromException<ServiceResult<EvaluationNoteMutationSuccess>>(new OperationCanceledException("Transport timeout"))
                : Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(DrawerCancellationSuccess(inputs[^1])));
        });
        RegisterMutableDrawer(notes);
        var module = JSInterop.SetupModule(DrawerModulePath);
        var original = new AddEvaluationNoteInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = 301, Content = "Original drawer evidence" };
        module.Setup<string?>("readOperation", _ => true).SetResult(replay ? StoredDrawerAdd(original) : null);
        var clear = module.SetupVoid("clearOperation", _ => true);
        clear.SetVoidResult();
        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters.Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301));
        if (!replay)
        {
            FindButtonByText(cut, "Add note").Click();
            cut.Find("textarea").Input(original.Content);
        }
        FindButtonByText(cut, replay ? "Recover original operation" : "Save note").Click();
        cut.WaitForAssertion(() => FindButtonByText(cut, "Recover original operation").HasAttribute("disabled").ShouldBeFalse());
        clear.Invocations.ShouldBeEmpty();
        FindButtonByText(cut, "Recover original operation").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Note saved."));
        inputs.Count.ShouldBe(2);
        inputs[1].ShouldBe(inputs[0]);
    }

    private void RegisterMutableDrawer(ICampaignEvaluationNoteService notes)
    {
        var query = Substitute.For<ICampaignParticipantQueryService>();
        query.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(capabilities: MutationCapabilities(canAddNote: true)))));
        RegisterServices(query, notes);
    }

    private static ICampaignEvaluationNoteService CancellableDrawerNoteService(List<AddEvaluationNoteInput> inputs, TaskCompletionSource<CancellationToken> entered)
    {
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            inputs.Add(call.Arg<AddEvaluationNoteInput>());
            return inputs.Count == 1 ? UntilCanceledAsync<EvaluationNoteMutationSuccess>(entered, call.Arg<CancellationToken>())
                : Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(DrawerCancellationSuccess(inputs[^1])));
        });
        return notes;
    }

    private static string StoredDrawerAdd(AddEvaluationNoteInput input) => JsonSerializer.Serialize(new
    {
        Kind = nameof(AddEvaluationNoteInput),
        Payload = JsonSerializer.Serialize(input)
    });

    private static EvaluationNoteMutationSuccess DrawerCancellationSuccess(AddEvaluationNoteInput input) => new(1, Guid.NewGuid(),
        new(input.OperationId, input.PlayerCampaignAssignmentId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24)));

#pragma warning disable CA1812 // bUnit constructs this event-cancellation observer through reflection.
    private sealed class CancellationDrawer(ICampaignParticipantQueryService participants, ICampaignEvaluationQueryService evidence,
        ICampaignEvaluationNoteService notes, ICampaignTagApplicationService tags, ITagDefinitionQueryService choices, IJSRuntime js)
        : CampaignParticipantDrawerComponent(participants, evidence, notes, tags, choices, js), IHandleEvent
#pragma warning restore CA1812
    {
        public TaskCompletionSource<CancellationToken> EventCancellation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem item, object? arg)
        {
            try { var task = item.InvokeAsync(arg); StateHasChanged(); await task; }
            catch (OperationCanceledException exception) { EventCancellation.TrySetResult(exception.CancellationToken); throw; }
            finally { if (!ComponentCancellationToken.IsCancellationRequested) { StateHasChanged(); } }
        }
    }
}
