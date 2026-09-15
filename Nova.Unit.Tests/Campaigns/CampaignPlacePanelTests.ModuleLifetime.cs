using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignPlacePanelTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalReleasesTheImportedStorageModuleExactlyOnceAsync(bool loadedBeforeDisposal)
    {
        RegisterServices();
        const string Path = "./_content/Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor.js";
        var runtime = new PendingModuleRuntime(JSInterop.JSRuntime, Path);
        await using var module = new PlacementLifetimeModule(await JSInterop.JSRuntime.InvokeAsync<IJSObjectReference>("import", Path));
        Services.AddSingleton<IJSRuntime>(runtime);
        if (loadedBeforeDisposal) { runtime.CompleteImport(module); }
        var cut = RenderPanel();
        await cut.WaitForAssertionAsync(() => runtime.ImportedPaths.ShouldBe([Path]));

        var disposal = cut.Instance.DisposeAsync().AsTask();
        if (!loadedBeforeDisposal)
        {
            disposal.IsCompleted.ShouldBeFalse();
            runtime.CompleteImport(module);
        }
        await disposal;
        await cut.Instance.DisposeAsync();

        module.DisposeCount.ShouldBe(1);
        module.ReadCount.ShouldBe(loadedBeforeDisposal ? 1 : 0);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("javascript")]
    [InlineData("unavailable")]
    [InlineData("cancelled")]
    public async Task DisposalCompletesWhenThePendingStorageImportFailsAsync(string failure)
    {
        RegisterServices();
        const string Path = "./_content/Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor.js";
        var runtime = new PendingModuleRuntime(JSInterop.JSRuntime, Path);
        Services.AddSingleton<IJSRuntime>(runtime);
        var cut = RenderPanel();
        await cut.WaitForAssertionAsync(() => runtime.ImportedPaths.ShouldBe([Path]));
        var disposal = cut.Instance.DisposeAsync().AsTask();
        disposal.IsCompleted.ShouldBeFalse();

        if (string.Equals(failure, "cancelled", StringComparison.Ordinal)) { runtime.CancelImport(Xunit.TestContext.Current.CancellationToken); }
        else
        {
            runtime.FailImport(string.Equals(failure, "javascript", StringComparison.Ordinal)
            ? new JSException("import failed") : new InvalidOperationException("interop unavailable"));
        }
        await disposal;
        await cut.Instance.DisposeAsync();

        runtime.ImportedPaths.ShouldBe([Path]);
    }
}

/// <summary>Records lifetime effects while forwarding interop to the real bUnit module handler.</summary>
internal sealed class PlacementLifetimeModule(IJSObjectReference fallback) : IJSObjectReference
{
    public int ReadCount { get; private set; }
    public int DisposeCount { get; private set; }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        => InvokeAsync<TValue>(identifier, default, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        if (string.Equals(identifier, "readRecovery", StringComparison.Ordinal)) { ReadCount++; }
        return fallback.InvokeAsync<TValue>(identifier, cancellationToken, args);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return fallback.DisposeAsync();
    }
}
