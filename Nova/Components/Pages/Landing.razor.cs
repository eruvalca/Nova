using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Nova.Components.Pages;

/// <summary>
/// Serves the anonymous public Nova landing page at the application root. Fully onboarded,
/// authenticated club members are redirected to the authenticated dashboard; everyone else sees
/// the public marketing content.
/// </summary>
/// <param name="navigationManager">The navigation manager used for the auth-aware redirect.</param>
/// <param name="authenticationStateProvider">The authentication state provider used to detect an onboarded member.</param>
public partial class Landing(NavigationManager navigationManager, AuthenticationStateProvider authenticationStateProvider)
{
    /// <summary>
    /// Gets the absolute canonical URL of the landing page, derived from the request host.
    /// </summary>
    protected string CanonicalUrl => navigationManager.ToAbsoluteUri("/").AbsoluteUri;

    /// <summary>
    /// Redirects fully onboarded authenticated visitors to the dashboard and otherwise leaves the
    /// public landing page rendering.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task OnInitializedAsync()
    {
        var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
        if (authState.User.Identity?.IsAuthenticated == true)
        {
            // Both onboarding gates have already passed for any authenticated user reaching this
            // page, so a photo-complete club member is safe to send to the authenticated home. The
            // replace flag swaps the landing entry for the dashboard so Back does not bounce to the
            // public page after sign-in.
            navigationManager.NavigateTo("/dashboard", forceLoad: true, replace: true);
        }
    }
}
