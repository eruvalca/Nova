using Nova.Integration.Tests.Http;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Browser-level acceptance for the redesigned account-management surfaces. These scenarios use
/// real Identity registration, cookie redirects, static-SSR posts, and the genuine authenticator
/// enrollment flow so visual changes cannot silently replace account behavior.
/// </summary>
/// <param name="fixture">The shared Aspire-hosted browser suite fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class AccountManageBrowserTests(BrowserSuiteFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies that the profile phone number saves through the static-SSR form and persists after
    /// a full navigation/reload.
    /// </summary>
    [Fact]
    public async Task ProfilePhone_Update_PersistsAfterReload()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = await AccountManageBrowserHelpers.SeedPhotoCompleteUserAsync(
            fixture,
            "manage-phone",
            Password,
            cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(email, Password);
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/Account/Manage").ToString());
        const string phone = "+1 512 555 0198";
        await page.GetByLabel("Phone number").FillAsync(phone);
        await page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Your profile has been updated");
        await page.ReloadAsync();
        await Expect(page.GetByLabel("Phone number")).ToHaveValueAsync(phone);
    }

    /// <summary>
    /// Verifies the complete password-change journey: the new password is accepted, logout clears
    /// the session, and the new password signs in successfully.
    /// </summary>
    [Fact]
    public async Task PasswordChange_SignOutAndSignInWithNewPassword()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = await AccountManageBrowserHelpers.SeedPhotoCompleteUserAsync(
            fixture,
            "manage-password",
            Password,
            cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(email, Password);
        var page = context.Pages[0];
        const string newPassword = "Changed#Passw0rd!";

        await page.GotoAsync(new Uri(fixture.BaseUri, "/Account/Manage/ChangePassword").ToString());
        await page.GetByLabel("Old password").FillAsync(Password);
        await page.GetByLabel("New password").FillAsync(newPassword);
        await page.GetByLabel("Confirm password").FillAsync(newPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Update password", Exact = true }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Your password has been changed");
        await page.GetByRole(AriaRole.Button, new() { Name = "Logout", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(
            url => url.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase),
            new() { WaitUntil = WaitUntilState.Commit });

        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Password").FillAsync(newPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Log in", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(
            url => !url.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase),
            new() { WaitUntil = WaitUntilState.Commit });
        // Identity returns to the original manage route supplied by the logout form. A non-login
        // URL plus the authenticated Logout control proves the new password was accepted.
        new Uri(page.Url).AbsolutePath.ShouldBe("/Account/Manage/ChangePassword");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Logout", Exact = true })).ToBeVisibleAsync();
    }

    /// <summary>
    /// Verifies that reachable manage destinations render in the working hall and activate the
    /// matching directory panel at both desktop and narrow viewports.
    /// </summary>
    [Fact]
    public async Task ManageRoutes_NarrowViewport_RenderInsideWorkingHallWithActivePanel()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = await AccountManageBrowserHelpers.SeedPhotoCompleteUserAsync(
            fixture,
            "manage-routes",
            Password,
            cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(email, Password);
        var page = context.Pages[0];

        var routes = new[]
        {
            (Path: "/Account/Manage", Panel: "Profile", Heading: "Profile"),
            (Path: "/Account/Manage/Email", Panel: "Email", Heading: "Manage email"),
            (Path: "/Account/Manage/ChangePassword", Panel: "Password", Heading: "Change password"),
            (Path: "/Account/Manage/TwoFactorAuthentication", Panel: "Two-factor authentication", Heading: "Two-factor authentication (2FA)"),
            (Path: "/Account/Manage/Passkeys", Panel: "Passkeys", Heading: "Manage your passkeys"),
            (Path: "/Account/Manage/PersonalData", Panel: "Personal data", Heading: "Personal data"),
            (Path: "/Account/Manage/ProfilePhoto", Panel: "Profile photo", Heading: "Profile photo")
        };

        foreach (var viewportWidth in new[] { 1280, 575 })
        {
            await page.SetViewportSizeAsync(viewportWidth, 800);
            foreach (var route in routes)
            {
                await page.GotoAsync(new Uri(fixture.BaseUri, route.Path).ToString());
                await Expect(page.GetByRole(AriaRole.Heading, new() { Name = route.Heading, Exact = true }))
                    .ToBeVisibleAsync(new() { Timeout = 30_000 });
                await Expect(page.Locator(".account-working-hall")).ToBeVisibleAsync();

                var activePanel = page
                    .GetByRole(AriaRole.Navigation, new() { Name = "Account areas" })
                    .GetByRole(AriaRole.Link, new() { Name = route.Panel, Exact = true });
                await Expect(activePanel).ToHaveAttributeAsync("aria-current", "page");

                if (route.Path == "/Account/Manage/Email")
                {
                    var emailFieldBounds = await page.Locator("#email").BoundingBoxAsync();
                    var verificationButtonBounds = await page
                        .GetByRole(AriaRole.Button, new() { Name = "Send verification email", Exact = true })
                        .BoundingBoxAsync();
                    emailFieldBounds.ShouldNotBeNull();
                    verificationButtonBounds.ShouldNotBeNull();

                    var verificationGap = (double)verificationButtonBounds!.Y
                        - ((double)emailFieldBounds!.Y + emailFieldBounds.Height);
                    verificationGap.ShouldBeGreaterThanOrEqualTo(7.5,
                        "the verification action needs breathing room below the current email field");
                }

                if (route.Path == "/Account/Manage/Passkeys")
                {
                    var boards = page.Locator(".manage-passkeys > .manage-board");
                    await Expect(boards).ToHaveCountAsync(2);
                    var registeredBounds = await boards.Nth(0).BoundingBoxAsync();
                    var addBounds = await boards.Nth(1).BoundingBoxAsync();
                    registeredBounds.ShouldNotBeNull();
                    addBounds.ShouldNotBeNull();

                    var boardGap = (double)addBounds!.Y
                        - ((double)registeredBounds!.Y + registeredBounds.Height);
                    boardGap.ShouldBeGreaterThanOrEqualTo(15.5,
                        "stacked passkey boards need a clear section break");
                }

                if (viewportWidth <= 575)
                {
                    var buttonHeights = await page.Locator(".account-working-hall .btn")
                        .EvaluateAllAsync<double[]>("elements => elements"
                            + ".map(element => element.getBoundingClientRect().height)"
                            + ".filter(height => height > 0)");
                    // Interactive islands can replace their action markup after the static
                    // snapshot; assert every button that is present and laid out in this pass.
                    foreach (var buttonHeight in buttonHeights)
                    {
                        buttonHeight.ShouldBeGreaterThanOrEqualTo(43.5,
                            "account actions must keep a 2.75rem touch target on phones");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Verifies that a genuine authenticator enrollment produces the expected protected 2FA
    /// management state and recovery-code action surface.
    /// </summary>
    [Fact]
    public async Task TwoFactor_GenuineEnrollment_ShowsRecoveryManagementState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = await AccountManageBrowserHelpers.SeedPhotoCompleteUserAsync(
            fixture,
            "manage-2fa",
            Password,
            cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(email, Password);
        var page = context.Pages[0];

        await AccountManageBrowserHelpers.EnableTwoFactorAsync(page, fixture.BaseUri);
        await page.GotoAsync(new Uri(fixture.BaseUri, "/Account/Manage/TwoFactorAuthentication").ToString());

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Protection status", Exact = true }))
            .ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Disable 2FA", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Reset recovery codes", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Reset authenticator app", Exact = true })).ToBeVisibleAsync();
    }

    /// <summary>
    /// Verifies that a photo-less user can complete the onboarding crop/save flow and leave the
    /// required photo gate after the cookie-refresh redirect.
    /// </summary>
    [Fact]
    public async Task ProfilePhoto_OnboardingUploadAndSave_LeavesPhotoGate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = await AccountManageBrowserHelpers.SeedPhotoLessUserAsync(
            fixture,
            "manage-onboarding-photo",
            Password,
            cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(email, Password);
        var page = context.Pages[0];

        await page.WaitForURLAsync(
            url => url.Contains("/Account/ProfilePhoto", StringComparison.OrdinalIgnoreCase),
            new() { WaitUntil = WaitUntilState.Commit });
        await page.GotoAsync(new Uri(fixture.BaseUri, "/Account/ProfilePhoto?ReturnUrl=/dashboard").ToString());
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page);

        var photoPath = await WriteTempPhotoAsync();
        try
        {
            var input = page.GetByLabel("Choose a photo");
            var cropper = page.Locator("div.profile-photo-cropper-frame");
            for (var attempt = 0; attempt < BrowserRetryPolicy.MaxAttempts; attempt++)
            {
                if (await cropper.IsVisibleAsync())
                {
                    break;
                }

                try
                {
                    await input.SetInputFilesAsync(photoPath);
                }
                catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
                {
                    // The interactive island can replace the input during the upload; retrying the
                    // same file is safe and re-issues the change event.
                }

                if (await cropper.IsVisibleAsync())
                {
                    break;
                }

                await page.WaitForTimeoutAsync(BrowserRetryPolicy.Delay);
            }

            await Expect(cropper).ToBeVisibleAsync(new() { Timeout = 30_000 });
            var savePhoto = page.GetByRole(AriaRole.Button, new() { Name = "Save photo", Exact = true });
            await Expect(savePhoto).ToBeEnabledAsync(new() { Timeout = 30_000 });
            await InteractionHelpers.ClickUntilAsync(
                page,
                savePhoto,
                () => Task.FromResult(!page.Url.Contains("/Account/ProfilePhoto", StringComparison.OrdinalIgnoreCase)));

            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Welcome to Nova", Exact = true }))
                .ToBeVisibleAsync(new() { Timeout = 30_000 });
            new Uri(page.Url).AbsolutePath.ShouldBe("/Clubs/Onboarding");
        }
        finally
        {
            File.Delete(photoPath);
        }
    }

    /// <summary>
    /// Writes a small valid JPEG for the onboarding file input.
    /// </summary>
    /// <returns>The temporary image path.</returns>
    private static async Task<string> WriteTempPhotoAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nova-manage-onboarding-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(path, SeedingHelpers.CreateJpegBytes());
        return path;
    }
}
