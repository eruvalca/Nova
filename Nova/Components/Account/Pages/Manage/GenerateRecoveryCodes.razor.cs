using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Nova.Entities;

namespace Nova.Components.Account.Pages.Manage;

/// <summary>
/// Handles generating new two-factor authentication (2FA) recovery codes for the current user.
/// </summary>
public partial class GenerateRecoveryCodes(
    UserManager<NovaUserEntity> userManager,
    IdentityRedirectManager redirectManager,
    ILogger<GenerateRecoveryCodes> logger)
{
    /// <summary>
    /// Stores the status message to display after recovery codes are generated.
    /// </summary>
    private string? message;

    /// <summary>
    /// Stores the current user entity.
    /// </summary>
    private NovaUserEntity? user;

    /// <summary>
    /// Stores the generated recovery codes for display.
    /// </summary>
    private IEnumerable<string>? recoveryCodes;

    /// <summary>
    /// Gets the cascading HTTP context from the parent component.
    /// </summary>
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <summary>
    /// Initializes the component and validates that 2FA is currently enabled.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task OnInitializedAsync()
    {
        user = await userManager.GetUserAsync(HttpContext.User);
        if (user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        var isTwoFactorEnabled = await userManager.GetTwoFactorEnabledAsync(user);
        if (!isTwoFactorEnabled)
        {
            throw new InvalidOperationException("Cannot generate recovery codes for user because they do not have 2FA enabled.");
        }
    }

    /// <summary>
    /// Handles the form submission to generate new recovery codes.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnSubmitAsync()
    {
        if (user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        var userId = await userManager.GetUserIdAsync(user);
        recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        message = "You have generated new recovery codes.";

        LogUserGeneratedRecoveryCodes(userId);
    }

    /// <summary>
    /// Logs that a user has generated new 2FA recovery codes.
    /// </summary>
    /// <param name="userId">The unique identifier of the user.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "User with ID '{UserId}' has generated new 2FA recovery codes.")]
    private partial void LogUserGeneratedRecoveryCodes(string? userId);
}
