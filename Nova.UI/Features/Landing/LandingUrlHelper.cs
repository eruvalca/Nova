using Microsoft.AspNetCore.Components;

namespace Nova.UI.Features.Landing;

/// <summary>
/// Provides the shared, single source of truth for the landing page's identity-action URLs.
/// </summary>
/// <remarks>
/// The landing page exposes a <c>Sign in</c> action and a <c>Create your club</c> registration
/// action in two places: the public header (see <see cref="Components.PublicLayout"/>) and the
/// page body (hero and final CTA). Both build the same identity URL that carries the safe local
/// <c>/dashboard</c> continuation so an anonymous visitor who signs in or registers continues to
/// the authenticated dashboard. Keep the path and continuation in one place.
/// </remarks>
public static class LandingUrlHelper
{
    /// <summary>
    /// Gets the base-relative path to the authenticated dashboard destination used as the safe
    /// continuation for landing-page identity actions.
    /// </summary>
    public static string DashboardRoute => "/dashboard";

    /// <summary>
    /// Builds the sign-in URL that carries the safe local <c>/dashboard</c> continuation so a
    /// visitor who signs in from the landing page continues to the authenticated dashboard.
    /// </summary>
    /// <param name="navigationManager">The navigation manager used to build the absolute URL.</param>
    /// <returns>The absolute sign-in URL with the return-URL query parameter.</returns>
    public static string CreateSignInUrl(NavigationManager navigationManager) =>
        BuildIdentityUrl(navigationManager, "Account/Login");

    /// <summary>
    /// Builds the registration URL that carries the safe local <c>/dashboard</c> continuation so a
    /// visitor who registers from the landing page continues to the authenticated dashboard.
    /// </summary>
    /// <param name="navigationManager">The navigation manager used to build the absolute URL.</param>
    /// <returns>The absolute registration URL with the return-URL query parameter.</returns>
    public static string CreateClubUrl(NavigationManager navigationManager) =>
        BuildIdentityUrl(navigationManager, "Account/Register");

    /// <summary>
    /// Builds an Identity URL that carries the safe local <c>/dashboard</c> return URL.
    /// </summary>
    /// <param name="navigationManager">The navigation manager used to build the absolute URL.</param>
    /// <param name="path">The base identity path, e.g. <c>Account/Login</c>.</param>
    /// <returns>The absolute URL with the return-URL query parameter.</returns>
    private static string BuildIdentityUrl(NavigationManager navigationManager, string path) =>
        navigationManager.GetUriWithQueryParameters(
            path,
            new Dictionary<string, object?> { ["returnUrl"] = DashboardRoute });
}
