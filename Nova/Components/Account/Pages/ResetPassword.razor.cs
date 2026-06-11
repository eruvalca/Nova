using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Nova.Data;
using Nova.Entities;

namespace Nova.Components.Account.Pages;

/// <summary>
/// Handles password reset for user accounts. Validates the reset code and new password,
/// then updates the user's password.
/// </summary>
/// <param name="userManager">The user manager for Identity operations.</param>
/// <param name="redirectManager">The redirect manager for Identity page redirects.</param>
public partial class ResetPassword(
    UserManager<NovaUserEntity> userManager,
    IdentityRedirectManager redirectManager)
{
    /// <summary>
    /// Stores identity errors returned from the password reset operation.
    /// </summary>
    private IEnumerable<IdentityError>? identityErrors;

    /// <summary>
    /// Gets or sets the form input data containing the email, password, and code.
    /// </summary>
    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    /// <summary>
    /// Gets or sets the password reset code from the query string.
    /// </summary>
    [SupplyParameterFromQuery]
    private string? Code { get; set; }

    /// <summary>
    /// Gets the message to display to the user containing error descriptions if password reset failed.
    /// </summary>
    private string? Message => identityErrors is null ? null : $"Error: {string.Join(", ", identityErrors.Select(error =>
        error.Description))}";

    /// <summary>
    /// Initializes the component by ensuring the Input model is instantiated and decoding the reset code.
    /// Redirects to an error page if no code is provided.
    /// </summary>
    protected override void OnInitialized()
    {
        Input ??= new();

        if (Code is null)
        {
            redirectManager.RedirectTo("Account/InvalidPasswordReset");
            return;
        }

        Input.Code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Code));
    }

    /// <summary>
    /// Handles the form submission to reset the user's password.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnValidSubmitAsync()
    {
        var user = await userManager.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            // Don't reveal that the user does not exist
            redirectManager.RedirectTo("Account/ResetPasswordConfirmation");
            return;
        }

        var result = await userManager.ResetPasswordAsync(user, Input.Code, Input.Password);
        if (result.Succeeded)
        {
            redirectManager.RedirectTo("Account/ResetPasswordConfirmation");
            return;
        }

        identityErrors = result.Errors;
    }

    /// <summary>
    /// Input model for the reset password form containing email, password, confirmation password, and code.
    /// </summary>
    private sealed class InputModel
    {
        /// <summary>
        /// Gets or sets the email address of the account for which to reset the password.
        /// </summary>
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        /// <summary>
        /// Gets or sets the new password.
        /// </summary>
        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        /// <summary>
        /// Gets or sets the confirmation password that must match the password field.
        /// </summary>
        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = "";

        /// <summary>
        /// Gets or sets the password reset code that was generated for the user.
        /// </summary>
        [Required]
        public string Code { get; set; } = "";
    }
}
