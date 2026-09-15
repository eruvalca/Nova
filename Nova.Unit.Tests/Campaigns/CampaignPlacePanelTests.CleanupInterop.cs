using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignPlacePanelTests
{
    private async Task<PlacementCleanupModule> DelayPlacementCleanupAsync(string identifier)
    {
        const string Path = "./_content/Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor.js";
        var fallback = await JSInterop.JSRuntime.InvokeAsync<IJSObjectReference>("import", Path);
        var module = new PlacementCleanupModule(fallback, identifier);
        Services.AddSingleton<IJSRuntime>(new PlacementCleanupRuntime(JSInterop.JSRuntime, module));
        return module;
    }
}

internal sealed class PlacementCleanupRuntime(IJSRuntime fallback, PlacementCleanupModule module) : IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        if (string.Equals(identifier, "import", StringComparison.Ordinal) && args?[0] is string path &&
            path.Contains("CampaignPlacePanel.razor.js", StringComparison.Ordinal))
        {
            return ValueTask.FromResult((TValue)(object)module);
        }
        return fallback.InvokeAsync<TValue>(identifier, cancellationToken, args);
    }
}

/// <summary>Delays an explicitly configured cleanup result without bUnit's missing-result timeout.</summary>
internal sealed class PlacementCleanupModule(IJSObjectReference fallback, string delayedIdentifier) : IJSObjectReference, IDisposable
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
    public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        if (string.Equals(identifier, delayedIdentifier, StringComparison.Ordinal))
        {
            Entered.TrySetResult();
            await Release.Task;
        }
        return await fallback.InvokeAsync<TValue>(identifier, cancellationToken, args);
    }
    public void Dispose() => Release.TrySetResult();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
