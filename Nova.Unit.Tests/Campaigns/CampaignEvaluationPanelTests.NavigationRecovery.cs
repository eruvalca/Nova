using System.Text.Json.Nodes;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Nova.SharedKernel.Features.Campaigns;
using Nova.UI.Features.Campaigns.Services;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignEvaluationPanelTests
{
    [Fact]
    public async Task EvaluationUnreadableRecoveryCanKeepWorkingThenLeaveWithoutChangingStoredDataAsync()
    {
        var (storage, module) = PrepareEvaluationUnreadableNavigation();
        module.ResumeResults.Enqueue(Task.FromResult(false));
        var cut = Panel(new() { ParticipantId = 301 });
        await cut.WaitForAssertionAsync(() => Button(cut, "Retry storage").ShouldNotBeNull());
        await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/retained", "retained-history"));
        cut.Find(".evaluation-protection").TextContent.ShouldContain("a previous submission's outcome may be unknown");

        await Button(cut, "Keep working").ClickAsync(new MouseEventArgs());

        cut.FindAll(".evaluation-protection").ShouldBeEmpty();
        module.ReleasedOwners.ShouldBeEmpty();
        await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/retained", "retained-history"));
        await Button(cut, "Leave and keep recovery data").ClickAsync(new MouseEventArgs());
        module.CanceledOwners.ShouldBe([module.Owner]);
        cut.Markup.ShouldContain("Navigation was interrupted");
        await Button(cut, "Leave and keep recovery data").ClickAsync(new MouseEventArgs());

        module.RetainUnreadable.ShouldBe([true, true]);
        module.ResumedKeys.ShouldBe(["retained-history", "retained-history"]);
        cut.FindAll(".evaluation-protection").ShouldBeEmpty();
        storage.ReadJson.ShouldBe("{}");
        storage.ClearCalls.ShouldBe(0);
        module.WriteCalls.ShouldBe(0);
        _storage.Writes.ShouldBeEmpty();
        _notes.ReceivedCalls().ShouldBeEmpty();
        _tags.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvaluationUnreadableDepartureCannotOutliveRestorationOrOwnerChangeAsync(bool changeOwner)
    {
        var (storage, module) = PrepareEvaluationUnreadableNavigation();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cleanup = new NavigationCompletionCleanup(() => release.TrySetResult());
        module.ReleaseResults.Enqueue(release.Task);
        var cut = Panel(new() { ParticipantId = 301 });
        await cut.WaitForAssertionAsync(() => Button(cut, "Retry storage").ShouldNotBeNull());
        var owner = module.Owner;
        var originalUrl = Services.GetRequiredService<NavigationManager>().Uri;
        await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(owner, module.Lease, "/obsolete", "obsolete-history"));
        var leave = Button(cut, "Leave and keep recovery data").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => module.ReleasedOwners.ShouldBe([owner]));
        module.WriteCalls.ShouldBe(0);
        if (changeOwner)
        {
            await cut.InvokeAsync(() => cut.Render(parameters => parameters.Add(component => component.State, new CampaignWorkspaceEvaluationState { ParticipantId = 302 })));
            await cut.WaitForAssertionAsync(() => module.Owner.ShouldNotBe(owner, StringComparer.Ordinal));
        }
        else
        {
            storage.ReadJson = EvaluationRecoverySnapshot(new() { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = 301, Content = "Original pending evidence" });
            await Button(cut, "Retry storage").ClickAsync(new MouseEventArgs());
            cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("Original pending evidence");
        }
        if (changeOwner) { await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/newest", "newest-history")); }
        var writesBeforeReleaseCompletes = module.WriteCalls;

        release.SetResult();
        await leave;

        if (!changeOwner) { await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/newest", "newest-history")); }
        module.ResumedKeys.ShouldBeEmpty();
        module.RetainUnreadable.ShouldBe([true]);
        module.ReleasedOwners.ShouldBe([owner]);
        Services.GetRequiredService<NavigationManager>().Uri.ShouldBe(originalUrl);
        if (changeOwner) { module.CanceledOwners.ShouldNotContain(module.Owner, StringComparer.Ordinal); cut.Markup.ShouldContain("Alex Morgan"); }
        else
        {
            cut.FindAll(".evaluation-protection button").ShouldContain(button => string.Equals(button.TextContent.Trim(), "Retry original operation", StringComparison.Ordinal));
            cut.FindAll("button").ShouldNotContain(button => button.TextContent.Contains("Leave and keep", StringComparison.Ordinal));
            JsonNode.DeepEquals(JsonNode.Parse(storage.ReadJson!)!["Pending"], JsonNode.Parse(_storage.Writes.Single())!["Pending"]).ShouldBeTrue();
        }
        storage.ClearCalls.ShouldBe(0);
        module.WriteCalls.ShouldBe(writesBeforeReleaseCompletes);
        _notes.ReceivedCalls().ShouldBeEmpty();
        _tags.ReceivedCalls().ShouldBeEmpty();
    }

    private (StorageRecoveryModule Storage, NavigationRecoveryModule Navigation) PrepareEvaluationUnreadableNavigation()
    {
        Services.AddSingleton(_ => new StorageRecoveryModule(_storage) { ReadJson = "{}" });
        Services.AddSingleton(provider => new NavigationRecoveryModule(provider.GetRequiredService<StorageRecoveryModule>()));
        Services.AddSingleton<IJSRuntime>(provider => new NavigationRecoveryRuntime(JSInterop.JSRuntime, provider.GetRequiredService<NavigationRecoveryModule>()));
        return (Services.GetRequiredService<StorageRecoveryModule>(), Services.GetRequiredService<NavigationRecoveryModule>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EvaluationInterruptedHistoryDepartureAcceptsNewestPromptAndRetryAsync(bool throws, bool newerBeforeCompletion)
    {
        var module = PrepareEvaluationNavigation();
        var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cleanup = new NavigationCompletionCleanup(() => resume.TrySetResult(false));
        module.ResumeResults.Enqueue(resume.Task);
        var cut = Panel(new() { ParticipantId = 301 });
        await cut.Find("#evaluation-note").InputAsync(new ChangeEventArgs { Value = "Original draft" });
        await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/original", "old-history"));
        var discard = Button(cut, "Discard and leave").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => module.ResumedKeys.ShouldBe(["old-history"]));
        if (newerBeforeCompletion)
        {
            await cut.Find("#evaluation-note").InputAsync(new ChangeEventArgs { Value = "New draft during departure" });
            await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/newest", "new-history"));
            cut.FindAll(".evaluation-protection").Count.ShouldBe(1);
        }
        if (throws) { resume.SetException(new JSException("Original history replay failed")); }
        else { resume.SetResult(false); }
        await discard;
        await cut.WaitForAssertionAsync(() => Button(cut, "Discard and leave").ShouldNotBeNull());
        if (!newerBeforeCompletion)
        {
            cut.Markup.ShouldContain(throws ? "Navigation could not continue" : "Navigation was interrupted");
            await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/newest", "new-history"));
        }
        else
        {
            cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("New draft during departure");
            cut.Markup.ShouldNotContain("Navigation could not continue");
            cut.Markup.ShouldNotContain("Navigation was interrupted");
        }

        await Button(cut, "Discard and leave").ClickAsync(new MouseEventArgs());

        module.ResumedKeys.ShouldBe(["old-history", "new-history"]);
        module.RetainUnreadable.ShouldBe([false, false]);
        module.ReleasedOwners.Count.ShouldBe(2);
        module.ReleasedOwners.ShouldAllBe(owner => string.Equals(owner, module.Owner, StringComparison.Ordinal));
        Services.GetRequiredService<NavigationManager>().Uri.ShouldNotContain("/original");
        cut.FindAll(".evaluation-protection").ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvaluationOldReleaseCompletionCannotNavigateOrOverwriteNewOwnerAsync(bool throws)
    {
        var module = PrepareEvaluationNavigation();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cleanup = new NavigationCompletionCleanup(() => release.TrySetResult());
        module.ReleaseResults.Enqueue(release.Task);
        var cut = Panel(new() { ParticipantId = 301 });
        var originalOwner = module.Owner;
        var originalUrl = Services.GetRequiredService<NavigationManager>().Uri;
        await cut.Find("#evaluation-note").InputAsync(new ChangeEventArgs { Value = "Original draft" });
        await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/old-owner-destination", null));
        var discard = Button(cut, "Discard and leave").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => module.ReleasedOwners.ShouldBe([originalOwner]));
        await cut.InvokeAsync(() => cut.Render(parameters => parameters.Add(component => component.State, new CampaignWorkspaceEvaluationState { ParticipantId = 302 })));
        await cut.WaitForAssertionAsync(() => module.Owner.ShouldNotBe(originalOwner, StringComparer.Ordinal));
        await cut.Find("#evaluation-note").InputAsync(new ChangeEventArgs { Value = "New owner draft" });
        await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/new-owner-destination", "new-owner-history"));

        if (throws) { release.SetException(new JSException("Old owner release failed")); }
        else { release.SetResult(); }
        await discard;

        Services.GetRequiredService<NavigationManager>().Uri.ShouldBe(originalUrl);
        module.ReleasedOwners.ShouldBe([originalOwner]);
        module.CanceledOwners.ShouldNotContain(module.Owner, StringComparer.Ordinal);
        module.ResumedKeys.ShouldBeEmpty();
        cut.Markup.ShouldContain("Alex Morgan");
        cut.Markup.ShouldNotContain("Navigation could not continue");
        cut.Markup.ShouldNotContain("Navigation was interrupted");
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("New owner draft");
        await Button(cut, "Keep working").ClickAsync(new MouseEventArgs());
        cut.FindAll(".evaluation-protection").ShouldBeEmpty();
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("New owner draft");
    }

    [Fact]
    public async Task EvaluationNewDraftDuringReleaseRevokesDepartureWithoutLosingDraftAsync()
    {
        var module = PrepareEvaluationNavigation();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cleanup = new NavigationCompletionCleanup(() => release.TrySetResult());
        module.ReleaseResults.Enqueue(release.Task);
        var cut = Panel(new() { ParticipantId = 301 });
        var owner = module.Owner;
        var originalUrl = Services.GetRequiredService<NavigationManager>().Uri;
        await cut.Find("#evaluation-note").InputAsync(new ChangeEventArgs { Value = "Original draft" });
        await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(owner, module.Lease, "/history-target", "history-key"));
        var departure = Button(cut, "Discard and leave").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => module.ReleasedOwners.ShouldBe([owner]));

        await cut.Find("#evaluation-note").InputAsync(new ChangeEventArgs { Value = "New draft while release waits" });
        release.SetResult();
        await departure;

        module.CanceledOwners.ShouldBe([owner]);
        module.ResumedKeys.ShouldBeEmpty();
        Services.GetRequiredService<NavigationManager>().Uri.ShouldBe(originalUrl);
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("New draft while release waits");
        await Button(cut, "Keep working").ClickAsync(new MouseEventArgs());
        cut.FindAll(".evaluation-protection").ShouldBeEmpty();
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("New draft while release waits");
    }

    [Fact]
    public async Task EvaluationDuplicateDiscardDuringPendingPersistenceIssuesOneDepartureAsync()
    {
        var module = PrepareEvaluationNavigation();
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cleanup = new NavigationCompletionCleanup(() => persisted.TrySetResult());
        var cut = Panel(new() { ParticipantId = 301 });
        await cut.Find("#evaluation-note").InputAsync(new ChangeEventArgs { Value = "Original draft" });
        var writesBeforeDiscard = module.WriteCalls;
        module.WriteGates.Enqueue(persisted.Task);
        await cut.InvokeAsync(() => cut.Instance.ProtectNativeNavigationAsync(module.Owner, module.Lease, "/history-target", "history-key"));
        var departure = Button(cut, "Discard and leave").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => module.WriteCalls.ShouldBe(writesBeforeDiscard + 1));

        await Button(cut, "Discard and leave").ClickAsync(new MouseEventArgs());

        module.WriteCalls.ShouldBe(writesBeforeDiscard + 1);
        module.ReleasedOwners.ShouldBeEmpty();
        module.ResumedKeys.ShouldBeEmpty();
        persisted.SetResult();
        await departure;
        module.ReleasedOwners.ShouldBe([module.Owner]);
        module.ResumedKeys.ShouldBe(["history-key"]);
        cut.FindAll(".evaluation-protection").ShouldBeEmpty();
    }

    private NavigationRecoveryModule PrepareEvaluationNavigation()
    {
        var module = new NavigationRecoveryModule(_storage);
        Services.AddSingleton<IJSRuntime>(new NavigationRecoveryRuntime(JSInterop.JSRuntime, module));
        return module;
    }
}

