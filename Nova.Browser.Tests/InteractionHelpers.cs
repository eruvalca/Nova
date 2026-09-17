namespace Nova.Browser.Tests;

/// <summary>
/// Shared SSR-hydration interaction retry helpers for browser scenarios. All retry windows are
/// driven by <see cref="BrowserRetryPolicy"/>; per-interaction Playwright timeouts (for example the
/// 3s click timeout and the 400&nbsp;ms focus probe) remain hard-coded because they bound a single
/// interaction rather than the whole retry window.
/// </summary>
internal static class InteractionHelpers
{
    private const string InstallNavigationProbe = """
        () => {
            if (!window.__novaNavigationEvidence) {
                const probe = window.__novaNavigationEvidence = { started: 0, completed: 0, events: [] };
                const record = kind => {
                    probe.events.push({kind, url: location.href, key: window.navigation?.currentEntry?.key});
                    if (probe.events.length > 64) probe.events.shift();
                };
                Blazor.addEventListener('enhancednavigationstart', () => { probe.started++; record('start'); });
                Blazor.addEventListener('enhancednavigationend', () => { probe.completed = probe.started; record('end'); });
                window.addEventListener('popstate', () => record('popstate'));
                window.navigation?.addEventListener('currententrychange', () => record('entrychange'));
            }
            return window.__novaNavigationEvidence.started;
        }
        """;

    /// <summary>Waits for one enhanced navigation to finish applying its destination document.</summary>
    /// <param name="page">A page using Blazor's per-page enhanced navigation.</param>
    /// <param name="navigate">The single native link or browser-history action.</param>
    /// <returns>A task that completes after the destination response has been applied.</returns>
    public static async Task NavigateEnhancedAsync(IPage page, Func<Task> navigate)
    {
        var started = await page.EvaluateAsync<int>(InstallNavigationProbe);
        try
        {
            await navigate();
            await page.WaitForFunctionAsync("previous => { const probe = window.__novaNavigationEvidence; return probe?.started > previous && probe.completed === probe.started; }", started);
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException($"Enhanced navigation did not complete. {await CaptureNavigationAsync(page)}", exception);
        }
    }

    /// <summary>Retains navigation evidence when a later scenario action or assertion fails.</summary>
    /// <param name="page">The page under test.</param>
    /// <param name="scenario">The actions and assertions whose original failure must be preserved.</param>
    /// <returns>A task that completes when the scenario passes.</returns>
    public static async Task WithNavigationDiagnosticsAsync(IPage page, Func<Task> scenario)
    {
        try { await scenario(); }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Navigation scenario failed. {await CaptureNavigationAsync(page)}", exception);
        }
    }

    private static async Task<string> CaptureNavigationAsync(IPage page)
    {
        var evidence = $"URL: {page.Url}";
        try
        {
            evidence += await page.EvaluateAsync<string>("""
                () => JSON.stringify({url: location.href, navigation: window.__novaNavigationEvidence,
                    entries: window.navigation?.entries().map(entry => ({url: entry.url, key: entry.key})),
                    forms: document.querySelectorAll('#player-first-name').length,
                    links: [...document.querySelectorAll('main a')].map(a => ({text: a.textContent, href: a.href}))})
                """);
            evidence += " ARIA: " + await page.Locator("body").AriaSnapshotAsync();
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            // A closed page or replaced execution context must not obscure the original failure.
        }
        return evidence;
    }

    /// <summary>Repeatedly clicks a locator until the supplied settle predicate succeeds.</summary>
    /// <param name="page">The page to drive.</param>
    /// <param name="locator">The element to click.</param>
    /// <param name="settled">The predicate that reports when the interaction has settled.</param>
    /// <returns>A task that completes once the interaction has settled.</returns>
    public static async Task ClickUntilAsync(IPage page, ILocator locator, Func<Task<bool>> settled)
        => await ActUntilAsync(page, () => locator.ClickAsync(new() { Timeout = 3000 }), settled);

    /// <summary>
    /// Repeats an interaction until the supplied settle predicate succeeds, tolerating the SSR
    /// hydration window during which Blazor click/key handlers are not yet attached.
    /// </summary>
    /// <param name="page">The page to drive.</param>
    /// <param name="act">The interaction to repeat.</param>
    /// <param name="settled">The predicate that reports when the interaction has settled.</param>
    /// <returns>A task that completes once the interaction has settled.</returns>
    public static async Task ActUntilAsync(IPage page, Func<Task> act, Func<Task<bool>> settled)
    {
        for (var attempt = 0; attempt < BrowserRetryPolicy.MaxAttempts; attempt++)
        {
            if (await settled())
            {
                return;
            }

            try
            {
                await act();
            }
            catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
            {
                // The element was replaced mid-interaction, is not yet actionable (Playwright throws
                // System.TimeoutException when an action cannot complete within its timeout), or the
                // click was swallowed by the SSR hydration window; retry.
            }

            await page.WaitForTimeoutAsync(BrowserRetryPolicy.Delay);
        }

        var evidence = $"URL: {page.Url}";
        try
        {
            evidence = await page.EvaluateAsync<string>("""
                JSON.stringify({
                    url: location.href,
                    readyState: document.readyState,
                    alerts: [...document.querySelectorAll('[role="alert"], .invalid-feedback, .validation-message')].map(element => element.textContent),
                    text: document.querySelector('main')?.innerText.slice(0, 4000)
                })
                """);
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            // Preserve the original interaction failure when the page is no longer inspectable.
        }
        throw new TimeoutException($"Interaction did not settle within the retry window. {evidence}");
    }

    /// <summary>
    /// Presses Tab until the target receives keyboard focus, then returns. Fails if the target is
    /// never reached within the policy's attempt budget.
    /// </summary>
    /// <param name="page">The page to drive.</param>
    /// <param name="target">The locator expected to receive focus.</param>
    /// <returns>A task that completes once the target is focused.</returns>
    public static async Task TabUntilFocusedAsync(IPage page, ILocator target)
    {
        for (var attempt = 0; attempt < BrowserRetryPolicy.MaxAttempts; attempt++)
        {
            try
            {
                await Expect(target).ToBeFocusedAsync(new() { Timeout = 400 });
                return;
            }
            catch (PlaywrightException)
            {
                // Not focused yet; advance to the next tab stop.
                await page.Keyboard.PressAsync("Tab");
            }
        }

        throw new TimeoutException("The target never received keyboard focus.");
    }
}
