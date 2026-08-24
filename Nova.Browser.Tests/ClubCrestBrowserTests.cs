using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.Integration.Tests.Http;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Photos;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Browser-level acceptance of the required club crest (issue #140): a photo-complete club-less
/// user creates a club from the onboarding page with a crest upload, and the crest replaces the
/// building icon in the navigation bar and renders in the club detail header; a club administrator
/// can replace and remove the crest from the club admin page and the navigation follows suit.
/// </summary>
/// <param name="fixture">The Aspire-hosted browser suite fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class ClubCrestBrowserTests(BrowserSuiteFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// CC1: after creating a club through the real onboarding flow with a crest upload, the
    /// navigation bar club item renders the small crest variant instead of the building icon, and
    /// the club detail page header shows the crest.
    /// </summary>
    [Fact]
    public async Task ClubCrest_OnboardingCreate_ShowsCrestInNavAndClubDetail()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = await SeedPhotoCompleteClubLessUserAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(email, Password);
        var page = context.Pages[0];

        // The onboarding gate takes the photo-complete club-less user to the club onboarding page.
        await page.WaitForURLAsync(url => url.Contains("/Clubs/Onboarding", StringComparison.OrdinalIgnoreCase));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Welcome to Nova" })).ToBeVisibleAsync();

        var suffix = Guid.NewGuid().ToString("N");
        var clubName = $"Crest Club {suffix}";
        await page.Locator("#club-name").FillAsync(clubName);
        await page.Locator("#club-city").FillAsync("Austin");
        await page.Locator("#club-state").FillAsync("TX");

        // Upload the required crest and wait for the client-side preview to render.
        var crestPath = await WriteTempCrestAsync("onboarding");
        try
        {
            await page.SetInputFilesAsync("#club-crest", crestPath);
            await Expect(page.Locator("img.club-crest-preview")).ToBeVisibleAsync();

            var submit = page.GetByRole(AriaRole.Button, new() { Name = "Create Club", Exact = true });
            await InteractionHelpers.ActUntilAsync(
                page,
                () => submit.ClickAsync(new() { Timeout = 3000 }),
                () => Task.FromResult(!page.Url.Contains("/Clubs/Onboarding", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            File.Delete(crestPath);
        }

        // Post-create cookie-refresh hop lands on the dashboard with the fresh membership claims.
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();

        await AssertCrestInNavAsync(page, clubName);
        var clubId = await GetClubIdFromNavAsync(page, clubName);

        // The club detail header shows the medium crest variant next to the club name.
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/Clubs/{clubId}").ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = clubName })).ToBeVisibleAsync();
        var detailCrest = page.Locator("img.club-detail-crest");
        await Expect(detailCrest).ToHaveCountAsync(1);
        await Expect(detailCrest).ToHaveAttributeAsync(
            "src",
            ClubCrestEndpoints.GetCrestUrl(clubId, ProfilePhotoSize.Medium));
    }

    /// <summary>
    /// CC2: a club administrator replaces the crest from the club admin page — the navigation keeps
    /// showing the (new) crest after the refreshed cookie is applied — then removes it, and the
    /// navigation falls back to the building icon.
    /// </summary>
    [Fact]
    public async Task ClubCrest_AdminReplacesAndRemoves_NavReflectsCrestPresence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminWithCrestClubAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.Email, Password);
        var page = context.Pages[0];

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard" })).ToBeVisibleAsync();
        var clubName = await GetClubNameAsync(seed.ClubId, cancellationToken);
        await AssertCrestInNavAsync(page, clubName);

        // Open the club admin page; the crest manager island shows the current crest.
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/Clubs/{seed.ClubId}/admin").ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club Administration" })).ToBeVisibleAsync();
        await Expect(page.Locator("#crest-file")).ToHaveCountAsync(1);

        var replacementPath = await WriteTempCrestAsync("replacement");
        try
        {
            await page.SetInputFilesAsync("#crest-file", replacementPath);
            await Expect(page.Locator("img.club-crest-preview")).ToBeVisibleAsync();

            var change = page.GetByRole(AriaRole.Button, new() { Name = "Change crest", Exact = true });
            await InteractionHelpers.ClickUntilAsync(
                page,
                change,
                () => ExpectToSeeTextAsync(page, "Club crest updated."));
        }
        finally
        {
            File.Delete(replacementPath);
        }

        // The change endpoint refreshed the admin's cookie; a reload re-renders the nav with the
        // refreshed HasClubCrest claim and the crest is still shown.
        await page.ReloadAsync();
        await AssertCrestInNavAsync(page, clubName);

        // Remove the crest through the confirmation panel.
        var remove = page.GetByRole(AriaRole.Button, new() { Name = "Remove crest", Exact = true });
        await InteractionHelpers.ClickUntilAsync(page, remove, () => IsVisibleAsync(page.GetByText("Remove the club crest?")));
        var confirm = page.GetByRole(AriaRole.Button, new() { Name = "Remove", Exact = true });
        await InteractionHelpers.ClickUntilAsync(
            page,
            confirm,
            () => ExpectToSeeTextAsync(page, "Club crest removed."));

        // After a reload the nav falls back to the building icon for the club item.
        await page.ReloadAsync();
        var nav = page.Locator("nav.navbar");
        var clubLink = nav.GetByRole(AriaRole.Link, new() { Name = clubName, Exact = true });
        await Expect(clubLink).ToBeVisibleAsync();
        await Expect(nav.Locator("img[alt=\"Club crest\"]")).ToHaveCountAsync(0);
        await Expect(clubLink.Locator("span.bi-building")).ToHaveCountAsync(1);
    }

    /// <summary>
    /// Registers a photo-complete, club-less user through the real Identity flows.
    /// </summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The registered user's e-mail address.</returns>
    private async Task<string> SeedPhotoCompleteClubLessUserAsync(CancellationToken cancellationToken)
    {
        using var client = fixture.AppHost.CreateNovaHttpClient();
        var email = SeedingHelpers.UniqueEmail("crest-owner");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        return email;
    }

    /// <summary>
    /// Registers a photo-complete club administrator and creates a crest-bearing club through the
    /// real HTTP flow.
    /// </summary>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The seeded administrator's credentials and club identifier.</returns>
    private async Task<SeededAdminClub> SeedAdminWithCrestClubAsync(CancellationToken cancellationToken)
    {
        using var client = fixture.AppHost.CreateNovaHttpClient();
        var email = SeedingHelpers.UniqueEmail("crest-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        var club = await SeedingHelpers.CreateClubAsync(client, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);
        return new SeededAdminClub(email, club.ClubId);
    }

    /// <summary>
    /// Asserts the navigation bar club item renders the small crest variant and the building icon
    /// fallback is gone.
    /// </summary>
    /// <param name="page">The page to inspect.</param>
    /// <param name="clubName">The club item label.</param>
    /// <returns>The crest-bearing club link locator.</returns>
    private static async Task<ILocator> AssertCrestInNavAsync(IPage page, string clubName)
    {
        var nav = page.Locator("nav.navbar");
        // The accessible name of the crest-bearing club link includes the crest image alt text, so
        // the club name is matched as a substring (Playwright's default, case-insensitive).
        var club = nav.GetByRole(AriaRole.Link, new() { Name = clubName });
        await Expect(club).ToBeVisibleAsync();
        var crest = club.Locator("img.nav-avatar");
        await Expect(crest).ToHaveCountAsync(1);
        await Expect(crest).ToHaveAttributeAsync("alt", "Club crest");
        var src = await crest.GetAttributeAsync("src");
        src.ShouldNotBeNull();
        src.ShouldContain("/api/clubs/");
        src.ShouldEndWith("/crest?size=small");
        await Expect(club.Locator("span.bi-building")).ToHaveCountAsync(0);
        return club;
    }

    /// <summary>
    /// Extracts the club identifier from the navigation bar club link href (<c>Clubs/{id}</c>).
    /// </summary>
    /// <param name="page">The page to inspect.</param>
    /// <param name="clubName">The club item label.</param>
    /// <returns>The club identifier.</returns>
    private static async Task<long> GetClubIdFromNavAsync(IPage page, string clubName)
    {
        var club = page.Locator("nav.navbar").GetByRole(AriaRole.Link, new() { Name = clubName });
        var href = await club.GetAttributeAsync("href");
        href.ShouldNotBeNull();
        var segments = href!.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return long.Parse(segments[^1]);
    }

    /// <summary>
    /// Writes a small valid JPEG crest to a temporary file for the file input.
    /// </summary>
    /// <param name="prefix">A short label used in the file name.</param>
    /// <returns>The temporary file path.</returns>
    private static async Task<string> WriteTempCrestAsync(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"nova-{prefix}-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(path, SeedingHelpers.CreateJpegBytes());
        return path;
    }

    /// <summary>
    /// Reads the seeded club's display name.
    /// </summary>
    /// <param name="clubId">The club identifier.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The club display name.</returns>
    private async Task<string> GetClubNameAsync(long clubId, CancellationToken cancellationToken)
    {
        await using var context = fixture.AppHost.CreateAdminContext();
        return (await context.Clubs.SingleAsync(club => club.ClubId == clubId, cancellationToken)).Name;
    }

    /// <summary>
    /// Returns whether the given text locator resolves to a visible element.
    /// </summary>
    /// <param name="locator">The text locator.</param>
    /// <returns><see langword="true"/> when the element is visible.</returns>
    private static async Task<bool> IsVisibleAsync(ILocator locator)
    {
        try
        {
            await locator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2000 });
            return true;
        }
        catch (PlaywrightException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns whether the given text is visible on the page.
    /// </summary>
    /// <param name="page">The page to inspect.</param>
    /// <param name="text">The expected text.</param>
    /// <returns><see langword="true"/> when the text is visible.</returns>
    private static async Task<bool> ExpectToSeeTextAsync(IPage page, string text) => await IsVisibleAsync(page.GetByText(text));

    /// <summary>
    /// The seeded administrator's credentials and club identifier.
    /// </summary>
    /// <param name="Email">The administrator's login e-mail.</param>
    /// <param name="ClubId">The administrator's club identifier.</param>
    private sealed record SeededAdminClub(string Email, long ClubId);
}
