using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Nova.Data;
using Nova.Entities;

namespace Nova.Components.Account.Pages;

/// <summary>
/// Handles two-factor authentication login flow using authenticator codes.
/// </summary>
public partial class LoginWith2fa(
    SignInManager<NovaUserEntity> signInManager,
    UserManager<NovaUserEntity> userManager,
    IdentityRedirectManager redirectManager,
    ILogger<LoginWith2fa> logger)
{
    /// <summary>
    /// Stores the error message to display to the user.
    /// </summary>
    private string? message;

    /// <summary>
    /// Caches the currently authenticating user entity.
    /// </summary>
    private NovaUserEntity user = default!;

    /// <summary>
    /// Gets or sets the 2FA form input model supplied from the form post.
    /// </summary>
    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    /// <summary>
    /// Gets or sets the return URL from the query string.
    /// </summary>
    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the login should be remembered.
    /// </summary>
    [SupplyParameterFromQuery]
    private bool RememberMe { get; set; }

    /// <summary>
    /// Ensures the user has gone through the username & password authentication step first.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        // Ensure the user has gone through the username & password screen first
        user = await signInManager.GetTwoFactorAuthenticationUserAsync() ??
            throw new InvalidOperationException("Unable to load two-factor authentication user.");
    }

    /// <summary>
    /// Validates the authenticator code and completes 2FA sign-in.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnValidSubmitAsync()
    {
        var authenticatorCode = Input.TwoFactorCode!.Replace(" ", string.Empty).Replace("-", string.Empty);
        var result = await signInManager.TwoFactorAuthenticatorSignInAsync(authenticatorCode, RememberMe,
Input.RememberMachine);
        var userId = await userManager.GetUserIdAsync(user);

        if (result.Succeeded)
        {
            LogUserLoggedInWith2fa(userId);
            redirectManager.RedirectTo(ReturnUrl);
        }
        else if (result.IsLockedOut)
        {
            LogUserLockedOut(userId);
            redirectManager.RedirectTo("Account/Lockout");
        }
        else
        {
            LogInvalidAuthenticatorCodeEntered(userId);
            message = "Error: Invalid authenticator code.";
        }
    }

    /// <summary>
    /// Logs that a user successfully logged in with two-factor authentication.
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "User with ID '{UserId}' logged in with 2fa.")]
    private partial void LogUserLoggedInWith2fa(string userId);

    /// <summary>
    /// Logs that a user account has been locked out after 2FA attempts.
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "User with ID '{UserId}' account locked out.")]
    private partial void LogUserLockedOut(string userId);

    /// <summary>
    /// Logs that an invalid authenticator code was entered.
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid authenticator code entered for user with ID '{UserId}'.")]
    private partial void LogInvalidAuthenticatorCodeEntered(string userId);

    /// <summary>
    /// Form input model for two-factor authentication, containing the authenticator code and remember machine flag.
    /// </summary>
    private sealed class InputModel
    {
        /// <summary>
        /// Gets or sets the authenticator code from the user's authentication device.
        /// </summary>
        [Required]
        [StringLength(7, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Text)]
        [Display(Name = "Authenticator code")]
        public string? TwoFactorCode { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to remember this machine for future logins.
        /// </summary>
        [Display(Name = "Remember this machine")]
        public bool RememberMachine { get; set; }
    }
}
