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
/// and then <em>verifies</em> the switch by confirming the reloaded page did <em>not</em> re-establish
/// the InteractiveServer SignalR circuit (<c>/_blazor/negotiate</c>), retrying with more boot time if it
/// did.
/// </remarks>
public static class WasmWarmupHelper
{
    private const int WebAssemblyBootDelayMilliseconds = 15_000;
    private const int NegotiateSettleDelayMilliseconds = 3_000;
    private const int MaxReloadAttempts = 3;

    /// <summary>
    /// Lets the background WASM runtime finish booting, reloads the page, and verifies it switched to
    /// WebAssembly by confirming no <c>/_blazor/negotiate</c> request was issued — that request only
    /// occurs while the page is still rendering on InteractiveServer. If the circuit was re-established
    /// (the switch did not happen), it waits for more boot time and reloads again, up to a bounded
    /// number of attempts. Callers re-assert their page's "loaded" state after the reload.
    /// </summary>
    /// <param name="page">The page currently rendered on the InteractiveServer circuit.</param>
    public static async Task ReloadAsWebAssemblyAsync(IPage page)
    {
        for (var attempt = 1; attempt <= MaxReloadAttempts; attempt++)
        {
            // Let the background WASM runtime finish booting before this reload. Waiting *before* the
            // reload preserves the InteractiveAuto switch: a reload too early tears down the booting
            // runtime's context before it has finished initializing.
            await page.WaitForTimeoutAsync(WebAssemblyBootDelayMilliseconds);

            // A reloaded InteractiveServer page re-establishes its SignalR circuit via /_blazor/negotiate;
            // a reloaded WebAssembly page does not. Its absence is therefore the switch's proof.
            var reestablishedCircuit = false;

            void OnRequest(object? sender, IRequest request)
            {
                if (request.Url.Contains("/_blazor/negotiate", StringComparison.Ordinal))
                {
                    reestablishedCircuit = true;
                }
            }

            page.Request += OnRequest;
            try
            {
                await page.ReloadAsync();
                // Give the boot script time to negotiate if it is going to stay on the server circuit.
                await page.WaitForTimeoutAsync(NegotiateSettleDelayMilliseconds);
            }
            finally
            {
                page.Request -= OnRequest;
            }

            if (!reestablishedCircuit)
            {
                return;
            }
        }

        throw new TimeoutException(
            $"Page did not switch to WebAssembly after {MaxReloadAttempts} reloads; the InteractiveServer circuit (/_blazor/negotiate) was re-established each time.");
    }
}
