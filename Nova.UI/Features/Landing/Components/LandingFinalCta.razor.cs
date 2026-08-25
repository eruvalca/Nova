using Microsoft.AspNetCore.Components;
using Nova.UI.Components;

namespace Nova.UI.Features.Landing.Components;

/// <summary>
/// Renders the landing page's closing call to action: repeat the primary <c>Create your club</c>
/// registration action against the same safe local <c>/dashboard</c> continuation used by the hero.
/// </summary>
/// <param name="navigationManager">The navigation manager used to build the safe registration URL.</param>
public partial class LandingFinalCta(NavigationManager navigationManager) : NovaComponentBase
{
    /// <summary>
    /// Gets the registration URL that carries the safe local <c>/dashboard</c> continuation so a
    /// visitor who registers from the landing page continues to the authenticated dashboard.
    /// </summary>
    protected string CreateClubUrl => LandingUrlHelper.CreateClubUrl(navigationManager);
}
