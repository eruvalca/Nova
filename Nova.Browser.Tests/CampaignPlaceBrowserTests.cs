using Microsoft.Playwright;
using Nova.SharedKernel.Enums;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Acceptance for the Place destination: the player-first queue, the selected participant's evidence, the
/// decision loop, and the immutable Closed posture.
/// </summary>
/// <param name="fixture">The serial Aspire browser fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class CampaignPlaceBrowserTests(BrowserSuiteFixture fixture)
{
    [Fact]
    public async Task MemberRecordsADecisionAndTheAuthoritativeQueueAndTotalsMoveAsync()
    {
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=place").ToString());

        // The queue opens on Needs placement and carries its unfiltered total.
        await Expect(page.Locator("#placements-region-heading")).ToBeAttachedAsync();
        await Expect(page.Locator("button.place-section.leads")).ToContainTextAsync("Needs placement");
        await Expect(page.Locator("button.place-section.leads")).ToContainTextAsync(PlacementSeed.ParticipantCount.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Selecting a player opens their evidence sheet and keeps the selection in the URL.
        var firstRow = page.Locator("button.place-row").First;
        var playerName = (await firstRow.Locator(".place-row-name").InnerTextAsync()).Trim();
        await firstRow.ClickAsync();
        await Expect(page.Locator(".place-name")).ToHaveTextAsync(playerName);
        page.Url.ShouldContain("placementParticipant=");
        await Expect(page.Locator("#place-outcome")).ToBeEnabledAsync();

        // Recording the decision reconciles the queue, the totals, and the selected player authoritatively.
        await page.Locator("#place-outcome").SelectOptionAsync(nameof(PlacementOutcome.NotSelected));
        await page.GetByRole(AriaRole.Button, new() { Name = "Save placement", Exact = true }).ClickAsync();
        await Expect(page.Locator(".alert-success")).ToContainTextAsync("Placement saved.");
        await Expect(page.Locator(".place-evidence")).ToContainTextAsync("Not selected");

        // Nothing was patched client-side: the reloaded queue no longer carries that participant.
        await Expect(page.Locator("button.place-row")).ToHaveCountAsync(PlacementSeed.ParticipantCount - 1);
    }

    [Fact]
    public async Task QueueSearchWidensBeyondTheOpenSectionAndKeepsFilterAndPageTruthAsync()
    {
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=place").ToString());
        await Expect(page.Locator("button.place-row").First).ToBeVisibleAsync();

        // A name search spans every section rather than the Needs-placement browsing default.
        await page.Locator("#roster-search").FillAsync("a");
        await Expect(page.Locator("button.place-row").First).ToBeVisibleAsync();
        page.Url.ShouldContain("placementSearch=a");
        page.Url.ShouldNotContain("placementEligibility=");

        // The whole-campaign totals stay independent of the filtered page.
        await Expect(page.Locator("button.place-section.leads")).ToContainTextAsync(
            PlacementSeed.ParticipantCount.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Clearing the search restores the browsing default.
        await page.Locator("#roster-search").FillAsync(string.Empty);
        await Expect(page.Locator("button.place-section.leads")).ToHaveAttributeAsync("aria-pressed", "true");
    }

    [Fact]
    public async Task QueueIsPagedSoTheWholeCampaignIsNeverRenderedAtOnceAsync()
    {
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=place").ToString());

        // 60 participants exceed the bounded 50-row page, so the queue pages rather than truncating silently.
        await Expect(page.Locator("button.place-row")).ToHaveCountAsync(50);
        await page.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();
        await Expect(page.Locator("button.place-row")).ToHaveCountAsync(PlacementSeed.ParticipantCount - 50);
        page.Url.ShouldContain("placementPage=2");
        await Expect(page.Locator("button.place-section.leads")).ToContainTextAsync(
            PlacementSeed.ParticipantCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ClosedCampaignStaysReadOnlyForEveryRoleAsync()
    {
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.ClosedCampaignId}?tab=place").ToString());

        await Expect(page.Locator(".place-readonly")).ToContainTextAsync("Read-only — campaign is closed.");
        await Expect(page.Locator("#place-outcome")).ToHaveCountAsync(0);
        await Expect(page.Locator("#place-team")).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Save placement", Exact = true })).ToHaveCountAsync(0);
        // The immutable local record is still presented as read-only evidence.
        await Expect(page.Locator("button.place-row").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task NarrowViewportStagesTheQueueAndSheetWithReachableControlsAsync()
    {
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password,
            new ViewportSize { Width = 390, Height = 844 });
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=place").ToString());

        // The queue is the first stage.
        await Expect(page.Locator("button.place-row").First).ToBeVisibleAsync();
        await Expect(page.Locator(".place-sheet")).ToBeVisibleAsync();

        var row = page.Locator("button.place-row").First;
        var box = (await row.BoundingBoxAsync())!;
        box.Height.ShouldBeGreaterThanOrEqualTo(44);

        // Selecting a player promotes the sheet to the focused stage with an explicit way back.
        await row.ClickAsync();
        await Expect(page.Locator(".place-name")).ToBeVisibleAsync();
        await Expect(page.Locator("button.place-row")).ToHaveCountAsync(0);
        var back = page.GetByRole(AriaRole.Button, new() { Name = "Back to placements", Exact = true });
        await Expect(back).ToBeVisibleAsync();
        var backBox = (await back.BoundingBoxAsync())!;
        backBox.Height.ShouldBeGreaterThanOrEqualTo(44);

        // Keyboard users can reach the decision controls and read the written outcome state.
        await page.Locator("#place-outcome").FocusAsync();
        await Expect(page.Locator("#place-outcome")).ToBeFocusedAsync();

        await back.ClickAsync();
        await Expect(page.Locator("button.place-row").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ConcurrentDecisionShowsTheConflictRecoveryAndTheReloadShowsTheWinnerAsync()
    {
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var first = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        await using var second = await fixture.NewSignedInContextAsync(seed.SecondAdminEmail, PlacementSeed.Password);
        var firstPage = first.Pages[0];
        var secondPage = second.Pages[0];
        var url = new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=place").ToString();
        await firstPage.GotoAsync(url);
        await secondPage.GotoAsync(url);

        var firstRow = firstPage.Locator("button.place-row").First;
        await firstRow.ClickAsync();
        await secondPage.Locator("button.place-row").First.ClickAsync();
        var name = (await firstPage.Locator(".place-name").InnerTextAsync()).Trim();
        (await secondPage.Locator(".place-name").InnerTextAsync()).Trim().ShouldBe(name);

        // The first member commits.
        await firstPage.Locator("#place-outcome").SelectOptionAsync(nameof(PlacementOutcome.NotSelected));
        await firstPage.GetByRole(AriaRole.Button, new() { Name = "Save placement", Exact = true }).ClickAsync();
        await Expect(firstPage.Locator(".alert-success")).ToContainTextAsync("Placement saved.");

        // The second member's stale save is refused rather than overwriting the winner.
        await secondPage.Locator("#place-outcome").SelectOptionAsync(nameof(PlacementOutcome.Withdrawn));
        await secondPage.GetByRole(AriaRole.Button, new() { Name = "Save placement", Exact = true }).ClickAsync();
        await Expect(secondPage.Locator(".place-conflict")).ToBeVisibleAsync();
        await Expect(secondPage.Locator(".place-conflict")).ToContainTextAsync("Nobody was overwritten");
        await Expect(secondPage.Locator("#place-outcome")).ToHaveCountAsync(0);

        // Reloading re-establishes authoritative state and shows the winner.
        await secondPage.GetByRole(AriaRole.Button, new() { Name = "Close and reload", Exact = true }).ClickAsync();
        await Expect(secondPage.Locator("#place-outcome")).ToBeEnabledAsync();
        await Expect(secondPage.Locator(".place-evidence")).ToContainTextAsync("Not selected");
    }

    [Fact]
    public async Task CompatibleTeamChoicesExcludeIncompatibleTeamsAsync()
    {
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=place").ToString());

        await page.Locator("button.place-row").First.ClickAsync();
        await Expect(page.Locator("#place-outcome")).ToBeEnabledAsync();
        await page.Locator("#place-outcome").SelectOptionAsync(nameof(PlacementOutcome.Assigned));

        await Expect(page.Locator("#place-team")).ToBeVisibleAsync();
        var options = await page.Locator("#place-team option").AllTextContentsAsync();
        // Only active teams whose cutoff matches the selected graduation year are offered.
        options.ShouldContain(option => option.Contains(seed.EligibleTeamName, StringComparison.Ordinal));
        options.ShouldNotContain(option => option.Contains(seed.IneligibleTeamName, StringComparison.Ordinal));
    }
}
