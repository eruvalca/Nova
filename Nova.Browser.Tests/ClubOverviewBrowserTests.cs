using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.Integration.Tests.Http;
using Nova.Shared.Features.Clubs;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>Browser coverage for the canonical Club shell, responsive directory, and legacy handoffs.</summary>
[Collection(BrowserSuiteCollection.Name)]
public sealed class ClubOverviewBrowserTests(BrowserSuiteFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    [Fact]
    public async Task Overview_Desktop_RendersAdministratorDirectoryAndIndependentWaypoints()
    {
        var seed = await SeedAdminAsync(TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.Email, Password, new() { Width = 1280, Height = 800 });
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.Overview).ToString());

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Overview", Exact = true })).ToBeVisibleAsync();
        var directory = page.GetByRole(AriaRole.Navigation, new() { Name = "Club directory" });
        await Expect(directory).ToBeVisibleAsync();
        foreach (var label in new[] { "Overview", "Seasons", "Teams", "Members", "Requests", "Tags", "Crest" })
        {
            await Expect(directory.GetByRole(AriaRole.Link, new() { Name = label, Exact = true })).ToBeVisibleAsync();
        }
        await Expect(page.Locator(".club-identity").GetByText(seed.ClubName, new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Current season", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Active work", Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Overview_MobileSheet_OpensCompleteDirectory_AndNoScriptShowsRoutes()
    {
        var seed = await SeedAdminAsync(TestContext.Current.CancellationToken);
        var viewport = new ViewportSize { Width = 390, Height = 844 };
        await using (var context = await fixture.NewSignedInContextAsync(seed.Email, Password, viewport))
        {
            var page = context.Pages[0];
            await page.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.Overview).ToString());

            // Move the page to WebAssembly before driving the collapse: on the first visit the page
            // renders on the InteractiveServer circuit, and a server-side re-render of the Club shell
            // (fired shortly after load, once its async state settles) replaces the <nav> element and
            // resets Bootstrap's runtime-added classes — the re-render can land mid-transition, snapping
            // the sheet closed before the keyboard walk.
            //
            // Navigation focus review requirement: Wait for the shell's async state to settle (roles +
            // club identity + season data) before opening the sheet. Once settled, the collapse is the
            // only writer of the sheet's classes, and a keyboard Enter on a directory link routes
            // through the interactive Router, whose FocusOnNavigate moves focus to the destination h1.
            await Expect(page.Locator(".club-identity").GetByText(seed.ClubName, new() { Exact = true })).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Current season", Exact = true })).ToBeVisibleAsync();

            var toggle = page.Locator(".club-directory-toggle");
            await Expect(toggle).ToBeVisibleAsync();
            var box = await toggle.BoundingBoxAsync();
            box.ShouldNotBeNull();
            box.Height.ShouldBeGreaterThanOrEqualTo(44);

            // Issue #201 keyboard acceptance: the sheet must open under keyboard activation
            // (focus + Enter, then tabbing reaches the directory links), not only pointer click.
            await toggle.FocusAsync();
            await Expect(toggle).ToBeFocusedAsync();
            await page.Keyboard.PressAsync("Enter");
            var directory = page.GetByRole(AriaRole.Navigation, new() { Name = "Club directory" });
            await Expect(directory.GetByRole(AriaRole.Link, new() { Name = "Crest", Exact = true })).ToBeVisibleAsync();
            var teams = directory.GetByRole(AriaRole.Link, new() { Name = "Teams", Exact = true });

            // Wait for the Bootstrap collapse to finish its transition and reach the fully-open
            // "show" state before walking the tab order: while the nav is "collapsing" its links
            // are not yet tab-reachable (focus bounces toggle -> body -> toggle, never into the
            // directory), which makes the keyboard tab loop fail deterministically.
            await Expect(page.Locator(".club-route-directory")).ToHaveClassAsync(new Regex(@"\bshow\b"));
            await InteractionHelpers.TabUntilFocusedAsync(page, teams);

            // Following a directory route with the keyboard must land focus on the destination
            // page's h1 (Routes.razor FocusOnNavigate selector) — the focus-loop acceptance
            // criterion for the Club shell.
            await page.Keyboard.PressAsync("Enter");
            await page.WaitForURLAsync(
                url => new Uri(url).AbsolutePath.Equals(ClubRoutes.Teams, StringComparison.OrdinalIgnoreCase),
                new() { WaitUntil = WaitUntilState.Commit });
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Teams", Exact = true })).ToBeFocusedAsync();
        }

        await using var noScript = await fixture.NewSignedInContextAsync(seed.Email, Password, viewport, javaScriptEnabled: false);
        var noScriptPage = noScript.Pages[0];
        await noScriptPage.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.Overview).ToString());
        await Expect(noScriptPage.Locator(".club-directory-toggle")).ToBeHiddenAsync();
        await Expect(noScriptPage.GetByRole(AriaRole.Link, new() { Name = "Crest", Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task LegacyClubAndTeamsRoutes_ResolveToCanonicalClubUrls()
    {
        var seed = await SeedAdminAsync(TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.Email, Password);
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, $"/Clubs/{seed.ClubId}").ToString());
        await page.WaitForURLAsync(url => url.EndsWith(ClubRoutes.Overview, StringComparison.OrdinalIgnoreCase));

        await page.GotoAsync(new Uri(fixture.BaseUri, "/teams").ToString());
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath.Equals(ClubRoutes.Teams, StringComparison.OrdinalIgnoreCase));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Teams", Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Overview_MemberDirectory_HidesAdministratorRoutes_AndDeniedRouteShowsPermissionNotice()
    {
        var seed = await SeedMemberAsync(TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.Email, Password);
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.Overview).ToString());
        var directory = page.GetByRole(AriaRole.Navigation, new() { Name = "Club directory" });
        await Expect(directory.GetByRole(AriaRole.Link, new() { Name = "Overview", Exact = true })).ToBeVisibleAsync();
        await Expect(directory.GetByRole(AriaRole.Link, new() { Name = "Teams", Exact = true })).ToBeVisibleAsync();
        foreach (var label in new[] { "Seasons", "Members", "Requests", "Tags", "Crest" })
        {
            await Expect(directory.GetByRole(AriaRole.Link, new() { Name = label, Exact = true })).ToHaveCountAsync(0);
        }

        await page.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.Seasons).ToString());
        new Uri(page.Url).PathAndQuery.ShouldBe(ClubRoutes.OverviewWithPermissionsChanged);
        await Expect(page.GetByText("Your permissions changed. Club navigation now reflects your current access."))
            .ToBeVisibleAsync();
    }

    private async Task<(long ClubId, string ClubName, string Email)> SeedAdminAsync(CancellationToken cancellationToken)
    {
        using var client = fixture.AppHost.CreateNovaHttpClient();
        var email = SeedingHelpers.UniqueEmail("club-overview-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        var club = await SeedingHelpers.CreateClubAsync(client, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);
        return (club.ClubId, club.Name, email);
    }

    private async Task<(long ClubId, string ClubName, string Email)> SeedMemberAsync(CancellationToken cancellationToken)
    {
        var club = await SeedAdminAsync(cancellationToken);
        using var member = fixture.AppHost.CreateNovaHttpClient();
        var email = SeedingHelpers.UniqueEmail("club-overview-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(member, email, Password, cancellationToken);
        await using (var context = fixture.AppHost.CreateAdminContext())
        {
            var normalizedEmail = email.ToUpperInvariant();
            var user = await context.Users.SingleAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken);
            user.ClubId = club.ClubId;
            await context.SaveChangesAsync(cancellationToken);
        }

        await SeedingHelpers.RefreshClubMembershipCookieAsync(member, cancellationToken);
        return (club.ClubId, club.ClubName, email);
    }
}
