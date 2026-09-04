using Microsoft.AspNetCore.Components;
using Nova.Shared.Features.Clubs;

namespace Nova.UI.Features.Clubs.Pages;

/// <summary>Provides honest placeholders for reserved administrator Club destinations.</summary>
/// <param name="navigationManager">The navigation manager used to identify the active reserved route.</param>
public partial class ClubReservedSection(NavigationManager navigationManager)
{
    /// <summary>Gets the heading and unavailable-yet explanation for the current route.</summary>
    protected (string Title, string Explanation) Section
    {
        get
        {
            var path = "/" + navigationManager.ToBaseRelativePath(navigationManager.Uri).Split('?', '#')[0].TrimEnd('/');
            return path.ToLowerInvariant() switch
            {
                ClubRoutes.Seasons => ("Seasons", "Season management is reserved for issue #204 and is not available here yet."),
                ClubRoutes.Members => ("Members", "Member management is reserved for issue #205 and is not available here yet."),
                ClubRoutes.Requests => ("Requests", "Join-request management is reserved for issue #206 and is not available here yet."),
                ClubRoutes.Tags => ("Tags", "Tag management is reserved for issue #207 and is not available here yet."),
                ClubRoutes.Crest => ("Crest", "Crest management is reserved for issue #207 and is not available here yet."),
                _ => ("Club", "This Club section is not available yet.")
            };
        }
    }
}
