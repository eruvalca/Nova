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

    /// <summary>Verifies mobile keyboard navigation, touch sizing, and the no-script directory fallback.</summary>
    /// <returns>A task that completes after both directory variants have been checked.</returns>
    [Fact]
    public async Task Overview_MobileSheet_OpensCompleteDirectory_AndNoScriptShowsRoutes()
    {
        var seed = await SeedAdminAsync(TestContext.Current.CancellationToken);
        var viewport = new ViewportSize { Width = 390, Height = 844 };
        await using (var context = await fixture.NewSignedInContextAsync(seed.Email, Password, viewport))
        {
            var page = context.Pages[0];
            await page.GotoAsync(new Uri(fixture.BaseUri, ClubRoutes.Overview).ToString());

            // The Club shell owns the sheet's open state in Blazor (@onclick + aria-expanded),
            // so re-renders cannot snap the sheet closed as Bootstrap's runtime class plumbing
            // could. The async-state settle wait is still needed: the toggle's @onclick handler is
            // inert until the interactive circuit attaches, so wait for the identity + season to
            // settle, then retry the Enter until the sheet shows (the hydration-retry pattern).
            await Expect(page.Locator(".club-identity").GetByText(seed.ClubName, new() { Exact = true })).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Current season", Exact = true })).ToBeVisibleAsync();

            var toggle = page.Locator(".club-directory-toggle");
            await Expect(toggle).ToBeVisibleAsync();
            // Issue #201 keyboard acceptance: the sheet must open under keyboard activation
            // (focus + Enter, then tabbing reaches the directory links), not only pointer click.
            await toggle.FocusAsync();
            await Expect(toggle).ToBeFocusedAsync();
            var directory = page.GetByRole(AriaRole.Navigation, new() { Name = "Club directory" });
            // Resolve the current button on each retry: interactive attach can replace the
            // focused prerendered node, leaving a document-level Enter aimed at the body.
            await InteractionHelpers.ActUntilAsync(
                page,
                () => toggle.PressAsync("Enter"),
                async () => await page.Locator(".club-route-directory").EvaluateAsync<bool>(
                    "el => el.classList.contains('show')"));

            // The sheet is fully open and its links are tab-reachable. While the nav is in a
            // transition its links are not yet reachable (focus bounces toggle -> body -> toggle),
            // so wait for the "show" state before walking the tab order.
            await Expect(page.Locator(".club-route-directory")).ToHaveClassAsync(new Regex(@"\bshow\b"));
            // Successful activation proves attachment before measuring a node that SSR replacement could remove.
            var box = await toggle.BoundingBoxAsync();
            box.ShouldNotBeNull();
            box.Height.ShouldBeGreaterThanOrEqualTo(44);
            await Expect(directory.GetByRole(AriaRole.Link, new() { Name = "Crest", Exact = true })).ToBeVisibleAsync();
            var teams = directory.GetByRole(AriaRole.Link, new() { Name = "Teams", Exact = true });
            await InteractionHelpers.TabUntilFocusedAsync(page, teams);

            // Following a directory route with the keyboard must land focus on the destination
            // page's h1 and retain that focus when the new shell attaches interactively.
            await page.Keyboard.PressAsync("Enter");
            await page.WaitForURLAsync(
                url => new Uri(url).AbsolutePath.Equals(ClubRoutes.Teams, StringComparison.OrdinalIgnoreCase),
                new() { WaitUntil = WaitUntilState.Commit });
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Teams", Exact = true })).ToBeFocusedAsync();

            // A delayed focus-restoration call must not take focus from a control the user selected.
            var destinationToggle = page.Locator(".club-directory-toggle");
            await destinationToggle.FocusAsync();
            await page.Locator(".club-hall").EvaluateAsync("""
                async hall => {
                    const module = await import('/_content/Nova.UI/Features/Clubs/Components/ClubShell.razor.js');
                    module.restoreHeadingFocusAfterAttach(hall);
                }
                """);
            await Expect(destinationToggle).ToBeFocusedAsync();
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
        await Expect(page.GetByText("You don't have access to that section. Club navigation reflects your current permissions."))
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
