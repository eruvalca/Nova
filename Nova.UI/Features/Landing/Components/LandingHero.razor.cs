using Microsoft.AspNetCore.Components;
using Nova.UI.Components;

namespace Nova.UI.Features.Landing.Components;

/// <summary>
/// Renders the landing page hero: the approved headline, administrator-first supporting copy, and
/// the primary <c>Create your club</c> and secondary <c>See how it works</c> actions.
/// </summary>
/// <param name="navigationManager">The navigation manager used to build the safe registration URL.</param>
public partial class LandingHero(NavigationManager navigationManager) : NovaComponentBase
{
    /// <summary>
    /// Gets the registration URL that carries the safe local <c>/dashboard</c> continuation so a
    /// visitor who registers from the landing page continues to the authenticated dashboard.
    /// </summary>
    protected string CreateClubUrl => navigationManager.GetUriWithQueryParameters(
        "Account/Register",
        new Dictionary<string, object?> { ["returnUrl"] = "/dashboard" });
}
