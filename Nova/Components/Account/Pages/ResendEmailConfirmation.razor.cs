using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Nova.Data;
using Nova.Entities;

namespace Nova.Components.Account.Pages;

/// <summary>
/// Handles resending email confirmation links to users who need to confirm their email address.
/// </summary>
/// <param name="userManager">The user manager for Identity operations.</param>
/// <param name="emailSender">The email sender service for Identity operations.</param>
/// <param name="navigationManager">The navigation manager for page navigation.</param>
public partial class ResendEmailConfirmation(
    UserManager<NovaUserEntity> userManager,
    IEmailSender<NovaUserEntity> emailSender,
    NavigationManager navigationManager)
{
    /// <summary>
    /// Stores the message to display to the user regarding resend confirmation status.
    /// </summary>
    private string? message;

    /// <summary>
    /// Gets or sets the form input data containing the user's email address.
    /// </summary>
    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    /// <summary>
    /// Initializes the component, ensuring the Input model is instantiated.
    /// </summary>
    protected override void OnInitialized()
    {
        Input ??= new();
    }

    /// <summary>
    /// Handles the form submission to resend the email confirmation link if the email address is valid.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnValidSubmitAsync()
    {
        var user = await userManager.FindByEmailAsync(Input.Email!);
        if (user is null)
        {
            message = "Verification email sent. Please check your email.";
            return;
        }

        var userId = await userManager.GetUserIdAsync(user);
        var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        var callbackUrl = navigationManager.GetUriWithQueryParameters(
            navigationManager.ToAbsoluteUri("Account/ConfirmEmail").AbsoluteUri,
            new Dictionary<string, object?> { ["userId"] = userId, ["code"] = code });
        await emailSender.SendConfirmationLinkAsync(user, Input.Email, HtmlEncoder.Default.Encode(callbackUrl));

        message = "Verification email sent. Please check your email.";
    }

    /// <summary>
    /// Input model for the resend email confirmation form containing the email address.
    /// </summary>
    private sealed class InputModel
    {
        /// <summary>
        /// Gets or sets the email address for which to resend the confirmation link.
        /// </summary>
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";
    }
}
