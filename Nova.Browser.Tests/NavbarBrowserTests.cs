using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Entities;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Browser-level acceptance of the Fieldhouse Wayfinding navigation (issue #134 + follow-up
/// polish): the authenticated navigation renders as a fixed left rail at md+ (768px+) and a fixed
/// bottom strip below md that is horizontally scrollable; items show icon + label items (inline
/// rows at md+, stacked icon-above-label tabs below md) with bootstrap-icons glyphs; account
/// items (Manage, Logout) stay present in both layouts; the active item carries a kelp teal edge
/// rail at desktop
/// (left-edge bar) and a top marker on the mobile bottom strip, swapping its outline glyph for the
/// matching <c>-fill</c> variant (CSS overlay, no layout shift); the Manage link navigates to
/// /Account/Manage; Logout posts the antiforgery form and returns to the login page; and the
/// unauthenticated root renders the public landing page with no authenticated icon links.
/// </summary>
/// <param name="fixture">The Aspire-hosted browser suite fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class NavbarBrowserTests(BrowserSuiteFixture fixture)
{
    private const string ExpectedKelpTealRgb = "rgb(14, 124, 123)";

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
        // Players, Teams).
        await AssertInlineRowLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Dashboard", Exact = true }));
        await AssertInlineRowLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = clubName, Exact = true }));
        await AssertInlineRowLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true }));
        await AssertInlineRowLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Players", Exact = true }));
        await AssertInlineRowLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Teams", Exact = true }));
    }

    /// <summary>
    /// NB7: at a mobile (&lt;md) viewport the authorized nav renders as a fixed bottom strip whose
    /// links stack icon above label (the DESIGN.md tab-strip layout) and are reachable by
    /// horizontal scrolling (the account items — Manage/Logout — are part of the same scrollable
    /// row, so nothing is hidden).
    /// </summary>
    [Fact]
    public async Task Navbar_Mobile_BottomStrip_StackedTabsAndAccountItems()
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
        Math.Abs((double)navBox!.Y + (double)navBox.Height - viewportHeight).ShouldBeLessThanOrEqualTo(1.0, "the strip must be fixed to the bottom edge at <md");

        // The collapse is force-flexed and horizontally scrollable (no toggler needed).
        var collapse = nav.Locator(".navbar-collapse");
        await Expect(collapse).ToHaveCountAsync(1);
        var overflowX = await collapse.EvaluateAsync<string>("(el) => getComputedStyle(el).overflowX");
        overflowX.ShouldBe("auto");

        // Each authorized item keeps the stacked icon-above-label tab layout at <md (Dashboard,
        // the club, Campaigns, Players, Teams).
        await AssertStackedTabLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Dashboard", Exact = true }));
        await AssertStackedTabLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = clubName, Exact = true }));
        await AssertStackedTabLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true }));
        await AssertStackedTabLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Players", Exact = true }));
        await AssertStackedTabLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Teams", Exact = true }));

        // The account items stay part of the mobile strip (Manage + Logout).
        await Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Manage", Exact = false })).ToBeVisibleAsync();
        await Expect(nav.GetByRole(AriaRole.Button, new() { Name = "Logout", Exact = true })).ToBeVisibleAsync();

        // An inactive link (Campaigns at the dashboard root) keeps its outline glyph visible; the
        // fill overlay stays hidden at mobile just as at desktop.
        await AssertInactiveOutlineGlyphAsync(
            nav.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true }),
            "bi-calendar-check-fill",
            "bi-calendar-check");
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
    /// Asserts the link keeps the inline icon+label row (flex row, icon box beside — not above —
    /// the label box, both vertically centered, and the icon slot at its 1.25rem mobile baseline)
    /// — the reference layout for both the desktop rail and the mobile bottom strip.
    /// </summary>
    private static async Task AssertInlineRowLayoutAsync(ILocator link)
    {
        var flexDirection = await link.EvaluateAsync<string>("(el) => getComputedStyle(el).flexDirection");
        flexDirection.ShouldBe("row");
        var iconBox = await link.Locator("span.nav-icon-slot").BoundingBoxAsync();
        var labelBox = await link.Locator("span.nav-label").BoundingBoxAsync();
        iconBox.ShouldNotBeNull();
        labelBox.ShouldNotBeNull();
        iconBox!.X.ShouldBeLessThan(labelBox!.X, "the icon must sit beside the label in the inline row");
        ((double)iconBox!.Width).ShouldBeInRange(19.5, 20.5, "the icon slot must keep its 1.25rem baseline width");
        ((double)iconBox!.Height).ShouldBeInRange(19.5, 20.5, "the icon slot must keep its 1.25rem baseline height");
        var iconCenterY = (double)iconBox.Y + (iconBox.Height / 2);
        var labelCenterY = (double)labelBox.Y + (labelBox.Height / 2);
        Math.Abs(iconCenterY - labelCenterY).ShouldBeLessThanOrEqualTo(2.0, "the icon and label must stay vertically centered");
    }

    /// <summary>
    /// Asserts the link keeps the stacked icon-above-label tab layout (flex column, icon box above
    /// — not beside — the label box, both horizontally centered on the tab's center axis) — the
    /// DESIGN.md route-strip tab layout used by the mobile bottom strip at &lt;md.
    /// </summary>
    private static async Task AssertStackedTabLayoutAsync(ILocator link)
    {
        var flexDirection = await link.EvaluateAsync<string>("(el) => getComputedStyle(el).flexDirection");
        flexDirection.ShouldBe("column");
        var iconBox = await link.Locator("span.nav-icon-slot").BoundingBoxAsync();
        var labelBox = await link.Locator("span.nav-label").BoundingBoxAsync();
        iconBox.ShouldNotBeNull();
        labelBox.ShouldNotBeNull();
        iconBox!.Y.ShouldBeLessThan(labelBox!.Y, "the icon must sit above the label in the stacked tab");
        ((double)iconBox!.Width).ShouldBeInRange(19.5, 20.5, "the icon slot must keep its 1.25rem baseline width");
        ((double)iconBox!.Height).ShouldBeInRange(19.5, 20.5, "the icon slot must keep its 1.25rem baseline height");
        var iconCenterX = (double)iconBox.X + (iconBox.Width / 2);
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
    /// Resolves the seeded club's display name for the club nav link assertion.
    /// </summary>
    private async Task<string> GetClubNameAsync(long clubId, CancellationToken cancellationToken)
    {
        await using var context = fixture.AppHost.CreateAdminContext();
        return (await context.Clubs.SingleAsync(club => club.ClubId == clubId, cancellationToken)).Name;
    }
}
