#pragma warning disable CA1515 // Razor generates a public component partial class.
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Nova.Entities;

namespace Nova.Components.Account.Pages.Manage;

/// <summary>
/// Handles management of external login providers (social login) for the user account.
/// </summary>
public partial class ExternalLogins(
    UserManager<NovaUserEntity> userManager,
    SignInManager<NovaUserEntity> signInManager,
    IUserStore<NovaUserEntity> userStore,
    IdentityRedirectManager redirectManager)
{
    /// <summary>
    /// The callback action name used when linking a new external login.
    /// Referenced externally by <see cref="IdentityComponentsEndpointRouteBuilderExtensions"/>.
    /// </summary>
    public const string LinkLoginCallbackAction = "LinkLoginCallback";

    /// <summary>
    /// Stores the current user entity.
    /// </summary>
    private NovaUserEntity? _user;

    /// <summary>
    /// Stores the list of currently linked external logins for the user.
    /// </summary>
    private IList<UserLoginInfo>? _currentLogins;

    /// <summary>
    /// Stores the list of available external authentication schemes not yet linked.
    /// </summary>
    private IList<AuthenticationScheme>? _otherLogins;

    /// <summary>
    /// Indicates whether the user can remove an external login.
    /// </summary>
    private bool _showRemoveButton;

    /// <summary>
    /// Gets the cascading HTTP context from the parent component.
    /// </summary>
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <summary>
    /// Gets or sets the login provider name supplied from the form post.
    /// </summary>
    [SupplyParameterFromForm]
    private string? LoginProvider { get; set; }

    /// <summary>
    /// Gets or sets the provider key supplied from the form post.
    /// </summary>
    [SupplyParameterFromForm]
    private string? ProviderKey { get; set; }

    /// <summary>
    /// Gets or sets the action query parameter from the URL.
    /// </summary>
    [SupplyParameterFromQuery]
    private string? Action { get; set; }

    /// <summary>
    /// Initializes the component, loading the current user's external logins and available providers.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task OnInitializedAsync()
    {
        _user = await userManager.GetUserAsync(HttpContext.User);
        if (_user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        _currentLogins = await userManager.GetLoginsAsync(_user);
        _otherLogins = (await signInManager.GetExternalAuthenticationSchemesAsync())
            .Where(auth => _currentLogins.All(ul => !string.Equals(auth.Name, ul.LoginProvider, StringComparison.Ordinal)))
            .ToList();

        string? passwordHash = null;
        if (userStore is IUserPasswordStore<NovaUserEntity> userPasswordStore)
        {
            passwordHash = await userPasswordStore.GetPasswordHashAsync(_user, HttpContext.RequestAborted);
        }

        _showRemoveButton = passwordHash is not null || _currentLogins.Count > 1;

        if (HttpMethods.IsGet(HttpContext.Request.Method) && string.Equals(Action, LinkLoginCallbackAction, StringComparison.Ordinal))
        {
            await OnGetLinkLoginCallbackAsync();
        }
    }

    /// <summary>
    /// Handles the form submission to remove an external login from the user account.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnSubmitAsync()
    {
        if (_user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        var result = await userManager.RemoveLoginAsync(_user, LoginProvider!, ProviderKey!);
        if (!result.Succeeded)
        {
            redirectManager.RedirectToCurrentPageWithStatus("Error: The external login was not removed.", HttpContext);
        }
        else
        {
            await signInManager.RefreshSignInAsync(_user);
            redirectManager.RedirectToCurrentPageWithStatus("The external login was removed.", HttpContext);
        }
    }

    /// <summary>
    /// Handles the callback from an external login link operation, completing the link process.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnGetLinkLoginCallbackAsync()
    {
        if (_user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        var userId = await userManager.GetUserIdAsync(_user);
        var info = await signInManager.GetExternalLoginInfoAsync(userId);
        if (info is null)
        {
            redirectManager.RedirectToCurrentPageWithStatus("Error: Could not load external login info.", HttpContext);
            return;
        }

        var result = await userManager.AddLoginAsync(_user, info);
        if (result.Succeeded)
        {
            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            redirectManager.RedirectToCurrentPageWithStatus("The external login was added.", HttpContext);
        }
        else
        {
            redirectManager.RedirectToCurrentPageWithStatus("Error: The external login was not added. External logins can only be associated with one account.", HttpContext);
        }
    }
}
