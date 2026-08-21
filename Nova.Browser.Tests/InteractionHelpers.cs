namespace Nova.Browser.Tests;

/// <summary>
/// Shared SSR-hydration interaction retry helpers for browser scenarios. All retry windows are
/// driven by <see cref="BrowserRetryPolicy"/>; per-interaction Playwright timeouts (for example the
/// 3s click timeout and the 400&nbsp;ms focus probe) remain hard-coded because they bound a single
/// interaction rather than the whole retry window.
/// </summary>
internal static class InteractionHelpers
{
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

        throw new TimeoutException("Interaction did not settle within the retry window.");
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
