using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Nova.Entities;

namespace Nova.Components.Account.Pages;

/// <summary>
/// Handles email confirmation for user accounts. Validates the provided userId and confirmation code,
/// and updates the user's email confirmed status.
/// </summary>
/// <param name="userManager">The user manager for Identity operations.</param>
/// <param name="redirectManager">The redirect manager for Identity page redirects.</param>
public partial class ConfirmEmail(
    UserManager<NovaUserEntity> userManager,
    IdentityRedirectManager redirectManager)
{
    /// <summary>
    /// Stores the status message to display to the user regarding email confirmation success or failure.
    /// </summary>
    private string? statusMessage;

    /// <summary>
    /// Gets or sets the cascading HTTP context from the parent component.
    /// </summary>
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <summary>
    /// Gets or sets the user ID from the query string.
    /// </summary>
    [SupplyParameterFromQuery]
    private string? UserId { get; set; }

    /// <summary>
    /// Gets or sets the confirmation code from the query string.
    /// </summary>
    [SupplyParameterFromQuery]
    private string? Code { get; set; }

    /// <summary>
    /// Initializes the component by validating the userId and code parameters,
    /// confirming the user's email if both are valid.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task OnInitializedAsync()
    {
        if (UserId is null || Code is null)
        {
            redirectManager.RedirectTo("");
            return;
        }

        var user = await userManager.FindByIdAsync(UserId);
        if (user is null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            statusMessage = $"Error loading user with ID {UserId}";
        }
        else
        {
            var code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Code));
            var result = await userManager.ConfirmEmailAsync(user, code);
            statusMessage = result.Succeeded ? "Thank you for confirming your email." : "Error confirming your email.";
        }
    }
}
