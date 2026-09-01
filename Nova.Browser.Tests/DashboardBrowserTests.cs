namespace Nova.Browser.Tests;

/// <summary>
/// Browser-level validation of the club dashboard cross-slice scenarios: the administrator happy path,
/// evaluator role rendering, onboarding-gate preservation, empty states, direct URL/back navigation,
/// and keyboard/accessibility across viewports.
/// </summary>
/// <param name="fixture">The Aspire-hosted browser suite fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class DashboardBrowserTests(BrowserSuiteFixture fixture)
{
    /// <summary>
    /// BS1: the administrator signs in and lands on the dashboard, sees the seeded campaign rows with
    /// participant/unresolved counts, follows a workspace link to the campaign page and returns via Back,
    /// sees the roster/team counts, and sees the attention card with working review links.
    /// </summary>
    [Fact]
    public async Task Dashboard_Admin_SeesCampaignsRosterTeamsAndAttention_WithWorkingLinks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, DashboardSeed.Password);
        var page = context.Pages[0];

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();

        // Two campaign rows with the seeded participant/unresolved counts.
        var rows = page.Locator("tbody tr");
        await Expect(rows).ToHaveCountAsync(2);
        var undecidedRow = rows.Filter(new() { HasText = seed.UndecidedCampaignName });
        await Expect(undecidedRow.Locator("td").Nth(1)).ToHaveTextAsync("2");
        await Expect(undecidedRow.Locator("td").Nth(2)).ToHaveTextAsync("2");
        var decidedRow = rows.Filter(new() { HasText = seed.DecidedCampaignName });
        await Expect(decidedRow.Locator("td").Nth(1)).ToHaveTextAsync("1");
        await Expect(decidedRow.Locator("td").Nth(2)).ToHaveTextAsync("0");

        // The workspace link is a plain <a> that performs a full navigation.
        await page.GetByRole(AriaRole.Link, new() { Name = $"Open workspace for {seed.UndecidedCampaignName}" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains($"/campaigns/{seed.UndecidedCampaignId}", StringComparison.OrdinalIgnoreCase));
        await page.GoBackAsync(new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();

        // Roster and team cards show the seeded counts.
        var summary = page.Locator("[aria-label='Club summary']");
        await Expect(summary).ToContainTextAsync($"{seed.ActivePlayerCount} active");
        await Expect(summary).ToContainTextAsync($"{seed.ArchivedPlayerCount} archived");
        await Expect(summary).ToContainTextAsync($"{seed.ActiveTeamCount} active");
        await Expect(summary).ToContainTextAsync($"{seed.ArchivedTeamCount} archived");

        // Administrator attention card shows both counts and working review links.
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Admin attention" })).ToBeVisibleAsync();
        await Expect(summary).ToContainTextAsync($"{seed.PendingJoinRequestCount} pending join requests");
        await Expect(summary).ToContainTextAsync($"{seed.UnresolvedPlacementCount} unresolved placements");
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Review requests" }))
            .ToHaveAttributeAsync("href", $"/Clubs/{seed.ClubId}/admin");
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Review placements" }))
            .ToHaveAttributeAsync("href", $"/campaigns/{seed.FirstUnresolvedCampaignId}");
    }

    /// <summary>
    /// BS2: the evaluator signs in, sees the campaign rows and a recent-activity entry with the seeded
    /// actor's display name (a campaign-opened event), and sees no administrator attention card or
    /// review links.
    /// </summary>
    [Fact]
    public async Task Dashboard_Evaluator_SeesCampaignsAndActivity_WithoutAdminAttention()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, DashboardSeed.Password);
        var page = context.Pages[0];

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(2);

        // Recent activity includes the seeded member-visible event and resolves the evaluator actor's
        // display name.
        await Expect(page.Locator("section[aria-labelledby='recent-activity-heading']")).ToContainTextAsync("Bob Observer");
        await Expect(page.Locator("section[aria-labelledby='recent-activity-heading']")).ToContainTextAsync("opened");

        // No administrator-only attention card or review links.
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Admin attention" })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Review requests" })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Review placements" })).ToHaveCountAsync(0);
    }

    /// <summary>
    /// BS3: a photo-less user lands on the profile-photo page, and a photo-complete club-less user lands
    /// on the club onboarding page.
    /// </summary>
    [Fact]
    public async Task Dashboard_OnboardingGates_PhotoLessAndClubLessUsers_AreRedirected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);

        await using (var photoLessContext = await fixture.NewSignedInContextAsync(seed.PhotoLessEmail, DashboardSeed.Password))
        {
            var page = photoLessContext.Pages[0];
            await page.WaitForURLAsync(url => url.Contains("/Account/ProfilePhoto", StringComparison.OrdinalIgnoreCase));
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Profile photo" })).ToBeVisibleAsync();
        }

        await using (var clubLessContext = await fixture.NewSignedInContextAsync(seed.ClubLessEmail, DashboardSeed.Password))
        {
            var page = clubLessContext.Pages[0];
            await page.WaitForURLAsync(url => url.Contains("/Clubs/Onboarding", StringComparison.OrdinalIgnoreCase));
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Welcome to Nova" })).ToBeVisibleAsync();
        }
    }

    /// <summary>
    /// BS4: a no-campaign club shows the administrator Create campaign call to action and the evaluator
    /// neutral empty state without a call to action.
    /// </summary>
    [Fact]
    public async Task Dashboard_NoCampaignClub_AdminSeesCreateCta_EvaluatorSeesNeutralState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedEmptyClubAsync(fixture.AppHost, cancellationToken);

        await using (var adminContext = await fixture.NewSignedInContextAsync(seed.AdminEmail, DashboardSeed.Password))
        {
            var page = adminContext.Pages[0];
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();
            var createLink = page.GetByRole(AriaRole.Link, new() { Name = "Create campaign" });
            await Expect(createLink).ToBeVisibleAsync();
            await Expect(createLink).ToHaveAttributeAsync("href", "campaigns/new");
        }

        await using (var evaluatorContext = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, DashboardSeed.Password))
        {
            var page = evaluatorContext.Pages[0];
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();
            await Expect(page.GetByText("No active campaigns right now. Check back once an administrator creates one.")).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Create campaign" })).ToHaveCountAsync(0);
        }
    }

    /// <summary>
    /// BS5: navigating directly to the dashboard root renders it without redirect, and after opening a
    /// workspace the browser Back restores the dashboard with its counts intact.
    /// </summary>
    [Fact]
    public async Task Dashboard_DirectUrlAndBackNavigation_PreserveEntryContext()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, DashboardSeed.Password);
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/dashboard").ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(2);

        await page.GetByRole(AriaRole.Link, new() { Name = $"Open workspace for {seed.UndecidedCampaignName}" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains($"/campaigns/{seed.UndecidedCampaignId}", StringComparison.OrdinalIgnoreCase));
        await page.GoBackAsync(new() { WaitUntil = WaitUntilState.Commit });

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();
        await Expect(page.Locator("tbody tr")).ToHaveCountAsync(2);
    }

    /// <summary>
    /// BS6: across wide and narrow viewports the workspace link carries its programmatic label, the
    /// labelled regions resolve, keyboard tab order reaches a dashboard control with visible focus, and
    /// the workspace control meets the minimum touch-target size without relying on color alone.
    /// </summary>
    [Fact]
    public async Task Dashboard_KeyboardAndA11y_AcrossViewports()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);

        await using (var wideContext = await fixture.NewSignedInContextAsync(seed.AdminEmail, DashboardSeed.Password, new ViewportSize { Width = 1280, Height = 800 }))
        {
            var page = wideContext.Pages[0];
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();

            var workspaceLink = page.GetByRole(AriaRole.Link, new() { Name = $"Open workspace for {seed.UndecidedCampaignName}" });
            await Expect(workspaceLink).ToHaveAttributeAsync("aria-label", $"Open workspace for {seed.UndecidedCampaignName}");
            await Expect(page.Locator("section[aria-labelledby='active-campaigns-heading']")).ToBeVisibleAsync();
            await Expect(page.Locator("section[aria-labelledby='recent-activity-heading']")).ToBeVisibleAsync();

            // Tab order reaches the workspace control and leaves it visibly focused.
            await InteractionHelpers.TabUntilFocusedAsync(page, workspaceLink);
            await Expect(workspaceLink).ToBeFocusedAsync();
        }

        await using (var narrowContext = await fixture.NewSignedInContextAsync(seed.AdminEmail, DashboardSeed.Password, new ViewportSize { Width = 480, Height = 800 }))
        {
            var page = narrowContext.Pages[0];
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();

            var workspaceLink = page.GetByRole(AriaRole.Link, new() { Name = $"Open workspace for {seed.UndecidedCampaignName}" });
            await Expect(workspaceLink).ToBeVisibleAsync();
            await A11yMeasurementHelpers.AssertTouchTargetAsync(page, workspaceLink, "Open workspace");
        }
    }

    /// <summary>
    /// BS7: the campaign list renders the active campaign's <c>text-bg-success</c> status badge, whose
    /// text/background contrast meets the WCAG AA 4.5:1 threshold (closing the residual from #69).
    /// </summary>
    [Fact]
    public async Task CampaignList_ActiveBadge_MeetsContrastThreshold()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, DashboardSeed.Password);
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/campaigns").ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Campaigns" })).ToBeVisibleAsync();

        var badge = page.Locator("span.badge.text-bg-success").First;
        await Expect(badge).ToHaveTextAsync("Active");
        await A11yMeasurementHelpers.AssertContrastRatioAsync(badge, 4.5, "campaign list active status badge");
    }

    /// <summary>
    /// Captures dashboard accessibility evidence (screenshots) when <c>NOVA_A11Y_SCREENSHOTS=1</c>;
    /// otherwise skips so a green run always means the assertions executed.
    /// </summary>
    [Fact]
    public async Task Dashboard_A11yEvidence_CapturesScreenshots()
    {
        if (Environment.GetEnvironmentVariable("NOVA_A11Y_SCREENSHOTS") != "1")
        {
            Assert.Skip("Set NOVA_A11Y_SCREENSHOTS=1 to capture dashboard accessibility evidence.");
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, DashboardSeed.Password);
        var page = context.Pages[0];
        var outputDirectory = Path.Combine(Path.GetTempPath(), "nova-a11y-screenshots");
        Directory.CreateDirectory(outputDirectory);

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();
        await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "dashboard-admin-wide.png") });

        await using var narrowContext = await fixture.NewSignedInContextAsync(seed.AdminEmail, DashboardSeed.Password, new ViewportSize { Width = 480, Height = 800 });
        var narrowPage = narrowContext.Pages[0];
        await Expect(narrowPage.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();
        await narrowPage.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "dashboard-admin-narrow.png") });
    }
}
