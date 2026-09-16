using Microsoft.EntityFrameworkCore;
using Nova.SharedKernel.Enums;
using Shouldly;

namespace Nova.Browser.Tests;

public sealed partial class CampaignCloseBrowserTests
{
    [Fact]
    public async Task LostCloseResponseShowsUnknownAttemptAndCurrentClosedStateWithoutReplayAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, token);
        await using var member = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var memberPage = member.Pages[0];
        await memberPage.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=place&placementParticipant={seed.AssignmentIds[0]}").ToString());
        await using var admin = await fixture.NewSignedInContextAsync(seed.AdminEmail, EvaluationSeed.Password);
        var page = admin.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=close").ToString());
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page, async () =>
        {
            await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Review close", Exact = true }),
                () => page.Locator(".confirmation").IsVisibleAsync());
            await page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        });
        var requests = 0;
        await page.RouteAsync($"**/api/campaigns/{seed.CampaignId}/close", async route =>
        {
            Interlocked.Increment(ref requests);
            var response = await route.FetchAsync();
            response.Status.ShouldBe(204);
            await route.AbortAsync("failed");
        });
        await page.GetByRole(AriaRole.Button, new() { Name = "Review close", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Close campaign", Exact = true }).ClickAsync();
        await Expect(page.Locator(".lifecycle-checkpoint")).ToContainTextAsync("request outcome is unknown");
        await Expect(page.Locator("#lifecycle-heading")).ToHaveTextAsync("Closed campaign");
        await Expect(page.Locator(".lifecycle-checkpoint")).Not.ToContainTextAsync("Campaign closed.");
        await Expect(page.Locator(".confirmation")).ToHaveCountAsync(0);
        requests.ShouldBe(1);
        await memberPage.ReloadAsync();
        await Expect(memberPage.Locator("#place-outcome, #place-team")).ToHaveCountAsync(0);
        await Expect(memberPage.Locator(".readiness-context")).ToContainTextAsync("Campaign record is read-only");
        await using var db = fixture.AppHost.CreateAdminContext();
        (await db.ActivityEvents.CountAsync(row => row.CampaignId == seed.CampaignId && row.EventKind == ActivityEventKind.CampaignClosed, token)).ShouldBe(1);
    }
}
