using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Nova.Components;

/// <summary>
/// Redirects unauthorized route visits to login for anonymous users and to access denied for authenticated users.
/// </summary>
/// <param name="navigationManager">The navigation manager used for full-document redirects.</param>
/// <param name="authenticationStateProvider">The authentication state provider used to inspect the current user.</param>
public partial class RedirectToLoginOrAccessDenied(
    NavigationManager navigationManager,
    AuthenticationStateProvider authenticationStateProvider)
{
    /// <summary>
    /// Resolves the current authentication state and performs the appropriate full-document redirect.
    /// </summary>
    /// <returns>A task that completes once the redirect is issued.</returns>
    protected override async Task OnInitializedAsync()
    {
        var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
        if (authState.User.Identity?.IsAuthenticated == true)
        {
            navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
            return;
        }

        navigationManager.NavigateTo(
            $"/Account/Login?returnUrl={Uri.EscapeDataString(navigationManager.Uri)}",
            forceLoad: true);
    }
}
