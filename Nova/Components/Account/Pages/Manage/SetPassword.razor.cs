using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Nova.Entities;

namespace Nova.Components.Account.Pages.Manage;

/// <summary>
/// Handles setting a password for a user who previously had only external logins.
/// </summary>
public partial class SetPassword(
    UserManager<NovaUserEntity> userManager,
    SignInManager<NovaUserEntity> signInManager,
    IdentityRedirectManager redirectManager)
{
    /// <summary>
    /// Stores the error or status message to display.
    /// </summary>
    private string? message;

    /// <summary>
    /// Stores the current user entity.
    /// </summary>
    private NovaUserEntity? user;

    /// <summary>
    /// Gets the cascading HTTP context from the parent component.
    /// </summary>
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <summary>
    /// Gets or sets the set password form input model supplied from the form post.
    /// </summary>
    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    /// <summary>
    /// Initializes the component and validates that the user does not already have a password.
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

        var hasPassword = await userManager.HasPasswordAsync(user);
        if (hasPassword)
        {
            redirectManager.RedirectTo("Account/Manage/ChangePassword");
        }
    }

    /// <summary>
    /// Handles the form submission to set a password for the user.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnValidSubmitAsync()
    {
        if (user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        var addPasswordResult = await userManager.AddPasswordAsync(user, Input.NewPassword!);
        if (!addPasswordResult.Succeeded)
        {
            message = $"Error: {string.Join(",", addPasswordResult.Errors.Select(error => error.Description))}";
            return;
        }

        await signInManager.RefreshSignInAsync(user);
        redirectManager.RedirectToCurrentPageWithStatus("Your password has been set.", HttpContext);
    }

    /// <summary>
    /// Form input model for setting a new password.
    /// </summary>
    private sealed class InputModel
    {
        /// <summary>
        /// Gets or sets the new password.
        /// </summary>
        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string? NewPassword { get; set; }

        /// <summary>
        /// Gets or sets the password confirmation value.
        /// </summary>
        [DataType(DataType.Password)]
        [Display(Name = "Confirm new password")]
        [Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
        public string? ConfirmPassword { get; set; }
    }
}
