using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Nova.Data;
using Nova.Entities;

namespace Nova.Components.Account.Pages.Manage;

/// <summary>
/// Handles the deletion of a user's personal data and account, requiring password confirmation.
/// </summary>
public partial class DeletePersonalData(
    UserManager<NovaUserEntity> userManager,
    SignInManager<NovaUserEntity> signInManager,
    IdentityRedirectManager redirectManager,
    ILogger<DeletePersonalData> logger)
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
    /// Indicates whether the user has a password that must be confirmed for deletion.
    /// </summary>
    private bool requirePassword;

    /// <summary>
    /// Gets the cascading HTTP context from the parent component.
    /// </summary>
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <summary>
    /// Gets or sets the deletion form input model supplied from the form post.
    /// </summary>
    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    /// <summary>
    /// Initializes the component, loading the current user and checking if a password is required.
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
        requirePassword = await userManager.HasPasswordAsync(user);
    }

    /// <summary>
    /// Handles the form submission to delete the user's account and personal data.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnValidSubmitAsync()
    {
        if (user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        if (requirePassword && !await userManager.CheckPasswordAsync(user, Input.Password))
        {
            message = "Error: Incorrect password.";
            return;
        }

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Unexpected error occurred deleting user.");
        }

        await signInManager.SignOutAsync();

        var userId = await userManager.GetUserIdAsync(user);
        LogUserDeleted(userId);

        redirectManager.RedirectToCurrentPage();
    }

    /// <summary>
    /// Logs that a user deleted themselves.
    /// </summary>
    /// <param name="userId">The ID of the user who deleted themselves.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "User with ID '{UserId}' deleted themselves.")]
    private partial void LogUserDeleted(string userId);

    /// <summary>
    /// Form input model for account deletion, including optional password confirmation.
    /// </summary>
    private sealed class InputModel
    {
        /// <summary>
        /// Gets or sets the user's password for confirmation purposes.
        /// </summary>
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";
    }
}
