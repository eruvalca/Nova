namespace Nova.Browser.Tests;

/// <summary>Observable, non-submitting attachment checks shared by evaluation browser scenarios.</summary>
internal static class EvaluationInteractionHelpers
{
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
