using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Nova.Data;
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
    private NovaUserEntity? user;

    /// <summary>
    /// Stores the list of currently linked external logins for the user.
    /// </summary>
    private IList<UserLoginInfo>? currentLogins;

    /// <summary>
    /// Stores the list of available external authentication schemes not yet linked.
    /// </summary>
    private IList<AuthenticationScheme>? otherLogins;

    /// <summary>
    /// Indicates whether the user can remove an external login.
    /// </summary>
    private bool showRemoveButton;

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
        user = await userManager.GetUserAsync(HttpContext.User);
        if (user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        currentLogins = await userManager.GetLoginsAsync(user);
        otherLogins = (await signInManager.GetExternalAuthenticationSchemesAsync())
            .Where(auth => currentLogins.All(ul => auth.Name != ul.LoginProvider))
            .ToList();

        string? passwordHash = null;
        if (userStore is IUserPasswordStore<NovaUserEntity> userPasswordStore)
        {
            passwordHash = await userPasswordStore.GetPasswordHashAsync(user, HttpContext.RequestAborted);
        }

        showRemoveButton = passwordHash is not null || currentLogins.Count > 1;

        if (HttpMethods.IsGet(HttpContext.Request.Method) && Action == LinkLoginCallbackAction)
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
        if (user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        var result = await userManager.RemoveLoginAsync(user, LoginProvider!, ProviderKey!);
        if (!result.Succeeded)
        {
            redirectManager.RedirectToCurrentPageWithStatus("Error: The external login was not removed.", HttpContext);
        }
        else
        {
            await signInManager.RefreshSignInAsync(user);
            redirectManager.RedirectToCurrentPageWithStatus("The external login was removed.", HttpContext);
        }
    }

    /// <summary>
    /// Handles the callback from an external login link operation, completing the link process.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnGetLinkLoginCallbackAsync()
    {
        if (user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        var userId = await userManager.GetUserIdAsync(user);
        var info = await signInManager.GetExternalLoginInfoAsync(userId);
        if (info is null)
        {
            redirectManager.RedirectToCurrentPageWithStatus("Error: Could not load external login info.", HttpContext);
            return;
        }

        var result = await userManager.AddLoginAsync(user, info);
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
