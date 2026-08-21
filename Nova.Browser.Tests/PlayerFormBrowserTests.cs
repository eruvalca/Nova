using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.Integration.Tests.Http;
using Nova.Shared.Enums;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Browser-level validation of the player add/edit form and the player-detail status badge:
/// validation, successful creation, failure with retry, responsive rendering, keyboard operability,
/// and the active-campaign status badge contrast.
/// </summary>
/// <param name="fixture">The Aspire-hosted browser suite fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class PlayerFormBrowserTests(BrowserSuiteFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    [Fact]
    public async Task PlayerForm_Validation_RejectsWhitespaceFirstName_AndStaysOnForm()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenPlayersAsync(page);

        await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Add player" }), () =>
        {
            return page.Locator("#player-first-name").IsVisibleAsync();
        });

        await page.Locator("#player-first-name").FillAsync("   ");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();

        await Expect(page.Locator("div.text-danger").First).ToBeVisibleAsync();
        await Expect(page.Locator("div.alert-success[role=status]")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task PlayerForm_Success_CreatesPlayer_AndReflectsInRoster()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenPlayersAsync(page);

        await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Add player" }), () =>
        {
            return page.Locator("#player-first-name").IsVisibleAsync();
        });

        var suffix = Guid.NewGuid().ToString("N");
        var firstName = "Form";
        var lastName = $"Player {suffix}";
        await page.Locator("#player-first-name").FillAsync(firstName);
        await page.Locator("#player-last-name").FillAsync(lastName);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();

        await Expect(page.Locator("div.alert-success[role=status]")).ToContainTextAsync("Player created successfully.");
        await Expect(page.GetByText($"{firstName} {lastName}")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task PlayerForm_Responsive_PreservesInputs_AcrossViewports()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenPlayersAsync(page);

        await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Add player" }), () =>
        {
            return page.Locator("#player-first-name").IsVisibleAsync();
        });

        var firstName = $"Narrow {Guid.NewGuid():N}";
        await page.Locator("#player-first-name").FillAsync(firstName);
        await page.SetViewportSizeAsync(480, 800);

        await Expect(page.Locator("#player-first-name")).ToHaveValueAsync(firstName);
        await Expect(page.GetByLabel("First name")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task PlayerForm_Keyboard_TabAndEnter_Submits()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenPlayersAsync(page);

        await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Add player" }), () =>
        {
            return page.Locator("#player-first-name").IsVisibleAsync();
        });

        var suffix = Guid.NewGuid().ToString("N");
        await page.Locator("#player-first-name").FocusAsync();
        await page.Keyboard.TypeAsync("Form");
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.TypeAsync($"Player {suffix}");

        var submit = page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true });
        await InteractionHelpers.TabUntilFocusedAsync(page, submit);
        await page.Keyboard.PressAsync("Enter");

        await Expect(page.Locator("div.alert-success[role=status]")).ToContainTextAsync("Player created successfully.");
        await Expect(page.GetByText($"Form Player {suffix}")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task PlayerDetail_ActiveCampaignBadge_MeetsContrastThreshold()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        var playerId = await SeedPlayerInActiveCampaignAsync(seed, cancellationToken);

        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/players/{playerId}").ToString());
        await Expect(page.Locator("#campaign-history-heading")).ToBeVisibleAsync();

        // The campaign-history entry renders the active campaign's status badge (text-bg-success).
        var badge = page.Locator("article span.badge.text-bg-success").First;
        await Expect(badge).ToHaveTextAsync("Active");
        await A11yMeasurementHelpers.AssertContrastRatioAsync(badge, 4.5, "player detail active-campaign status badge");
    }

    /// <summary>
    /// Captures player-detail accessibility evidence (screenshot + status-badge measurement) when
    /// <c>NOVA_A11Y_SCREENSHOTS=1</c>; otherwise skips so a green run always means the assertions executed.
    /// </summary>
    [Fact]
    public async Task PlayerDetail_A11yEvidence_CapturesScreenshots()
    {
        if (Environment.GetEnvironmentVariable("NOVA_A11Y_SCREENSHOTS") != "1")
        {
            Assert.Skip("Set NOVA_A11Y_SCREENSHOTS=1 to capture player detail accessibility evidence.");
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        var playerId = await SeedPlayerInActiveCampaignAsync(seed, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        var outputDirectory = Path.Combine(Path.GetTempPath(), "nova-a11y-screenshots");
        Directory.CreateDirectory(outputDirectory);

        await page.GotoAsync(new Uri(fixture.BaseUri, $"/players/{playerId}").ToString());
        await Expect(page.Locator("#campaign-history-heading")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "player-detail.png") });

        var badge = page.Locator("article span.badge.text-bg-success").First;
        var ratio = await A11yMeasurementHelpers.MeasureContrastRatioAsync(badge);
        await File.AppendAllTextAsync(
            Path.Combine(outputDirectory, "measurements.txt"),
            $"player-detail-active-badge contrast={ratio:F2}{Environment.NewLine}",
            cancellationToken);
    }

    /// <summary>Navigates to the players roster page and waits for it to render.</summary>
    private async Task OpenPlayersAsync(IPage page)
    {
        await page.GotoAsync(new Uri(fixture.BaseUri, "/players").ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Players", Exact = true })).ToBeVisibleAsync();
    }

    /// <summary>Seeds a club with a single administrator and returns the login credentials and identifiers.</summary>
    private async Task<(long ClubId, string AdminEmail, long AdminUserId)> SeedAdminAsync(CancellationToken cancellationToken)
    {
        using var adminClient = fixture.AppHost.CreateNovaHttpClient();
        var adminEmail = SeedingHelpers.UniqueEmail("player-form-admin");
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

    /// <summary>Seeds a player enrolled in an active campaign and returns the player identifier.</summary>
    private async Task<long> SeedPlayerInActiveCampaignAsync(
        (long ClubId, string AdminEmail, long AdminUserId) seed,
        CancellationToken cancellationToken)
    {
        var campaign = await SeedingHelpers.SeedCampaignWithParticipantsAsync(
            fixture.AppHost, seed.ClubId, seed.AdminEmail, "Player Badge", 1, PlacementOutcome.Undecided, cancellationToken);

        await using var context = fixture.AppHost.CreateAdminContext();
        return await context.PlayerCampaignAssignments
            .Where(assignment => assignment.PlayerCampaignAssignmentId == campaign.AssignmentIds[0])
            .Select(assignment => assignment.PlayerId)
            .SingleAsync(cancellationToken);
    }
}
