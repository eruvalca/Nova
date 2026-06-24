using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Nova.Entities;
using Nova.Shared.Security;

namespace Nova.Components.Account.Pages;

/// <summary>
/// Handles external login flow (OAuth/OpenID providers), account linking, and new account creation.
/// </summary>
public partial class ExternalLogin(
    SignInManager<NovaUserEntity> signInManager,
    UserManager<NovaUserEntity> userManager,
    IUserStore<NovaUserEntity> userStore,
    IEmailSender<NovaUserEntity> emailSender,
    NavigationManager navigationManager,
    IdentityRedirectManager redirectManager,
    ILogger<ExternalLogin> logger)
{
    /// <summary>
    /// The callback action name used by external login callback handlers.
    /// </summary>
    public const string LoginCallbackAction = "LoginCallback";

    /// <summary>
    /// Stores the error or status message to display to the user.
    /// </summary>
    private string? message;

    /// <summary>
    /// Caches the external login information for the current session.
    /// </summary>
    private ExternalLoginInfo? externalLoginInfo;

    /// <summary>
    /// Gets the cascading HTTP context from the parent component.
    /// </summary>
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <summary>
    /// Gets or sets the registration form input model supplied from the form post.
    /// </summary>
    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    /// <summary>
    /// Gets or sets the remote error from the external provider query string.
    /// </summary>
    [SupplyParameterFromQuery]
    private string? RemoteError { get; set; }

    /// <summary>
    /// Gets or sets the return URL from the query string.
    /// </summary>
    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    /// <summary>
    /// Gets or sets the action parameter from the query string (e.g., "LoginCallback").
    /// </summary>
    [SupplyParameterFromQuery]
    private string? Action { get; set; }

    /// <summary>
    /// Gets the display name of the external login provider.
    /// </summary>
    private string? ProviderDisplayName => externalLoginInfo?.ProviderDisplayName;

    /// <summary>
    /// Initializes the external login flow, validating provider info and routing to appropriate handler.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        if (RemoteError is not null)
        {
            redirectManager.RedirectToWithStatus("Account/Login", $"Error from external provider: {RemoteError}", HttpContext);
            return;
        }

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            redirectManager.RedirectToWithStatus("Account/Login", "Error loading external login information.", HttpContext);
            return;
        }

        externalLoginInfo = info;

        if (HttpMethods.IsGet(HttpContext.Request.Method))
        {
            if (Action == LoginCallbackAction)
            {
                await OnLoginCallbackAsync();
                return;
            }

            // We should only reach this page via the login callback, so redirect back to
            // the login page if we get here some other way.
            redirectManager.RedirectTo("Account/Login");
        }
    }

    /// <summary>
    /// Handles the login callback for users with existing external login records.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnLoginCallbackAsync()
    {
        if (externalLoginInfo is null)
        {
            redirectManager.RedirectToWithStatus("Account/Login", "Error loading external login information.", HttpContext);
            return;
        }

        // Sign in the user with this external login provider if the user already has a login.
        var result = await signInManager.ExternalLoginSignInAsync(
            externalLoginInfo.LoginProvider,
            externalLoginInfo.ProviderKey,
            isPersistent: false,
            bypassTwoFactor: true);

        if (result.Succeeded)
        {
            LogExternalLoginSucceeded(externalLoginInfo.LoginProvider);
            redirectManager.RedirectTo(ReturnUrl);
            return;
        }
        else if (result.IsLockedOut)
        {
            redirectManager.RedirectTo("Account/Lockout");
            return;
        }

        // If the user does not have an account, then ask the user to create an account.
        if (externalLoginInfo.Principal.HasClaim(c => c.Type == ClaimTypes.Email))
        {
            Input.Email = externalLoginInfo.Principal.FindFirstValue(ClaimTypes.Email) ?? "";
        }
    }

    /// <summary>
    /// Creates a new user account and links it to the external login provider.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnValidSubmitAsync()
    {
        if (externalLoginInfo is null)
        {
            redirectManager.RedirectToWithStatus("Account/Login", "Error loading external login information during confirmation.",
HttpContext);
            return;
        }

        var emailStore = GetEmailStore();
        var user = CreateUser();

        await userStore.SetUserNameAsync(user, Input.Email, ComponentCancellationToken);
        await emailStore.SetEmailAsync(user, Input.Email, ComponentCancellationToken);

        var result = await userManager.CreateAsync(user);
        if (result.Succeeded)
        {
            result = await userManager.AddLoginAsync(user, externalLoginInfo);
            if (result.Succeeded)
            {
                LogAccountCreatedFromExternalProvider(externalLoginInfo.LoginProvider);

                var roleResult = await userManager.AddToRoleAsync(user, Roles.StandardUser);
                if (!roleResult.Succeeded)
                {
                    LogRoleAssignmentFailed(
                        Roles.StandardUser,
                        string.Join("; ", roleResult.Errors.Select(e => e.Description)));
                }

                var userId = await userManager.GetUserIdAsync(user);
                var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

                var callbackUrl = navigationManager.GetUriWithQueryParameters(
                    navigationManager.ToAbsoluteUri("Account/ConfirmEmail").AbsoluteUri,
                    new Dictionary<string, object?> { ["userId"] = userId, ["code"] = code });
                await emailSender.SendConfirmationLinkAsync(user, Input.Email, HtmlEncoder.Default.Encode(callbackUrl));

                // If account confirmation is required, we need to show the link if we don't have a real email sender
                if (userManager.Options.SignIn.RequireConfirmedAccount)
                {
                    redirectManager.RedirectTo("Account/RegisterConfirmation", new() { ["email"] = Input.Email });
                }
                else
                {
                    await signInManager.SignInAsync(user, isPersistent: false, externalLoginInfo.LoginProvider);
                    redirectManager.RedirectTo(ReturnUrl);
                }
            }
        }
        else
        {
            message = $"Error: {string.Join(",", result.Errors.Select(error => error.Description))}";
        }
    }

    /// <summary>
    /// Creates a new user entity instance.
    /// </summary>
    /// <returns>A new user entity.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the user entity cannot be instantiated.</exception>
    private NovaUserEntity CreateUser()
    {
        try
        {
            return Activator.CreateInstance<NovaUserEntity>();
        }
        catch
        {
            throw new InvalidOperationException($"Can't create an instance of '{nameof(NovaUserEntity)}'. " +
                $"Ensure that '{nameof(NovaUserEntity)}' is not an abstract class and has a parameterless constructor");
        }
    }

    /// <summary>
    /// Gets the email store for the user manager, or throws if email is not supported.
    /// </summary>
    /// <returns>The user email store.</returns>
    /// <exception cref="NotSupportedException">Thrown when the user store does not support email operations.</exception>
    private IUserEmailStore<NovaUserEntity> GetEmailStore()
    {
        if (!userManager.SupportsUserEmail)
        {
            throw new NotSupportedException("The default UI requires a user store with email support.");
        }
        return (IUserEmailStore<NovaUserEntity>)userStore;
    }

    /// <summary>
    /// Logs that a user logged in with an external provider.
    /// </summary>
    /// <param name="loginProvider">The name of the login provider.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "User logged in with {LoginProvider} provider.")]
    private partial void LogExternalLoginSucceeded(string loginProvider);

    /// <summary>
    /// Logs that a user created an account using an external provider.
    /// </summary>
    /// <param name="loginProvider">The name of the login provider.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "User created an account using {LoginProvider} provider.")]
    private partial void LogAccountCreatedFromExternalProvider(string loginProvider);

    /// <summary>
    /// Logs that role assignment to a user failed.
    /// </summary>
    /// <param name="role">The role that failed to be assigned.</param>
    /// <param name="errors">Comma-separated error descriptions.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to add user to role {Role}: {Errors}")]
    private partial void LogRoleAssignmentFailed(string role, string errors);

    /// <summary>
    /// Form input model for external login, containing email address.
    /// </summary>
    private sealed class InputModel
    {
        /// <summary>
        /// Gets or sets the user's email address.
        /// </summary>
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";
    }
}
