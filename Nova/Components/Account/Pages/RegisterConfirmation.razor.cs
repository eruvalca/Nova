using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Nova.Data;
using Nova.Entities;

namespace Nova.Components.Account.Pages;

/// <summary>
/// Displays registration confirmation information and generates an email confirmation link for the user.
/// </summary>
/// <param name="userManager">The user manager for Identity operations.</param>
/// <param name="emailSender">The email sender service for Identity operations.</param>
/// <param name="navigationManager">The navigation manager for page navigation.</param>
/// <param name="redirectManager">The redirect manager for Identity page redirects.</param>
public partial class RegisterConfirmation(
    UserManager<NovaUserEntity> userManager,
    IEmailSender<NovaUserEntity> emailSender,
    NavigationManager navigationManager,
    IdentityRedirectManager redirectManager)
{
    /// <summary>
    /// Stores the email confirmation link for display when no real email sender is configured.
    /// </summary>
    private string? emailConfirmationLink;

    /// <summary>
    /// Stores the status message to display to the user.
    /// </summary>
    private string? statusMessage;

    /// <summary>
    /// Gets or sets the cascading HTTP context from the parent component.
    /// </summary>
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <summary>
    /// Gets or sets the email address from the query string.
    /// </summary>
    [SupplyParameterFromQuery]
    private string? Email { get; set; }

    /// <summary>
    /// Gets or sets the return URL from the query string.
    /// </summary>
    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    /// <summary>
    /// Initializes the component by retrieving the user and generating an email confirmation link
    /// if a no-op email sender is configured (for development purposes).
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task OnInitializedAsync()
    {
        if (Email is null)
        {
            redirectManager.RedirectTo("");
            return;
        }

        var user = await userManager.FindByEmailAsync(Email);
        if (user is null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            statusMessage = "Error finding user for unspecified email";
        }
        else if (emailSender is IdentityNoOpEmailSender)
        {
            // Once you add a real email sender, you should remove this code that lets you confirm the account
            var userId = await userManager.GetUserIdAsync(user);
            var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            emailConfirmationLink = navigationManager.GetUriWithQueryParameters(
                navigationManager.ToAbsoluteUri("Account/ConfirmEmail").AbsoluteUri,
                new Dictionary<string, object?> { ["userId"] = userId, ["code"] = code, ["returnUrl"] = ReturnUrl });
        }
    }
}
