namespace Nova.Browser.Tests;

/// <summary>Observable, non-submitting attachment checks shared by evaluation browser scenarios.</summary>
internal static class EvaluationInteractionHelpers
{
    /// <summary>Observes the storage-failure state created by interactive recovery without changing retained data.</summary>
    /// <param name="page">The evaluation page whose recovery storage is unreadable.</param>
    /// <returns>A task that completes once interop has reported the blocked recovery state.</returns>
    public static async Task AssertRecoveryBlockedAttachedAsync(IPage page)
    {
        var alert = page.Locator(".evaluation-workspace [role='alert']").Filter(new() { HasText = "Tab storage is unavailable." });
        var retry = alert.GetByRole(AriaRole.Button, new() { Name = "Retry storage", Exact = true });
        // This alert is set only after the interactive JS recovery read fails. Observe it within
        // the shared post-reload hydration budget; never click Retry or alter the protected draft.
        await InteractionHelpers.ActUntilAsync(page, () => Task.CompletedTask, () => retry.IsVisibleAsync());
        await Expect(alert).ToContainTextAsync("Nothing will be submitted until recovery storage is working.");
        await Expect(page.Locator(".evaluation-save")).ToBeDisabledAsync();
    }

    /// <summary>Proves the composer handles input after storage recovery, then restores its empty state.</summary>
    /// <param name="page">The evaluation page with a selected participant and an empty composer.</param>
    /// <returns>A task that completes after input binding and saving readiness are verified.</returns>
    public static async Task AssertComposerAttachedAsync(IPage page)
    {
        var note = page.Locator("#evaluation-note");
        var count = page.Locator("#evaluation-note-help");
        // Save is deliberately disabled during attachment and recovery. Prove a handled input
        // through the shared hydration policy before checking readiness or taking captures.
        await InteractionHelpers.ActUntilAsync(page, async () =>
        {
            if (await note.CountAsync() > 0 && await note.IsEditableAsync())
            {
                await note.FillAsync("Attach probe", new() { Timeout = 3000 });
            }
        }, async () =>
        {
            var alerts = await page.Locator(".evaluation-workspace [role='alert']").AllTextContentsAsync();
            if (alerts.Count > 0)
            {
                var message = string.Join(" | ", alerts);
                throw new InvalidOperationException($"Evaluation startup failed: {message[..Math.Min(message.Length, 4000)]}");
            }
            return await count.CountAsync() > 0 && (await count.InnerTextAsync()).Contains("12 / 4000", StringComparison.Ordinal);
        });
        await note.FillAsync(string.Empty);
        await Expect(count).ToContainTextAsync("0 / 4000");
        await Expect(page.Locator(".evaluation-save")).ToBeEnabledAsync();
    }
}
