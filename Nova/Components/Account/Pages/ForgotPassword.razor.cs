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
/// Handles password reset requests by sending a password reset email to the user.
/// </summary>
/// <param name="userManager">The user manager for Identity operations.</param>
/// <param name="emailSender">The email sender service for Identity operations.</param>
/// <param name="navigationManager">The navigation manager for page navigation.</param>
/// <param name="redirectManager">The redirect manager for Identity page redirects.</param>
public partial class ForgotPassword(
    UserManager<NovaUserEntity> userManager,
    IEmailSender<NovaUserEntity> emailSender,
    NavigationManager navigationManager,
    IdentityRedirectManager redirectManager)
{
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
    /// Handles the form submission to send a password reset email if the email address is valid.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnValidSubmitAsync()
    {
        var user = await userManager.FindByEmailAsync(Input.Email);
        if (user is null || !(await userManager.IsEmailConfirmedAsync(user)))
        {
            // Don't reveal that the user does not exist or is not confirmed
            redirectManager.RedirectTo("Account/ForgotPasswordConfirmation");
            return;
        }

        // For more information on how to enable account confirmation and password reset please
        // visit https://go.microsoft.com/fwlink/?LinkID=532713
        var code = await userManager.GeneratePasswordResetTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        var callbackUrl = navigationManager.GetUriWithQueryParameters(
            navigationManager.ToAbsoluteUri("Account/ResetPassword").AbsoluteUri,
            new Dictionary<string, object?> { ["code"] = code });

        await emailSender.SendPasswordResetLinkAsync(user, Input.Email, HtmlEncoder.Default.Encode(callbackUrl));

        redirectManager.RedirectTo("Account/ForgotPasswordConfirmation");
    }

    /// <summary>
    /// Input model for the forgot password form containing the email address.
    /// </summary>
    private sealed class InputModel
    {
        /// <summary>
        /// Gets or sets the email address of the account for which to reset the password.
        /// </summary>
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";
    }
}
