using System.Text.Json;
using System.Text.Json.Nodes;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
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
    [InlineData(CampaignStatus.Active)]
    [InlineData(CampaignStatus.Closed)]
    public void EvaluationUnavailableCaptureDoesNotMisreportActiveCampaignAsReadOnly(CampaignStatus status)
    {
        _participants.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(Identity(301) with
            {
                CampaignStatus = status,
                Capabilities = Identity(301).Capabilities with { CanAddNote = false, CanApplyTag = false }
            })));
        var cut = Panel(new() { ParticipantId = 301 }, status);
        cut.WaitForAssertion(() => cut.Find(".evaluation-readonly").TextContent.ShouldContain(status == CampaignStatus.Closed
            ? "This campaign is read-only" : "Evidence capture is unavailable for this player"));
        if (status == CampaignStatus.Active) { cut.Markup.ShouldNotContain("This campaign is read-only"); }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("import")]
    [InlineData("import-sync")]
    [InlineData("attach")]
    [InlineData("read")]
    public async Task EvaluationRetriesFailedInteropInitializationBeforeSavingAsync(string failedStep)
    {
        var runtime = PrepareEvaluationStorageRecovery(failedStep);
        var cut = Panel(new() { ParticipantId = 301 });
        await cut.WaitForAssertionAsync(() => Button(cut, "Retry storage").ShouldNotBeNull());
        cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeTrue();
        _notes.ReceivedCalls().ShouldBeEmpty();
        var importsBeforeRetry = runtime.ImportCalls;
        cut.Render();
        runtime.ImportCalls.ShouldBe(importsBeforeRetry);

        await Button(cut, "Retry storage").ClickAsync(new MouseEventArgs());

        await cut.WaitForAssertionAsync(() => cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse());
        runtime.Module.SuccessfulCalls.ShouldContain("attach", StringComparer.Ordinal);
        runtime.Module.SuccessfulCalls.ShouldContain("read", StringComparer.Ordinal);
        if (failedStep?.StartsWith("import", StringComparison.Ordinal) == true) { runtime.ImportCalls.ShouldBe(2); }
        await cut.Find("#evaluation-note").InputAsync(new ChangeEventArgs { Value = "Saved after recovery initialization" });
        await Button(cut, "Save note").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Note saved."));
        _ = _notes.Received(1).AddAsync(Arg.Is<AddEvaluationNoteInput>(input => string.Equals(input.Content, "Saved after recovery initialization", StringComparison.Ordinal) && input.PlayerCampaignAssignmentId == 301), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("null-draft")]
    [InlineData("missing-trait-search")]
    [InlineData("null-trait-search")]
    [InlineData("negative-revision")]
    [InlineData("missing-kind")]
    [InlineData("unknown-kind")]
    [InlineData("null-text")]
    [InlineData("wrong-assignment")]
    [InlineData("empty-operation")]
    public async Task EvaluationRejectsMalformedSnapshotThenReplaysValidOriginalOperationAsync(string malformed)
    {
        var runtime = PrepareEvaluationStorageRecovery();
        var original = new AddEvaluationNoteInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = 301, Content = "Original retained evidence" };
        var valid = EvaluationRecoverySnapshot(original);
        runtime.Module.ReadJson = MalformedEvaluationSnapshot(valid, malformed);
        var cut = Panel(new() { ParticipantId = 301 });
        await cut.WaitForAssertionAsync(() => Button(cut, "Retry storage").ShouldNotBeNull());
        cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeTrue();
        _notes.ReceivedCalls().ShouldBeEmpty();
        runtime.Module.ClearCalls.ShouldBe(0);
        _storage.Writes.ShouldBeEmpty();
        _tags.ReceivedCalls().ShouldBeEmpty();

        runtime.Module.ReadJson = valid;
        await Button(cut, "Retry storage").ClickAsync(new MouseEventArgs());

        await cut.WaitForAssertionAsync(() => Button(cut, "Retry original operation").ShouldNotBeNull());
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe(original.Content);
        _notes.ReceivedCalls().ShouldBeEmpty();
        await Button(cut, "Retry original operation").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Note saved."));
        _ = _notes.Received(1).AddAsync(Arg.Is<AddEvaluationNoteInput>(input => input == original), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("attach", false)]
    [InlineData("attach", true)]
    [InlineData("read", false)]
    [InlineData("read", true)]
    public async Task EvaluationIgnoresOldOwnerInteropCompletionAfterNewOwnerRestoresAsync(string delayedStep, bool failure)
    {
        var runtime = PrepareEvaluationStorageRecovery();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cleanup = new NavigationCompletionCleanup(() => { gate.TrySetResult(); read.TrySetResult(null); });
        if (string.Equals(delayedStep, "read", StringComparison.Ordinal)) { runtime.Module.ReadResults.Enqueue(read.Task); }
        else { runtime.Module.Gates[delayedStep] = new Queue<Task>([gate.Task]); }
        var cut = Render<StorageObservedEvaluationPanel>(parameters => parameters.Add(component => component.CampaignId, 10)
            .Add(component => component.Status, CampaignStatus.Active).Add(component => component.AuthorityScope, "coach:club-1:False")
            .Add(component => component.CaptureScope, "coach:club-1").Add(component => component.State, new CampaignWorkspaceEvaluationState { ParticipantId = 301 })
            .Add(component => component.RosterState, new CampaignWorkspaceRosterState()));
        await cut.WaitForAssertionAsync(() => runtime.Module.StartedCalls.ShouldContain(delayedStep, StringComparer.Ordinal));
        var newSnapshot = JsonNode.Parse(EvaluationRecoverySnapshot(new() { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = 302, Content = "New owner recovered draft" }))!.AsObject();
        newSnapshot["Pending"] = null;
        runtime.Module.ReadJson = newSnapshot.ToJsonString();

        await cut.InvokeAsync(() => cut.Render(parameters => parameters.Add(component => component.State, new CampaignWorkspaceEvaluationState { ParticipantId = 302 })));
        await cut.WaitForAssertionAsync(() => cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("New owner recovered draft"));
        if (failure)
        {
            if (string.Equals(delayedStep, "read", StringComparison.Ordinal)) { read.SetException(new JSException("Old read failure")); }
            else { gate.SetException(new JSException("Old attachment failure")); }
        }
        else
        {
            gate.TrySetResult();
            read.TrySetResult(EvaluationRecoverySnapshot(new() { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = 301, Content = "Old owner evidence" }));
        }
        await cut.Instance.InitialInteropCompleted.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);

        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("New owner recovered draft");
        cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse();
        cut.FindAll("button").ShouldNotContain(button => string.Equals(button.TextContent.Trim(), "Retry storage", StringComparison.Ordinal));
        runtime.Module.ClearCalls.ShouldBe(0);
        runtime.Module.SuccessfulCalls.ShouldNotContain("detach", StringComparer.Ordinal);
        runtime.Module.ReadOwners.Count.ShouldBe(string.Equals(delayedStep, "read", StringComparison.Ordinal) ? 2 : 1);
        runtime.Module.ReadOwners[^1].ShouldEndWith(":302");
    }

    [Fact]
    public async Task EvaluationDuplicateStorageRetrySharesPendingReadAndRecoversOriginalOperationAsync()
    {
        var runtime = PrepareEvaluationStorageRecovery("read");
        var cut = Panel(new() { ParticipantId = 301 });
        await cut.WaitForAssertionAsync(() => Button(cut, "Retry storage").ShouldNotBeNull());
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cleanup = new NavigationCompletionCleanup(() => pending.TrySetResult(null));
        runtime.Module.ReadResults.Enqueue(pending.Task);
        var readsBeforeRetry = runtime.Module.ReadOwners.Count;
        var retry = Button(cut, "Retry storage").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => runtime.Module.ReadOwners.Count.ShouldBe(readsBeforeRetry + 1));

        await Button(cut, "Retry storage").ClickAsync(new MouseEventArgs());

        runtime.Module.ReadOwners.Count.ShouldBe(readsBeforeRetry + 1);
        _notes.ReceivedCalls().ShouldBeEmpty();
        _storage.Writes.ShouldBeEmpty();
        var original = new AddEvaluationNoteInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = 301, Content = "One original operation" };
        pending.SetResult(EvaluationRecoverySnapshot(original));
        await retry;
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe(original.Content);
        await Button(cut, "Retry original operation").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Note saved."));
        _ = _notes.Received(1).AddAsync(Arg.Is<AddEvaluationNoteInput>(input => input == original), Arg.Any<CancellationToken>());
    }

    private StorageRecoveryRuntime PrepareEvaluationStorageRecovery(string? failedStep = null)
    {
        Services.AddSingleton(_ => new StorageRecoveryModule(_storage) { FailedStep = failedStep });
        Services.AddSingleton(provider => new StorageRecoveryRuntime(JSInterop.JSRuntime, provider.GetRequiredService<StorageRecoveryModule>()) { FailedStep = failedStep });
        Services.AddSingleton<IJSRuntime>(provider => provider.GetRequiredService<StorageRecoveryRuntime>());
        return Services.GetRequiredService<StorageRecoveryRuntime>();
    }

    private static string EvaluationRecoverySnapshot(AddEvaluationNoteInput original) => JsonSerializer.Serialize(new
    {
        Revision = 1,
        Draft = original.Content,
        TraitSearch = string.Empty,
        EditingNoteId = (long?)null,
        EditContent = string.Empty,
        EditOriginal = string.Empty,
        EditVersion = Guid.Empty,
        Pending = new { Kind = "add", original.OperationId, AssignmentId = original.PlayerCampaignAssignmentId, SubjectId = (long?)null, Version = Guid.Empty, Text = original.Content }
    });

    private static string MalformedEvaluationSnapshot(string valid, string malformed)
    {
        var snapshot = JsonNode.Parse(valid)!.AsObject();
        var pending = snapshot["Pending"]!.AsObject();
        switch (malformed)
        {
            case "null-draft": snapshot["Draft"] = null; break;
            case "missing-trait-search": snapshot.Remove("TraitSearch"); break;
            case "null-trait-search": snapshot["TraitSearch"] = null; break;
            case "negative-revision": snapshot["Revision"] = -1; break;
            case "missing-kind": pending.Remove("Kind"); break;
            case "unknown-kind": pending["Kind"] = "unknown"; break;
            case "null-text": pending["Text"] = null; break;
            case "wrong-assignment": pending["AssignmentId"] = 999; break;
            case "empty-operation": pending["OperationId"] = Guid.Empty; break;
            default: throw new ArgumentOutOfRangeException(nameof(malformed));
        }
        return snapshot.ToJsonString();
    }

