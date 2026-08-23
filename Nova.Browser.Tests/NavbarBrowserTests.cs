using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Entities;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Browser-level acceptance of the icon-first bottom navbar (issue #134): the authenticated
/// navbar shows icon + label items (Home, Club, Campaigns, Players, Teams) with bootstrap-icons
/// glyphs, the active item carries the kelp teal top indicator, the Manage link navigates to
/// /Account/Manage, Logout posts the antiforgery form and returns to the login page, and the
/// unauthenticated navbar shows only the Login link with no icon links.
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

        // The club item keeps the actual club name as its label.
        var club = nav.GetByRole(AriaRole.Link, new() { Name = clubName, Exact = true });
        await Expect(club).ToBeVisibleAsync();
        await AssertBootstrapIconGlyphAsync(club, "bi-building");

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
    /// NB2: the active nav item renders the kelp teal indicator bar along its top edge, both at
    /// the dashboard root (Home active) and on the campaigns page (Campaigns active).
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
        await AssertKelpTealTopIndicatorAsync(home);

        // On the campaigns page the Campaigns link becomes the active item.
        await page.GotoAsync(new Uri(fixture.BaseUri, "/campaigns").ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Campaigns" })).ToBeVisibleAsync();
        var campaigns = nav.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true });
        await AssertKelpTealTopIndicatorAsync(campaigns);
        await Expect(home).Not.ToHaveClassAsync(new Regex("\\bactive\\b"));
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
    /// Asserts the active nav link renders the kelp teal top indicator bar.
    /// </summary>
    private static async Task AssertKelpTealTopIndicatorAsync(ILocator link)
    {
        await Expect(link).ToHaveClassAsync(new Regex("\\bactive\\b"));
        var indicatorColor = await link.EvaluateAsync<string>(
            "(el) => getComputedStyle(el, '::after').backgroundColor");
        indicatorColor.ShouldBe(ExpectedKelpTealRgb);
        var indicatorHeight = await link.EvaluateAsync<string>(
            "(el) => getComputedStyle(el, '::after').height");
        indicatorHeight.ShouldBe("3px");
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
