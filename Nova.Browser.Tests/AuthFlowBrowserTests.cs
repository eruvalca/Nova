using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Http;
using Nova.Shared.Features.Clubs;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Verifies the static-SSR Identity flows and Hall of Panels account chrome through the real Aspire-hosted app.
/// </summary>
/// <param name="fixture">The shared Aspire-hosted browser suite fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class AuthFlowBrowserTests(BrowserSuiteFixture fixture)
{
    private const string Password = "Test123!";

    /// <summary>
    /// Verifies that registration remains available inside the shared account directory and exposes its real fields.
    /// </summary>
    [Fact]
    public async Task Register_Anonymous_ShowsAccountDirectoryAndForm()
    {
        await using var context = await fixture.NewAnonymousContextAsync();
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/Account/Register").ToString());

        var directory = page.GetByRole(AriaRole.Navigation, new() { Name = "Account areas" });
        await Expect(directory).ToBeVisibleAsync();
        await Expect(directory.GetByRole(AriaRole.Link, new() { Name = "Register", Exact = true }))
            .ToHaveAttributeAsync("aria-current", "page");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Register", Exact = true }))
            .ToBeVisibleAsync();
        await Expect(page.GetByLabel("First name")).ToBeVisibleAsync();
        await Expect(page.GetByLabel("Last name")).ToBeVisibleAsync();
        await Expect(page.GetByLabel("Email")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Register", Exact = true })).ToBeVisibleAsync();
    }

    /// <summary>
    /// Verifies that narrow account pages keep the active recovery and manage destinations visible
    /// without requiring an initial horizontal scroll.
    /// </summary>
    /// <param name="viewportWidth">The narrow viewport width to verify.</param>
    /// <param name="path">The account route to render.</param>
    /// <param name="expectedPanel">The directory panel expected to be active.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(320, "/Account/ForgotPassword", "Recover access")]
    [InlineData(320, "/Account/ConfirmEmailChange?userId=9223372036854775807&email=test%40example.com&code=unused", "Manage profile")]
    [InlineData(576, "/Account/ForgotPassword", "Recover access")]
    [InlineData(576, "/Account/ConfirmEmailChange?userId=9223372036854775807&email=test%40example.com&code=unused", "Manage profile")]
    public async Task AccountDirectory_NarrowViewport_ShowsActivePanel(
        int viewportWidth,
        string path,
        string expectedPanel)
    {
        await using var context = await fixture.NewAnonymousContextAsync(
            new ViewportSize { Width = viewportWidth, Height = 800 });
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, path).ToString());

        var activePanel = page.GetByRole(AriaRole.Link, new() { Name = expectedPanel, Exact = true });
        await Expect(activePanel).ToHaveAttributeAsync("aria-current", "page");
        await Expect(activePanel).ToBeInViewportAsync();
        var bounds = await activePanel.BoundingBoxAsync();
        bounds.ShouldNotBeNull();
        bounds.X.ShouldBeGreaterThanOrEqualTo(0);
        (bounds.X + bounds.Width).ShouldBeLessThanOrEqualTo(viewportWidth);
    }

    /// <summary>
    /// Verifies that a valid local login keeps the real Identity redirect into profile-photo onboarding.
    /// </summary>
    [Fact]
    public async Task Login_ValidCredentials_RedirectsToProfilePhotoOnboarding()
    {
        var email = await RegisterUserAsync("auth-login");
        await using var context = await fixture.NewAnonymousContextAsync();
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/Account/Login").ToString());
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();

        await page.WaitForURLAsync(
            url => url.Contains("/Account/ProfilePhoto", StringComparison.OrdinalIgnoreCase),
            new() { WaitUntil = WaitUntilState.Commit });
    }

    /// <summary>
    /// Verifies that invalid local credentials remain on the login page and render the bounded error status.
    /// </summary>
    [Fact]
    public async Task Login_InvalidPassword_ShowsErrorWithoutNavigation()
    {
        var email = await RegisterUserAsync("auth-invalid");
        await using var context = await fixture.NewAnonymousContextAsync();
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/Account/Login").ToString());
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password").FillAsync("Wrong123!");
        await page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Alert))
            .ToContainTextAsync("Invalid login attempt");
        new Uri(page.Url).AbsolutePath.ShouldBe("/Account/Login");
    }

    /// <summary>
    /// Verifies that password recovery posts through the static-SSR form and reaches its confirmation page.
    /// </summary>
    [Fact]
    public async Task ForgotPassword_ValidEmail_ShowsConfirmation()
    {
        await using var context = await fixture.NewAnonymousContextAsync();
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/Account/ForgotPassword").ToString());
        await page.GetByLabel("Email").FillAsync(SeedingHelpers.UniqueEmail("auth-recovery"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Reset password", Exact = true }).ClickAsync();

        await page.WaitForURLAsync(
            url => url.Contains("/Account/ForgotPasswordConfirmation", StringComparison.OrdinalIgnoreCase),
            new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Forgot password confirmation", Exact = true }))
            .ToBeVisibleAsync();
    }

    /// <summary>
    /// Verifies that an anonymous manage request retains a local return URL when redirected to login.
    /// </summary>
    [Fact]
    public async Task Manage_Anonymous_RedirectsToLoginWithReturnUrl()
    {
        await using var context = await fixture.NewAnonymousContextAsync();
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/Account/Manage").ToString());

        await page.WaitForURLAsync(
            new Regex(@"/Account/Login\?ReturnUrl=%2FAccount%2FManage", RegexOptions.IgnoreCase),
            new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Log in", Exact = true }))
            .ToBeVisibleAsync();
    }

    /// <summary>
    /// Verifies that a member denied an administrator route returns to the Club overview with a permission notice.
    /// </summary>
    [Fact]
    public async Task ClubAdmin_OrdinaryMember_ShowsOverviewPermissionNotice()
    {
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, DashboardSeed.Password);
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, $"/Clubs/{seed.ClubId}/admin").ToString());

        await page.WaitForURLAsync(
            url => new Uri(url).PathAndQuery == ClubRoutes.OverviewWithPermissionsChanged,
            new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Overview", Exact = true }))
            .ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Status).Filter(new() { HasText = "You don't have access to that section." }))
            .ToBeVisibleAsync();
        var directory = page.GetByRole(AriaRole.Navigation, new() { Name = "Club directory" });
        await Expect(directory.GetByRole(AriaRole.Link, new() { Name = "Teams", Exact = true })).ToBeVisibleAsync();
        await Expect(directory.GetByRole(AriaRole.Link, new() { Name = "Members", Exact = true })).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Verifies the static-SSR administrator form refreshes the actor's cookie from committed
    /// database state after self-demotion, denies further management, and recovers a later
    /// administrator-route request to the member-shaped Club overview.
    /// </summary>
    [Fact]
    public async Task ClubAdmin_SelfDemotion_ImmediatelyLosesAdministratorAccessButRetainsMembership()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedTwoAdministratorsAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.ActorEmail, Password);
        var page = context.Pages[0];
        var adminUrl = new Uri(fixture.BaseUri, $"/Clubs/{seed.Club.ClubId}/admin").ToString();

        await page.GotoAsync(adminUrl);
        var selfDemotionForm = page.Locator(
            $"form:has(input[name='MemberUserId'][value='{seed.ActorUserId}'])");
        await selfDemotionForm.GetByRole(AriaRole.Button, new() { Name = "Demote", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(
            url => new Uri(url).AbsolutePath == "/Account/AccessDenied",
            new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Access denied", Exact = true }))
            .ToBeVisibleAsync();

        await page.GotoAsync(adminUrl);
        await page.WaitForURLAsync(
            url => new Uri(url).PathAndQuery == ClubRoutes.OverviewWithPermissionsChanged,
            new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Overview", Exact = true }))
            .ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Status).Filter(new() { HasText = "You don't have access to that section." }))
            .ToBeVisibleAsync();

        await page.GotoAsync(new Uri(fixture.BaseUri, $"/Clubs/{seed.Club.ClubId}").ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = seed.Club.Name, Exact = true }))
            .ToBeVisibleAsync();
        var directory = page.GetByRole(AriaRole.Navigation, new() { Name = "Club directory" });
        await Expect(directory.GetByRole(AriaRole.Link, new() { Name = "Teams", Exact = true }))
            .ToBeVisibleAsync();
        await Expect(directory.GetByRole(AriaRole.Link, new() { Name = "Members", Exact = true }))
            .ToHaveCountAsync(0);
    }

    /// <summary>Seeds two administrators in one club through the real Identity and membership endpoints.</summary>
    /// <param name="cancellationToken">A token that cancels setup.</param>
    /// <returns>The actor identity and club used by the static-SSR self-demotion scenario.</returns>
    private async Task<SelfDemotionSeed> SeedTwoAdministratorsAsync(CancellationToken cancellationToken)
    {
        using var actorClient = fixture.AppHost.CreateNovaHttpClient();
        using var secondAdministratorClient = fixture.AppHost.CreateNovaHttpClient();
        var actorEmail = SeedingHelpers.UniqueEmail("self-demote-actor");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            actorClient,
            actorEmail,
            Password,
            cancellationToken);
        var club = await SeedingHelpers.CreateClubAsync(actorClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(actorClient, cancellationToken);

        var secondAdministratorEmail = SeedingHelpers.UniqueEmail("self-demote-second-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            secondAdministratorClient,
            secondAdministratorEmail,
            Password,
            cancellationToken);
        await SeedingHelpers.UpdateUserAsync(
            fixture.AppHost,
            secondAdministratorEmail,
            club.ClubId,
            cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(secondAdministratorClient, cancellationToken);

        await using var db = fixture.AppHost.CreateAdminContext();
        var normalizedActorEmail = actorEmail.ToUpperInvariant();
        var normalizedSecondAdministratorEmail = secondAdministratorEmail.ToUpperInvariant();
        var actorUserId = await db.Users
            .Where(user => user.NormalizedEmail == normalizedActorEmail)
            .Select(user => user.Id)
            .SingleAsync(cancellationToken);
        var secondAdministratorUserId = await db.Users
            .Where(user => user.NormalizedEmail == normalizedSecondAdministratorEmail)
            .Select(user => user.Id)
            .SingleAsync(cancellationToken);

        using var promotion = await actorClient.PostAsync(
            ClubEndpoints.PromoteMemberUrl(secondAdministratorUserId),
            content: null,
            cancellationToken);
        promotion.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        return new SelfDemotionSeed(actorEmail, actorUserId, club);
    }

    /// <summary>Identifies the actor and club for the static-SSR self-demotion browser scenario.</summary>
    /// <param name="ActorEmail">The actor's login email.</param>
    /// <param name="ActorUserId">The actor's identity user id.</param>
    /// <param name="Club">The club containing both administrators.</param>
    private sealed record SelfDemotionSeed(string ActorEmail, long ActorUserId, ClubDto Club);

    /// <summary>
    /// Registers a unique user through the HTTP Identity helper without using UI registration as test setup.
    /// </summary>
    /// <param name="emailPrefix">The stable prefix for the generated unique address.</param>
    /// <returns>The registered user's unique email address.</returns>
    private async Task<string> RegisterUserAsync(string emailPrefix)
    {
        var email = SeedingHelpers.UniqueEmail(emailPrefix);
        using var client = fixture.AppHost.CreateNovaHttpClient();
        await IdentityHttpClientHelper.RegisterUserAsync(
            client,
            email,
            Password,
            TestContext.Current.CancellationToken);
        return email;
    }
}
