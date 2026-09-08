#pragma warning disable CA1515 // Razor generates a public component partial class.
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Nova.Entities;
using Nova.SharedKernel.Features.Account;

namespace Nova.Components.Account.Pages.Manage;

/// <summary>
/// Handles the deletion of a user's personal data and account, requiring password confirmation.
/// </summary>
public partial class DeletePersonalData(
    UserManager<NovaUserEntity> userManager,
    SignInManager<NovaUserEntity> signInManager,
    IdentityRedirectManager redirectManager,
    ILogger<DeletePersonalData> logger,
    IAccountDeletionService accountDeletionService)
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
    /// Indicates whether the user has a password that must be confirmed for deletion.
    /// </summary>
    private bool _requirePassword;

    /// <summary>
    /// Contains the deletion preview data, including the scenario and club name if applicable.
    /// </summary>
    private AccountDeletionPreviewDto? _preview;

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

        _user = await userManager.GetUserAsync(HttpContext.User);
        if (_user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }
        _requirePassword = await userManager.HasPasswordAsync(_user);
        _preview = await accountDeletionService.GetDeletionPreviewAsync(ComponentCancellationToken);
    }

    /// <summary>
    /// Handles the form submission to delete the user's account and personal data.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnValidSubmitAsync()
    {
        if (_user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        if (_requirePassword && !await userManager.CheckPasswordAsync(_user, Input.Password))
        {
            _message = "Error: Incorrect password.";
            return;
        }

        // Capture the user ID before deletion as the user object may become invalid after deletion
        var userId = await userManager.GetUserIdAsync(_user);

        await accountDeletionService.DeleteAccountAsync(ComponentCancellationToken);

        await signInManager.SignOutAsync();

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
