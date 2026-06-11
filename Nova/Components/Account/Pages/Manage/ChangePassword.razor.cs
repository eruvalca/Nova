using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Nova.Data;
using Nova.Entities;

namespace Nova.Components.Account.Pages.Manage;

/// <summary>
/// Handles password changes for authenticated users, validating the old password and updating to the new one.
/// </summary>
public partial class ChangePassword(
    UserManager<NovaUserEntity> userManager,
    SignInManager<NovaUserEntity> signInManager,
    IdentityRedirectManager redirectManager,
    ILogger<ChangePassword> logger)
{
    /// <summary>
    /// Stores the status message to display after form submission.
    /// </summary>
    private string? message;

    /// <summary>
    /// Stores the current user entity.
    /// </summary>
    private NovaUserEntity? user;

    /// <summary>
    /// Indicates whether the user has a password set.
    /// </summary>
    private bool hasPassword;

    /// <summary>
    /// Gets the cascading HTTP context from the parent component.
    /// </summary>
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <summary>
    /// Gets or sets the password change form input model supplied from the form post.
    /// </summary>
    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    /// <summary>
    /// Initializes the component, loading the current user and checking if they have a password set.
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

        hasPassword = await userManager.HasPasswordAsync(user);
        if (!hasPassword)
        {
            redirectManager.RedirectTo("Account/Manage/SetPassword");
        }
    }

    /// <summary>
    /// Handles the form submission to change the user's password.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnValidSubmitAsync()
    {
        if (user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        var changePasswordResult = await userManager.ChangePasswordAsync(user, Input.OldPassword, Input.NewPassword);
        if (!changePasswordResult.Succeeded)
        {
            message = $"Error: {string.Join(",", changePasswordResult.Errors.Select(error => error.Description))}";
            return;
        }

        await signInManager.RefreshSignInAsync(user);
        LogPasswordChanged();

        redirectManager.RedirectToCurrentPageWithStatus("Your password has been changed", HttpContext);
    }

    /// <summary>
    /// Logs that a user successfully changed their password.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "User changed their password successfully.")]
    private partial void LogPasswordChanged();

    /// <summary>
    /// Form input model for changing password, including old and new password fields.
    /// </summary>
    private sealed class InputModel
    {
        /// <summary>
        /// Gets or sets the user's current password.
        /// </summary>
        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Current password")]
        public string OldPassword { get; set; } = "";

        /// <summary>
        /// Gets or sets the user's new password.
        /// </summary>
        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string NewPassword { get; set; } = "";

        /// <summary>
        /// Gets or sets the confirmation of the new password.
        /// </summary>
        [DataType(DataType.Password)]
        [Display(Name = "Confirm new password")]
        [Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }
}
