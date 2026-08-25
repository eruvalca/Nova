using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.Integration.Tests.Http;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Browser-level validation of the campaign-creation form (<c>/campaigns/new</c>): validation,
/// successful creation, responsive rendering, and keyboard operability.
/// </summary>
/// <param name="fixture">The Aspire-hosted browser suite fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class CampaignFormBrowserTests(BrowserSuiteFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    [Fact]
    public async Task CampaignForm_Validation_RejectsWhitespaceName_AndStaysOnForm()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenNewCampaignAsync(page);

        // Select the inline season and fill its required fields first, so the campaign name is the
        // only empty field when the form is submitted below (the radio toggle also proves hydration,
        // ensuring the submit drives Blazor validation instead of a swallowed pre-hydration form post).
        await CheckInlineSeasonAsync(page);
        await page.Locator("#inline-season-name").FillAsync("Whitespace Season");
        await page.Locator("#inline-season-start-date").FillAsync("2026-06-01");
        await page.Locator("#campaign-name").FillAsync("   ");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create campaign", Exact = true }).ClickAsync();

        // Only the campaign name field surfaces its validation message — exactly one field-level
        // message renders (the filled season fields are valid) and it is the name's required error.
        await Expect(page.Locator(".validation-message")).ToHaveCountAsync(1);
        await Expect(page.Locator(".validation-message"))
            .ToContainTextAsync("The Name field is required.");
        page.Url.ShouldContain("/campaigns/new");
    }

    [Fact]
    public async Task CampaignForm_Success_CreatesCampaign_AndRedirectsToCampaignList()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenNewCampaignAsync(page);

        var suffix = Guid.NewGuid().ToString("N");
        var campaignName = $"Form Campaign {suffix}";
        await CheckInlineSeasonAsync(page);
        await page.Locator("#campaign-name").FillAsync(campaignName);
        await page.Locator("#inline-season-name").FillAsync($"Form Season {suffix}");
        await page.Locator("#inline-season-start-date").FillAsync("2026-06-01");

        var submit = page.GetByRole(AriaRole.Button, new() { Name = "Create campaign", Exact = true });
        await InteractionHelpers.ActUntilAsync(
            page,
            () => submit.ClickAsync(new() { Timeout = 3000 }),
            () => Task.FromResult(page.Url.Contains("/campaigns", StringComparison.OrdinalIgnoreCase) && !page.Url.Contains("/new", StringComparison.OrdinalIgnoreCase)));

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Campaigns" })).ToBeVisibleAsync();
        await Expect(page.GetByText(campaignName)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task CampaignForm_Responsive_PreservesInputs_AcrossViewports()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenNewCampaignAsync(page);

        var name = $"Narrow {Guid.NewGuid():N}";
        await CheckInlineSeasonAsync(page);
        await page.Locator("#campaign-name").FillAsync(name);
        await page.Locator("#inline-season-name").FillAsync("Narrow Season");

        await page.SetViewportSizeAsync(480, 800);

        // The labelled input retains its value and the submit control remains reachable.
        await Expect(page.Locator("#campaign-name")).ToHaveValueAsync(name);
        await Expect(page.GetByLabel("Campaign name")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Create campaign", Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task CampaignForm_Keyboard_TabAndEnter_Submits()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenNewCampaignAsync(page);

        var suffix = Guid.NewGuid().ToString("N");
        var campaignName = $"Form Campaign {suffix}";
        await CheckInlineSeasonAsync(page);
        await page.Locator("#inline-season-name").FillAsync($"Form Season {suffix}");
        await page.Locator("#inline-season-start-date").FillAsync("2026-06-01");

        // Type the campaign name from the keyboard, reach the submit button via Tab, and submit with Enter.
        await page.Locator("#campaign-name").FocusAsync();
        await page.Keyboard.TypeAsync(campaignName);
        var submit = page.GetByRole(AriaRole.Button, new() { Name = "Create campaign", Exact = true });
        await InteractionHelpers.TabUntilFocusedAsync(page, submit);
        await page.Keyboard.PressAsync("Enter");

        // A valid keyboard submission creates the campaign and redirects to the campaign list.
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Campaigns" })).ToBeVisibleAsync();
        await Expect(page.GetByText(campaignName)).ToBeVisibleAsync();
    }

    /// <summary>
    /// Captures campaign-creation-form accessibility evidence when <c>NOVA_A11Y_SCREENSHOTS=1</c>;
    /// otherwise skips so a green run always means the assertions executed.
    /// </summary>
    [Fact]
    public async Task CampaignForm_A11yEvidence_CapturesScreenshots()
    {
        if (Environment.GetEnvironmentVariable("NOVA_A11Y_SCREENSHOTS") != "1")
        {
            Assert.Skip("Set NOVA_A11Y_SCREENSHOTS=1 to capture campaign form accessibility evidence.");
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        var outputDirectory = Path.Combine(Path.GetTempPath(), "nova-a11y-screenshots");
        Directory.CreateDirectory(outputDirectory);

        await OpenNewCampaignAsync(page);
        await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "campaign-form-wide.png") });

        await page.SetViewportSizeAsync(480, 800);
        await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "campaign-form-narrow.png") });
    }

    [Fact]
    public async Task CampaignForm_Loading_ShowsSubmitSpinner_ThenCompletes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenNewCampaignAsync(page);
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page);
        await OpenNewCampaignAsync(page);

        var suffix = Guid.NewGuid().ToString("N");
        var campaignName = $"Form Campaign {suffix}";
        await CheckInlineSeasonAsync(page);
        await page.Locator("#campaign-name").FillAsync(campaignName);
        await page.Locator("#inline-season-name").FillAsync($"Form Season {suffix}");
        await page.Locator("#inline-season-start-date").FillAsync("2026-06-01");

        // Hold the create mutation open while the submit spinner is asserted, then release it.
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var intercepted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync(
            IsCampaignCreateUrl,
            async route =>
            {
                if (route.Request.Method != "POST")
                {
                    await route.ContinueAsync();
                    return;
                }

                intercepted.TrySetResult(null);
                await release.Task;
                await route.ContinueAsync();
            });

        var submit = page.GetByRole(AriaRole.Button, new() { Name = "Create campaign", Exact = true });
        await submit.ClickAsync();
        await intercepted.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        await Expect(submit.Locator(".spinner-border")).ToBeVisibleAsync();

        release.TrySetResult(null);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Campaigns" })).ToBeVisibleAsync();
        await Expect(page.GetByText(campaignName)).ToBeVisibleAsync();
        await page.UnrouteAsync(IsCampaignCreateUrl);
    }

    [Fact]
    public async Task CampaignForm_Failure_ShowsRetry_AndRetryRecovers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenNewCampaignAsync(page);
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page);
        await OpenNewCampaignAsync(page);

        var suffix = Guid.NewGuid().ToString("N");
        var campaignName = $"Form Campaign {suffix}";
        await CheckInlineSeasonAsync(page);
        await page.Locator("#campaign-name").FillAsync(campaignName);
        await page.Locator("#inline-season-name").FillAsync($"Form Season {suffix}");
        await page.Locator("#inline-season-start-date").FillAsync("2026-06-01");

        await page.RouteAsync(
            IsCampaignCreateUrl,
            route => route.Request.Method == "POST"
                ? route.FulfillAsync(new() { Status = 500 })
                : route.ContinueAsync());

        var submit = page.GetByRole(AriaRole.Button, new() { Name = "Create campaign", Exact = true });
        await submit.ClickAsync();
        await Expect(page.Locator("div.alert-danger[role=alert]")).ToContainTextAsync("Failed to create the campaign");
        page.Url.ShouldContain("/campaigns/new");

        await page.UnrouteAsync(IsCampaignCreateUrl);
        await submit.ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Campaigns" })).ToBeVisibleAsync();
        await Expect(page.GetByText(campaignName)).ToBeVisibleAsync();
    }

    /// <summary>
    /// Matches the campaign create/list URL (<c>/api/campaigns</c>). The URL alone cannot distinguish
    /// the create <c>POST</c> from the list <c>GET</c>, so each <c>RouteAsync</c> handler additionally
    /// guards on <see cref="IRequest.Method"/>.
    /// </summary>
    private static bool IsCampaignCreateUrl(string url) =>
        url.Contains("/api/campaigns", StringComparison.Ordinal)
        && !url.Contains("/api/campaigns/", StringComparison.Ordinal);

    /// <summary>Navigates to the campaign-creation page and waits for the form.</summary>
    private async Task OpenNewCampaignAsync(IPage page)
    {
        await page.GotoAsync(new Uri(fixture.BaseUri, "/campaigns/new").ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Create campaign" })).ToBeVisibleAsync();
        await Expect(page.Locator("#campaign-name")).ToBeVisibleAsync();
    }

    /// <summary>
    /// Selects the "Create a new season" radio and waits for the inline-season fields to render,
    /// retrying through the SSR hydration window. The rendered inline fields are the hydration proof
    /// used by the scenarios that then drive the rest of the form.
    /// </summary>
    private static async Task CheckInlineSeasonAsync(IPage page)
    {
        var radio = page.Locator("#season-mode-inline");
        var inlineName = page.Locator("#inline-season-name");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (await inlineName.IsVisibleAsync())
            {
                return;
            }

            try
            {
                await radio.CheckAsync(new() { Timeout = 2000 });
            }
            catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
            {
                // The radio was replaced mid-interaction or the change was swallowed pre-hydration.
                // Playwright actionability timeouts surface as System.TimeoutException (not
                // PlaywrightException), so catch both.
            }

            try
            {
                await Expect(inlineName).ToBeVisibleAsync(new() { Timeout = 1500 });
                return;
            }
            catch (PlaywrightException)
            {
                await page.WaitForTimeoutAsync(250);
            }
        }

        await Expect(inlineName).ToBeVisibleAsync();
    }

    /// <summary>Seeds a club with a single administrator and returns the login credentials and identifiers.</summary>
    private async Task<(long ClubId, string AdminEmail, long AdminUserId)> SeedAdminAsync(CancellationToken cancellationToken)
    {
        using var adminClient = fixture.AppHost.CreateNovaHttpClient();
        var adminEmail = SeedingHelpers.UniqueEmail("campaign-form-admin");
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
}
