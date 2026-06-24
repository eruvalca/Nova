using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Nova.Entities;

namespace Nova.Components.Account.Pages.Manage;

/// <summary>
/// Handles resetting the authenticator key for two-factor authentication.
/// </summary>
public partial class ResetAuthenticator(
    UserManager<NovaUserEntity> userManager,
    SignInManager<NovaUserEntity> signInManager,
    IdentityRedirectManager redirectManager,
    ILogger<ResetAuthenticator> logger)
{
    /// <summary>
    /// Gets the cascading HTTP context from the parent component.
    /// </summary>
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <summary>
    /// Handles the form submission to reset the authenticator key.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnSubmitAsync()
    {
        var user = await userManager.GetUserAsync(HttpContext.User);
        if (user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        await userManager.SetTwoFactorEnabledAsync(user, false);
        await userManager.ResetAuthenticatorKeyAsync(user);
        var userId = await userManager.GetUserIdAsync(user);
        LogAuthenticatorKeyReset(userId);

        await signInManager.RefreshSignInAsync(user);

        redirectManager.RedirectToWithStatus(
            "Account/Manage/EnableAuthenticator",
            "Your authenticator app key has been reset, you will need to configure your authenticator app using the new key.",
            HttpContext);
    }

    /// <summary>
    /// Logs that a user has reset their authenticator key.
    /// </summary>
    /// <param name="userId">The unique identifier of the user.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "User with ID '{UserId}' has reset their authentication app key.")]
    private partial void LogAuthenticatorKeyReset(string? userId);
}
