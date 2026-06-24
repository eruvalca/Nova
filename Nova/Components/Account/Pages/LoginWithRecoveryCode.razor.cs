using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Nova.Entities;

namespace Nova.Components.Account.Pages;

/// <summary>
/// Handles recovery code-based authentication when the user cannot access their 2FA device.
/// </summary>
public partial class LoginWithRecoveryCode(
    SignInManager<NovaUserEntity> signInManager,
    UserManager<NovaUserEntity> userManager,
    IdentityRedirectManager redirectManager,
    ILogger<LoginWithRecoveryCode> logger)
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
    /// Gets or sets the recovery code form input model supplied from the form post.
    /// </summary>
    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    /// <summary>
    /// Gets or sets the return URL from the query string.
    /// </summary>
    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

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
    /// Validates the recovery code and completes sign-in.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnValidSubmitAsync()
    {
        var recoveryCode = Input.RecoveryCode.Replace(" ", string.Empty);

        var result = await signInManager.TwoFactorRecoveryCodeSignInAsync(recoveryCode);

        var userId = await userManager.GetUserIdAsync(user);

        if (result.Succeeded)
        {
            LogUserLoggedInWithRecoveryCode(userId);
            redirectManager.RedirectTo(ReturnUrl);
        }
        else if (result.IsLockedOut)
        {
            LogUserLockedOut();
            redirectManager.RedirectTo("Account/Lockout");
        }
        else
        {
            LogInvalidRecoveryCodeEntered(userId);
            message = "Error: Invalid recovery code entered.";
        }
    }

    /// <summary>
    /// Logs that a user successfully logged in using a recovery code.
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "User with ID '{UserId}' logged in with a recovery code.")]
    private partial void LogUserLoggedInWithRecoveryCode(string userId);

    /// <summary>
    /// Logs that a user account has been locked out.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "User account locked out.")]
    private partial void LogUserLockedOut();

    /// <summary>
    /// Logs that an invalid recovery code was entered.
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid recovery code entered for user with ID '{UserId}'")]
    private partial void LogInvalidRecoveryCodeEntered(string userId);

    /// <summary>
    /// Form input model for recovery code authentication, containing the recovery code.
    /// </summary>
    private sealed class InputModel
    {
        /// <summary>
        /// Gets or sets the recovery code used for 2FA bypass.
        /// </summary>
        [Required]
        [DataType(DataType.Text)]
        [Display(Name = "Recovery Code")]
        public string RecoveryCode { get; set; } = "";
    }
}