#pragma warning disable CA1812 // bUnit constructs this lifecycle-completion observer through reflection.
    private sealed class StorageObservedEvaluationPanel(ICampaignParticipantQueryService participants, IEffectivePlacementQueryService placements,
        ICampaignEvaluationQueryService evidence, ICampaignEvaluationNoteService notes, ICampaignTagApplicationService tags,
        NavigationManager navigation, IJSRuntime js) : CampaignEvaluationPanel(participants, placements, evidence, notes, tags, navigation, js)
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

internal sealed class StorageRecoveryRuntime(IJSRuntime fallback, StorageRecoveryModule module) : IJSRuntime
{
    public StorageRecoveryModule Module { get; } = module;
    public string? FailedStep { get; set; }
    public int ImportCalls { get; private set; }
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        if (string.Equals(identifier, "import", StringComparison.Ordinal) && args?[0] is string path &&
            (path.Contains("CampaignEvaluationPanel.razor.js", StringComparison.Ordinal) || path.Contains("CampaignParticipantDrawer.razor.js", StringComparison.Ordinal)))
        {
            ImportCalls++;
            if (string.Equals(FailedStep, "import-sync", StringComparison.Ordinal))
            {
                FailedStep = null;
                throw new JSException("Synchronous module import unavailable");
            }
            if (string.Equals(FailedStep, "import", StringComparison.Ordinal))
            {
                FailedStep = null;
                return ValueTask.FromException<TValue>(new JSException("Module import unavailable"));
            }
            return ValueTask.FromResult((TValue)(object)Module);
        }
        return fallback.InvokeAsync<TValue>(identifier, cancellationToken, args);
    }
}

