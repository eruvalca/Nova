using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
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
    [InlineData("import")]
    [InlineData("import-sync")]
    [InlineData("open")]
    [InlineData("protectNavigation")]
    [InlineData("readOperation")]
    public async Task DrawerRetriesFailedInteropInitializationBeforeSavingAsync(string failedStep)
    {
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
            Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(DrawerCancellationSuccess(call.Arg<AddEvaluationNoteInput>()))));
        var runtime = PrepareDrawerStorageRecovery(notes, failedStep);
        var cut = StorageRecoveryDrawer();
        await cut.WaitForAssertionAsync(() => FindButtonByText(cut, "Retry recovery storage").ShouldNotBeNull());
        notes.ReceivedCalls().ShouldBeEmpty();
        var importsBeforeRetry = runtime.ImportCalls;
        cut.Render();
        runtime.ImportCalls.ShouldBe(importsBeforeRetry);

        await FindButtonByText(cut, "Retry recovery storage").ClickAsync(new MouseEventArgs());

        await cut.WaitForAssertionAsync(() => runtime.Module.SuccessfulCalls.ShouldContain("readOperation", StringComparer.Ordinal));
        runtime.Module.SuccessfulCalls.ShouldContain("open", StringComparer.Ordinal);
        runtime.Module.SuccessfulCalls.ShouldContain("protectNavigation", StringComparer.Ordinal);
        if (failedStep?.StartsWith("import", StringComparison.Ordinal) == true) { runtime.ImportCalls.ShouldBe(2); }
        await FindButtonByText(cut, "Add note").ClickAsync(new MouseEventArgs());
        await cut.Find("textarea").InputAsync(new ChangeEventArgs { Value = "Saved after recovery initialization" });
        await FindButtonByText(cut, "Save note").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Note added."));
        _ = notes.Received(1).AddAsync(Arg.Is<AddEvaluationNoteInput>(input => string.Equals(input.Content, "Saved after recovery initialization", StringComparison.Ordinal) && input.PlayerCampaignAssignmentId == 301), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Kind\":\"AddEvaluationNoteInput\"}")]
    [InlineData("{\"Payload\":\"{}\"}")]
    [InlineData("{\"Kind\":\"AddEvaluationNoteInput\",\"Payload\":null}")]
    [InlineData("{\"Kind\":\"AddEvaluationNoteInput\",\"Payload\":\"null\"}")]
    [InlineData("{\"Kind\":\"AddEvaluationNoteInput\",\"Payload\":\"{}\"}")]
    [InlineData("{\"Kind\":\"UnknownOperation\",\"Payload\":\"{}\"}")]
    public async Task DrawerRejectsMalformedStoredOperationThenReplaysValidOriginalAsync(string malformed)
    {
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
            Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(DrawerCancellationSuccess(call.Arg<AddEvaluationNoteInput>()))));
        var runtime = PrepareDrawerStorageRecovery(notes);
        runtime.Module.ReadJson = malformed;
        var cut = StorageRecoveryDrawer();
        await cut.WaitForAssertionAsync(() => FindButtonByText(cut, "Retry recovery storage").ShouldNotBeNull());
        notes.ReceivedCalls().ShouldBeEmpty();
        runtime.Module.ClearCalls.ShouldBe(0);
        runtime.Module.SuccessfulCalls.ShouldNotContain("writeOperation", StringComparer.Ordinal);
        Services.GetRequiredService<ICampaignTagApplicationService>().ReceivedCalls().ShouldBeEmpty();
        var original = new AddEvaluationNoteInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = 301, Content = "Original retained evidence" };

        runtime.Module.ReadJson = StoredDrawerAdd(original);
        await FindButtonByText(cut, "Retry recovery storage").ClickAsync(new MouseEventArgs());

        await cut.WaitForAssertionAsync(() => FindButtonByText(cut, "Recover original operation").ShouldNotBeNull());
        cut.Find("textarea").GetAttribute("value").ShouldBe(original.Content);
        notes.ReceivedCalls().ShouldBeEmpty();
        await FindButtonByText(cut, "Recover original operation").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Note saved."));
        _ = notes.Received(1).AddAsync(Arg.Is<AddEvaluationNoteInput>(input => input == original), Arg.Any<CancellationToken>());
        runtime.Module.ClearCalls.ShouldBe(1);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("open", false)]
    [InlineData("open", true)]
    [InlineData("protectNavigation", false)]
    [InlineData("protectNavigation", true)]
    [InlineData("readOperation", false)]
    [InlineData("readOperation", true)]
    public async Task DrawerIgnoresOldOwnerInteropCompletionAfterNewOwnerRestoresAsync(string delayedStep, bool failure)
    {
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        var runtime = PrepareDrawerStorageRecovery(notes);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cleanup = new NavigationCompletionCleanup(() => { gate.TrySetResult(); read.TrySetResult(null); });
        if (string.Equals(delayedStep, "readOperation", StringComparison.Ordinal)) { runtime.Module.ReadResults.Enqueue(read.Task); }
        else { runtime.Module.Gates[delayedStep] = new Queue<Task>([gate.Task]); }
        var cut = Render<StorageObservedDrawer>(parameters => parameters.Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301));
        await cut.WaitForAssertionAsync(() => runtime.Module.StartedCalls.ShouldContain(delayedStep, StringComparer.Ordinal));
        var original = new AddEvaluationNoteInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = 301, Content = "New owner retained evidence" };
        runtime.Module.ReadJson = StoredDrawerAdd(original);

        await cut.InvokeAsync(() => cut.Render(parameters => parameters.Add(component => component.AuthorityScope, "new-user:club-2:False")));
        await cut.WaitForAssertionAsync(() => cut.Find("textarea").GetAttribute("value").ShouldBe(original.Content));
        if (failure)
        {
            if (string.Equals(delayedStep, "readOperation", StringComparison.Ordinal)) { read.SetException(new JSException("Old read failure")); }
            else { gate.SetException(new JSException("Old guard failure")); }
        }
        else
        {
            gate.TrySetResult();
            read.TrySetResult(StoredDrawerAdd(original with { Content = "Old owner evidence" }));
        }
        await cut.Instance.InitialInteropCompleted.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);

        cut.Find("textarea").GetAttribute("value").ShouldBe(original.Content);
        FindButtonByText(cut, "Recover original operation").HasAttribute("disabled").ShouldBeFalse();
        cut.FindAll("button").ShouldNotContain(button => string.Equals(button.TextContent.Trim(), "Retry recovery storage", StringComparison.Ordinal));
        runtime.Module.ClearCalls.ShouldBe(0);
        runtime.Module.SuccessfulCalls.ShouldNotContain("close", StringComparer.Ordinal);
        notes.ReceivedCalls().ShouldBeEmpty();
        runtime.Module.ReadOwners.Count.ShouldBe(string.Equals(delayedStep, "readOperation", StringComparison.Ordinal) ? 2 : 1);
        runtime.Module.ReadOwners[^1].ShouldStartWith("new-user:club-2:False:");
    }

    [Fact]
    public async Task DrawerDuplicateStorageRetrySharesPendingReadAndRecoversOriginalOperationAsync()
    {
        var notes = Substitute.For<ICampaignEvaluationNoteService>();
        notes.AddAsync(Arg.Any<AddEvaluationNoteInput>(), Arg.Any<CancellationToken>()).Returns(call =>
            Task.FromResult(new ServiceResult<EvaluationNoteMutationSuccess>(DrawerCancellationSuccess(call.Arg<AddEvaluationNoteInput>()))));
        var runtime = PrepareDrawerStorageRecovery(notes, "readOperation");
        var cut = StorageRecoveryDrawer();
        await cut.WaitForAssertionAsync(() => FindButtonByText(cut, "Retry recovery storage").ShouldNotBeNull());
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cleanup = new NavigationCompletionCleanup(() => pending.TrySetResult(null));
        runtime.Module.ReadResults.Enqueue(pending.Task);
        var readsBeforeRetry = runtime.Module.ReadOwners.Count;
        var retry = FindButtonByText(cut, "Retry recovery storage").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => runtime.Module.ReadOwners.Count.ShouldBe(readsBeforeRetry + 1));

        await FindButtonByText(cut, "Retry recovery storage").ClickAsync(new MouseEventArgs());

        runtime.Module.ReadOwners.Count.ShouldBe(readsBeforeRetry + 1);
        notes.ReceivedCalls().ShouldBeEmpty();
        runtime.Module.ClearCalls.ShouldBe(0);
        var original = new AddEvaluationNoteInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = 301, Content = "One original operation" };
        pending.SetResult(StoredDrawerAdd(original));
        await retry;
        cut.Find("textarea").GetAttribute("value").ShouldBe(original.Content);
        await FindButtonByText(cut, "Recover original operation").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Note saved."));
        _ = notes.Received(1).AddAsync(Arg.Is<AddEvaluationNoteInput>(input => input == original), Arg.Any<CancellationToken>());
    }

    private StorageRecoveryRuntime PrepareDrawerStorageRecovery(ICampaignEvaluationNoteService notes, string? failedStep = null)
    {
        RegisterMutableDrawer(notes);
        Services.AddSingleton(_ => new StorageRecoveryModule(Substitute.For<IJSObjectReference>()) { FailedStep = failedStep });
        Services.AddSingleton(provider => new StorageRecoveryRuntime(JSInterop.JSRuntime, provider.GetRequiredService<StorageRecoveryModule>()) { FailedStep = failedStep });
        Services.AddSingleton<IJSRuntime>(provider => provider.GetRequiredService<StorageRecoveryRuntime>());
        return Services.GetRequiredService<StorageRecoveryRuntime>();
    }

    private IRenderedComponent<CampaignParticipantDrawerComponent> StorageRecoveryDrawer() => Render<CampaignParticipantDrawerComponent>(parameters => parameters
        .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301));

#pragma warning disable CA1812 // bUnit constructs this lifecycle-completion observer through reflection.
    private sealed class StorageObservedDrawer(ICampaignParticipantQueryService participants, ICampaignEvaluationQueryService evidence,
        ICampaignEvaluationNoteService notes, ICampaignTagApplicationService tags, ITagDefinitionQueryService choices, IJSRuntime js)
        : CampaignParticipantDrawerComponent(participants, evidence, notes, tags, choices, js)
#pragma warning restore CA1812
    {
        public TaskCompletionSource InitialInteropCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            try { await base.OnAfterRenderAsync(firstRender); }
            finally { if (firstRender) { InitialInteropCompleted.TrySetResult(); } }
        }
    }
}
