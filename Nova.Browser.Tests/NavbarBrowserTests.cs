using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Entities;
using Nova.Integration.Tests.Http;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Browser-level acceptance of the Fieldhouse Wayfinding navigation (issue #134 + follow-up
/// polish + #156 mobile-menu redesign): the authenticated navigation renders as a fixed left
/// rail at md+ (768px+) and, below md, as a fixed bottom bar showing the brand lockup and a
/// hamburger only; the hamburger opens one paper sheet that lists every route — primary
/// (Dashboard, club, Campaigns, Players, Teams) and account (Manage, Logout) — as
/// full-width, left-aligned rows with legible full-size labels (no forced horizontal scroll,
/// avatar never overlaps its label); items show icon + label (inline rows at md+, menu rows
/// in the mobile sheet) with bootstrap-icons glyphs; the active item carries a kelp teal edge
/// rail at desktop (left-edge bar) and a top marker in the mobile menu, swapping its outline
/// glyph for the matching <c>-fill</c> variant (CSS overlay, no layout shift); the Manage
/// link navigates to /Account/Manage; Logout posts the antiforgery form and returns to the
/// login page; the unauthenticated root renders the public landing page with no
/// authenticated icon links; and the fallback branches are covered — the anonymous
/// single-Login tab stays inline with the hamburger hidden, and with scripting disabled all
/// routes fall back to the inline scrollable strip through a component-local scripting media
/// query.
/// </summary>
/// <param name="fixture">The Aspire-hosted browser suite fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class NavbarBrowserTests(BrowserSuiteFixture fixture)
{
    private const string ExpectedKelpTealRgb = "rgb(14, 124, 123)";
    // --bs-light (sea glass) in the theme: the nav-bar surface. The mobile sheet must share it
    // so the sheet and the bar read as one continuous surface (issue #159 report A).
    private const string ExpectedSeaGlassRgb = "rgb(221, 242, 236)";
    // The opened sheet's active-row field: color-mix(in srgb, var(--bs-primary) 10%,
    // var(--bs-light)) — a token-derived deeper sea-glass blend over the unified sheet, so the
    // field still reads (issue #159 review: --bs-primary-bg-subtle is --bs-light byte-identical).
    // Computed color-mix values serialize as `color(srgb r g b)` in modern Chromium while
    // legacy declarations serialize as `rgb(r, g, b)`; both formats are normalized before
    // comparison in AssertColorEqualsAsync.
    private const string ExpectedActiveFieldColor = "rgb(200, 230, 225)";

    /// <summary>
    /// NB1: after signing in, the navbar shows the icon-first items — Dashboard, the club name,
    /// Campaigns, Players, and Teams — each with a bootstrap-icons glyph that renders with the
    /// bootstrap-icons font family, and the Manage/Logout controls on the right.
    /// </summary>
    [Fact]
    public async Task Navbar_Authenticated_ShowsIconFirstItemsWithBootstrapIcons()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, DashboardSeed.Password);
        var page = context.Pages[0];

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();
        var clubName = await GetClubNameAsync(seed.ClubId, cancellationToken);

        var nav = page.Locator("nav[aria-label='Primary']");

        // Dashboard is the first item and points at the dashboard path.
        var dashboard = nav.GetByRole(AriaRole.Link, new() { Name = "Dashboard", Exact = true });
        await Expect(dashboard).ToBeVisibleAsync();
        await Expect(dashboard).ToHaveAttributeAsync("href", "/dashboard");
        await AssertBootstrapIconGlyphAsync(dashboard, "bi-house");

        // The club item keeps the actual club name as its label. Every club now has a required
        // crest (#142), so the club item renders the crest avatar image instead of the building
        // glyph fallback.
        var club = nav.GetByRole(AriaRole.Link, new() { Name = clubName, Exact = true });
        await Expect(club).ToBeVisibleAsync();
        await Expect(club.Locator("img.nav-avatar")).ToHaveCountAsync(1);

        // Campaigns / Players / Teams icon + label items.
        await AssertIconLinkAsync(nav, "Campaigns", "campaigns", "bi-calendar-check");
        await AssertIconLinkAsync(nav, "Players", "players", "bi-people");
        await AssertIconLinkAsync(nav, "Teams", "teams", "bi-shield");

        // Manage keeps the avatar (the seeded admin has a completed profile photo) and text.
        var manage = nav.GetByRole(AriaRole.Link, new() { Name = "Manage", Exact = false });
        await Expect(manage).ToBeVisibleAsync();
        await Expect(manage).ToHaveAttributeAsync("href", "Account/Manage");
        await Expect(manage.Locator("img.nav-avatar")).ToHaveCountAsync(1);

        // Logout is a button carrying an icon glyph.
        var logout = nav.GetByRole(AriaRole.Button, new() { Name = "Logout", Exact = true });
        await Expect(logout).ToBeVisibleAsync();
        await AssertBootstrapIconGlyphAsync(logout, "bi-box-arrow-right");
    }

    /// <summary>
    /// NB2: the active nav item renders the kelp teal edge rail — a left-edge bar at md+ — both at
    /// the dashboard root (Dashboard active) and on the campaigns page (Campaigns active); the
    /// active item swaps its outline glyph for the matching <c>-fill</c> variant (CSS overlay, so
    /// the previously-active item returns to outline with no layout jump).
    /// </summary>
    [Fact]
    public async Task Navbar_ActiveItem_ShowsKelpTealEdgeRail()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, DashboardSeed.Password);
        var page = context.Pages[0];
        var nav = page.Locator("nav[aria-label='Primary']");

        // At the dashboard root the Dashboard link is the active item.
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();
        var dashboard = nav.GetByRole(AriaRole.Link, new() { Name = "Dashboard", Exact = true });
        await AssertKelpTealEdgeRailAsync(dashboard);
        await AssertActiveFillGlyphAsync(dashboard, "bi-house-fill", "bi-house");

        // On the campaigns page the Campaigns link becomes the active item.
        await page.GotoAsync(new Uri(fixture.BaseUri, "/campaigns").ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Campaigns" })).ToBeVisibleAsync();
        var campaigns = nav.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true });
        await AssertKelpTealEdgeRailAsync(campaigns);
        await AssertActiveFillGlyphAsync(campaigns, "bi-calendar-check-fill", "bi-calendar-check");
        await Expect(dashboard).Not.ToHaveClassAsync(new Regex("\\bactive\\b"));
        // The previously-active link returned to its outline glyph (fill overlay off).
        await AssertInactiveOutlineGlyphAsync(dashboard, "bi-house-fill", "bi-house");
    }

    /// <summary>
    /// NB3: the Manage link navigates to /Account/Manage.
    /// </summary>
    [Fact]
    public async Task Navbar_ManageLink_NavigatesToAccountManage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, DashboardSeed.Password);
        var page = context.Pages[0];

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();
        var manage = page.Locator("nav[aria-label='Primary']").GetByRole(AriaRole.Link, new() { Name = "Manage", Exact = false });
        await manage.ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/Account/Manage", StringComparison.OrdinalIgnoreCase));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Profile", Exact = true })).ToBeVisibleAsync();
    }

    /// <summary>
    /// NB4: clicking Logout posts the antiforgery-protected form and returns to the login page,
    /// where the unauthenticated navbar shows only the Login link.
    /// </summary>
    [Fact]
    public async Task Navbar_Logout_PostsAndReturnsToLogin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, DashboardSeed.Password);
        var page = context.Pages[0];

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();
        var logout = page.Locator("nav[aria-label='Primary']").GetByRole(AriaRole.Button, new() { Name = "Logout", Exact = true });
        await logout.ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase));

        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Login", Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator("nav[aria-label='Primary'] .bi")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// NB5: an anonymous visit to / renders the public landing page — not the authenticated
    /// primary nav — so no icon-first items leak to unauthenticated users.
    /// </summary>
    [Fact]
    public async Task Navbar_Unauthenticated_ShowsPublicLanding_WithNoIconLinks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await fixture.NewAnonymousContextAsync();
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/").ToString());

        // The public landing page (not the login screen) renders for anonymous users.
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Run better tryouts. Build stronger teams.", Exact = true })).ToBeVisibleAsync();

        // No authenticated primary navbar leaks to unauthenticated users.
        await Expect(page.Locator("nav[aria-label='Primary']")).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Dashboard", Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Players", Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Teams", Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Logout", Exact = true })).ToHaveCountAsync(0);
    }

    /// <summary>
    /// NB6: at a desktop (md+) viewport the authorized nav rail lays out as a left rail: the nav
    /// itself is fixed to the left edge with a 15rem width, the links render as inline rows
    /// (icon beside label), and the brand lockup is visible.
    /// </summary>
    [Fact]
    public async Task Navbar_Desktop_RendersAsLeftRail()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(
            seed.AdminEmail,
            DashboardSeed.Password,
            viewport: new ViewportSize { Width = 1280, Height = 800 });
        var page = context.Pages[0];

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();
        var clubName = await GetClubNameAsync(seed.ClubId, cancellationToken);
        var nav = page.Locator("nav[aria-label='Primary']");

        // The nav is fixed to the left edge at 15rem on md+.
        var navBox = await nav.BoundingBoxAsync();
        navBox.ShouldNotBeNull();
        ((double)navBox!.X).ShouldBe(0, 1.0, "the rail must be fixed to the left edge at md+");
        ((double)navBox!.Width).ShouldBeInRange(239.0, 241.0, "the rail must keep its 15rem (240px) width at md+");

        // The brand lockup is visible at md+.
        await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Nova dashboard" })).ToBeVisibleAsync();

        // Each authorized item renders an inline row at md+ (Dashboard, the club, Campaigns,
        // Players, Teams). At md+ every glyph slot is the 2rem uniform icon lane (issue #156
        // alignment: the club crest avatar must not look larger than the other icons).
        await AssertInlineRowLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Dashboard", Exact = true }), 32.0);
        await AssertInlineRowLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = clubName, Exact = true }), 32.0);
        await AssertInlineRowLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true }), 32.0);
        await AssertInlineRowLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Players", Exact = true }), 32.0);
        await AssertInlineRowLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Teams", Exact = true }), 32.0);

        // All seven rows (Dashboard, club crest, Campaigns, Players, Teams, Manage, Logout)
        // share one uniform leading lane at md+ — identical box, identical x-offset — so the
        // labels align and the crest reads visually equal to the glyph icons.
        await AssertUniformIconLaneAsync(nav, clubName);
    }

    /// <summary>
    /// NB7: at a mobile (&lt;md) viewport the authorized nav renders as a fixed bottom bar
    /// showing the brand lockup plus the hamburger — never five shrinking truncated tabs (the
    /// #156 complaint). The collapse container is fully hidden at rest (display:none removes
    /// every route from the accessibility tree), and clicking the hamburger opens one paper
    /// sheet that lists EVERY route — primary (Dashboard, club, Campaigns, Players, Teams)
    /// and account (Manage, Logout) — as full-width, left-aligned rows; the sheet is the only
    /// place the routes surface at &lt;md, so no horizontal scroll is forced anywhere. The
    /// sheet reuses the bar's --bs-light sea-glass surface so the two read as one continuous
    /// surface (issue #159 report A: the sheet used to fall back to --bs-body-bg paper white),
    /// asserted via computed background-color equality.
    /// </summary>
    [Fact]
    public async Task Navbar_Mobile_RestShowsBrandAndToggle_MenuSheetListsEveryRoute()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(
            seed.AdminEmail,
            DashboardSeed.Password,
            viewport: new ViewportSize { Width = 480, Height = 800 });
        var page = context.Pages[0];

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();
        var clubName = await GetClubNameAsync(seed.ClubId, cancellationToken);
        var nav = page.Locator("nav[aria-label='Primary']");

        // The nav is fixed to the bottom edge at <md.
        var navBox = await nav.BoundingBoxAsync();
        navBox.ShouldNotBeNull();
        var viewportHeight = await page.EvaluateAsync<int>("() => window.innerHeight");
        Math.Abs((double)navBox!.Y + (double)navBox.Height - viewportHeight).ShouldBeLessThanOrEqualTo(1.0, "the bar must be fixed to the bottom edge at <md");

        // The collapse is the menu container; at rest it is display:none so no route (primary or
        // account) is in the accessibility tree — the bar must never show five shrinking tabs.
        var collapse = nav.Locator(".navbar-collapse");
        await Expect(collapse).ToHaveCountAsync(1);
        var collapseDisplay = await collapse.EvaluateAsync<string>("(el) => getComputedStyle(el).display");
        collapseDisplay.ShouldBe("none", "the whole route list must be hidden at rest behind the toggle");

        // At rest: brand lockup visible, hamburger visible with a ≥2.75rem touch target.
        await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Nova dashboard" })).ToBeVisibleAsync();
        var toggler = nav.GetByRole(AriaRole.Button, new() { Name = "Toggle navigation" });
        await Expect(toggler).ToBeVisibleAsync();
        var togglerBox = await toggler.BoundingBoxAsync();
        togglerBox.ShouldNotBeNull();
        ((double)togglerBox!.Height).ShouldBeGreaterThanOrEqualTo(43.5, "the toggler must keep a 2.75rem touch target");
        ((double)togglerBox!.Width).ShouldBeGreaterThanOrEqualTo(43.5, "the toggler must keep a 2.75rem touch target");

        // No route is reachable visually (or in the accessibility tree) until the menu opens.
        foreach (var label in new[] { "Dashboard", clubName, "Campaigns", "Players", "Teams" })
        {
            await Expect(nav.GetByRole(AriaRole.Link, new() { Name = label, Exact = true })).ToHaveCountAsync(0);
        }

        await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Manage", Exact = false })).ToHaveCountAsync(0);
        await Expect(nav.GetByRole(AriaRole.Button, new() { Name = "Logout", Exact = true })).ToHaveCountAsync(0);

        // Opening the menu (Bootstrap collapse) reveals every route in the rising sheet.
        await toggler.ClickAsync();
        await Expect(collapse).ToHaveClassAsync(new Regex(@"\bshow\b"));
        await Expect(toggler).ToHaveAttributeAsync("aria-expanded", "true");

        // Issue #159 report A regression guard: the opened sheet must read as one continuous
        // surface with the bar — the sheet's computed background equals the bar's, both the
        // --bs-light sea-glass token (#DDF2EC). The sheet used to fall back to --bs-body-bg
        // (paper white), so the bar looked green-tinted and the sheet white — a visible seam.
        var sheetBackground = await collapse.EvaluateAsync<string>("(el) => getComputedStyle(el).backgroundColor");
        sheetBackground.ShouldBe(ExpectedSeaGlassRgb, "the opened sheet must use the bar's --bs-light surface");
        var barBackground = await nav.EvaluateAsync<string>("(el) => getComputedStyle(el).backgroundColor");
        barBackground.ShouldBe(ExpectedSeaGlassRgb, "the bottom bar must keep its --bs-light surface");
        barBackground.ShouldBe(sheetBackground, "the sheet and the bar must share one background so they read as one continuous surface");

        // Issue #159 review B regression guard: with the sheet surface unified on --bs-light,
        // the active row's field must still read. The base .nav-link.active rule paints
        // --bs-primary-bg-subtle, which the theme defines byte-identical to --bs-light, so the
        // sheet overrides it with the token-derived deeper sea-glass blend (color-mix 10%
        // primary over light). An inactive row keeps the transparent sheet surface.
        var dashboardLink = nav.GetByRole(AriaRole.Link, new() { Name = "Dashboard", Exact = true });
        await AssertColorEqualsAsync(dashboardLink, ExpectedActiveFieldColor, "the active sheet row must carry the token-derived deeper sea-glass field");
        await AssertColorNotEqualsAsync(dashboardLink, sheetBackground, "the active field must not blend into the unified sheet surface");
        var inactiveField = await nav.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true })
            .EvaluateAsync<string>("(el) => getComputedStyle(el).backgroundColor");
        inactiveField.ShouldBe("rgba(0, 0, 0, 0)", "inactive sheet rows must stay transparent on the unified sheet");

        await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Dashboard", Exact = true })).ToBeVisibleAsync();
        await Expect(nav.GetByRole(AriaRole.Link, new() { Name = clubName, Exact = true })).ToBeVisibleAsync();
        await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true })).ToBeVisibleAsync();
        await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Players", Exact = true })).ToBeVisibleAsync();
        await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Teams", Exact = true })).ToBeVisibleAsync();
        await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Manage", Exact = false })).ToBeVisibleAsync();
        await Expect(nav.GetByRole(AriaRole.Button, new() { Name = "Logout", Exact = true })).ToBeVisibleAsync();

        // Every sheet row is a full-width, left-aligned menu row — not a shrink-to-fit tab.
        await AssertMenuSheetRowAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Dashboard", Exact = true }));
        await AssertMenuSheetRowAsync(nav.GetByRole(AriaRole.Link, new() { Name = clubName, Exact = true }));
        await AssertMenuSheetRowAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Teams", Exact = true }));
        await AssertMenuSheetRowAsync(nav.GetByRole(AriaRole.Button, new() { Name = "Logout", Exact = true }));

        // The collapse container itself never forces horizontal scrolling at <md; the menu
        // stacks vertically inside a bounded sheet instead of cramming five tabs in a row.
        // (overflow-y: auto makes the CSS-computed overflow-x report "auto" too — the spec
        // computes the other axis to auto when one axis is auto — so the real guard is that
        // nothing actually scrolls horizontally: scrollWidth must not exceed clientWidth.)
        var horizontalOverflow = await collapse.EvaluateAsync<string>(
            "(el) => el.scrollWidth > el.clientWidth ? 'scrolls' : 'fits'");
        horizontalOverflow.ShouldBe("fits", "the menu must not scroll horizontally at <md");
        var flexDirection = await collapse.EvaluateAsync<string>("(el) => getComputedStyle(el).flexDirection");
        flexDirection.ShouldBe("column", "the sheet must stack rows vertically");

        // An inactive link (Campaigns at the dashboard root) keeps its outline glyph visible; the
        // fill overlay stays hidden in the sheet just as at desktop.
        await AssertInactiveOutlineGlyphAsync(
            nav.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true }),
            "bi-calendar-check-fill",
            "bi-calendar-check");

        // All seven rows (Dashboard, club crest, Campaigns, Players, Teams, Manage, Logout) share
        // one uniform leading lane in the opened sheet — identical box, identical x-offset — so
        // the glyph rows no longer read off-lane next to the avatar rows (issue #156 alignment
        // gap: the glyph slot used to drop back to 1.25rem in the sheet while the avatars kept
        // their 2rem slot).
        await AssertUniformIconLaneAsync(nav, clubName);
    }

    /// <summary>
    /// Verifies that enhanced navigation from an open mobile menu restores the collapsed
    /// hamburger state instead of leaving the route list visible as a horizontal strip.
    /// </summary>
    [Fact]
    public async Task Navbar_Mobile_CollapsesAfterEnhancedNavigation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(
            seed.AdminEmail,
            DashboardSeed.Password,
            viewport: new ViewportSize { Width = 480, Height = 800 });
        var page = context.Pages[0];

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();

        var nav = page.Locator("nav[aria-label='Primary']");
        var toggler = nav.Locator(".navbar-toggler");
        var collapse = nav.Locator(".navbar-collapse");

        await toggler.ClickAsync();
        await Expect(collapse).ToHaveClassAsync(new Regex(@"\bshow\b"));
        await page.EvaluateAsync("() => window.__novaEnhancedNavigationProbe = true");

        await nav.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(
            url => new Uri(url).AbsolutePath.Equals("/campaigns", StringComparison.OrdinalIgnoreCase),
            new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Campaigns", Exact = true })).ToBeVisibleAsync();

        var usedEnhancedNavigation = await page.EvaluateAsync<bool>(
            "() => window.__novaEnhancedNavigationProbe === true");
        usedEnhancedNavigation.ShouldBeTrue("the route change must exercise Blazor enhanced navigation");
        await AssertSheetContractAtRestAsync(nav, toggler, collapse);
    }

    /// <summary>
    /// Verifies that Bootstrap's intermediate collapsing state keeps the opened sheet's fixed
    /// geometry and surface instead of flashing as an unstyled in-flow navigation strip.
    /// </summary>
    [Fact]
    public async Task Navbar_Mobile_ToggleKeepsSheetGeometryDuringCollapseTransition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(
            seed.AdminEmail,
            DashboardSeed.Password,
            viewport: new ViewportSize { Width = 480, Height = 800 });
        var page = context.Pages[0];

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();

        var nav = page.Locator("nav[aria-label='Primary']");
        var toggler = nav.Locator(".navbar-toggler");
        var collapse = nav.Locator(".navbar-collapse");
        await collapse.EvaluateAsync(
            """
            (element) => {
                window.__novaCollapseTransitionStates = [];
                new MutationObserver(() => {
                    if (!element.classList.contains("collapsing")) {
                        return;
                    }

                    const style = getComputedStyle(element);
                    window.__novaCollapseTransitionStates.push(
                        `${style.position}|${style.backgroundColor}|${style.bottom}`);
                }).observe(element, { attributes: true, attributeFilter: ["class"] });
            }
            """);

        await toggler.ClickAsync();
        await Expect(collapse).ToHaveClassAsync(new Regex(@"\bshow\b"));
        await toggler.ClickAsync();
        await Expect(collapse).Not.ToHaveClassAsync(new Regex(@"\bshow\b"));

        var transitionStates = await page.EvaluateAsync<string[]>(
            "() => window.__novaCollapseTransitionStates");
        transitionStates.ShouldNotBeEmpty("the Bootstrap collapse lifecycle must exercise its intermediate state");
        foreach (var state in transitionStates)
        {
            state.StartsWith(
                $"absolute|{ExpectedSeaGlassRgb}|",
                StringComparison.Ordinal).ShouldBeTrue(
                    "the intermediate collapse state must retain the sheet geometry and surface");
            state.ShouldNotEndWith("|auto", "the intermediate sheet must remain anchored above the mobile bar");
        }
    }

    /// <summary>
    /// Verifies that the separated account routes retain the same compact row rhythm as the
    /// primary routes in the opened mobile sheet.
    /// </summary>
    [Fact]
    public async Task Navbar_Mobile_AccountRoutesMatchPrimaryRowRhythm()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(
            seed.AdminEmail,
            DashboardSeed.Password,
            viewport: new ViewportSize { Width = 480, Height = 800 });
        var page = context.Pages[0];

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();

        var nav = page.Locator("nav[aria-label='Primary']");
        await nav.Locator(".navbar-toggler").ClickAsync();
        await Expect(nav.Locator(".navbar-collapse")).ToHaveClassAsync(new Regex(@"\bshow\b"));

        var teamsBox = await nav.GetByRole(AriaRole.Link, new() { Name = "Teams", Exact = true }).BoundingBoxAsync();
        var manageBox = await nav.GetByRole(AriaRole.Link, new() { Name = "Manage", Exact = false }).BoundingBoxAsync();
        var logoutBox = await nav.GetByRole(AriaRole.Button, new() { Name = "Logout", Exact = true }).BoundingBoxAsync();
        teamsBox.ShouldNotBeNull();
        manageBox.ShouldNotBeNull();
        logoutBox.ShouldNotBeNull();

        var sectionGap = manageBox!.Y - (teamsBox!.Y + teamsBox.Height);
        sectionGap.ShouldBeInRange(3.5f, 9.5f, "the account separator must not create an oversized gap");

        var accountRowGap = logoutBox!.Y - (manageBox.Y + manageBox.Height);
        accountRowGap.ShouldBeInRange(3.5f, 4.5f, "Manage and Logout must use the same compact row gap as primary routes");
        Math.Abs(manageBox.Height - logoutBox.Height).ShouldBeLessThanOrEqualTo(
            1.0f,
            "Manage and Logout must have matching row heights");
    }

    /// <summary>
    /// NB8: on the Account/Manage page the Manage link is the active item and its kelp teal edge
    /// rail is still flush with the rail's left edge.
    /// </summary>
    [Fact]
    public async Task Navbar_ManageActive_EdgeRailAndAvatarLarger()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(
            seed.AdminEmail,
            DashboardSeed.Password,
            viewport: new ViewportSize { Width = 1280, Height = 800 });
        var page = context.Pages[0];

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();
        var nav = page.Locator("nav[aria-label='Primary']");

        // Navigate to Account/Manage; the Manage link becomes the active item.
        await page.GotoAsync(new Uri(fixture.BaseUri, "/Account/Manage").ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Profile", Exact = true })).ToBeVisibleAsync();

        var manage = nav.GetByRole(AriaRole.Link, new() { Name = "Manage", Exact = false });
        await AssertKelpTealEdgeRailAsync(manage);

        // The avatar is a 2rem (32px) circle with a visible border, clearly larger than the 1.5rem
        // (24px) nav icons.
        await AssertNavAvatarAsync(manage);
    }

    /// <summary>
    /// NB13: the /Account/Manage/ProfilePhoto page renders inside the shared account hall frame —
    /// the "Manage your account" title + lead, the directory wall with its panel list, the
    /// working hall hosting the profile photo editor — and the "Profile photo" panel in the
    /// directory wall carries the active (punched) state while on that page. Also proves the
    /// <c>ProfilePhotoEditor</c> island is interactive (not static SSR): after moving the page to
    /// WebAssembly (InteractiveAuto's eventual render mode), uploading a JPEG through the real
    /// file input fires the choose handler and renders the cropper frame, which would never happen
    /// if the island lost its <c>@rendermode</c> annotation. Regression guard for issue #156:
    /// previously the manage profile photo page was served by a Nova.UI page with no ManageLayout,
    /// so it rendered under the bare MainLayout with no account-hall frame.
    /// </summary>
    [Fact]
    public async Task Account_ManageProfilePhoto_RendersInsideAccountHallFrame()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(
            seed.AdminEmail,
            DashboardSeed.Password,
            viewport: new ViewportSize { Width = 1280, Height = 800 });
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/Account/Manage/ProfilePhoto").ToString());

        // The shared manage frame: title, lead, directory wall, and the working hall. The routed
        // page owns the document's single h1; the shell text is contextual chrome.
        await Expect(page.GetByText("Manage your account", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByText("Change your account settings", new() { Exact = true })).ToBeVisibleAsync();

        var wall = page.Locator("nav.account-panel-wall");
        await Expect(wall).ToHaveCountAsync(1);

        var hall = page.Locator("section.account-working-hall");
        await Expect(hall).ToHaveCountAsync(1);

        // The page content: the Profile photo heading and the photo editor inside the hall.
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Profile photo", Exact = true })).ToBeVisibleAsync();
        await Expect(hall.Locator(".profile-photo-editor")).ToHaveCountAsync(1);
        await Expect(hall.GetByLabel("Choose a photo")).ToBeVisibleAsync();

        // The Profile photo panel in the directory wall is the active (punched) one.
        var profilePhotoPanel = wall.GetByRole(AriaRole.Link, new() { Name = "Profile photo" });
        await Expect(profilePhotoPanel).ToHaveCountAsync(1);
        await Expect(profilePhotoPanel).ToHaveClassAsync(new Regex("\\bactive\\b"));

        // The other panels are not active.
        await Expect(wall.GetByRole(AriaRole.Link, new() { Name = "Profile", Exact = true }))
            .Not.ToHaveClassAsync(new Regex("\\bactive\\b"));
        await Expect(wall.GetByRole(AriaRole.Link, new() { Name = "Email", Exact = true }))
            .Not.ToHaveClassAsync(new Regex("\\bactive\\b"));

        // Move the island to WebAssembly before driving the file input: InteractiveAuto attaches on
        // WASM once the runtime has booted, and the file-upload round trip over the InteractiveServer
        // circuit stalls under the suite's 4-way parallel Chromium load (same approach and rationale
        // as ClubCrestBrowserTests). The interactivity proof is unchanged: a static island has no
        // WASM component to receive the change event, so the cropper frame never appears.
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page);
        await Expect(hall.Locator(".profile-photo-editor")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // The editor island is truly interactive, not static SSR: uploading a JPEG through the
        // real file input fires the choose handler (OnFileSelectedAsync), which renders the
        // cropper frame. A page that lost its @rendermode annotation would serve the same static
        // markup — the input and labels would be visible — but the change event would never reach
        // the component, so the cropper frame would never appear; the upload retry through
        // BrowserRetryPolicy therefore fails without the render mode. This is the end-to-end
        // interactivity proof for the InteractiveAuto island (review finding, issue #156).
        var input = hall.GetByLabel("Choose a photo");
        await Expect(input).ToBeVisibleAsync();
        var photoPath = await WriteTempProfilePhotoAsync();
        try
        {
            var cropper = hall.Locator("div.profile-photo-cropper-frame");
            for (var attempt = 0; attempt < BrowserRetryPolicy.MaxAttempts; attempt++)
            {
                if (await cropper.IsVisibleAsync())
                {
                    break;
                }

                try
                {
                    // Uploading the same file twice is idempotent; each attempt re-triggers the
                    // change→crop round-trip, which can exceed a fixed Expect window under the
                    // suite's parallel Chromium load.
                    await input.SetInputFilesAsync(photoPath);
                }
                catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
                {
                    // The input was re-rendered mid-upload, or the actionability timeout surfaced
                    // as System.TimeoutException; the next attempt re-issues the upload.
                }

                if (await cropper.IsVisibleAsync())
                {
                    break;
                }

                await page.WaitForTimeoutAsync(BrowserRetryPolicy.Delay);
            }

            await Expect(cropper).ToBeVisibleAsync(new() { Timeout = 30_000 });

            // Complete the replacement flow as well as proving the island can render its cropper:
            // the save endpoint must refresh the profile-photo claim and return to the manage page.
            var savePhoto = hall.GetByRole(AriaRole.Button, new() { Name = "Save photo", Exact = true });
            await Expect(savePhoto).ToBeEnabledAsync(new() { Timeout = 30_000 });
            await InteractionHelpers.ClickUntilAsync(
                page,
                savePhoto,
                () => Task.FromResult(new Uri(page.Url).AbsolutePath.Equals(
                    "/Account/Manage",
                    StringComparison.OrdinalIgnoreCase)));
            await page.WaitForURLAsync(
                url => new Uri(url).AbsolutePath.Equals("/Account/Manage", StringComparison.OrdinalIgnoreCase),
                new() { WaitUntil = WaitUntilState.Commit });
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Profile", Exact = true })).ToBeVisibleAsync();
        }
        finally
        {
            File.Delete(photoPath);
        }
    }

    /// <summary>
    /// NB9: the touch targets in the nav meet the minimum 2.75rem (44px) height at desktop so the
    /// rail items remain comfortably tappable.
    /// </summary>
    [Fact]
    public async Task Navbar_NavItems_MeetTouchTargetMinimum()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(
            seed.AdminEmail,
            DashboardSeed.Password,
            viewport: new ViewportSize { Width = 1280, Height = 800 });
        var page = context.Pages[0];

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();
        var nav = page.Locator("nav[aria-label='Primary']");

        foreach (var label in new[] { "Dashboard", "Campaigns", "Players", "Teams" })
        {
            var box = await nav.GetByRole(AriaRole.Link, new() { Name = label, Exact = true }).BoundingBoxAsync();
            box.ShouldNotBeNull();
            ((double)box!.Height).ShouldBeGreaterThanOrEqualTo(43.5, $"the {label} link must keep its 2.75rem touch target");
        }
    }

    /// <summary>
    /// NB10: at a mobile (&lt;md) viewport the anonymous nav keeps the single Login tab inline in
    /// the strip (the server-computed <c>account-routes-single</c> marker branch) and hides the
    /// hamburger — with only one account route there is nothing for the sheet to reveal, so Login
    /// must never be pushed behind a toggle. The test asserts the scripting-capable branch is
    /// active (browser reports scripting as available), the Login link is visible, the
    /// account-routes list is not collapsed, and the toggler computes to <c>display: none</c>.
    /// </summary>
    [Fact]
    public async Task Navbar_Mobile_Anonymous_SingleLoginStaysInline_AndHamburgerHidden()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await fixture.NewAnonymousContextAsync(
            viewport: new ViewportSize { Width = 480, Height = 800 });
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/Account/Login").ToString());

        var nav = page.Locator("nav[aria-label='Primary']");

        // The sheet contract is active in this JS-enabled context: the brand lockup is visible
        // at <md only when the component-local scripting-disabled fallback is inactive.
        await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Nova dashboard" })).ToBeVisibleAsync();
        var login = nav.GetByRole(AriaRole.Link, new() { Name = "Login", Exact = true });
        await Expect(login).ToBeVisibleAsync();
        await Expect(login).ToHaveAttributeAsync("href", "Account/Login");

        // The anonymous state is computed server-side and emitted as the account-routes-single
        // marker class on the nav, so the contract does not depend on :has() support.
        await Expect(nav).ToHaveClassAsync(new Regex("\\baccount-routes-single\\b"));

        // The single-item exception keeps the account-routes list rendered inline (display: flex),
        // not collapsed into the sheet (display: none).
        var accountRoutes = nav.Locator(".nova-account-routes");
        await Expect(accountRoutes).ToHaveCountAsync(1);
        var display = await accountRoutes.EvaluateAsync<string>("(el) => getComputedStyle(el).display");
        display.ShouldBe("flex", "the single Login tab must stay inline in the strip");

        // The hamburger is pointless with a single account route and must be display:none. It is
        // removed from the accessibility tree (display:none), so assert presence at the DOM level
        // and then the computed style.
        var toggler = nav.Locator(".navbar-toggler");
        await Expect(toggler).ToHaveCountAsync(1);
        var togglerDisplay = await toggler.EvaluateAsync<string>("(el) => getComputedStyle(el).display");
        togglerDisplay.ShouldBe("none", "the hamburger must be hidden when only the Login tab exists");
    }

    /// <summary>
    /// NB11: with scripting disabled (<c>javaScriptEnabled: false</c>) at a mobile (&lt;md)
    /// viewport the nav falls back to the no-scripting contract — the sheet contract cannot
    /// open without scripting, so the account items (Manage, Logout) render inline in the strip
    /// and stay reachable, and the hamburger that could never open the menu is hidden. The
    /// fallback is applied by the component stylesheet's <c>scripting: none</c> media query.
    /// Also asserts the strip becomes gently scrollable (<c>overflow-x: auto</c>) so many inline
    /// items never overflow unreadably.
    /// </summary>
    [Fact]
    public async Task Navbar_Mobile_NoJavaScript_AccountItemsStayInlineAndReachable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewAnonymousContextAsync(
            viewport: new ViewportSize { Width = 480, Height = 800 },
            javaScriptEnabled: false);
        var page = context.Pages[0];

        // Signs in through the real login form. The SSR EditForm posts natively (no enhanced
        // navigation, no page JS required), so the no-JS session reaches the authenticated nav.
        await page.GotoAsync(new Uri(fixture.BaseUri, "/Account/Login").ToString());
        await page.GetByLabel("Email").FillAsync(seed.AdminEmail);
        await page.GetByLabel("Password").FillAsync(DashboardSeed.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase));

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();

        var nav = page.Locator("nav[aria-label='Primary']");

        // The account items are present and inline (not hidden behind a sheet).
        var manage = nav.GetByRole(AriaRole.Link, new() { Name = "Manage", Exact = false });
        var logout = nav.GetByRole(AriaRole.Button, new() { Name = "Logout", Exact = true });
        await Expect(manage).ToBeVisibleAsync();
        await Expect(logout).ToBeVisibleAsync();

        var accountRoutes = nav.Locator(".nova-account-routes");
        var accountRoutesDisplay = await accountRoutes.EvaluateAsync<string>("(el) => getComputedStyle(el).display");
        accountRoutesDisplay.ShouldBe("flex", "the account routes must stay inline without JS");

        // The hamburger is hidden without JS (it can never open the sheet). It is display:none,
        // so assert DOM presence and the computed style.
        var toggler = nav.Locator(".navbar-toggler");
        await Expect(toggler).ToHaveCountAsync(1);
        var togglerDisplay = await toggler.EvaluateAsync<string>("(el) => getComputedStyle(el).display");
        togglerDisplay.ShouldBe("none", "the hamburger must be hidden without JS");

        // The strip is the no-JS scroll container: overflow-x: auto keeps every inline item
        // reachable when they exceed the viewport width. flex-wrap: nowrap is asserted too —
        // with wrap, items would wrap onto a second line before overflow ever engaged and the
        // fixed strip would grow taller instead of scrolling (overflow-x would be a no-op).
        var collapse = nav.Locator(".navbar-collapse");
        var overflowX = await collapse.EvaluateAsync<string>("(el) => getComputedStyle(el).overflowX");
        overflowX.ShouldBe("auto", "the no-JS strip must be horizontally scrollable");
        var flexWrap = await collapse.EvaluateAsync<string>("(el) => getComputedStyle(el).flexWrap");
        flexWrap.ShouldBe("nowrap", "the no-JS strip must not wrap, or horizontal scroll never engages");

        // The no-JS fallback keeps the previous stacked icon-above-label tab layout for the
        // primary items (Dashboard and the club crest both covered) — the strip-fallback
        // reference layout for the #156 fix. The club-creset avatar (2rem) stays above and
        // never overlaps its label in the stacked tab.
        var clubName = await GetClubNameAsync(seed.ClubId, cancellationToken);
        await AssertStackedTabLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Dashboard", Exact = true }));
        await AssertStackedTabLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = clubName, Exact = true }));
        await AssertStackedTabLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true }));
    }

    /// <summary>
    /// NB12: the #156 regression guard — at a mobile (&lt;md) viewport, after opening the menu,
    /// every primary and account route is visible with its FULL label (no "Dash…"-style
    /// truncation), and the club crest avatar never overlaps its label: the avatar box and the
    /// label box must not intersect (the 1.25rem-slot overflow defect), and the avatar keeps its
    /// 2rem circle. Also asserts the whole club (crest + name) and account rows are reachable
    /// from /Account/Manage.
    /// </summary>
    [Fact]
    public async Task Navbar_Mobile_Menu_AllItemsVisibleWithFullLabels_AvatarNeverOverlapsLabel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(
            seed.AdminEmail,
            DashboardSeed.Password,
            viewport: new ViewportSize { Width = 480, Height = 800 },
            javaScriptEnabled: true);

        var page = context.Pages[0];
        // Start on the Account/Manage area so the club crest + Manage avatar are both rendered
        // and the account row is present in the menu (the user-reported case).
        await page.GotoAsync(new Uri(fixture.BaseUri, "/Account/Manage").ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Profile", Exact = true })).ToBeVisibleAsync();

        var nav = page.Locator("nav[aria-label='Primary']");

        // Open the menu at <768px.
        var toggler = nav.GetByRole(AriaRole.Button, new() { Name = "Toggle navigation" });
        await Expect(toggler).ToBeVisibleAsync();
        await toggler.ClickAsync();
        var collapse = nav.Locator(".navbar-collapse");
        await Expect(collapse).ToHaveClassAsync(new Regex(@"\bshow\b"));

        var clubName = await GetClubNameAsync(seed.ClubId, cancellationToken);

        // Every primary route is visible with an accessible (non-empty) name — full label, no
        // truncation-based accessible-name loss.
        foreach (var label in new[] { "Dashboard", clubName, "Campaigns", "Players", "Teams" })
        {
            var link = nav.GetByRole(AriaRole.Link, new() { Name = label, Exact = true });
            await Expect(link).ToBeVisibleAsync();
        }

        // Account routes are visible too.
        var manage = nav.GetByRole(AriaRole.Link, new() { Name = "Manage", Exact = false });
        await Expect(manage).ToBeVisibleAsync();
        await Expect(nav.GetByRole(AriaRole.Button, new() { Name = "Logout", Exact = true })).ToBeVisibleAsync();

        // The club crest avatar and its label must not overlap: the avatar keeps its 2rem box,
        // and the label box must start strictly to the right of the avatar's right edge (row
        // layout) with no horizontal intersection. This is the exact defect-1 regression guard
        // (previously the 2rem avatar overflowed its 1.25rem slot onto the label).
        var clubLink = nav.GetByRole(AriaRole.Link, new() { Name = clubName, Exact = true });
        var avatar = clubLink.Locator("img.nav-avatar");
        await Expect(avatar).ToHaveCountAsync(1);
        var avatarBox = await avatar.BoundingBoxAsync();
        var clubLabelBox = await clubLink.Locator("span.nav-label").BoundingBoxAsync();
        avatarBox.ShouldNotBeNull();
        clubLabelBox.ShouldNotBeNull();
        ((double)avatarBox!.Width).ShouldBeInRange(31.5, 32.5, "the club crest must keep its 2rem (32px) width");
        ((double)avatarBox!.Height).ShouldBeInRange(31.5, 32.5, "the club crest must keep its 2rem (32px) height");
        ((double)avatarBox!.X + (double)avatarBox.Width).ShouldBeLessThanOrEqualTo((double)clubLabelBox!.X + 1.0, "the club crest avatar must not overlap the club label");
        // Side-by-side row: the label sits on the avatar's vertical center line (never above or
        // below the row it belongs to).
        var avatarCenterY = (double)avatarBox!.Y + (avatarBox.Height / 2);
        var labelCenterY = (double)clubLabelBox!.Y + (clubLabelBox.Height / 2);
        Math.Abs(avatarCenterY - labelCenterY).ShouldBeLessThanOrEqualTo(2.0, "the avatar and club label must stay vertically centered in the row");

        // The Manage avatar (profile photo) is also 2rem and must not overlap the Manage label.
        var manageAvatar = manage.Locator("img.nav-avatar");
        await Expect(manageAvatar).ToHaveCountAsync(1);
        var manageAvatarBox = await manageAvatar.BoundingBoxAsync();
        var manageLabelBox = await manage.Locator("span.nav-label").BoundingBoxAsync();
        manageAvatarBox.ShouldNotBeNull();
        manageLabelBox.ShouldNotBeNull();
        ((double)manageAvatarBox!.Width).ShouldBeInRange(31.5, 32.5, "the Manage avatar must keep its 2rem (32px) width");
        ((double)manageAvatarBox!.X + (double)manageAvatarBox.Width).ShouldBeLessThanOrEqualTo((double)manageLabelBox!.X + 1.0, "the Manage avatar must not overlap the Manage label");

        // Each visible menu row meets the ≥2.75rem touch target and has a full-size label that
        // is not ellipsized (legibility, defect-2 regression guard).
        await AssertMenuSheetRowAsync(clubLink);
        await AssertMenuSheetRowAsync(manage);
        await AssertMenuSheetRowAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Dashboard", Exact = true }));
        await AssertMenuSheetRowAsync(nav.GetByRole(AriaRole.Button, new() { Name = "Logout", Exact = true }));

        // The opened sheet's leading lane is uniform across ALL rows — glyph rows (Dashboard,
        // Campaigns, Players, Teams, Logout) and avatar rows (club crest, Manage) measure the
        // same 2rem box and start at the same x-offset, so every label shares one x-offset. This
        // is the mobile half of the #156 alignment regression guard: previously the glyph slot
        // was 1.25rem in the sheet while the avatar slots stayed 2rem, so each label started at
        // a different x-offset depending on its row's leading content.
        await AssertUniformIconLaneAsync(nav, clubName);
    }

    /// <summary>
    /// NB14: the issue #159 report B regression guard — with JavaScript ENABLED at a mobile
    /// (&lt;md) viewport, the collapse default is the sheet contract (hamburger visible, collapse
    /// hidden at rest, no horizontal scroll), and it HOLDS across a viewport resize: resizing to
    /// desktop (md+) and back to mobile must leave the toggler visible and the collapse hidden at
    /// rest with no inline scroll strip.
    /// </summary>
    [Fact]
    public async Task Navbar_Mobile_JavaScriptEnabled_HoldsSheetContractAcrossResize()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(
            seed.AdminEmail,
            DashboardSeed.Password,
            viewport: new ViewportSize { Width = 1280, Height = 800 },
            javaScriptEnabled: true);
        var page = context.Pages[0];

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();

        var nav = page.Locator("nav[aria-label='Primary']");
        // DOM-level locator: at md+ the toggler is display:none and therefore absent from the
        // accessibility tree, so role-based lookup finds nothing there — the computed display
        // (via the DOM element) is the contract, exactly as NB10/NB11 assert it.
        var toggler = nav.Locator(".navbar-toggler");
        var collapse = nav.Locator(".navbar-collapse");
        await Expect(toggler).ToHaveCountAsync(1);

        // 1. Start at desktop (md+): the rail contract — toggler hidden, collapse flexed inline.
        var togglerDisplay = await toggler.EvaluateAsync<string>("(el) => getComputedStyle(el).display");
        togglerDisplay.ShouldBe("none", "the toggler must be hidden at md+ (rail contract)");

        // 2. Shrink to a real mobile viewport (480x800) with JS ON: the sheet contract is the
        //    unconditional default — toggler visible, collapse display:none at rest, no scroll.
        await page.SetViewportSizeAsync(480, 800);
        await AssertSheetContractAtRestAsync(nav, toggler, collapse);

        // 3. Grow back to desktop and re-assert the rail contract, then back to mobile again:
        //    the sheet contract must hold every time (no sporadic reversion to the scroll strip).
        await page.SetViewportSizeAsync(1280, 800);
        var railCollapseDisplay = await collapse.EvaluateAsync<string>("(el) => getComputedStyle(el).display");
        railCollapseDisplay.ShouldBe("flex", "the rail must flex inline at md+");
        var railTogglerDisplay = await toggler.EvaluateAsync<string>("(el) => getComputedStyle(el).display");
        railTogglerDisplay.ShouldBe("none", "the toggler must be hidden again at md+");

        await page.SetViewportSizeAsync(480, 800);
        await AssertSheetContractAtRestAsync(nav, toggler, collapse);

        // 4. The menu still opens and lists routes after the resizes (the collapse is functional,
        //    not merely styled into hiding).
        await toggler.ClickAsync();
        await Expect(collapse).ToHaveClassAsync(new Regex(@"\bshow\b"));
        await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Dashboard", Exact = true })).ToBeVisibleAsync();
    }

    /// <summary>
    /// Asserts the JS-enabled mobile sheet contract at rest: toggler visible with a ≥2.75rem
    /// touch target, collapse hidden (display: none), no inline horizontal scroll strip
    /// (the computed overflow-x must not be auto/scroll). Used by NB14 after viewport
    /// resizes. The overflow-x assertion reads the computed style instead of
    /// scrollWidth/clientWidth: at rest the collapse is display: none, so both measure 0 and
    /// a scrollWidth &gt; clientWidth check would be inert. getComputedStyle still returns
    /// the specified overflow-x for display:none elements, so a re-added
    /// overflow-x: auto strip (e.g. from the scripting-disabled fallback) would be caught.
    /// </summary>
    private static async Task AssertSheetContractAtRestAsync(ILocator nav, ILocator toggler, ILocator collapse)
    {
        await Expect(toggler).ToBeVisibleAsync();
        await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Nova dashboard" })).ToBeVisibleAsync();

        var togglerBox = await toggler.BoundingBoxAsync();
        togglerBox.ShouldNotBeNull();
        ((double)togglerBox!.Height).ShouldBeGreaterThanOrEqualTo(43.5, "the toggler must keep a 2.75rem touch target");
        ((double)togglerBox!.Width).ShouldBeGreaterThanOrEqualTo(43.5, "the toggler must keep a 2.75rem touch target");

        var collapseDisplay = await collapse.EvaluateAsync<string>("(el) => getComputedStyle(el).display");
        collapseDisplay.ShouldBe("none", "the collapse must be hidden at rest at <md with JS enabled");

        var overflowX = await collapse.EvaluateAsync<string>("(el) => getComputedStyle(el).overflowX");
        overflowX.ShouldNotBe("auto", "the JS-enabled collapse must never become an inline horizontal scroll strip");
        overflowX.ShouldNotBe("scroll", "the JS-enabled collapse must never become an inline horizontal scroll strip");

        // The routes are not in the accessibility tree while the sheet is closed — proving the
        // strip is not implicitly showing inline tabs.
        foreach (var label in new[] { "Dashboard", "Campaigns", "Players", "Teams" })
        {
            await Expect(nav.GetByRole(AriaRole.Link, new() { Name = label, Exact = true })).ToHaveCountAsync(0);
        }
    }

    /// <summary>
    /// Asserts the link keeps the inline icon+label row (flex row, icon box beside — not above —
    /// the label box, both vertically centered). The leading box may be the glyph slot
    /// (bootstrap-icons) or the 2rem avatar slot (club crest / profile photo); each keeps its
    /// committed baseline size and neither can overlap the label because the slot owns its width.
    /// </summary>
    private static async Task AssertInlineRowLayoutAsync(ILocator link, double expectedIconSlotSize = 20.0)
    {
        var flexDirection = await link.EvaluateAsync<string>("(el) => getComputedStyle(el).flexDirection");
        flexDirection.ShouldBe("row");
        var leadingBox = await GetLeadingSlotBoxAsync(link, expectedIconSlotSize);
        var labelBox = await link.Locator("span.nav-label").BoundingBoxAsync();
        labelBox.ShouldNotBeNull();
        leadingBox!.X.ShouldBeLessThan(labelBox!.X, "the icon must sit beside the label in the inline row");
        var iconCenterY = (double)leadingBox.Y + (leadingBox.Height / 2);
        var labelCenterY = (double)labelBox.Y + (labelBox.Height / 2);
        Math.Abs(iconCenterY - labelCenterY).ShouldBeLessThanOrEqualTo(2.0, "the icon and label must stay vertically centered");
    }

    /// <summary>
    /// Resolves the link's leading box: a glyph slot for bootstrap-icons items, or a 2rem avatar
    /// slot for the club crest / profile-photo items, each at its committed size. The expected
    /// glyph-slot size is passed by the caller because the slot is 2rem in the md+ lane and in
    /// the opened mobile sheet (uniform leading lane everywhere the full-width menu rows render)
    /// but stays the compact 1.25rem in the no-JS inline stacked-tab strip (the avatar slot is
    /// always 2rem).
    /// </summary>
    private static async Task<LocatorBoundingBoxResult?> GetLeadingSlotBoxAsync(ILocator link, double expectedIconSlotSize = 20.0)
    {
        var avatarSlot = link.Locator("span.nav-avatar-slot");
        if (await avatarSlot.CountAsync() == 1)
        {
            var avatarBox = await avatarSlot.BoundingBoxAsync();
            avatarBox.ShouldNotBeNull();
            ((double)avatarBox!.Width).ShouldBeInRange(31.5, 32.5, "the avatar slot must keep its 2rem (32px) width");
            ((double)avatarBox!.Height).ShouldBeInRange(31.5, 32.5, "the avatar slot must keep its 2rem (32px) height");
            return avatarBox;
        }

        var iconBox = await link.Locator("span.nav-icon-slot").BoundingBoxAsync();
        iconBox.ShouldNotBeNull();
        ((double)iconBox!.Width).ShouldBeInRange(expectedIconSlotSize - 0.5, expectedIconSlotSize + 0.5, "the icon slot must keep its committed lane width");
        ((double)iconBox!.Height).ShouldBeInRange(expectedIconSlotSize - 0.5, expectedIconSlotSize + 0.5, "the icon slot must keep its committed lane height");
        return iconBox;
    }

    /// <summary>
    /// Asserts the uniform leading lane at md+ and in the opened mobile sheet: every authorized
    /// row's leading slot (glyph slot or avatar slot) has the same width/height and the same
    /// x-offset, and every row's label therefore starts at the same x-offset. This is the #156
    /// alignment guard — the club crest avatar (2rem circle) shares one lane box with the glyph
    /// icons so no row reads larger or misaligned. In the sheet (NB7/NB12) the glyph slot is
    /// 2rem too (same uniform lane), so the same guard applies; only the no-JS inline strip
    /// keeps the compact 1.25rem glyph slot (asserted separately in NB11).
    /// Rows: Dashboard, the club crest, Campaigns, Players, Teams, Manage (avatar), Logout
    /// (glyph).
    /// </summary>
    private static async Task AssertUniformIconLaneAsync(ILocator nav, string clubName)
    {
        var rows = new (ILocator Link, string Name)[]
        {
            (nav.GetByRole(AriaRole.Link, new() { Name = "Dashboard", Exact = true }), "Dashboard"),
            (nav.GetByRole(AriaRole.Link, new() { Name = clubName, Exact = true }), "club crest"),
            (nav.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true }), "Campaigns"),
            (nav.GetByRole(AriaRole.Link, new() { Name = "Players", Exact = true }), "Players"),
            (nav.GetByRole(AriaRole.Link, new() { Name = "Teams", Exact = true }), "Teams"),
            (nav.GetByRole(AriaRole.Link, new() { Name = "Manage", Exact = false }), "Manage"),
            (nav.GetByRole(AriaRole.Button, new() { Name = "Logout", Exact = true }), "Logout"),
        };

        var leadingBoxes = new List<(double X, double Width, double Height, string Name)>();
        foreach (var row in rows)
        {
            var box = await GetLeadingSlotBoxAsync(row.Link, 32.0);
            box.ShouldNotBeNull();
            leadingBoxes.Add((box!.X, box.Width, box.Height, row.Name));
        }

        var first = leadingBoxes[0];
        foreach (var box in leadingBoxes.Skip(1))
        {
            Math.Abs(box.Width - first.Width).ShouldBeLessThanOrEqualTo(1.0, $"the leading slot of row '{box.Name}' must match the uniform lane width");
            Math.Abs(box.Height - first.Height).ShouldBeLessThanOrEqualTo(1.0, $"the leading slot of row '{box.Name}' must match the uniform lane height");
            Math.Abs(box.X - first.X).ShouldBeLessThanOrEqualTo(1.0, $"the leading slot of row '{box.Name}' must share the lane x-offset");
        }
    }

    /// <summary>
    /// Asserts the element's settled background color equals the expected value, comparing
    /// normalized RGB so that both serialization formats Chromium produces for the same color
    /// — legacy <c>rgb(r, g, b)</c> and modern <c>color(srgb r g b)</c> (returned for
    /// <c>color-mix()</c> results) — compare equal.
    /// </summary>
    private static async Task AssertColorEqualsAsync(ILocator element, string expectedColor, string customMessage)
    {
        await element.EvaluateAsync<object?>(
            """
            (el) => Promise.all(
                el.getAnimations()
                    .filter(animation => animation.transitionProperty === "background-color")
                    .map(animation => animation.finished.catch(() => undefined)))
            """);
        var actualColor = await element.EvaluateAsync<string>("(el) => getComputedStyle(el).backgroundColor");
        NormalizeRgbColor(actualColor).ShouldBe(NormalizeRgbColor(expectedColor), customMessage);
    }

    /// <summary>
    /// Asserts the element's computed background color differs from the specified color
    /// (which may be a parsed <c>rgb(...)</c> string), comparing normalized RGB.
    /// </summary>
    private static async Task AssertColorNotEqualsAsync(ILocator element, string otherColor, string customMessage)
    {
        var actualColor = await element.EvaluateAsync<string>("(el) => getComputedStyle(el).backgroundColor");
        NormalizeRgbColor(actualColor).ShouldNotBe(NormalizeRgbColor(otherColor), customMessage);
    }

    /// <summary>
    /// Normalizes a CSS color string to a canonical <c>rgb(r, g, b)</c> shape. Chromium
    /// serializes <c>color-mix()</c> results as <c>color(srgb r g b)</c> (floats) while legacy
    /// declarations serialize as <c>rgb(r, g, b)</c> (integers); both represent the same color
    /// and must compare as equal. Colors with an alpha channel of 0 normalize to a fixed
    /// transparent marker so they can never falsely equal a fully opaque color.
    /// </summary>
    private static string NormalizeRgbColor(string color)
    {
        color = color.Trim();

        // Transparent / fully transparent colors: any alpha of 0 is the same visual color.
        var alphaMatch = Regex.Match(color, @"(?:^|,)\s*(?:0(?:\.0+)?|\.0+)\s*\)$");
        if (alphaMatch.Success || color.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            return "rgba(0, 0, 0, 0)";
        }

        // color(srgb r g b) — modern serialization of color-mix() results.
        var srgbMatch = Regex.Match(color, @"^color\(\s*srgb\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s*\)$", RegexOptions.IgnoreCase);
        if (srgbMatch.Success)
        {
            var r = Math.Round(double.Parse(srgbMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 255.0);
            var g = Math.Round(double.Parse(srgbMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) * 255.0);
            var b = Math.Round(double.Parse(srgbMatch.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) * 255.0);
            return $"rgb({r:0}, {g:0}, {b:0})";
        }

        // Legacy rgb(r, g, b) — leave as-is but canonicalize whitespace.
        return Regex.Replace(color, @"\s*,\s*", ", ");
    }

    /// <summary>
    /// Asserts a mobile menu row: full-width (spans the sheet), left-aligned flex row, ≥2.75rem
    /// tall (touch target), with a legible full-size (0.875rem) label that is never clipped.
    /// Used by the expanded mobile sheet (NB7/NB12); the no-JS stacked-tab fallback is asserted
    /// separately in NB11 via <see cref="AssertStackedTabLayoutAsync"/>.
    /// </summary>
    private static async Task AssertMenuSheetRowAsync(ILocator row)
    {
        var flexDirection = await row.EvaluateAsync<string>("(el) => getComputedStyle(el).flexDirection");
        flexDirection.ShouldBe("row", "the sheet row must be an inline row, not a stacked tab");
        var box = await row.BoundingBoxAsync();
        box.ShouldNotBeNull();
        ((double)box!.Height).ShouldBeGreaterThanOrEqualTo(43.5, "the sheet row must keep the 2.75rem touch target");
        ((double)box.Width).ShouldBeGreaterThanOrEqualTo(150.0, "the sheet row must be full-width, not a shrink-to-fit tab");

        var labelBox = await row.Locator("span.nav-label").BoundingBoxAsync();
        labelBox.ShouldNotBeNull();
        var labelFontSize = await row.Locator("span.nav-label").EvaluateAsync<string>("(el) => getComputedStyle(el).fontSize");
        labelFontSize.ShouldBe("14px", "the sheet label must render at the 0.875rem menu size");
        var textOverflow = await row.Locator("span.nav-label").EvaluateAsync<string>("(el) => getComputedStyle(el).textOverflow");
        textOverflow.ShouldNotBe("ellipsis", "the sheet label must never be ellipsized");
    }

    /// <summary>
    /// Asserts the link keeps the stacked icon-above-label tab layout (flex column, icon box above
    /// — not beside — the label box, both horizontally centered on the tab's center axis). This is
    /// the no-JS strip-fallback layout at &lt;md; the JS menu renders full-width
    /// rows instead (see <see cref="AssertMenuSheetRowAsync"/>). The leading box may be the
    /// compact 1.25rem glyph slot or the 2rem avatar slot (club crest / profile photo); either
    /// way the avatar slot keeps its 2rem box and the label stays fully readable below it — never
    /// overlapped.
    /// </summary>
    private static async Task AssertStackedTabLayoutAsync(ILocator link)
    {
        var flexDirection = await link.EvaluateAsync<string>("(el) => getComputedStyle(el).flexDirection");
        flexDirection.ShouldBe("column");
        var leadingBox = await GetLeadingSlotBoxAsync(link);
        var labelBox = await link.Locator("span.nav-label").BoundingBoxAsync();
        labelBox.ShouldNotBeNull();
        leadingBox!.Y.ShouldBeLessThan(labelBox!.Y, "the icon must sit above the label in the stacked tab");
        var iconCenterX = (double)leadingBox.X + (leadingBox.Width / 2);
        var labelCenterX = (double)labelBox.X + (labelBox.Width / 2);
        Math.Abs(iconCenterX - labelCenterX).ShouldBeLessThanOrEqualTo(2.0, "the icon and label must stay horizontally centered");
    }

    /// <summary>
    /// Asserts the active nav link renders the kelp teal edge rail at desktop: the bar is the
    /// <c>::after</c> pseudo-element at the left edge of the nav (the rail's edge), 0.25rem wide.
    /// </summary>
    private static async Task AssertKelpTealEdgeRailAsync(ILocator link)
    {
        await Expect(link).ToHaveClassAsync(new Regex("\\bactive\\b"));
        var indicatorColor = await link.EvaluateAsync<string>(
            "(el) => getComputedStyle(el, '::after').backgroundColor");
        indicatorColor.ShouldBe(ExpectedKelpTealRgb);
        var indicatorWidth = await link.EvaluateAsync<string>(
            "(el) => getComputedStyle(el, '::after').width");
        indicatorWidth.ShouldBe("4px"); // 0.25rem
    }

    /// <summary>
    /// Asserts the Manage link's avatar renders as a 2rem (32px) circle with a visible border —
    /// comfortably larger than the 1.5rem (24px) desktop nav icons. Bootstrap's reboot applies
    /// <c>box-sizing: border-box</c> globally, so the 2rem width is the border-box size: the
    /// bounding box is exactly 32px including the border. Blank-slate users without a profile
    /// photo render no avatar.
    /// </summary>
    private static async Task AssertNavAvatarAsync(ILocator manage)
    {
        var avatar = manage.Locator("img.nav-avatar");
        await Expect(avatar).ToHaveCountAsync(1);
        var avatarBox = await avatar.BoundingBoxAsync();
        avatarBox.ShouldNotBeNull();
        // 2rem = 32px border-box; the range allows only for sub-pixel rounding.
        ((double)avatarBox!.Width).ShouldBeInRange(31.5, 32.5, "the avatar must render at its 2rem (32px) target width");
        ((double)avatarBox!.Height).ShouldBeInRange(31.5, 32.5, "the avatar must render at its 2rem (32px) target height");
        var borderRadius = await avatar.EvaluateAsync<string>("(el) => getComputedStyle(el).borderRadius");
        borderRadius.ShouldContain("50%");
    }

    /// <summary>
    /// Asserts the active link swaps to the <c>-fill</c> glyph overlay: the fill span is the
    /// visible one (opacity 1) and the outline span is hidden (opacity 0), while both stay
    /// rendered in the DOM (overlay approach; no re-render or layout shift).
    /// </summary>
    private static async Task AssertActiveFillGlyphAsync(ILocator link, string fillClass, string outlineClass)
    {
        var fill = link.Locator($"span.{fillClass}");
        var outline = link.Locator($"span.{outlineClass}");

        await Expect(fill).ToHaveCountAsync(1);
        await Expect(outline).ToHaveCountAsync(1);

        // The fill span must be visible (opacity 1) and the outline span hidden (opacity 0).
        var fillOpacity = await fill.EvaluateAsync<string>(
            "(el) => getComputedStyle(el).opacity");
        fillOpacity.ShouldBe("1");
        var outlineOpacity = await outline.EvaluateAsync<string>(
            "(el) => getComputedStyle(el).opacity");
        outlineOpacity.ShouldBe("0");
    }

    /// <summary>
    /// Asserts an inactive nav link keeps the outline glyph visible and hides the fill overlay
    /// (both spans remain in the DOM; the CSS overlay just flips opacity).
    /// </summary>
    private static async Task AssertInactiveOutlineGlyphAsync(ILocator link, string fillClass, string outlineClass)
    {
        var fill = link.Locator($"span.{fillClass}");
        var outline = link.Locator($"span.{outlineClass}");

        await Expect(fill).ToHaveCountAsync(1);
        await Expect(outline).ToHaveCountAsync(1);

        var outlineOpacity = await outline.EvaluateAsync<string>(
            "(el) => getComputedStyle(el).opacity");
        outlineOpacity.ShouldBe("1");
        var fillOpacity = await fill.EvaluateAsync<string>(
            "(el) => getComputedStyle(el).opacity");
        fillOpacity.ShouldBe("0");
    }

    /// <summary>
    /// Asserts an icon + label nav link exists with the expected href and a bootstrap-icons glyph.
    /// </summary>
    private static async Task AssertIconLinkAsync(ILocator nav, string label, string href, string glyphClass)
    {
        var link = nav.GetByRole(AriaRole.Link, new() { Name = label, Exact = true });
        await Expect(link).ToBeVisibleAsync();
        await Expect(link).ToHaveAttributeAsync("href", href);
        await AssertBootstrapIconGlyphAsync(link, glyphClass);
    }

    /// <summary>
    /// Asserts the element carries the bootstrap-icons glyph class, that the glyph resolves to the
    /// bootstrap-icons font family (proving the Sass import compiled the <c>.bi</c> rules into the
    /// theme), and that the <c>@font-face</c> actually loaded (proving the font Copy step and
    /// static-web-asset registration are wired): <see cref="FontFaceSet.Check"/> returns
    /// <see langword="false"/> when the font file failed to load, unlike the declared
    /// <c>font-family</c>, which stays applied regardless of a missing font resource.
    /// </summary>
    private static async Task AssertBootstrapIconGlyphAsync(ILocator element, string glyphClass)
    {
        var icon = element.Locator($"span.{glyphClass}");
        await Expect(icon).ToHaveCountAsync(1);
        var fontFamily = await icon.EvaluateAsync<string>(
            "(el) => getComputedStyle(el, '::before').fontFamily");
        fontFamily.ShouldContain("bootstrap-icons");
        var fontLoaded = await icon.EvaluateAsync<bool>(
            @"async () => {
                await document.fonts.ready;
                return document.fonts.check('16px ""bootstrap-icons""');
            }");
        fontLoaded.ShouldBeTrue();
    }

    /// <summary>
    /// Writes a small valid JPEG to a temporary file for the profile photo file input.
    /// </summary>
    /// <returns>The temporary file path.</returns>
    private static async Task<string> WriteTempProfilePhotoAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nova-profile-photo-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(path, SeedingHelpers.CreateJpegBytes());
        return path;
    }

    /// <summary>
    /// Resolves the seeded club's display name for the club nav link assertion.
    /// </summary>
    private async Task<string> GetClubNameAsync(long clubId, CancellationToken cancellationToken)
    {
        await using var context = fixture.AppHost.CreateAdminContext();
        return (await context.Clubs.SingleAsync(club => club.ClubId == clubId, cancellationToken)).Name;
    }
}