internal sealed class StorageRecoveryModule(IJSObjectReference fallback) : IJSObjectReference
{
    private static readonly JsonSerializerOptions _readOptions = new() { PropertyNameCaseInsensitive = true };
    public string? FailedStep { get; set; }
    public string? ReadJson { get; set; }
    public Queue<Task<string?>> ReadResults { get; } = new();
    public Dictionary<string, Queue<Task>> Gates { get; } = new(StringComparer.Ordinal);
    public List<string> StartedCalls { get; } = [];
    public List<string> ReadOwners { get; } = [];
    public List<string> SuccessfulCalls { get; } = [];
    public int ClearCalls { get; private set; }
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        StartedCalls.Add(identifier);
        if (Gates.TryGetValue(identifier, out var gates) && gates.Count > 0) { return AwaitGateAsync<TValue>(gates.Dequeue(), identifier, args, cancellationToken); }
        if (string.Equals(identifier, FailedStep, StringComparison.Ordinal))
        {
            FailedStep = null;
            return ValueTask.FromException<TValue>(new JSException($"Unavailable {identifier}"));
        }
        SuccessfulCalls.Add(identifier);
        if (identifier is "clearOperation" or "clear" or "remove") { ClearCalls++; }
        if (identifier is "read" or "readOperation")
        {
            ReadOwners.Add((string)args![1]!);
            return CompleteReadAsync<TValue>(identifier, ReadResults.Count > 0 ? ReadResults.Dequeue() : Task.FromResult(ReadJson));
        }
        return fallback.InvokeAsync<TValue>(identifier, cancellationToken, args);
    }
    private async ValueTask<TValue> AwaitGateAsync<TValue>(Task gate, string identifier, object?[]? args, CancellationToken cancellationToken)
    {
        await gate;
        return await InvokeAsync<TValue>(identifier, cancellationToken, args);
    }
    private static async ValueTask<TValue> CompleteReadAsync<TValue>(string identifier, Task<string?> pending)
    {
        var json = await pending;
        if (string.Equals(identifier, "readOperation", StringComparison.Ordinal)) { return (TValue)(object?)json!; }
        return json is null ? default! : JsonSerializer.Deserialize<TValue>(json, _readOptions)!;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
