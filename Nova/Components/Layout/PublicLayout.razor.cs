using Microsoft.AspNetCore.Components;

namespace Nova.Components.Layout;

/// <summary>
/// Provides the anonymous public shell for the landing page: a header carrying the Nova identity,
/// anchored section navigation, and the <c>Sign in</c> / <c>Create your club</c> actions, plus a
/// compact footer. Unlike <see cref="MainLayout"/>, it never renders the authenticated
/// <see cref="NavMenu"/> so the landing page stays a true public entry point.
/// </summary>
public partial class PublicLayout : LayoutComponentBase
{
    /// <summary>
    /// Gets the navigation manager used to build the identity return-URL based action links.
    /// </summary>
    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    /// <summary>
    /// Gets the sign-in URL that preserves the safe local <c>/dashboard</c> continuation.
    /// </summary>
    protected string SignInUrl => BuildIdentityUrl("Account/Login");

    /// <summary>
    /// Gets the registration URL that preserves the safe local <c>/dashboard</c> continuation.
    /// </summary>
    protected string CreateClubUrl => BuildIdentityUrl("Account/Register");

    /// <summary>
    /// Builds an Identity URL that carries the safe local <c>/dashboard</c> return URL so a visitor
    /// who signs in or registers from the landing page continues to the authenticated dashboard.
    /// </summary>
    /// <param name="path">The base identity path, e.g. <c>Account/Login</c>.</param>
    /// <returns>The absolute URL with the return-URL query parameter.</returns>
    private string BuildIdentityUrl(string path) => NavigationManager.GetUriWithQueryParameters(
        path,
        new Dictionary<string, object?> { ["returnUrl"] = "/dashboard" });
}
