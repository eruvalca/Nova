using Microsoft.AspNetCore.Components;
using Nova.Shared.Features.Clubs;

namespace Nova.UI.Features.Teams.Pages;

/// <summary>Redirects legacy Teams routes to their canonical Club-local destinations.</summary>
/// <param name="navigationManager">The navigation manager used to replace the legacy URL.</param>
public partial class LegacyTeamsRedirect(NavigationManager navigationManager)
{
    /// <summary>Gets or sets the optional team identifier supplied by the legacy detail route.</summary>
    [Parameter]
    public long? TeamId { get; set; }

    /// <summary>Replaces the legacy route with its canonical Club-local equivalent.</summary>
    protected override void OnInitialized()
        => navigationManager.NavigateTo(
            TeamId is long teamId ? ClubRoutes.TeamDetail(teamId) : ClubRoutes.Teams,
            replace: true);
}
