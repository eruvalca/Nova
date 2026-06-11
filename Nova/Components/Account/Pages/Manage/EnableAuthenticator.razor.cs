using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Nova.Data;
using Nova.Entities;

namespace Nova.Components.Account.Pages.Manage;

/// <summary>
/// Handles enabling two-factor authentication via authenticator app, generating QR codes and recovery codes.
/// </summary>
public partial class EnableAuthenticator(
    UserManager<NovaUserEntity> userManager,
    IdentityRedirectManager redirectManager,
    UrlEncoder urlEncoder,
    ILogger<EnableAuthenticator> logger)
{
    /// <summary>
    /// The URI format string for generating TOTP authenticator URIs.
    /// </summary>
    private const string AuthenticatorUriFormat = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";

    /// <summary>
    /// Stores the status message to display after form submission.
    /// </summary>
    private string? message;

    /// <summary>
    /// Stores the current user entity.
    /// </summary>
    private NovaUserEntity? user;

    /// <summary>
    /// Stores the formatted shared key for display and manual entry.
    /// </summary>
    private string? sharedKey;

    /// <summary>
    /// Stores the authenticator URI for QR code generation.
    /// </summary>
    private string? authenticatorUri;

    /// <summary>
    /// Stores the recovery codes generated after 2FA setup.
    /// </summary>
    private IEnumerable<string>? recoveryCodes;

    /// <summary>
    /// Gets the cascading HTTP context from the parent component.
    /// </summary>
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <summary>
    /// Gets or sets the authenticator verification code input model supplied from the form post.
    /// </summary>
    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    /// <summary>
    /// Initializes the component, loading the current user and generating the authenticator QR code.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        user = await userManager.GetUserAsync(HttpContext.User);
        if (user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        await LoadSharedKeyAndQrCodeUriAsync(user);
    }

    /// <summary>
    /// Handles the form submission to verify and enable the authenticator app.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnValidSubmitAsync()
    {
        if (user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        // Strip spaces and hyphens
        var verificationCode = Input.Code.Replace(" ", string.Empty).Replace("-", string.Empty);

        var is2faTokenValid = await userManager.VerifyTwoFactorTokenAsync(
            user, userManager.Options.Tokens.AuthenticatorTokenProvider, verificationCode);

        if (!is2faTokenValid)
        {
            message = "Error: Verification code is invalid.";
            return;
        }

        await userManager.SetTwoFactorEnabledAsync(user, true);
        var userId = await userManager.GetUserIdAsync(user);
        LogAuthenticatorEnabled(userId);

        message = "Your authenticator app has been verified.";

        if (await userManager.CountRecoveryCodesAsync(user) == 0)
        {
            recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        }
        else
        {
            redirectManager.RedirectToWithStatus("Account/Manage/TwoFactorAuthentication", message, HttpContext);
        }
    }

    /// <summary>
    /// Loads the shared authenticator key and generates the QR code URI for setup.
    /// </summary>
    /// <param name="user">The user to load the authenticator key for.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async ValueTask LoadSharedKeyAndQrCodeUriAsync(NovaUserEntity user)
    {
        // Load the authenticator key & QR code URI to display on the form
        var unformattedKey = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(unformattedKey))
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
            unformattedKey = await userManager.GetAuthenticatorKeyAsync(user);
        }

        sharedKey = FormatKey(unformattedKey!);

        var email = await userManager.GetEmailAsync(user);
        authenticatorUri = GenerateQrCodeUri(email!, unformattedKey!);
    }

    /// <summary>
    /// Formats the shared authenticator key by inserting spaces every 4 characters.
    /// </summary>
    /// <param name="unformattedKey">The unformatted authenticator key.</param>
    /// <returns>The formatted authenticator key.</returns>
    private string FormatKey(string unformattedKey)
    {
        var result = new StringBuilder();
        int currentPosition = 0;
        while (currentPosition + 4 < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition, 4)).Append(' ');
            currentPosition += 4;
        }
        if (currentPosition < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition));
        }

        return result.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// Generates the QR code URI for the authenticator app configuration.
    /// </summary>
    /// <param name="email">The user's email address.</param>
    /// <param name="unformattedKey">The unformatted authenticator key.</param>
    /// <returns>The generated TOTP URI for QR code display.</returns>
    private string GenerateQrCodeUri(string email, string unformattedKey)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            AuthenticatorUriFormat,
            urlEncoder.Encode("Microsoft.AspNetCore.Identity.UI"),
            urlEncoder.Encode(email),
            unformattedKey);
    }

    /// <summary>
    /// Logs that a user has enabled 2FA with an authenticator app.
    /// </summary>
    /// <param name="userId">The ID of the user who enabled 2FA.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "User with ID '{UserId}' has enabled 2FA with an authenticator app.")]
    private partial void LogAuthenticatorEnabled(string userId);

    /// <summary>
    /// Form input model for authenticator verification code submission.
    /// </summary>
    private sealed class InputModel
    {
        /// <summary>
        /// Gets or sets the verification code from the authenticator app.
        /// </summary>
        [Required]
        [StringLength(7, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Text)]
        [Display(Name = "Verification Code")]
        public string Code { get; set; } = "";
    }
}
