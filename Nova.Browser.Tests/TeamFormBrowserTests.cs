using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.Integration.Tests.Http;
using Nova.Shared.Enums;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Browser-level validation of the team add/edit form and the team-detail status badge: validation,
/// successful creation, failure with retry, responsive rendering, keyboard operability, and the
/// active-campaign status badge contrast.
/// </summary>
/// <param name="fixture">The Aspire-hosted browser suite fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class TeamFormBrowserTests(BrowserSuiteFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    [Fact]
    public async Task TeamForm_Validation_RejectsWhitespaceName_AndStaysOnForm()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenTeamsAsync(page);

        await ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Add team" }), () =>
        {
            return page.Locator("#team-name").IsVisibleAsync();
        });

        await page.Locator("#team-name").FillAsync("   ");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create team", Exact = true }).ClickAsync();

        await Expect(page.Locator("div.text-danger").First).ToBeVisibleAsync();
        await Expect(page.Locator("div.alert-success[role=status]")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task TeamForm_Success_CreatesTeam_AndReflectsInRoster()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenTeamsAsync(page);

        await ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Add team" }), () =>
        {
            return page.Locator("#team-name").IsVisibleAsync();
        });

        var teamName = $"Form Team {Guid.NewGuid():N}";
        await page.Locator("#team-name").FillAsync(teamName);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create team", Exact = true }).ClickAsync();

        await Expect(page.Locator("div.alert-success[role=status]")).ToContainTextAsync("Team created successfully.");
        await Expect(page.GetByText(teamName)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TeamForm_Responsive_PreservesInputs_AcrossViewports()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenTeamsAsync(page);

        await ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Add team" }), () =>
        {
            return page.Locator("#team-name").IsVisibleAsync();
        });

        var teamName = $"Narrow {Guid.NewGuid():N}";
        await page.Locator("#team-name").FillAsync(teamName);
        await page.SetViewportSizeAsync(480, 800);

        await Expect(page.Locator("#team-name")).ToHaveValueAsync(teamName);
        await Expect(page.GetByLabel("Team name", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Create team", Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TeamForm_Keyboard_TabAndEnter_Submits()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenTeamsAsync(page);

        await ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Add team" }), () =>
        {
            return page.Locator("#team-name").IsVisibleAsync();
        });

        var teamName = $"Form Team {Guid.NewGuid():N}";
        await page.Locator("#team-name").FocusAsync();
        await page.Keyboard.TypeAsync(teamName);

        var submit = page.GetByRole(AriaRole.Button, new() { Name = "Create team", Exact = true });
        await TabUntilFocusedAsync(page, submit);
        await page.Keyboard.PressAsync("Enter");

        await Expect(page.Locator("div.alert-success[role=status]")).ToContainTextAsync("Team created successfully.");
        await Expect(page.GetByText(teamName)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TeamDetail_ActiveCampaignBadge_MeetsContrastThreshold()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        var teamId = await SeedTeamWithActivePlacementAsync(seed, cancellationToken);

        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/teams/{teamId}").ToString());
        await Expect(page.Locator("#placement-history-heading")).ToBeVisibleAsync();

        // The placement-history entry renders the active campaign's status badge (text-bg-success).
        var badge = page.Locator("article span.badge.text-bg-success").First;
        await Expect(badge).ToHaveTextAsync("Active");
        await A11yMeasurementHelpers.AssertContrastRatioAsync(badge, 4.5, "team detail active-campaign status badge");
    }

    /// <summary>
    /// Captures team-detail accessibility evidence (screenshot + status-badge measurement) when
    /// <c>NOVA_A11Y_SCREENSHOTS=1</c>; otherwise skips so a green run always means the assertions executed.
    /// </summary>
    [Fact]
    public async Task TeamDetail_A11yEvidence_CapturesScreenshots()
    {
        if (Environment.GetEnvironmentVariable("NOVA_A11Y_SCREENSHOTS") != "1")
        {
            Assert.Skip("Set NOVA_A11Y_SCREENSHOTS=1 to capture team detail accessibility evidence.");
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        var teamId = await SeedTeamWithActivePlacementAsync(seed, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        var outputDirectory = Path.Combine(Path.GetTempPath(), "nova-a11y-screenshots");
        Directory.CreateDirectory(outputDirectory);

        await page.GotoAsync(new Uri(fixture.BaseUri, $"/teams/{teamId}").ToString());
        await Expect(page.Locator("#placement-history-heading")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "team-detail.png") });

        var badge = page.Locator("article span.badge.text-bg-success").First;
        var ratio = await A11yMeasurementHelpers.MeasureContrastRatioAsync(badge);
        await File.AppendAllTextAsync(
            Path.Combine(outputDirectory, "measurements.txt"),
            $"team-detail-active-badge contrast={ratio:F2}{Environment.NewLine}",
            cancellationToken);
    }

    /// <summary>Navigates to the teams roster page and waits for it to render.</summary>
    private async Task OpenTeamsAsync(IPage page)
    {
        await page.GotoAsync(new Uri(fixture.BaseUri, "/teams").ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Teams", Exact = true })).ToBeVisibleAsync();
    }

    /// <summary>Seeds a club with a single administrator and returns the login credentials and identifiers.</summary>
    private async Task<(long ClubId, string AdminEmail, long AdminUserId)> SeedAdminAsync(CancellationToken cancellationToken)
    {
        using var adminClient = fixture.AppHost.CreateNovaHttpClient();
        var adminEmail = SeedingHelpers.UniqueEmail("team-form-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture.AppHost, adminEmail, clubId: null, cancellationToken, firstName: "Alice", lastName: "Author");
        var club = await SeedingHelpers.CreateClubAsync(adminClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        long adminUserId;
        await using (var context = fixture.AppHost.CreateAdminContext())
        {
            adminUserId = (await context.Users.SingleAsync(user => user.NormalizedEmail == adminEmail.ToUpperInvariant(), cancellationToken)).Id;
        }

        return (club.ClubId, adminEmail, adminUserId);
    }

    /// <summary>Seeds a team with an active placement history entry and returns the team identifier.</summary>
    private async Task<long> SeedTeamWithActivePlacementAsync(
        (long ClubId, string AdminEmail, long AdminUserId) seed,
        CancellationToken cancellationToken)
    {
        var campaign = await SeedingHelpers.SeedCampaignWithParticipantsAsync(
            fixture.AppHost, seed.ClubId, seed.AdminEmail, "Team Badge", 1, PlacementOutcome.Undecided, cancellationToken);
        var teamId = await SeedingHelpers.InsertTeamAsync(
            fixture.AppHost, seed.ClubId, seed.AdminEmail, $"Badge Team {Guid.NewGuid():N}", 2030, cancellationToken);
        await SeedingHelpers.AssignPlacementAsync(fixture.AppHost, campaign.AssignmentIds[0], teamId, cancellationToken);
        return teamId;
    }

    /// <summary>Repeatedly clicks a locator until the supplied settle predicate succeeds.</summary>
    private static async Task ClickUntilAsync(IPage page, ILocator locator, Func<Task<bool>> settled)
        => await ActUntilAsync(page, () => locator.ClickAsync(new() { Timeout = 3000 }), settled);

    /// <summary>Repeats an interaction until the settle predicate succeeds, tolerating the SSR hydration window.</summary>
    private static async Task ActUntilAsync(IPage page, Func<Task> act, Func<Task<bool>> settled)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (await settled())
            {
                return;
            }

            try
            {
                await act();
            }
            catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
            {
                // The element was replaced mid-interaction or the click was swallowed pre-hydration.
            }

            await page.WaitForTimeoutAsync(250);
        }

        throw new TimeoutException("Interaction did not settle within the retry window.");
    }

    /// <summary>Presses Tab until the target receives keyboard focus, then returns.</summary>
    private static async Task TabUntilFocusedAsync(IPage page, ILocator target)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                await Expect(target).ToBeFocusedAsync(new() { Timeout = 400 });
                return;
            }
            catch (PlaywrightException)
            {
                await page.Keyboard.PressAsync("Tab");
            }
        }

        throw new TimeoutException("The target never received keyboard focus.");
    }
}
