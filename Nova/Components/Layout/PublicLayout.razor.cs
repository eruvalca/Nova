using Microsoft.AspNetCore.Components;
using Nova.UI.Features.Landing;

namespace Nova.Components.Layout;

/// <summary>
/// Provides the anonymous public shell for the landing page: a header carrying the Nova identity,
/// anchored section navigation, and the <c>Sign in</c> / <c>Create your club</c> actions. Unlike
/// <see cref="MainLayout"/>, it never renders the authenticated <see cref="NavMenu"/> so the landing
/// page stays a true public entry point. The page's footer is supplied by the page itself via the
/// <see cref="Nova.UI.Features.Landing.Components.LandingFooter"/> component rendered through <c>@Body</c>.
/// </summary>
/// <param name="navigationManager">The navigation manager used to build the identity action URLs.</param>
public partial class PublicLayout(NavigationManager navigationManager) : LayoutComponentBase
{
    /// <summary>
    /// Gets the sign-in URL that preserves the safe local <c>/dashboard</c> continuation.
    /// </summary>
    protected string SignInUrl => LandingUrlHelper.CreateSignInUrl(navigationManager);

    /// <summary>
    /// Gets the registration URL that preserves the safe local <c>/dashboard</c> continuation.
    /// </summary>
    protected string CreateClubUrl => LandingUrlHelper.CreateClubUrl(navigationManager);
}
