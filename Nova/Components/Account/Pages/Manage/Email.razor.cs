#pragma warning disable CA1515 // Razor generates a public component partial class.
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Nova.Entities;

namespace Nova.Components.Account.Pages.Manage;

/// <summary>
/// Handles email management including email changes and verification link sending.
/// </summary>
public partial class Email(
    UserManager<NovaUserEntity> userManager,
    IEmailSender<NovaUserEntity> emailSender,
    NavigationManager navigationManager,
    IdentityRedirectManager redirectManager)
{
    /// <summary>
    /// Stores the status message to display after form submission.
    /// </summary>
    private string? _message;

    /// <summary>
    /// Stores the current user entity.
    /// </summary>
    private NovaUserEntity? _user;

    /// <summary>
    /// Stores the current email address of the user.
    /// </summary>
    private string? _email;

    /// <summary>
    /// Indicates whether the current email address is confirmed.
    /// </summary>
    private bool _isEmailConfirmed;

    /// <summary>
    /// Gets the cascading HTTP context from the parent component.
    /// </summary>
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <summary>
    /// Gets or sets the email change form input model supplied from the form post.
    /// </summary>
    [SupplyParameterFromForm(FormName = "change-email")]
    private InputModel Input { get; set; } = default!;

    /// <summary>
    /// Initializes the component, loading the current user's email and confirmation status.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        _user = await userManager.GetUserAsync(HttpContext.User);
        if (_user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        _email = await userManager.GetEmailAsync(_user);
        _isEmailConfirmed = await userManager.IsEmailConfirmedAsync(_user);

        Input.NewEmail ??= _email;
    }

    /// <summary>
    /// Handles the form submission to change the user's email address.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnValidSubmitAsync()
    {
        if (Input.NewEmail is null || string.Equals(Input.NewEmail, _email, StringComparison.Ordinal))
        {
            _message = "Your email is unchanged.";
            return;
        }

        if (_user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        var userId = await userManager.GetUserIdAsync(_user);
        var code = await userManager.GenerateChangeEmailTokenAsync(_user, Input.NewEmail);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        var callbackUrl = navigationManager.GetUriWithQueryParameters(
            navigationManager.ToAbsoluteUri("Account/ConfirmEmailChange").AbsoluteUri,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["userId"] = userId, ["email"] = Input.NewEmail, ["code"] = code });

        await emailSender.SendConfirmationLinkAsync(_user, Input.NewEmail, HtmlEncoder.Default.Encode(callbackUrl));

        _message = "Confirmation link to change email sent. Please check your email.";
    }

    /// <summary>
    /// Handles the form submission to send an email verification link.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnSendEmailVerificationAsync()
    {
        if (_email is null)
        {
            return;
        }

        if (_user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        var userId = await userManager.GetUserIdAsync(_user);
        var code = await userManager.GenerateEmailConfirmationTokenAsync(_user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        var callbackUrl = navigationManager.GetUriWithQueryParameters(
            navigationManager.ToAbsoluteUri("Account/ConfirmEmail").AbsoluteUri,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["userId"] = userId, ["code"] = code });

        await emailSender.SendConfirmationLinkAsync(_user, _email, HtmlEncoder.Default.Encode(callbackUrl));

        _message = "Verification email sent. Please check your email.";
    }

    /// <summary>
    /// Form input model for changing email, including the new email field.
    /// </summary>
    private sealed class InputModel
    {
        /// <summary>
        /// Gets or sets the new email address.
        /// </summary>
        [Required]
        [EmailAddress]
        [Display(Name = "New email")]
        public string? NewEmail { get; set; }
    }
}
