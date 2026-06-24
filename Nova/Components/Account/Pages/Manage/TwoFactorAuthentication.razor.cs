using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Nova.Entities;

namespace Nova.Components.Account.Pages.Manage;

/// <summary>
/// Manages two-factor authentication settings and status for the current user.
/// </summary>
public partial class TwoFactorAuthentication(
    UserManager<NovaUserEntity> userManager,
    SignInManager<NovaUserEntity> signInManager,
    IdentityRedirectManager redirectManager)
{
    /// <summary>
    /// Indicates whether the browser can track the user.
    /// </summary>
    private bool canTrack;

    /// <summary>
    /// Indicates whether the user has an authenticator configured.
    /// </summary>
    private bool hasAuthenticator;

    /// <summary>
    /// Stores the number of remaining recovery codes.
    /// </summary>
    private int recoveryCodesLeft;

    /// <summary>
    /// Indicates whether two-factor authentication is enabled.
    /// </summary>
    private bool is2faEnabled;

    /// <summary>
    /// Indicates whether the current machine is remembered for 2FA.
    /// </summary>
    private bool isMachineRemembered;

    /// <summary>
    /// Gets the cascading HTTP context from the parent component.
    /// </summary>
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <summary>
    /// Initializes the component and loads the user's two-factor authentication settings.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task OnInitializedAsync()
    {
        var user = await userManager.GetUserAsync(HttpContext.User);
        if (user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        canTrack = HttpContext.Features.Get<ITrackingConsentFeature>()?.CanTrack ?? true;
        hasAuthenticator = await userManager.GetAuthenticatorKeyAsync(user) is not null;
        is2faEnabled = await userManager.GetTwoFactorEnabledAsync(user);
        isMachineRemembered = await signInManager.IsTwoFactorClientRememberedAsync(user);
        recoveryCodesLeft = await userManager.CountRecoveryCodesAsync(user);
    }

    /// <summary>
    /// Handles the form submission to forget the current browser for 2FA purposes.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnSubmitForgetBrowserAsync()
    {
        await signInManager.ForgetTwoFactorClientAsync();

        redirectManager.RedirectToCurrentPageWithStatus(
            "The current browser has been forgotten. When you login again from this browser you will be prompted for your 2fa code.",
            HttpContext);
    }
}
