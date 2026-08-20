namespace Nova.Browser.Tests;

/// <summary>
/// Moves an <see cref="InteractiveAuto"/> page from its first-visit InteractiveServer render to
/// WebAssembly so that list loads and mutations become browser <c>/api/...</c> fetches, which
/// Playwright <c>RouteAsync</c> can then intercept.
/// </summary>
/// <remarks>
/// <see cref="InteractiveAuto"/> renders on the server for the first visit and on WebAssembly once the
/// WASM runtime has finished downloading <em>and booting</em>; the switch takes effect on the next full
/// document load, not within the current document. The bounded localhost delay below lets that
/// background boot complete before the reload that activates WebAssembly.
/// </remarks>
public static class WasmWarmupHelper
{
    private const int WebAssemblyBootDelayMilliseconds = 15_000;

    /// <summary>
    /// Waits for the background WebAssembly runtime to finish booting, then forces a full document
    /// load so the next render — and every subsequent interactive transition — runs client-side.
    /// Callers re-assert their page's "loaded" state after the reload.
    /// </summary>
    /// <param name="page">The page currently rendered on the InteractiveServer circuit.</param>
    public static async Task ReloadAsWebAssemblyAsync(IPage page)
    {
        await page.WaitForTimeoutAsync(WebAssemblyBootDelayMilliseconds);
        await page.ReloadAsync();
    }
}
