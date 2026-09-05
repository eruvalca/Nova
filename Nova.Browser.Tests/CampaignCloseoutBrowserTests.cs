using Microsoft.Extensions.Logging.Abstractions;
using Nova.Features.Players;
using Nova.Shared.Enums;
using Nova.Shared.Features.Players;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Browser-level validation of the campaign closeout cross-slice scenarios: the administrator
/// overview-to-closeout happy path, blocked-close behavior, the stale blocked-close conflict, the
/// reopen confirmation flow, read-only rendering for non-administrators, direct URL/back-navigation
/// tab preservation, and keyboard/accessibility across viewports.
/// </summary>
/// <param name="fixture">The Aspire-hosted browser suite fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class CampaignCloseoutBrowserTests(BrowserSuiteFixture fixture)
{
    [Fact]
    public async Task Admin_OverviewAndCloseout_HappyPath_ResolvesBlockers_AndCloses_IntoReadOnlyState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, CloseoutSeed.Password);
        var page = context.Pages[0];

        // Overview shows the snapshot, the blocked readiness line, and the administrator closeout link.
        await OpenOverviewAsync(page, seed.BlockedCampaignId);
        await Expect(page.Locator("#overview-region-heading")).ToBeVisibleAsync();
        await Expect(page.Locator("span[role=status]")).ToContainTextAsync("Closeout blocked");
        await OpenCloseoutFromOverviewAsync(page);

        // Closeout shows authoritative counts and the three blocker rows with Count + Message.
        var blockerRows = page.Locator("li.list-group-item.list-group-item-warning");
        await Expect(blockerRows).ToHaveCountAsync(3);
        await Expect(blockerRows.Nth(0)).ToContainTextAsync("Undecided");
        await Expect(blockerRows.Nth(0)).ToContainTextAsync("1");
        await Expect(blockerRows.Nth(1)).ToContainTextAsync("Eligibility");
        await Expect(blockerRows.Nth(2)).ToContainTextAsync("Archived teams");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Close campaign" })).ToBeDisabledAsync();

        // Resolve the outcomes blocker through the unresolved drill-down, then the eligibility and
        // archived-team blockers through their no-filter drill-downs.
        await ResolveBlockerAsync(page, "Undecided", seed.BlockedAssignmentIds[0]);
        await ResolveBlockerAsync(page, "Eligibility", seed.BlockedAssignmentIds[1]);
        await ResolveBlockerAsync(page, "Archived teams", seed.BlockedAssignmentIds[2]);

        await Expect(page.Locator("li.list-group-item.list-group-item-warning")).ToHaveCountAsync(0);
        await Expect(page.Locator("span.text-success")).ToHaveCountAsync(3);
        var closeButton = page.GetByRole(AriaRole.Button, new() { Name = "Close campaign" });
        await Expect(closeButton).ToBeEnabledAsync();
        await InteractionHelpers.ClickUntilAsync(page, closeButton, () => page.GetByText("Campaign closed.").IsVisibleAsync());

        // The panel switches to the closed read-only view and announces the close.
        await Expect(page.Locator("div.alert-success[role=status]")).ToContainTextAsync("Campaign closed.");
        await Expect(page.Locator("div.alert-secondary[role=note]")).ToContainTextAsync("This campaign is closed and read-only.");
        await Expect(page.Locator("[aria-label='Final outcome summary']")).ToBeVisibleAsync();

        // Overview activity now records the closed transition.
        await OpenOverviewAsync(page, seed.BlockedCampaignId);
        await Expect(page.Locator("#overview-region-heading")).ToContainTextAsync("Overview");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Activity" })).ToBeVisibleAsync();
        await Expect(page.Locator("section[aria-labelledby='overview-region-heading']")).ToContainTextAsync("closed the campaign");
    }

    [Fact]
    public async Task Admin_BlockedClose_ShowsBlockerDetails_CloseDisabled_AndNothingFrozen()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, CloseoutSeed.Password);
        var page = context.Pages[0];

        await OpenCloseoutAsync(page, seed.BlockedCampaignId);

        var blockerRows = page.Locator("li.list-group-item.list-group-item-warning");
        await Expect(blockerRows).ToHaveCountAsync(3);
        // Blocker detail is text (Count + Message), not color-only.
        await Expect(blockerRows.Nth(0)).ToContainTextAsync("Undecided");
        await Expect(blockerRows.Nth(0)).ToContainTextAsync("undecided participation record");
        await Expect(blockerRows.Nth(1)).ToContainTextAsync("Eligibility");
        await Expect(blockerRows.Nth(1)).ToContainTextAsync("Ineligible assignment ids");
        await Expect(blockerRows.Nth(2)).ToContainTextAsync("Archived teams");
        await Expect(blockerRows.Nth(2)).ToContainTextAsync("Blocked assignment ids");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Close campaign" })).ToBeDisabledAsync();

        // Nothing is frozen: an administrator placement save still succeeds.
        await OpenPlacementsAsync(page, seed.BlockedCampaignId);
        var firstRow = page.Locator("tbody tr[id^='placement-row-']").First;
        await Expect(firstRow).ToBeVisibleAsync();
        await SavePlacementOutcomeAsync(page, firstRow, PlacementOutcome.NotSelected);
        await Expect(page.Locator("div.alert-success[role=status]")).ToContainTextAsync("Placement saved.");
    }

    /// <summary>Verifies automatic enrollment invalidates stale readiness without freezing campaign editing.</summary>
    /// <returns>A task representing the concurrent closeout browser scenario.</returns>
    [Fact]
    public async Task Admin_StaleBlockedClose_ShowsConflictAlert_WithoutFreezing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await CloseoutSeed.ActivateCampaignAsync(
            fixture.AppHost,
            seed.ClubId,
            seed.ReadyCampaignId,
            seed.AdminUserId,
            cancellationToken);
        await using var adminContext = await fixture.NewSignedInContextAsync(seed.AdminEmail, CloseoutSeed.Password);
        await using var secondAdminContext = await fixture.NewSignedInContextAsync(seed.SecondAdminEmail, CloseoutSeed.Password);
        var adminPage = adminContext.Pages[0];
        var secondPage = secondAdminContext.Pages[0];

        // Admin A loads the ready campaign's closeout, which reports ready.
        await OpenCloseoutAsync(adminPage, seed.ReadyCampaignId);
        var closeButton = adminPage.GetByRole(AriaRole.Button, new() { Name = "Close campaign" });
        await Expect(closeButton).ToBeEnabledAsync();

        // Admin B creates a player while Admin A holds stale readiness. The real creation service
        // automatically enrolls the player, introducing an unresolved participation without clearing a decision.
        using (fixture.AppHost.UseUser(seed.SecondAdminUserId, seed.ClubId, isClubAdmin: true))
        {
            var players = new PlayerManagementService(
                fixture.AppHost.CreateTenantContextFactory(),
                fixture.AppHost.CurrentUser,
                NullLogger<PlayerManagementService>.Instance);
            var created = await players.CreateAsync(new CreatePlayerInput
            {
                FirstName = "Late",
                LastName = $"Arrival {Guid.NewGuid():N}",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030
            }, cancellationToken);
            created.IsSuccess.ShouldBeTrue();
        }
        await OpenPlacementsAsync(secondPage, seed.ReadyCampaignId);
        await Expect(secondPage.Locator("div.placement-summary[role=status]")).ToContainTextAsync("1 undecided");

        // Admin A's stale close is rejected with an actionable conflict and refetches the blockers.
        var conflictAlert = adminPage.Locator("div.alert-warning[role=alert]");
        await InteractionHelpers.ClickUntilAsync(adminPage, closeButton, () => conflictAlert.IsVisibleAsync());
        await Expect(conflictAlert).ToContainTextAsync("Resolve all campaign close blockers before closing this campaign.");
        await Expect(adminPage.Locator("li.list-group-item.list-group-item-warning")).ToHaveCountAsync(1);
        await Expect(adminPage.Locator("li.list-group-item.list-group-item-warning")).ToContainTextAsync("Undecided");

        // The campaign is still active and editable for Admin B.
        await Expect(secondPage.Locator("select[aria-label^='Outcome for']").First).ToBeVisibleAsync();
        await Expect(secondPage.Locator("select[aria-label^='Outcome for']").First).ToBeEnabledAsync();
    }

    [Fact]
    public async Task Admin_ReopenConfirm_RestoresEditing_PreservingOutcomesAndHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await CloseoutSeed.CloseActiveCampaignAsync(
            fixture.AppHost,
            seed.ClubId,
            seed.AdminUserId,
            cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, CloseoutSeed.Password);
        var page = context.Pages[0];

        await OpenCloseoutAsync(page, seed.ClosedCampaignId);
        await Expect(page.Locator("div.alert-secondary[role=note]")).ToContainTextAsync("This campaign is closed and read-only.");

        // Reopen requires an inline confirmation; Cancel hides it without effect.
        var reopenButton = page.GetByRole(AriaRole.Button, new() { Name = "Reopen campaign" });
        var confirmGroup = page.Locator("[role=group][aria-label='Reopen confirmation']");
        await InteractionHelpers.ClickUntilAsync(page, reopenButton, () => confirmGroup.IsVisibleAsync());
        await Expect(confirmGroup).ToContainTextAsync("Reopening restores editing without discarding outcomes and is recorded for audit.");
        await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }), () => confirmGroup.IsHiddenAsync());

        await InteractionHelpers.ClickUntilAsync(page, reopenButton, () => confirmGroup.IsVisibleAsync());
        await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Confirm reopen" }), () => page.GetByText("Campaign reopened.").IsVisibleAsync());
        await Expect(page.Locator("div.alert-success[role=status]")).ToContainTextAsync("Campaign reopened.");

        // The panel returns to the active checklist.
        var panelText = (await page.Locator("section[aria-labelledby='closeout-region-heading']").TextContentAsync()) ?? string.Empty;
        panelText.Contains("Enrolled", StringComparison.Ordinal).ShouldBeTrue($"Panel text after reopen was: {panelText}");

        // Overview activity shows both the closed and reopened transitions.
        await OpenOverviewAsync(page, seed.ClosedCampaignId);
        var overviewText = (await page.Locator("section[aria-labelledby='overview-region-heading']").TextContentAsync()) ?? string.Empty;
        overviewText.Contains("closed the campaign", StringComparison.Ordinal).ShouldBeTrue($"Overview text was: {overviewText}");
        overviewText.Contains("reopened the campaign", StringComparison.Ordinal).ShouldBeTrue($"Overview text was: {overviewText}");

        // Editing is restored and previously decided outcomes are unchanged.
        await OpenPlacementsAsync(page, seed.ClosedCampaignId);
        var firstRow = page.Locator("tbody tr[id^='placement-row-']").First;
        await SavePlacementOutcomeAsync(page, firstRow, PlacementOutcome.Assigned, seed.EligibleTeamId);
        await Expect(page.Locator("div.placement-summary[role=status]")).ToContainTextAsync("1 assigned");
        await Expect(page.Locator("div.placement-summary[role=status]")).ToContainTextAsync("2 not selected");
    }

    [Fact]
    public async Task NonAdmin_ClosedCampaign_RendersReadOnly_WithoutCloseReopenControls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, CloseoutSeed.Password);
        var page = context.Pages[0];

        await OpenCloseoutAsync(page, seed.ClosedCampaignId);
        await Expect(page.Locator("div.alert-secondary[role=note]")).ToContainTextAsync("This campaign is closed and read-only.");
        await Expect(page.Locator("[aria-label='Final outcome summary']")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Close campaign" })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Reopen campaign" })).ToHaveCountAsync(0);

        // Overview exposes no administrator-only closeout link.
        await OpenOverviewAsync(page, seed.ClosedCampaignId);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Open closeout" })).ToHaveCountAsync(0);

        // Placements render read-only with no enabled save controls.
        await OpenPlacementsAsync(page, seed.ClosedCampaignId);
        await Expect(page.Locator("select[aria-label^='Outcome for']")).ToHaveCountAsync(0);
        await Expect(page.Locator("select[aria-label^='Team for']")).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Save" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task DirectCloseoutOverviewUrls_AndBackNavigation_PreserveTabContext()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, CloseoutSeed.Password);
        var page = context.Pages[0];

        // Direct closeout and overview URLs render their headings.
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.BlockedCampaignId}?tab=closeout").ToString());
        await Expect(page.Locator("#closeout-region-heading")).ToBeVisibleAsync();
        page.Url.ShouldContain("tab=closeout");

        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.BlockedCampaignId}?tab=overview").ToString());
        await Expect(page.Locator("#overview-region-heading")).ToBeVisibleAsync();
        page.Url.ShouldContain("tab=overview");

        // The workspace tab buttons track the tab= query parameter via client-side navigation.
        await InteractionHelpers.ClickUntilAsync(
            page,
            page.GetByRole(AriaRole.Tab, new() { Name = "Placements" }),
            () => page.Locator("#placements-region-heading").IsVisibleAsync());
        page.Url.ShouldContain("tab=placements");

        // Browser Back restores the overview tab (client-side history entry).
        await page.GoBackAsync(new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.Locator("#overview-region-heading")).ToBeVisibleAsync();

        // From the closeout tab, a blocker drill-down pushes a placements entry; Back returns to closeout.
        await InteractionHelpers.ClickUntilAsync(
            page,
            page.GetByRole(AriaRole.Tab, new() { Name = "Closeout" }),
            () => page.Locator("#closeout-region-heading").IsVisibleAsync());
        var outcomesRow = page.Locator("li.list-group-item.list-group-item-warning").Filter(new() { HasText = "Undecided" });
        await InteractionHelpers.ClickUntilAsync(
            page,
            outcomesRow.GetByRole(AriaRole.Button, new() { Name = "Review unresolved" }),
            () => page.Locator("#placements-region-heading").IsVisibleAsync());
        page.Url.ShouldContain("unresolvedOnly=true");

        await page.GoBackAsync(new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.Locator("#closeout-region-heading")).ToBeVisibleAsync();
        page.Url.ShouldContain("tab=closeout");
    }

    [Fact]
    public async Task Closeout_KeyboardAndA11y_AcrossWideAndNarrowViewports()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await CloseoutSeed.ActivateCampaignAsync(
            fixture.AppHost,
            seed.ClubId,
            seed.ReadyCampaignId,
            seed.AdminUserId,
            cancellationToken);

        // Wide viewport: keyboard-only close and reopen with visible focus and announcements.
        await using var wideContext = await fixture.NewSignedInContextAsync(seed.AdminEmail, CloseoutSeed.Password);
        var page = wideContext.Pages[0];

        await OpenOverviewAsync(page, seed.ReadyCampaignId);
        var openCloseout = page.GetByRole(AriaRole.Button, new() { Name = "Open closeout" });
        await InteractionHelpers.ActUntilAsync(
            page,
            async () =>
            {
                await openCloseout.FocusAsync();
                await page.Keyboard.PressAsync("Enter");
            },
            () => page.Locator("#closeout-region-heading").IsVisibleAsync());

        var closeButton = page.GetByRole(AriaRole.Button, new() { Name = "Close campaign" });
        await Expect(closeButton).ToBeEnabledAsync();
        await InteractionHelpers.ActUntilAsync(
            page,
            async () =>
            {
                await closeButton.FocusAsync();
                await page.Keyboard.PressAsync("Enter");
            },
            () => page.GetByText("Campaign closed.").IsVisibleAsync());
        await Expect(page.Locator("div.alert-success[role=status]")).ToContainTextAsync("Campaign closed.");

        await OpenCloseoutAsync(page, seed.ClosedCampaignId);
        var reopenButton = page.GetByRole(AriaRole.Button, new() { Name = "Reopen campaign" });
        var confirmGroup = page.Locator("[role=group][aria-label='Reopen confirmation']");
        await InteractionHelpers.ActUntilAsync(
            page,
            async () =>
            {
                await reopenButton.FocusAsync();
                await page.Keyboard.PressAsync("Enter");
            },
            () => confirmGroup.IsVisibleAsync());
        await Expect(confirmGroup).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Expect(confirmGroup).ToBeHiddenAsync();

        await InteractionHelpers.ActUntilAsync(
            page,
            async () =>
            {
                await reopenButton.FocusAsync();
                await page.Keyboard.PressAsync("Enter");
            },
            () => confirmGroup.IsVisibleAsync());
        await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Confirm reopen" }), () => page.GetByText("Campaign reopened.").IsVisibleAsync());
        await Expect(page.Locator("div.alert-success[role=status]")).ToContainTextAsync("Campaign reopened.");

        await CloseoutSeed.ActivateCampaignAsync(
            fixture.AppHost,
            seed.ClubId,
            seed.BlockedCampaignId,
            seed.AdminUserId,
            cancellationToken);

        // Narrow viewport: checklist renders, blocker rows are distinguishable by text, and the
        // close/reopen controls meet the WCAG 2.5.8 minimum target size (24×24 CSS px).
        await using var narrowContext = await fixture.NewSignedInContextAsync(
            seed.AdminEmail, CloseoutSeed.Password, new ViewportSize { Width = 480, Height = 800 });
        var narrowPage = narrowContext.Pages[0];

        await OpenCloseoutAsync(narrowPage, seed.BlockedCampaignId);
        var blockerRows = narrowPage.Locator("li.list-group-item.list-group-item-warning");
        await Expect(blockerRows).ToHaveCountAsync(3);
        await Expect(blockerRows.Nth(0)).ToContainTextAsync("Undecided");
        await Expect(blockerRows.Nth(1)).ToContainTextAsync("Eligibility");
        await Expect(blockerRows.Nth(2)).ToContainTextAsync("Archived teams");
        await A11yMeasurementHelpers.AssertTouchTargetAsync(narrowPage, narrowPage.GetByRole(AriaRole.Button, new() { Name = "Close campaign" }), "Close campaign");

        // The wide-viewport pass closed the ready campaign, so its closeout now shows "Reopen campaign".
        await OpenCloseoutAsync(narrowPage, seed.ReadyCampaignId);
        await A11yMeasurementHelpers.AssertTouchTargetAsync(narrowPage, narrowPage.GetByRole(AriaRole.Button, new() { Name = "Reopen campaign" }), "Reopen campaign");
    }

    [Fact]
    public async Task Closeout_A11yEvidence_CapturesScreenshots()
    {
        if (Environment.GetEnvironmentVariable("NOVA_A11Y_SCREENSHOTS") != "1")
        {
            Assert.Skip("Set NOVA_A11Y_SCREENSHOTS=1 to capture closeout accessibility evidence.");
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, CloseoutSeed.Password);
        var page = context.Pages[0];
        var outputDirectory = Path.Combine(Path.GetTempPath(), "nova-a11y-screenshots");
        Directory.CreateDirectory(outputDirectory);

        await OpenCloseoutAsync(page, seed.BlockedCampaignId);
        await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "closeout-blocked.png") });
        await OpenCloseoutAsync(page, seed.ClosedCampaignId);
        await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "closeout-closed.png") });
    }

    [Fact]
    public async Task Closeout_Loading_ShowsIndicator_ThenRendersChecklist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, CloseoutSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.BlockedCampaignId}").ToString());
        await Expect(page.Locator("#roster-region-heading")).ToBeVisibleAsync();
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page);
        await Expect(page.Locator("#roster-region-heading")).ToBeVisibleAsync();

        // Hold the closeout-readiness fetch open while the loading state is asserted, then release it.
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var intercepted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync(
            IsCloseoutReadinessUrl,
            async route =>
            {
                intercepted.TrySetResult(null);
                await release.Task;
                await route.ContinueAsync();
            });

        await InteractionHelpers.ActUntilAsync(
            page,
            () => page.GetByRole(AriaRole.Tab, new() { Name = "Closeout" }).ClickAsync(new() { Timeout = 3000 }),
            () => page.Locator("#closeout-region-heading").IsVisibleAsync());
        await intercepted.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        await Expect(page.GetByText("Loading closeout...")).ToBeVisibleAsync();

        release.TrySetResult(null);
        await Expect(page.Locator("li.list-group-item.list-group-item-warning")).ToHaveCountAsync(3);
        await page.UnrouteAsync(IsCloseoutReadinessUrl);
    }

    [Fact]
    public async Task Closeout_Failure_ShowsRetry_AndRetryRecovers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, CloseoutSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.BlockedCampaignId}").ToString());
        await Expect(page.Locator("#roster-region-heading")).ToBeVisibleAsync();
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page);
        await Expect(page.Locator("#roster-region-heading")).ToBeVisibleAsync();

        await page.RouteAsync(IsCloseoutReadinessUrl, route => route.FulfillAsync(new() { Status = 500 }));

        await InteractionHelpers.ActUntilAsync(
            page,
            () => page.GetByRole(AriaRole.Tab, new() { Name = "Closeout" }).ClickAsync(new() { Timeout = 3000 }),
            () => page.Locator("#closeout-region-heading").IsVisibleAsync());

        var errorAlert = page.Locator("div.alert-danger[role=alert]");
        await Expect(errorAlert).ToContainTextAsync("Failed to load closeout readiness");
        var retry = errorAlert.GetByRole(AriaRole.Button, new() { Name = "Retry" });
        await Expect(retry).ToBeVisibleAsync();

        await page.UnrouteAsync(IsCloseoutReadinessUrl);
        await retry.ClickAsync();

        await Expect(page.Locator("li.list-group-item.list-group-item-warning")).ToHaveCountAsync(3);
        await Expect(page.Locator("div.alert-danger[role=alert]")).ToHaveCountAsync(0);
    }

    /// <summary>Matches the closeout-readiness fetch.</summary>
    private static bool IsCloseoutReadinessUrl(string url) =>
        url.Contains("/api/campaigns/", StringComparison.Ordinal)
        && url.Contains("/closeout-readiness", StringComparison.Ordinal);

    /// <summary>Navigates to the closeout tab and waits for its heading.</summary>
    private async Task OpenCloseoutAsync(IPage page, long campaignId)
    {
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{campaignId}?tab=closeout").ToString());
        await Expect(page.Locator("#closeout-region-heading")).ToBeVisibleAsync();
    }

    /// <summary>Navigates to the overview tab and waits for its heading.</summary>
    private async Task OpenOverviewAsync(IPage page, long campaignId)
    {
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{campaignId}?tab=overview").ToString());
        await Expect(page.Locator("#overview-region-heading")).ToBeVisibleAsync();
    }

    /// <summary>Navigates to the placements tab and waits for its heading.</summary>
    private async Task OpenPlacementsAsync(IPage page, long campaignId)
    {
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{campaignId}?tab=placements").ToString());
        await Expect(page.Locator("#placements-region-heading")).ToBeVisibleAsync();
        await Expect(page.Locator("div.placement-summary[role=status]")).ToBeVisibleAsync();
    }

    /// <summary>Follows the overview "Open closeout" link, retrying through SSR hydration.</summary>
    private static async Task OpenCloseoutFromOverviewAsync(IPage page)
    {
        await InteractionHelpers.ClickUntilAsync(
            page,
            page.GetByRole(AriaRole.Button, new() { Name = "Open closeout" }),
            () => page.Locator("#closeout-region-heading").IsVisibleAsync());
    }

    /// <summary>
    /// Resolves a closeout blocker by following its "Review unresolved" drill-down, changing the
    /// target assignment to <see cref="PlacementOutcome.NotSelected"/>, and returning to the closeout tab.
    /// </summary>
    private static async Task ResolveBlockerAsync(IPage page, string rowLabel, long assignmentId)
    {
        var row = page.Locator("li.list-group-item.list-group-item-warning").Filter(new() { HasText = rowLabel });
        await InteractionHelpers.ClickUntilAsync(
            page,
            row.GetByRole(AriaRole.Button, new() { Name = "Review unresolved" }),
            () => page.Locator("#placements-region-heading").IsVisibleAsync());

        var placementRow = page.Locator($"tbody tr[id='placement-row-{assignmentId}']");
        await Expect(placementRow).ToBeVisibleAsync();
        await SavePlacementOutcomeAsync(page, placementRow, PlacementOutcome.NotSelected);

        await InteractionHelpers.ClickUntilAsync(
            page,
            page.GetByRole(AriaRole.Tab, new() { Name = "Closeout" }),
            () => page.Locator("#closeout-region-heading").IsVisibleAsync());
    }

    /// <summary>Saves a placement outcome on a specific row, retrying through SSR hydration.</summary>
    private static async Task SavePlacementOutcomeAsync(
        IPage page,
        ILocator row,
        PlacementOutcome outcome,
        long? teamId = null)
    {
        await Expect(row).ToBeVisibleAsync();
        var outcomeSelect = row.Locator("select[aria-label^='Outcome for']");
        var outcomeValue = ((int)outcome).ToString();
        var undecidedValue = ((int)PlacementOutcome.Undecided).ToString();
        var teamSelect = row.Locator("select[aria-label^='Team for']");
        var save = row.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });

        // The Save button only renders once the change reaches the Blazor draft state (draft.IsDirty).
        // Prerendered selects swallow change events until the circuit attaches, so retry the select
        // change and wait for the Save button as the hydration signal.
        for (var attempt = 0; attempt < 25; attempt++)
        {
            try
            {
                await outcomeSelect.SelectOptionAsync(undecidedValue);
                await outcomeSelect.SelectOptionAsync(outcomeValue);

                if (outcome == PlacementOutcome.Assigned)
                {
                    await Expect(teamSelect).ToBeEnabledAsync(new() { Timeout = 1500 });
                    await teamSelect.SelectOptionAsync(teamId!.Value.ToString());
                }
            }
            catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
            {
                // The select was replaced mid-interaction or the team select is not yet enabled.
                // Playwright actionability timeouts surface as System.TimeoutException.
            }

            try
            {
                await Expect(save).ToBeVisibleAsync(new() { Timeout = 1500 });
                break;
            }
            catch (PlaywrightException)
            {
                await page.WaitForTimeoutAsync(250);
            }
        }

        await Expect(save).ToBeVisibleAsync();
        await InteractionHelpers.ActUntilAsync(
            page,
            () => save.ClickAsync(new() { Timeout = 3000 }),
            () => page.GetByText("Placement saved.").IsVisibleAsync());
    }
}
