using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Integration.Tests.Http;
using Nova.SharedKernel.Features.Clubs;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Browser coverage for the member-readable Seasons directory: current-season identity, bounded
/// paging, absent-season states, role-shaped advancement, mobile touch sizing, keyboard focus, and
/// scripting-disabled navigation.
/// </summary>
[Collection(BrowserSuiteCollection.Name)]
public sealed class SeasonDirectoryBrowserTests(BrowserSuiteFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    [Fact]
    public async Task DirectoryMemberReadsCurrentSeasonAndHistoryWithoutAdministratorScopeAsync()
    {
        var seed = await SeedDirectoryAsync(pastSeasonCount: 2, TestContext.Current.CancellationToken);
        var member = await AttachMemberAsync(seed.ClubId, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(member, Password);
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.Seasons).ToString());

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Seasons", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Current season", Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator(".season-stop-current .season-status")).ToHaveTextAsync("Current");
        await Expect(page.Locator(".season-stop-current .season-stop-name")).ToHaveTextAsync(seed.CurrentSeasonName);
        // The member sees bounded history rows with live season links.
        await Expect(page.Locator(".season-stops .season-stop")).ToHaveCountAsync(2);
        foreach (var pastSeason in seed.PastSeasonNames)
        {
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = pastSeason, Exact = true })).ToBeVisibleAsync();
        }

        // Advancement is administrator-only; a member read never depends on administrator scope.
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Start next season", Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Navigation, new() { Name = "Club directory" })
            .GetByRole(AriaRole.Link, new() { Name = "Seasons", Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task DirectoryAdministratorReachesTheReservedAdvancementDestinationAsync()
    {
        var seed = await SeedDirectoryAsync(pastSeasonCount: 1, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.Email, Password);
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.Seasons).ToString());
        var advance = page.GetByRole(AriaRole.Link, new() { Name = "Start next season", Exact = true });
        await Expect(advance).ToBeVisibleAsync();
        await Expect(advance).ToHaveAttributeAsync("href", ClubRoutes.StartNextSeason);

        // The entry point is reachable, and the destination is honest about the slice that owns it.
        await advance.ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Start next season", Exact = true }))
            .ToBeVisibleAsync();
        await Expect(page.GetByText("Season advancement is reserved for issue #260 and is not available here yet."))
            .ToBeVisibleAsync();

        // The reserved destination's own control keeps the documented Nova control height.
        var back = page.GetByRole(AriaRole.Link, new() { Name = "Back to Seasons", Exact = true });
        await Expect(back).ToBeVisibleAsync();
        var backSize = await back.EvaluateAsync<double[]>(
            "(el) => { const r = el.getBoundingClientRect(); return [r.width, r.height]; }");
        backSize[1].ShouldBeGreaterThanOrEqualTo(44, "reserved-destination control height");
    }

    [Fact]
    public async Task DirectoryStatesTheFirstSeasonStateWhenTheClubHasNoSeasonAsync()
    {
        var seed = await SeedDirectoryAsync(
            pastSeasonCount: 0,
            cancellationToken: TestContext.Current.CancellationToken,
            includeCurrentSeason: false);
        await using var context = await fixture.NewSignedInContextAsync(seed.Email, Password);
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.Seasons).ToString());

        await Expect(page.GetByText("No season has been established yet")).ToBeVisibleAsync();
        await Expect(page.GetByText("Establish the club's first season, including when you create its first campaign.")).ToBeVisibleAsync();
        await Expect(page.GetByText("No past seasons are recorded yet")).ToBeVisibleAsync();
        await Expect(page.GetByText("No current season", new() { Exact = true })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task DirectoryPagesLongHistoryAndKeepsTheCurrentSeasonOffEveryPageAsync()
    {
        var seed = await SeedDirectoryAsync(pastSeasonCount: 25, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.Email, Password);
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.Seasons).ToString());

        // Page one loads 20 seasons including the current one, so 19 history rows follow it.
        await Expect(page.Locator(".season-stops .season-stop")).ToHaveCountAsync(19);
        await Expect(page.Locator(".season-pager-position")).ToContainTextAsync("Page 1 of 2");
        await Expect(page.Locator(".season-pager-position")).ToContainTextAsync("26 recorded seasons");
        // The current season is shown once, in the band, and never repeated in the history list.
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = seed.CurrentSeasonName, Exact = true })).ToHaveCountAsync(1);

        await page.GotoAsync(new Uri(fixture.BaseUri, $"{ClubRoutes.Seasons}?page=2").ToString());

        await Expect(page.Locator(".season-stops .season-stop")).ToHaveCountAsync(6);
        await Expect(page.Locator(".season-pager-position")).ToContainTextAsync("Page 2 of 2");
        // The current season stays visible on later pages, independent of the loaded history page.
        await Expect(page.Locator(".season-stop-current .season-stop-name")).ToHaveTextAsync(seed.CurrentSeasonName);
    }

    [Fact]
    public async Task DirectoryMobileKeepsTouchTargetsKeyboardFocusAndNoScriptLinksAsync()
    {
        var seed = await SeedDirectoryAsync(pastSeasonCount: 2, TestContext.Current.CancellationToken);
        var viewport = new ViewportSize { Width = 390, Height = 844 };
        await using var context = await fixture.NewSignedInContextAsync(seed.Email, Password, viewport);
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.Seasons).ToString());
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page, () => AssertShellInteractiveAsync(page));

        var currentLink = page.Locator(".season-stop-current .season-stop-name a");
        await A11yMeasurementHelpers.AssertTouchTargetAsync(page, currentLink, "current season link");
        var size = await currentLink.EvaluateAsync<double[]>(
            "(el) => { const r = el.getBoundingClientRect(); return [r.width, r.height]; }");
        size[1].ShouldBeGreaterThanOrEqualTo(44, "phone season-link height");

        // A phone-width directory must not force horizontal page overflow.
        var overflow = await page.EvaluateAsync<string>(
            @"() => {
                const doc = document.documentElement;
                let worst = null;
                for (const el of document.querySelectorAll('body *')) {
                    const r = el.getBoundingClientRect();
                    if (!worst || r.right > worst.right) {
                        const name = (el.className && el.className.baseVal !== undefined ? el.className.baseVal : el.className) || el.tagName;
                        worst = { right: r.right, label: name };
                    }
                }
                return [doc.scrollWidth, doc.clientWidth, worst ? Math.round(worst.right) + ' ' + worst.label : 'none'].join('|');
            }");
        var overflowParts = overflow.Split('|');
        double.Parse(overflowParts[0], CultureInfo.InvariantCulture)
            .ShouldBeLessThanOrEqualTo(double.Parse(overflowParts[1], CultureInfo.InvariantCulture) + 1,
                $"phone-width horizontal overflow; widest element: {overflowParts[2]}");

        // The season destination stays keyboard reachable and takes focus in reading order.
        await InteractionHelpers.TabUntilFocusedAsync(page, currentLink);
        await Expect(currentLink).ToBeFocusedAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Next page", Exact = true })).ToHaveCountAsync(0);

        // With scripting disabled the season destinations remain real anchors.
        await using var noScript = await fixture.NewSignedInContextAsync(
            seed.Email, Password, viewport, javaScriptEnabled: false);
        var noScriptPage = noScript.Pages[0];
        await noScriptPage.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.Seasons).ToString());
        await Expect(noScriptPage.Locator(".season-stop-current .season-stop-name a")).ToBeVisibleAsync();
        await Expect(noScriptPage.GetByRole(AriaRole.Link, new() { Name = seed.PastSeasonNames[0], Exact = true }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task DirectoryMemberReachesTheReservedSeasonDetailRouteAsync()
    {
        var seed = await SeedDirectoryAsync(pastSeasonCount: 1, TestContext.Current.CancellationToken);
        seed.CurrentSeasonId.ShouldNotBeNull();
        var member = await AttachMemberAsync(seed.ClubId, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(member, Password);
        var page = context.Pages[0];

        // The detail destination is member-readable, so a member reaches it instead of being denied.
        await page.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.SeasonDetail(seed.CurrentSeasonId.Value)).ToString());

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Season", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByText("A full season record is reserved for issue #259 and is not available here yet."))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task DirectoryMemberCannotReachTheAdvancementRouteDirectlyAsync()
    {
        var seed = await SeedDirectoryAsync(pastSeasonCount: 1, TestContext.Current.CancellationToken);
        var member = await AttachMemberAsync(seed.ClubId, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(member, Password);
        var page = context.Pages[0];

        // An ordinary member never sees the entry, and the route itself must refuse them too.
        await page.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.StartNextSeason).ToString());

        await page.WaitForURLAsync(url =>
            string.Equals(new Uri(url).PathAndQuery, ClubRoutes.OverviewWithPermissionsChanged, StringComparison.Ordinal));
        await Expect(page.GetByText("You don't have access to that section. Club navigation reflects your current permissions."))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task DirectoryPhoneSeasonLinkMeetsBothTargetDimensionsForAShortNameAsync()
    {
        var seed = await SeedDirectoryAsync(
            pastSeasonCount: 1,
            TestContext.Current.CancellationToken,
            currentSeasonName: "A");
        await using var context = await fixture.NewSignedInContextAsync(
            seed.Email, Password, new ViewportSize { Width = 390, Height = 844 });
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.Seasons).ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Seasons", Exact = true })).ToBeVisibleAsync();

        // A one-character season name must still present a full control box, not just a full height.
        var link = page.Locator(".season-stop-current .season-stop-name a");
        await Expect(link).ToBeVisibleAsync();
        var size = await link.EvaluateAsync<double[]>(
            "(el) => { const r = el.getBoundingClientRect(); return [r.width, r.height]; }");
        size[0].ShouldBeGreaterThanOrEqualTo(44, "phone season-link width for a short name");
        size[1].ShouldBeGreaterThanOrEqualTo(44, "phone season-link height for a short name");
    }

    /// <summary>
    /// Captures seasons-directory accessibility evidence when <c>NOVA_A11Y_SCREENSHOTS=1</c>;
    /// otherwise skips so a green run always means the assertions executed.
    /// </summary>
    [Fact]
#pragma warning disable MA0051 // Keep this complete evidence pass together so every captured state and its assertions remain reviewable.
    public async Task SeasonDirectoryA11yEvidenceCapturesScreenshotsAsync()
#pragma warning restore MA0051
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("NOVA_A11Y_SCREENSHOTS"), "1", StringComparison.Ordinal))
        {
            Assert.Skip("Set NOVA_A11Y_SCREENSHOTS=1 to capture seasons-directory accessibility evidence.");
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var outputDirectory = Path.Combine(Path.GetTempPath(), "nova-a11y-screenshots");
        Directory.CreateDirectory(outputDirectory);

        // Administrator: the lit current stop above bounded history.
        var seeded = await SeedDirectoryAsync(pastSeasonCount: 3, cancellationToken);
        await using (var desktop = await fixture.NewSignedInContextAsync(seeded.Email, Password))
        {
            var page = desktop.Pages[0];
            await page.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.Seasons).ToString());
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Seasons", Exact = true })).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Start next season", Exact = true })).ToBeVisibleAsync();
            await AssertDirectoryContrastAsync(page);
            await CaptureAsync(page, outputDirectory, "seasons-directory-desktop.png");
        }

        // The same directory at phone width.
        await using (var mobile = await fixture.NewSignedInContextAsync(
            seeded.Email, Password, new ViewportSize { Width = 390, Height = 844 }))
        {
            var page = mobile.Pages[0];
            await page.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.Seasons).ToString());
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Seasons", Exact = true })).ToBeVisibleAsync();
            await A11yMeasurementHelpers.AssertTouchTargetAsync(
                page, page.Locator(".season-stop-current .season-stop-name a"), "current season link");
            await CaptureAsync(page, outputDirectory, "seasons-directory-mobile.png");
        }

        // Member: the same read with no advancement entry point.
        var memberEmail = await AttachMemberAsync(seeded.ClubId, cancellationToken);
        await using (var member = await fixture.NewSignedInContextAsync(memberEmail, Password))
        {
            var page = member.Pages[0];
            await page.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.Seasons).ToString());
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Seasons", Exact = true })).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Start next season", Exact = true })).ToHaveCountAsync(0);
            await CaptureAsync(page, outputDirectory, "seasons-directory-member.png");
        }

        // First season: no season recorded at all.
        var seasonless = await SeedDirectoryAsync(
            pastSeasonCount: 0, cancellationToken, includeCurrentSeason: false);
        await using (var firstSeason = await fixture.NewSignedInContextAsync(seasonless.Email, Password))
        {
            var page = firstSeason.Pages[0];
            await page.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.Seasons).ToString());
            await Expect(page.GetByText("No season has been established yet")).ToBeVisibleAsync();
            await CaptureAsync(page, outputDirectory, "seasons-directory-first-season.png");
        }

        // Recovery: recorded seasons with no current one.
        var recovery = await SeedDirectoryAsync(
            pastSeasonCount: 2, cancellationToken, includeCurrentSeason: false);
        await using (var noCurrent = await fixture.NewSignedInContextAsync(recovery.Email, Password))
        {
            var page = noCurrent.Pages[0];
            await page.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.Seasons).ToString());
            await Expect(page.GetByText("No current season", new() { Exact = true })).ToBeVisibleAsync();
            await CaptureAsync(page, outputDirectory, "seasons-directory-recovery.png");
        }

        // Long history: a middle page showing both paging directions and the caption.
        var longHistory = await SeedDirectoryAsync(pastSeasonCount: 45, cancellationToken);
        await using (var paged = await fixture.NewSignedInContextAsync(longHistory.Email, Password))
        {
            var page = paged.Pages[0];
            await page.GotoAsync(new Uri(fixture.BaseUri, $"{ClubRoutes.Seasons}?page=2").ToString());
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Previous page", Exact = true })).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Next page", Exact = true })).ToBeVisibleAsync();
            await Expect(page.Locator(".season-pager-position")).ToContainTextAsync("Page 2 of 3");
            await CaptureAsync(page, outputDirectory, "seasons-directory-paged.png");
        }
    }

    /// <summary>
    /// Captures the full page with nothing focused. The Club shell focuses the destination heading
    /// after navigation, so its default focus ring is shell behaviour rather than this surface's.
    /// </summary>
    /// <param name="page">The rendered page to capture.</param>
    /// <param name="outputDirectory">The evidence output directory.</param>
    /// <param name="fileName">The capture file name.</param>
    /// <returns>A task that completes once the capture is written.</returns>
    private static async Task CaptureAsync(IPage page, string outputDirectory, string fileName)
    {
        await page.EvaluateAsync(
            "() => { if (document.activeElement instanceof HTMLElement) { document.activeElement.blur(); } }");
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(outputDirectory, fileName),
            FullPage = true
        });
    }

    /// <summary>Measures the directory's text/surface contrast on the lit stop and the ruled history rows.</summary>
    /// <param name="page">The rendered directory page.</param>
    /// <returns>A task that completes once every contrast assertion passes.</returns>
    private static async Task AssertDirectoryContrastAsync(IPage page)
    {
        // Ink on the sea-glass field, the Current chip, and a history stop link on the page surface.
        await A11yMeasurementHelpers.AssertContrastRatioAsync(
            page.Locator(".season-stop-current .season-stop-body").First, 4.5, "current season field");
        await A11yMeasurementHelpers.AssertContrastRatioAsync(
            page.Locator(".season-stop-current .season-status").First, 4.5, "current season chip");
        await A11yMeasurementHelpers.AssertContrastRatioAsync(
            page.Locator(".season-stops .season-stop-name a").First, 4.5, "history season link");

        // The lit stop's link sits on the sea-glass field, so composite the stacked backgrounds
        // instead of the shared helper's white approximation: plain link teal measures below 4.5:1
        // against that field, which is why the stop uses the theme's on-subtle emphasis token.
        var linkRatio = await page.Locator(".season-stop-current .season-stop-name a").First.EvaluateAsync<double>(
            @"(el) => {
                const parse = c => {
                    const m = c.match(/rgba?\(([\d.]+),\s*([\d.]+),\s*([\d.]+)(?:,\s*([\d.]+))?\)/);
                    return m ? { r: +m[1], g: +m[2], b: +m[3], a: m[4] === undefined ? 1 : +m[4] } : null;
                };
                const over = (c, base) => c.a === 1 ? c : {
                    r: c.r * c.a + base.r * (1 - c.a), g: c.g * c.a + base.g * (1 - c.a), b: c.b * c.a + base.b * (1 - c.a)
                };
                const lum = c => {
                    const f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
                    return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b);
                };
                const layers = [];
                for (let node = el; node instanceof Element; node = node.parentElement) {
                    const c = parse(getComputedStyle(node).backgroundColor);
                    if (c && c.a > 0) layers.push(c);
                }
                let bg = { r: 255, g: 255, b: 255 };
                for (let i = layers.length - 1; i >= 0; i--) bg = over(layers[i], bg);
                const fg = over(parse(getComputedStyle(el).color), bg);
                const l1 = lum(fg), l2 = lum(bg);
                const [hi, lo] = l1 >= l2 ? [l1, l2] : [l2, l1];
                return (hi + 0.05) / (lo + 0.05);
            }");
        linkRatio.ShouldBeGreaterThanOrEqualTo(4.5, "current season link contrast on the sea-glass field");
    }

    /// <summary>Proves the interactive circuit attached by driving the shell's Blazor-owned directory sheet.</summary>
    /// <param name="page">The page carrying the Club shell.</param>
    /// <returns>A task that completes once the sheet has opened and closed under keyboard activation.</returns>
    private static async Task AssertShellInteractiveAsync(IPage page)
    {
        var toggle = page.Locator(".club-directory-toggle");
        var directory = page.GetByRole(AriaRole.Navigation, new() { Name = "Club directory" });
        await InteractionHelpers.ActUntilAsync(page, () => toggle.PressAsync("Enter"),
            async () => string.Equals(await toggle.GetAttributeAsync("aria-expanded"), "true", StringComparison.Ordinal));
        await Expect(directory).ToBeVisibleAsync();
        await toggle.PressAsync("Enter");
        await Expect(directory).ToBeHiddenAsync();
    }

    private async Task<DirectorySeed> SeedDirectoryAsync(
        int pastSeasonCount,
        CancellationToken cancellationToken,
        bool includeCurrentSeason = true,
        string? currentSeasonName = null)
    {
        using var client = fixture.AppHost.CreateNovaHttpClient();
        var email = SeedingHelpers.UniqueEmail("seasons-directory-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        var club = await SeedingHelpers.CreateClubAsync(client, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);

        var suffix = Guid.NewGuid().ToString("N");
        var context = fixture.AppHost.CreateAdminContext();
        await using (context)
        {
            var user = await context.Users.SingleAsync(
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
                candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
#pragma warning restore CA1862
            // A seasonless club is a supported state, so seed only the seasons the scenario asks for.
            SeasonEntity? current = null;
            if (includeCurrentSeason)
            {
                current = NewSeason(currentSeasonName ?? $"Season {suffix}", 2026, club.ClubId, user.Id);
                context.Seasons.Add(current);
            }

            var past = new List<SeasonEntity>();
            for (var index = 1; index <= pastSeasonCount; index++)
            {
                var season = NewSeason($"Season {suffix} Past {index:00}", 2026 - index, club.ClubId, user.Id);
                context.Seasons.Add(season);
                past.Add(season);
            }

            await context.SaveChangesAsync(cancellationToken);
            if (current is not null)
            {
                var clubEntity = await context.Clubs.SingleAsync(
                    candidate => candidate.ClubId == club.ClubId, cancellationToken);
                clubEntity.CurrentSeasonId = current.SeasonId;
                await context.SaveChangesAsync(cancellationToken);
            }

            return new DirectorySeed(
                club.ClubId,
                email,
                current?.SeasonId,
                current?.Name ?? string.Empty,
                [.. past.Select(season => season.Name)]);
        }
    }

    private static SeasonEntity NewSeason(string name, int startYear, long clubId, long createdById) => new()
    {
        CreationOperationId = Guid.NewGuid(),
        Name = name,
        StartDate = new DateOnly(startYear, 9, 1),
        EndDate = new DateOnly(startYear + 1, 5, 31),
        ClubId = clubId,
        CreatedById = createdById
    };

    private async Task<string> AttachMemberAsync(long clubId, CancellationToken cancellationToken)
    {
        using var member = fixture.AppHost.CreateNovaHttpClient();
        var email = SeedingHelpers.UniqueEmail("seasons-directory-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(member, email, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture.AppHost, email, clubId, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(member, cancellationToken);
        return email;
    }

    private sealed record DirectorySeed(
        long ClubId,
        string Email,
        long? CurrentSeasonId,
        string CurrentSeasonName,
        IReadOnlyList<string> PastSeasonNames);
}
