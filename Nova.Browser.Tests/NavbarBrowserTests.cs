using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Entities;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Browser-level acceptance of the icon-first bottom navbar (issue #134 + follow-up polish):
/// the authenticated navbar shows icon + label items (Home, Club, Campaigns, Players, Teams)
/// with bootstrap-icons glyphs; on desktop (md+) the items stack the icon above the label while
/// mobile (&lt;md) keeps the inline icon+label row inside the expanded collapsed menu; the
/// active item carries a kelp teal indicator bar flush with the navbar's top edge and swaps its
/// outline glyph for the matching <c>-fill</c> variant (CSS overlay, no layout shift); the
/// Manage link navigates to /Account/Manage; Logout posts the antiforgery form and returns to
/// the login page; and the unauthenticated navbar shows only the Login link with no icon links.
/// </summary>
/// <param name="fixture">The Aspire-hosted browser suite fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class NavbarBrowserTests(BrowserSuiteFixture fixture)
{
    private const string ExpectedKelpTealRgb = "rgb(14, 124, 123)";

    /// <summary>
    /// NB1: after signing in, the navbar shows the icon-first items — Home, the club name,
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

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();
        var clubName = await GetClubNameAsync(seed.ClubId, cancellationToken);

        var nav = page.Locator("nav.navbar");

        // Home is the first item and points at the dashboard root.
        var home = nav.GetByRole(AriaRole.Link, new() { Name = "Home", Exact = true });
        await Expect(home).ToBeVisibleAsync();
        await Expect(home).ToHaveAttributeAsync("href", "/");
        await AssertBootstrapIconGlyphAsync(home, "bi-house");

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
    /// NB2: the active nav item renders the kelp teal indicator bar flush with the navbar's top
    /// edge, both at the dashboard root (Home active) and on the campaigns page (Campaigns
    /// active); the active item swaps its outline glyph for the matching <c>-fill</c> variant
    /// (CSS overlay, so the previously-active item returns to outline with no layout jump).
    /// </summary>
    [Fact]
    public async Task Navbar_ActiveItem_ShowsKelpTealTopIndicator()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, DashboardSeed.Password);
        var page = context.Pages[0];
        var nav = page.Locator("nav.navbar");

        // At the dashboard root the Home link is the active item.
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();
        var home = nav.GetByRole(AriaRole.Link, new() { Name = "Home", Exact = true });
        await AssertKelpTealTopIndicatorFlushAsync(home, nav);
        await AssertActiveFillGlyphAsync(home, "bi-house-fill", "bi-house");

        // On the campaigns page the Campaigns link becomes the active item.
        await page.GotoAsync(new Uri(fixture.BaseUri, "/campaigns").ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Campaigns" })).ToBeVisibleAsync();
        var campaigns = nav.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true });
        await AssertKelpTealTopIndicatorFlushAsync(campaigns, nav);
        await AssertActiveFillGlyphAsync(campaigns, "bi-calendar-check-fill", "bi-calendar-check");
        await Expect(home).Not.ToHaveClassAsync(new Regex("\\bactive\\b"));
        // The previously-active link returned to its outline glyph (fill overlay off).
        await AssertInactiveOutlineGlyphAsync(home, "bi-house-fill", "bi-house");
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

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();
        var manage = page.Locator("nav.navbar").GetByRole(AriaRole.Link, new() { Name = "Manage", Exact = false });
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

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();
        var logout = page.Locator("nav.navbar").GetByRole(AriaRole.Button, new() { Name = "Logout", Exact = true });
        await logout.ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase));

        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Login", Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator("nav.navbar .bi")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// NB5: an anonymous visit to / redirects to the login page, whose navbar shows only the
    /// Login link — no icon-first items leak to unauthenticated users.
    /// </summary>
    [Fact]
    public async Task Navbar_Unauthenticated_ShowsOnlyLogin_WithNoIconLinks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await fixture.NewAnonymousContextAsync();
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/").ToString());
        await page.WaitForURLAsync(url => url.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase));

        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Login", Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator("nav.navbar .bi")).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Home", Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Players", Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Teams", Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Logout", Exact = true })).ToHaveCountAsync(0);
    }

    /// <summary>
    /// NB6: at a desktop (md+) viewport the authorized nav items stack the icon above the label
    /// (<c>flex-direction: column</c>), so the icon box sits above the label box and the link is
    /// still horizontally centered. Mobile (&lt;md) keeps the inline icon+label row inside the
    /// expanded collapsed menu, asserted by NB7 in its own viewport context (asserting both
    /// viewports in one test is flaky because the collapse toggling also changes layout).
    /// </summary>
    [Fact]
    public async Task Navbar_Desktop_StacksIconAboveLabel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(
            seed.AdminEmail,
            DashboardSeed.Password,
            viewport: new ViewportSize { Width = 1280, Height = 800 });
        var page = context.Pages[0];

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();
        var clubName = await GetClubNameAsync(seed.ClubId, cancellationToken);
        var nav = page.Locator("nav.navbar");

        // Each authorized item stacks icon above label at md+ (Home, the club, Campaigns,
        // Players, Teams).
        await AssertStackedLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Home", Exact = true }));
        await AssertStackedLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = clubName, Exact = true }));
        await AssertStackedLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true }));
        await AssertStackedLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Players", Exact = true }));
        await AssertStackedLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Teams", Exact = true }));

        // The Manage link intentionally stays inline (icon row next to its label).
        var manage = nav.GetByRole(AriaRole.Link, new() { Name = "Manage", Exact = false });
        var manageFlex = await manage.EvaluateAsync<string>("(el) => getComputedStyle(el).flexDirection");
        manageFlex.ShouldBe("row");
    }

    /// <summary>
    /// NB7: at a mobile (&lt;md) viewport the authorized nav items keep the inline icon+label row
    /// (<c>flex-direction: row</c>) inside the expanded collapsed menu — so the mobile branch of
    /// the stacked-layout media query is covered (NB6 covers md+; the old collapsed-menu manual
    /// check is superseded). The icon box sits beside (not above) the label box in a fixed-size
    /// 1.25rem slot, and an inactive link keeps its outline glyph visible with the <c>-fill</c>
    /// overlay hidden.
    /// </summary>
    [Fact]
    public async Task Navbar_Mobile_ExpandedMenu_KeepsInlineRowAndOutlineGlyph()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(
            seed.AdminEmail,
            DashboardSeed.Password,
            viewport: new ViewportSize { Width = 480, Height = 800 });
        var page = context.Pages[0];

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();
        var clubName = await GetClubNameAsync(seed.ClubId, cancellationToken);
        var nav = page.Locator("nav.navbar");

        // Expand the collapsed menu behind the navbar toggler (Bootstrap's data API, no Blazor
        // circuit needed); once the show class lands the items settle at their final positions.
        await nav.Locator("button.navbar-toggler").ClickAsync();
        await Expect(page.Locator("#main-nav-menu")).ToHaveClassAsync(new Regex("\\bshow\\b"));

        // Each authorized item keeps the inline row at <md (Home, the club, Campaigns, Players,
        // Teams).
        await AssertInlineRowLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Home", Exact = true }));
        await AssertInlineRowLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = clubName, Exact = true }));
        await AssertInlineRowLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true }));
        await AssertInlineRowLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Players", Exact = true }));
        await AssertInlineRowLayoutAsync(nav.GetByRole(AriaRole.Link, new() { Name = "Teams", Exact = true }));

        // An inactive link (Campaigns at the dashboard root) keeps its outline glyph visible; the
        // fill overlay stays hidden at mobile just as at desktop.
        await AssertInactiveOutlineGlyphAsync(
            nav.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true }),
            "bi-calendar-check-fill",
            "bi-calendar-check");
    }

    /// <summary>
    /// NB8: on the Account/Manage page the Manage link is the active item and its kelp teal
    /// indicator bar is still flush with the navbar's top edge — the right-hand inline Manage item
    /// must top-align with the stacked left items (regression from the earlier centering behavior
    /// that left the Manage bar below the navbar edge). The Manage avatar renders as a 2rem circle
    /// (larger than the 1.5rem icons) with a visible border.
    /// </summary>
    [Fact]
    public async Task Navbar_ManageActive_IndicatorFlushAndAvatarLarger()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(
            seed.AdminEmail,
            DashboardSeed.Password,
            viewport: new ViewportSize { Width = 1280, Height = 800 });
        var page = context.Pages[0];

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();
        var nav = page.Locator("nav.navbar");

        // Navigate to Account/Manage; the Manage link becomes the active item.
        await page.GotoAsync(new Uri(fixture.BaseUri, "/Account/Manage").ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Profile", Exact = true })).ToBeVisibleAsync();

        var manage = nav.GetByRole(AriaRole.Link, new() { Name = "Manage", Exact = false });
        await AssertKelpTealTopIndicatorFlushAsync(manage, nav);

        // The avatar is a 2rem (32px) circle with a visible border, clearly larger than the 1.5rem
        // (24px) nav icons.
        await AssertNavAvatarAsync(manage);
    }

    /// <summary>
    /// NB9: the avatar text-and-image Manage item and the inline Manage link stay top-aligned with
    /// the stacked left items at desktop — the Manage link's top edge must match the left items'
    /// top edge so the active indicator keeps a single flush baseline for every NavMenu item. This
    /// guards the alignment rule that keeps the indicator flush regardless of the active item.
    /// </summary>
    [Fact]
    public async Task Navbar_Desktop_ManageLinkTopAlignedWithStackedItems()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(
            seed.AdminEmail,
            DashboardSeed.Password,
            viewport: new ViewportSize { Width = 1280, Height = 800 });
        var page = context.Pages[0];

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();
        var nav = page.Locator("nav.navbar");

        // The Manage link's top must match the first stacked item (Home) top edge.
        var manage = nav.GetByRole(AriaRole.Link, new() { Name = "Manage", Exact = false });
        var home = nav.GetByRole(AriaRole.Link, new() { Name = "Home", Exact = true });
        var manageBox = await manage.BoundingBoxAsync();
        var homeBox = await home.BoundingBoxAsync();
        manageBox.ShouldNotBeNull();
        homeBox.ShouldNotBeNull();
        Math.Abs((double)manageBox!.Y - homeBox!.Y).ShouldBeLessThanOrEqualTo(1.0, "the Manage link must top-align with the stacked items at md+");
    }

    /// <summary>
    /// Asserts the link keeps the inline icon+label row (flex row, icon box beside — not above —
    /// the label box, both vertically centered, and the icon slot at its 1.25rem mobile baseline)
    /// — the reference mobile layout.
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
        ((double)iconBox!.Width).ShouldBeInRange(19.5, 20.5, "the mobile icon slot must keep its 1.25rem baseline width");
        ((double)iconBox!.Height).ShouldBeInRange(19.5, 20.5, "the mobile icon slot must keep its 1.25rem baseline height");
        var iconCenterY = (double)iconBox.Y + (iconBox.Height / 2);
        var labelCenterY = (double)labelBox.Y + (labelBox.Height / 2);
        Math.Abs(iconCenterY - labelCenterY).ShouldBeLessThanOrEqualTo(2.0, "the icon and label must stay vertically centered");
    }

    /// <summary>
    /// Asserts the link stacks the icon above the label (icon box top above label box top) and is
    /// horizontally centered (both boxes share the same center x) — the reference layout.
    /// </summary>
    private static async Task AssertStackedLayoutAsync(ILocator link)
    {
        var flexDirection = await link.EvaluateAsync<string>("(el) => getComputedStyle(el).flexDirection");
        flexDirection.ShouldBe("column");
        var iconBox = await link.Locator("span.nav-icon-slot").BoundingBoxAsync();
        var labelBox = await link.Locator("span.nav-label").BoundingBoxAsync();
        iconBox.ShouldNotBeNull();
        labelBox.ShouldNotBeNull();
        iconBox!.Y.ShouldBeLessThan(labelBox!.Y, "the icon must sit above the label when stacked");
        var iconCenterX = (double)iconBox.X + (iconBox.Width / 2);
        var labelCenterX = (double)labelBox.X + (labelBox.Width / 2);
        Math.Abs(iconCenterX - labelCenterX).ShouldBeLessThanOrEqualTo(1.0, "the icon and label must stay horizontally centered");
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
    /// Asserts the active nav link renders the kelp teal indicator bar flush with the navbar's
    /// top edge: the bar's bounding-box top must match the <c>nav.navbar</c> element's top within
    /// a small tolerance (the bar is a <c>::after</c> pseudo-element, so Playwright measures it
    /// via the element's <c>getComputedStyle</c> + the known positions rather than a direct
    /// locator). The bar is expected to span the narrow width of the active item only.
    /// </summary>
    private static async Task AssertKelpTealTopIndicatorFlushAsync(ILocator link, ILocator nav)
    {
        await Expect(link).ToHaveClassAsync(new Regex("\\bactive\\b"));
        var indicatorColor = await link.EvaluateAsync<string>(
            "(el) => getComputedStyle(el, '::after').backgroundColor");
        indicatorColor.ShouldBe(ExpectedKelpTealRgb);
        var indicatorHeight = await link.EvaluateAsync<string>(
            "(el) => getComputedStyle(el, '::after').height");
        indicatorHeight.ShouldBe("3px");

        // Flush: the pseudo-element's top must equal the navbar's top edge (±1px). We compute the
        // ::after's absolute top from the link's own box and the offset, because Playwright
        // cannot locate pseudo-elements directly.
        var navTop = (await nav.BoundingBoxAsync())!.Y;
        var linkBox = await link.BoundingBoxAsync();
        linkBox.ShouldNotBeNull();
        // The ::after is positioned relative to the link's border box: its doc-space top =
        // linkBox.Y + parsed top offset (which is negative: flush raises it above the link).
        var afterTop = await link.EvaluateAsync<double>(
            "(el) => parseFloat(getComputedStyle(el, '::after').top)");
        var afterTopInDoc = (double)linkBox!.Y + afterTop;
        Math.Abs(afterTopInDoc - (double)navTop).ShouldBeLessThanOrEqualTo(2.0, "the indicator bar must be flush with the navbar top edge");

        // The bar must not be full-width: its width is bounded by the link's content width.
        var afterWidth = await link.EvaluateAsync<string>(
            "(el) => getComputedStyle(el, '::after').width");
        var afterWidthInPx = double.Parse(afterWidth.Replace("px", string.Empty), System.Globalization.CultureInfo.InvariantCulture);
        afterWidthInPx.ShouldBeLessThan(linkBox.Width, "the indicator bar must be a narrow bar above the active item, not full-width");
        afterWidthInPx.ShouldBeGreaterThan(0.0);
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
    /// Resolves the seeded club's display name for the club nav link assertion.
    /// </summary>
    private async Task<string> GetClubNameAsync(long clubId, CancellationToken cancellationToken)
    {
        await using var context = fixture.AppHost.CreateAdminContext();
        return (await context.Clubs.SingleAsync(club => club.ClubId == clubId, cancellationToken)).Name;
    }
}
