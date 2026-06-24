using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Nova.Entities;

namespace Nova.Components.Account.Pages;

/// <summary>
/// Handles email change confirmation for user accounts. Validates the provided userId, email, and confirmation code,
/// updates the user's email and username, and refreshes the sign-in session.
/// </summary>
/// <param name="userManager">The user manager for Identity operations.</param>
/// <param name="signInManager">The sign-in manager for Identity operations.</param>
/// <param name="redirectManager">The redirect manager for Identity page redirects.</param>
public partial class ConfirmEmailChange(
    UserManager<NovaUserEntity> userManager,
    SignInManager<NovaUserEntity> signInManager,
    IdentityRedirectManager redirectManager)
{
    /// <summary>
    /// Stores the message to display to the user regarding email change success or failure.
    /// </summary>
    private string? message;

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
    /// Gets or sets the new email address from the query string.
    /// </summary>
    [SupplyParameterFromQuery]
    private string? Email { get; set; }

    /// <summary>
    /// Gets or sets the email change confirmation code from the query string.
    /// </summary>
    [SupplyParameterFromQuery]
    private string? Code { get; set; }

    /// <summary>
    /// Initializes the component by validating the userId, email, and code parameters,
    /// confirming the email change and updating the username if the user is found and valid.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task OnInitializedAsync()
    {
        if (UserId is null || Email is null || Code is null)
        {
            redirectManager.RedirectToWithStatus(
                "Account/Login", "Error: Invalid email change confirmation link.", HttpContext);
            return;
        }

        var user = await userManager.FindByIdAsync(UserId);
        if (user is null)
        {
            message = "Unable to find user with Id '{userId}'";
            return;
        }

        var code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Code));
        var result = await userManager.ChangeEmailAsync(user, Email, code);
        if (!result.Succeeded)
        {
            message = "Error changing email.";
            return;
        }

        // In our UI email and user name are one and the same, so when we update the email
        // we need to update the user name.
        var setUserNameResult = await userManager.SetUserNameAsync(user, Email);
        if (!setUserNameResult.Succeeded)
        {
            message = "Error changing user name.";
            return;
        }

        await signInManager.RefreshSignInAsync(user);
        message = "Thank you for confirming your email change.";
    }
}