internal sealed class NavigationRecoveryRuntime(IJSRuntime fallback, NavigationRecoveryModule module) : IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        if (string.Equals(identifier, "import", StringComparison.Ordinal) && args?[0] is string path &&
            (path.Contains("CampaignEvaluationPanel.razor.js", StringComparison.Ordinal) || path.Contains("CampaignParticipantDrawer.razor.js", StringComparison.Ordinal)))
        {
            return ValueTask.FromResult((TValue)(object)module);
        }
        return fallback.InvokeAsync<TValue>(identifier, cancellationToken, args);
    }
}

internal sealed class NavigationCompletionCleanup(Action complete) : IDisposable
{
    public void Dispose() => complete();
}

/// <summary>Controls real navigation interop boundaries while retaining the surface's normal storage fake.</summary>
internal sealed class NavigationRecoveryModule(IJSObjectReference fallback) : IJSObjectReference
{
    public string Owner { get; private set; } = string.Empty;
    public string Lease { get; private set; } = string.Empty;
    public Queue<Task> ReleaseResults { get; } = new();
    public Queue<Task<bool>> ResumeResults { get; } = new();
    public Queue<Task> WriteGates { get; } = new();
    public int WriteCalls { get; private set; }
    public List<string> ReleasedOwners { get; } = [];
    public List<string> CanceledOwners { get; } = [];
    public List<string> ResumedKeys { get; } = [];
    public List<bool> RetainUnreadable { get; } = [];
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        if (identifier is "attach" or "protectNavigation") { Owner = (string)args![1]!; Lease = (string)args[2]!; }
        if (string.Equals(identifier, "write", StringComparison.Ordinal))
        {
            WriteCalls++;
            if (WriteGates.Count > 0) { return AwaitWriteAsync<TValue>(WriteGates.Dequeue(), args, cancellationToken); }
        }
        if (string.Equals(identifier, "cancelNavigation", StringComparison.Ordinal)) { CanceledOwners.Add((string)args![1]!); }
        if (string.Equals(identifier, "releaseNavigation", StringComparison.Ordinal))
        {
            ReleasedOwners.Add((string)args![1]!);
            RetainUnreadable.Add(args.Length > 3 && args[3] is true);
            return AwaitReleaseAsync<TValue>(ReleaseResults.Count > 0 ? ReleaseResults.Dequeue() : Task.CompletedTask);
        }
        if (string.Equals(identifier, "resumeHistory", StringComparison.Ordinal))
        {
            ResumedKeys.Add((string)args![3]!);
            return AwaitResumeAsync<TValue>(ResumeResults.Count > 0 ? ResumeResults.Dequeue() : Task.FromResult(true));
        }
        return fallback.InvokeAsync<TValue>(identifier, cancellationToken, args);
    }
    private static async ValueTask<TValue> AwaitReleaseAsync<TValue>(Task result) { await result; return default!; }
    private static async ValueTask<TValue> AwaitResumeAsync<TValue>(Task<bool> result) => (TValue)(object)await result;
    private async ValueTask<TValue> AwaitWriteAsync<TValue>(Task gate, object?[]? args, CancellationToken cancellationToken)
    {
        await gate;
        return await fallback.InvokeAsync<TValue>("write", cancellationToken, args);
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
