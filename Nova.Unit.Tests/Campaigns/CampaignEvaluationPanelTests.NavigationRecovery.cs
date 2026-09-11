using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Nova.SharedKernel.Features.Campaigns;
using Nova.UI.Features.Campaigns.Services;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignEvaluationPanelTests
{
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
