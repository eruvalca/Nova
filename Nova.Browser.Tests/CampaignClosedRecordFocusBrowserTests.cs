using System.Collections.Concurrent;
using Microsoft.AspNetCore.WebUtilities;
using Shouldly;

namespace Nova.Browser.Tests;

public sealed partial class CampaignClosedRecordBrowserTests
{
    [Fact]
    public async Task FailedRecordRefreshKeepsHistoryRetryableAndFocusesRestoredHeadingAsync()
    {
        var seed = await SeedClosedAsync();
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=close&closeParticipant={seed.AssignmentIds[0]}").ToString());
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page, async () =>
        {
            await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Review reopen", Exact = true }),
                () => page.Locator(".confirmation").IsVisibleAsync());
            await page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        });
        await Expect(page.Locator(".participant-history li")).ToHaveCountAsync(20);
        var errors = new ConcurrentQueue<string>();
        page.PageError += (_, message) => errors.Enqueue(message);
        page.Console += (_, message) =>
        {
            if (message.Text.Contains("Unable to focus an invalid element", StringComparison.Ordinal)
                || message.Text.Contains("Unhandled exception rendering component", StringComparison.Ordinal))
            {
                errors.Enqueue(message.Text);
            }
        };
        var routePattern = $"**/api/campaigns/{seed.CampaignId}/closed-roster?**";
        var failedReads = 0;
        await page.RouteAsync(routePattern, async route =>
        {
            var query = QueryHelpers.ParseQuery(new Uri(route.Request.Url).Query);
            if (query.ContainsKey("participantId") || !string.Equals(query["sortBy"], "closeout", StringComparison.Ordinal))
            {
                await route.ContinueAsync();
                return;
            }
            Interlocked.Increment(ref failedReads);
            await route.FulfillAsync(new() { Status = 500 });
        });
        try
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Review reopen", Exact = true }).ClickAsync();
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Retry record", Exact = true })).ToBeVisibleAsync();
            await Expect(page.Locator("#closed-history-heading")).ToHaveCountAsync(0);
            failedReads.ShouldBe(1);
        }
        finally { await page.UnrouteAsync(routePattern); }

        await page.GetByRole(AriaRole.Button, new() { Name = "Retry record", Exact = true }).ClickAsync();
        await Expect(page.Locator(".participant-history li")).ToHaveCountAsync(20);
        await Expect(page.Locator("#closed-history-heading")).ToBeFocusedAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Earlier changes", Exact = true }).ClickAsync();
        await Expect(page.Locator(".participant-history li")).ToHaveCountAsync(3);
        await Expect(page.Locator("#closed-history-heading")).ToBeFocusedAsync();
        errors.ShouldBeEmpty();
    }
}
