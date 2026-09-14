using Microsoft.Playwright;
using Nova.SharedKernel.Enums;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Acceptance for the Place destination: the player-first queue, the selected participant's evidence, the
/// decision loop, and the immutable Closed posture.
/// </summary>
/// <remarks>
/// Every interaction goes through <see cref="InteractionHelpers"/>, which tolerates the SSR hydration window
/// during which Blazor handlers are not yet attached. A bare click can be swallowed by that window, so a
/// visible prerendered control is never treated as proof that it can handle an event.
/// </remarks>
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

        // The queue opens on Needs placement and carries its unfiltered whole-campaign total.
        await Expect(page.Locator("#placements-region-heading")).ToBeAttachedAsync();
        await Expect(page.Locator("button.place-section.leads")).ToContainTextAsync("Needs placement");
        await Expect(page.Locator("button.place-section.leads")).ToContainTextAsync(TotalText());
        await Expect(page.Locator("a.place-row").First).ToBeVisibleAsync();

        // Selecting a player opens their evidence sheet and keeps the selection in the URL.
        var firstRow = page.Locator("a.place-row").First;
        var playerName = (await firstRow.Locator(".place-row-name").InnerTextAsync()).Trim();
        await InteractionHelpers.ClickUntilAsync(page, firstRow, () => Task.FromResult(Selected(page)));
        await Expect(page.Locator(".place-name")).ToHaveTextAsync(playerName);
        await Expect(page.Locator(".place-evidence")).ToContainTextAsync("Effective season placement");
        await Expect(page.Locator("#place-outcome")).ToBeEnabledAsync();

        // Recording the decision reconciles the queue, the totals, and the selected player authoritatively.
        await page.Locator("#place-outcome").SelectOptionAsync(nameof(PlacementOutcome.NotSelected));
        await InteractionHelpers.ClickUntilAsync(page, SaveButton(page),
            () => HasTextAsync(page.Locator(".alert-success"), "Placement saved."));
        await Expect(page.Locator(".place-evidence")).ToContainTextAsync("Not selected");

        // Nothing was patched client-side: the reloaded evidence is the server's, the participant has left
        // the Needs-placement queue, and the whole-campaign total has moved.
        await Expect(page.Locator("a.place-row").Filter(new() { HasText = playerName })).ToHaveCountAsync(0);
        await Expect(page.Locator("button.place-section.leads")).ToContainTextAsync(TotalText(-1));
    }

    [Fact]
    public async Task QueueSearchWidensBeyondTheOpenSectionAndKeepsFilterTruthAsync()
    {
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=place").ToString());

        // Prove interactive attachment with observable actions before relying on key events: a visible
        // prerendered control does not prove it can handle an event.
        await InteractionHelpers.ClickUntilAsync(page, page.Locator("a.place-row").First,
            () => Task.FromResult(Selected(page)));
        await InteractionHelpers.ClickUntilAsync(page,
            page.GetByRole(AriaRole.Link, new() { Name = "Back to placements", Exact = true }),
            () => Task.FromResult(!Selected(page)));

        // Type once and wait for the *full* term to be applied. The field debounces at 350 ms, so a retry
        // loop that re-types would keep resetting the timer before it could fire; and waiting only for a
        // non-empty search would return on the first partial keystroke pause and race the later navigation.
        await page.Locator("#roster-search").ClickAsync();
        await page.Keyboard.TypeAsync("Player 01");
        await page.WaitForURLAsync(
            url => url.Contains("placementSearch=Player%2001", StringComparison.Ordinal),
            new() { Timeout = 20000 });

        // A search spans every section rather than the Needs-placement browsing default, so the applied
        // state carries a search and no section filter at all.
        page.Url.ShouldContain("placementSearch=Player%2001");
        page.Url.ShouldNotContain("placementEligibility=");
        await Expect(page.Locator("a.place-row")).ToHaveCountAsync(1);

        // No section reads as the applied one while the browsed scope is wider than the browsing default.
        await Expect(page.Locator("button.place-section[aria-pressed='true']")).ToHaveCountAsync(0);

        // The whole-campaign total stays independent of the filtered page.
        await Expect(page.Locator("button.place-section.leads")).ToContainTextAsync(TotalText());

        // Clearing the filters restores the browsing default and the unfiltered queue.
        await InteractionHelpers.ClickUntilAsync(
            page,
            page.GetByRole(AriaRole.Button, new() { Name = "Clear filters", Exact = true }),
            () => Task.FromResult(!page.Url.Contains("placementSearch=", StringComparison.Ordinal)));
        await Expect(page.Locator("button.place-section.leads")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(page.Locator("a.place-row")).ToHaveCountAsync(50);
    }

    [Fact]
    public async Task QueueIsPagedSoTheWholeCampaignIsNeverRenderedAtOnceAsync()
    {
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=place").ToString());

        // 60 participants exceed the bounded 50-row page, so the queue pages rather than truncating silently.
        var pager = page.Locator("nav[aria-label='Roster pagination']");
        await Expect(page.Locator("a.place-row")).ToHaveCountAsync(50);
        await Expect(pager).ToContainTextAsync("Page 1 of 2");

        await InteractionHelpers.ClickUntilAsync(page, NextButton(page),
            () => Task.FromResult(page.Url.Contains("placementPage=2", StringComparison.Ordinal)));

        await Expect(page.Locator("a.place-row")).ToHaveCountAsync(PlacementSeed.ParticipantCount - 50);
        await Expect(pager).ToContainTextAsync("Page 2 of 2");

        // The written totals describe the whole campaign, not the loaded page.
        await Expect(page.Locator("button.place-section.leads")).ToContainTextAsync(TotalText());
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
        await Expect(SaveButton(page)).ToHaveCountAsync(0);
        // The campaign-local outcomes are still presented as read-only evidence.
        await Expect(page.Locator("button.place-section")).ToContainTextAsync(["No campaign decision", "Not selected"]);
        await Expect(page.Locator("a.place-row").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task NarrowViewportStagesTheQueueAndSheetWithReachableControlsAsync()
    {
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password,
            new ViewportSize { Width = 390, Height = 844 });
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=place").ToString());

        var row = page.Locator("a.place-row").First;
        await Expect(row).ToBeVisibleAsync();
        (await row.BoundingBoxAsync())!.Height.ShouldBeGreaterThanOrEqualTo(44);

        // Selecting a player promotes the sheet to the focused stage with an explicit way back. The rail is
        // hidden rather than removed, so assert visibility rather than a DOM count.
        await InteractionHelpers.ClickUntilAsync(page, row, () => IsHiddenAsync(page.Locator("a.place-row").First));
        await Expect(page.Locator(".place-name")).ToBeVisibleAsync();

        var back = page.GetByRole(AriaRole.Link, new() { Name = "Back to placements", Exact = true });
        await Expect(back).ToBeVisibleAsync();
        // A real link, so the way back exists even before scripting attaches.
        (await back.GetAttributeAsync("href")).ShouldNotBeNullOrEmpty();
        (await back.BoundingBoxAsync())!.Height.ShouldBeGreaterThanOrEqualTo(44);

        // Keyboard users reach the decision controls directly.
        await Expect(page.Locator("#place-outcome")).ToBeEnabledAsync();
        await page.Locator("#place-outcome").FocusAsync();
        await Expect(page.Locator("#place-outcome")).ToBeFocusedAsync();

        await InteractionHelpers.ClickUntilAsync(page, back, () => IsVisibleAsync(page.Locator("a.place-row").First));
        await Expect(page.Locator("a.place-row").First).ToBeVisibleAsync();
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

        await InteractionHelpers.ClickUntilAsync(firstPage, firstPage.Locator("a.place-row").First,
            () => Task.FromResult(Selected(firstPage)));
        var name = (await firstPage.Locator(".place-name").InnerTextAsync()).Trim();

        await InteractionHelpers.ClickUntilAsync(secondPage,
            secondPage.Locator("a.place-row").Filter(new() { HasText = name }),
            () => Task.FromResult(Selected(secondPage)));
        (await secondPage.Locator(".place-name").InnerTextAsync()).Trim().ShouldBe(name);

        // The first member commits.
        await firstPage.Locator("#place-outcome").SelectOptionAsync(nameof(PlacementOutcome.NotSelected));
        await InteractionHelpers.ClickUntilAsync(firstPage, SaveButton(firstPage),
            () => HasTextAsync(firstPage.Locator(".alert-success"), "Placement saved."));

        // The second member's stale save is refused rather than overwriting the winner.
        await secondPage.Locator("#place-outcome").SelectOptionAsync(nameof(PlacementOutcome.Withdrawn));
        await InteractionHelpers.ClickUntilAsync(secondPage, SaveButton(secondPage),
            () => IsVisibleAsync(secondPage.Locator(".place-conflict")));
        await Expect(secondPage.Locator(".place-conflict")).ToContainTextAsync("Nobody was overwritten");
        await Expect(secondPage.Locator("#place-outcome")).ToHaveCountAsync(0);

        // Reloading re-establishes authoritative state and shows the winner.
        var reload = secondPage.GetByRole(AriaRole.Button, new() { Name = "Close and reload", Exact = true });
        await InteractionHelpers.ClickUntilAsync(secondPage, reload,
            () => IsEnabledAsync(secondPage.Locator("#place-outcome")));
        await Expect(secondPage.Locator(".place-evidence")).ToContainTextAsync("Not selected");
    }

    [Fact]
    public async Task CompatibleTeamChoicesExcludeIncompatibleTeamsAsync()
    {
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=place").ToString());

        await InteractionHelpers.ClickUntilAsync(page, page.Locator("a.place-row").First,
            () => IsEnabledAsync(page.Locator("#place-outcome")));
        await page.Locator("#place-outcome").SelectOptionAsync(nameof(PlacementOutcome.Assigned));

        await Expect(page.Locator("#place-team")).ToBeVisibleAsync();
        var options = await page.Locator("#place-team option").AllTextContentsAsync();
        // Only active teams whose cutoff matches the selected graduation year are offered.
        options.ShouldContain(option => option.Contains(seed.EligibleTeamName, StringComparison.Ordinal));
        options.ShouldNotContain(option => option.Contains(seed.IneligibleTeamName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AssignedWithoutATeamBlocksTheSubmitWithAWrittenReasonAsync()
    {
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=place").ToString());

        await InteractionHelpers.ClickUntilAsync(page, page.Locator("a.place-row").First,
            () => IsEnabledAsync(page.Locator("#place-outcome")));
        await page.Locator("#place-outcome").SelectOptionAsync(nameof(PlacementOutcome.Assigned));

        // Assigned without a team is an invalid state: the submit is blocked and the reason is written out.
        await Expect(page.Locator(".place-decision-note")).ToContainTextAsync("Choose a team for an assigned participant.");
        await Expect(SaveButton(page)).ToBeDisabledAsync();
    }

    /// <summary>
    /// Captures the settled desktop and mobile Place surface for the evidence packet and the comp-diff
    /// measurement. Gated by <c>NOVA_PLACE_EVIDENCE</c> so an ordinary suite run does not write captures.
    /// </summary>
    [Fact]
    public async Task CaptureSettledPlaceSurfaceForEvidenceAsync()
    {
        var directory = Environment.GetEnvironmentVariable("NOVA_PLACE_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory))
        {
            Assert.Skip("Set NOVA_PLACE_EVIDENCE to a directory to capture the Place surface evidence.");
        }

        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password,
            new ViewportSize { Width = 1440, Height = 900 });
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=place").ToString());

        // Select a participant so the working sheet carries evidence, which is the composition the approved
        // comp governs, and prove attachment before capturing.
        await InteractionHelpers.ClickUntilAsync(page, page.Locator("a.place-row").First,
            () => IsEnabledAsync(page.Locator("#place-outcome")));
        await Expect(page.Locator(".place-evidence")).ToContainTextAsync("Effective season placement");
        await CaptureAsync(page, "desktop", directory, fullPage: true, seed.CampaignId);

        await page.SetViewportSizeAsync(390, 844);
        await Expect(page.Locator(".place-name")).ToBeVisibleAsync();
        await CaptureAsync(page, "mobile", directory, fullPage: true, seed.CampaignId);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string TotalText(int delta = 0)
        => (PlacementSeed.ParticipantCount + delta).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static bool Selected(IPage page)
        => page.Url.Contains("placementParticipant=", StringComparison.Ordinal);

    private static ILocator SaveButton(IPage page)
        => page.GetByRole(AriaRole.Button, new() { Name = "Save placement", Exact = true });

    private static ILocator NextButton(IPage page)
        => page.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true });

    private static async Task<bool> IsVisibleAsync(ILocator locator) => await locator.IsVisibleAsync();

    private static async Task<bool> IsHiddenAsync(ILocator locator) => !await locator.IsVisibleAsync();

    /// <summary>
    /// Reports whether a locator is both present and enabled. The presence check must come first and must
    /// not wait: Playwright's <c>IsEnabledAsync</c> waits for a missing element and then throws, which would
    /// abort the settle loop before the first interaction ever lands.
    /// </summary>
    /// <param name="locator">The locator to test.</param>
    /// <returns><see langword="true"/> when the element exists and is enabled.</returns>
    private static async Task<bool> IsEnabledAsync(ILocator locator)
        => await locator.CountAsync() > 0 && await locator.IsEnabledAsync();

    private static async Task<bool> HasTextAsync(ILocator locator, string text)
    {
        if (await locator.CountAsync() == 0)
        {
            return false;
        }

        var content = await locator.First.InnerTextAsync();
        return content.Contains(text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Captures one settled viewport plus its geometry sidecar so a later comparison knows the frame it
    /// measured rather than inferring it from raster dimensions.
    /// </summary>
    /// <param name="page">The page to capture.</param>
    /// <param name="name">The capture name without extension.</param>
    /// <param name="directory">The evidence directory to write into.</param>
    /// <param name="fullPage">Whether to capture the whole scrollable page.</param>
    /// <param name="campaignId">The campaign identifier, recorded in the sidecar.</param>
    /// <returns>A task that completes when the capture is written.</returns>
    private static async Task CaptureAsync(IPage page, string name, string directory, bool fullPage, long campaignId)
    {
        Directory.CreateDirectory(directory);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("window.scrollTo(0, 0)");
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, name + ".png"), FullPage = fullPage });

        // The surface's own content region is captured as an element so the scoped comp measurement needs
        // no estimation, and its box is recorded for the manifest.
        var board = page.Locator(".place-board");
        var box = await board.BoundingBoxAsync();
        await board.ScreenshotAsync(new() { Path = Path.Combine(directory, name + "-board.png") });

        var boardBounds = box is null
            ? null
            : new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["x"] = box.X,
                ["y"] = box.Y,
                ["width"] = box.Width,
                ["height"] = box.Height
            };
        var geometry = await page.EvaluateAsync<string>(
            "args => JSON.stringify({cssWidth:innerWidth,cssHeight:innerHeight,devicePixelRatio,documentHeight:document.documentElement.scrollHeight,campaignId:args.campaignId,board:args.board})",
            new { campaignId, board = boardBounds });
        await File.WriteAllTextAsync(Path.Combine(directory, name + ".json"), geometry, TestContext.Current.CancellationToken);
    }
}
