#pragma warning disable CA1515 // Razor generates a public component partial class.
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
#pragma warning disable CA1724 // The Landing page and its feature namespace are distinct qualified names.
public partial class Landing(NavigationManager navigationManager, AuthenticationStateProvider authenticationStateProvider)
#pragma warning restore CA1724
{
    /// <summary>
    /// Gets the absolute canonical URL of the landing page, derived from the request host.
    /// </summary>
#pragma warning disable CA1056 // Blazor binding and NavigationManager consume string URLs in this component contract.
    protected string CanonicalUrl => navigationManager.ToAbsoluteUri("/").AbsoluteUri;
#pragma warning restore CA1056

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
