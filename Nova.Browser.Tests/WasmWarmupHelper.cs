namespace Nova.Browser.Tests;

/// <summary>
/// Moves an <see cref="InteractiveAuto"/> page from its first-visit InteractiveServer render to
/// WebAssembly so that list loads and mutations become browser <c>/api/...</c> fetches, which
/// Playwright <c>RouteAsync</c> can then intercept.
/// </summary>
/// <remarks>
/// <see cref="InteractiveAuto"/> renders on the server for the first visit and on WebAssembly once the
/// WASM runtime has finished downloading <em>and booting</em>; the switch takes effect on the next full
/// document load, not within the current document. This helper lets the runtime finish booting, reloads,
/// and watches for the InteractiveServer SignalR circuit (<c>/_blazor/negotiate</c>), retrying with
/// more boot time if it appears. A caller-supplied interaction probe establishes positive attachment
/// evidence while that listener remains active. Without a probe, the helper only warms the runtime;
/// absence of a negotiation request within a fixed delay does not prove interactive attachment.
/// </remarks>
internal static class WasmWarmupHelper
{
    private const int WebAssemblyBootDelayMilliseconds = 15_000;
    private const int NegotiateSettleDelayMilliseconds = 3_000;
    private const int MaxReloadAttempts = 3;

    /// <summary>
    /// Lets the background WASM runtime finish booting and reloads within a bounded attempt budget.
    /// With an interaction probe, returns after the probe completes without observing a server circuit.
    /// Keep using that document: a later full navigation discards its attachment evidence.
    /// </summary>
    /// <param name="page">The page currently rendered on the InteractiveServer circuit.</param>
    /// <param name="assertAttached">Optional positive UI interaction, including any cleanup needed
    /// before another reload. Static prerendered content is not attachment evidence.</param>
    public static async Task ReloadAsWebAssemblyAsync(IPage page, Func<Task>? assertAttached = null)
    {
        for (var attempt = 1; attempt <= MaxReloadAttempts; attempt++)
        {
            // Let the background WASM runtime finish booting before this reload. Waiting *before* the
            // reload preserves the InteractiveAuto switch: a reload too early tears down the booting
            // runtime's context before it has finished initializing.
            await page.WaitForTimeoutAsync(WebAssemblyBootDelayMilliseconds);
            if (!await ReloadAndObserveAttachmentAsync(page, assertAttached))
            {
                return;
            }
        }

        throw new TimeoutException(
            $"Page did not switch to WebAssembly after {MaxReloadAttempts} reloads; the InteractiveServer circuit (/_blazor/negotiate) was re-established each time.");
    }

    /// <summary>Observes one complete reload and probe, returning whether a server circuit requires another attempt.</summary>
    private static async Task<bool> ReloadAndObserveAttachmentAsync(IPage page, Func<Task>? assertAttached)
    {
        var reestablishedCircuit = false;
        var messages = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var messageCount = 0;
        void Record(string message)
        {
            if (Interlocked.Increment(ref messageCount) <= 12) { messages.Enqueue(BoundDiagnostic(message, 1500)); }
        }
        void OnConsole(object? sender, IConsoleMessage message)
        {
            if (message.Type is "error" or "warning") { Record($"Console {message.Type}: {message.Text}"); }
        }
        void OnPageError(object? sender, string message) => Record($"Page error: {message}");
        void OnRequest(object? sender, IRequest request)
        {
            if (request.Url.Contains("/_blazor/negotiate", StringComparison.Ordinal)) { reestablishedCircuit = true; }
        }

        // Keep observation active throughout the probe, including late negotiation under load.
        page.Request += OnRequest;
        page.Console += OnConsole;
        page.PageError += OnPageError;
        try
        {
            await page.ReloadAsync();
            if (assertAttached is not null) { await assertAttached(); }
            else { await page.WaitForTimeoutAsync(NegotiateSettleDelayMilliseconds); }
        }
        catch (Exception exception) when (reestablishedCircuit && exception is PlaywrightException or TimeoutException)
        {
            // Retry only a document known to have selected InteractiveServer.
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException or InvalidOperationException)
        {
            var snapshot = await CaptureFailureSnapshotAsync(page);
            throw new InvalidOperationException($"WebAssembly attachment probe failed at {page.Url}.\n{string.Join("\n", messages)}\nPage snapshot:\n{snapshot}", exception);
        }
        finally
        {
            page.Request -= OnRequest;
            page.Console -= OnConsole;
            page.PageError -= OnPageError;
        }
        return reestablishedCircuit;
    }

    private static async Task<string> CaptureFailureSnapshotAsync(IPage page)
    {
        try { return BoundDiagnostic(await page.Locator("body").AriaSnapshotAsync(), 12_000); }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            return $"Snapshot unavailable: {BoundDiagnostic(exception.Message, 1500)}";
        }
    }

    private static string BoundDiagnostic(string value, int limit) => value.Length <= limit ? value : value[..limit] + " [truncated]";
}
