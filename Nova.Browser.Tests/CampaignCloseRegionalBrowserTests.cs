using Shouldly;

namespace Nova.Browser.Tests;

public sealed partial class CampaignCloseBrowserTests
{
    [Fact]
    public async Task PhoneRosterFailureKeepsReadinessAndSupportsRegionalRetryAsync()
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, EvaluationSeed.Password, new() { Width = 390, Height = 844 });
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=close").ToString());
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page, async () =>
        {
            await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Review close", Exact = true }),
                () => page.Locator(".confirmation").IsVisibleAsync());
            await page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        });
        var routePattern = $"**/api/campaigns/{seed.CampaignId}/effective-placements?**";
        var requests = 0;
        await page.RouteAsync(routePattern, async route =>
        {
            Interlocked.Increment(ref requests);
            await route.FulfillAsync(new() { Status = 500 });
        });
        try
        {
            await page.Locator("#close-search").FillAsync("Player");
            await page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
            await AssertPhoneTargetAsync(page.GetByRole(AriaRole.Button, new() { Name = "Retry roster", Exact = true }));
            await Expect(page.Locator(".close-review")).ToContainTextAsync("Ready to close");
            await CaptureAsync(page, "phone-roster-error");
            requests.ShouldBe(1);
        }
        finally { await page.UnrouteAsync(routePattern); }
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry roster", Exact = true }).ClickAsync();
        await Expect(page.Locator(".close-roster tbody a")).ToHaveCountAsync(50);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Retry roster", Exact = true })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AcceptedCloseResponseRequiresFreshReviewWithoutClaimingCompletionAsync()
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=close").ToString());
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page, async () =>
        {
            await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Review close", Exact = true }),
                () => page.Locator(".confirmation").IsVisibleAsync());
            await page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        });
        var routePattern = $"**/api/campaigns/{seed.CampaignId}/close";
        var requests = 0;
        await page.RouteAsync(routePattern, async route => { Interlocked.Increment(ref requests); await route.FulfillAsync(new() { Status = 202 }); });
        try
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Review close", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Close campaign", Exact = true }).ClickAsync();
            await Expect(page.Locator(".lifecycle-checkpoint")).ToContainTextAsync("request outcome is unknown");
            await Expect(page.Locator(".lifecycle-checkpoint")).ToContainTextAsync("Current state: Active");
            await Expect(page.Locator(".lifecycle-checkpoint")).Not.ToContainTextAsync("Campaign closed.");
            await Expect(page.Locator(".confirmation")).ToHaveCountAsync(0);
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Review close", Exact = true })).ToBeEnabledAsync();
            requests.ShouldBe(1);
        }
        finally { await page.UnrouteAsync(routePattern); }
    }
}
