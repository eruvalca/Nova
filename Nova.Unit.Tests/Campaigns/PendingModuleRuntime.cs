using Microsoft.JSInterop;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Holds one module import pending while forwarding unrelated interop to bUnit.
/// </summary>
internal sealed class PendingModuleRuntime(IJSRuntime fallback, string modulePath) : IJSRuntime
{
    private readonly TaskCompletionSource<IJSObjectReference> _import = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<string> _importedPaths = [];

    public IReadOnlyList<string> ImportedPaths => _importedPaths;

    public void CancelImport(CancellationToken cancellationToken) => _import.SetCanceled(cancellationToken);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        if (typeof(TValue) == typeof(IJSObjectReference)
            && string.Equals(identifier, "import", StringComparison.Ordinal)
            && args is [string requestedPath]
            && string.Equals(requestedPath, modulePath, StringComparison.Ordinal))
        {
            _importedPaths.Add(requestedPath);
            return AwaitImportAsync<TValue>();
        }

        return fallback.InvokeAsync<TValue>(identifier, cancellationToken, args);
    }

    private async ValueTask<TValue> AwaitImportAsync<TValue>() => (TValue)(object)await _import.Task;
}
