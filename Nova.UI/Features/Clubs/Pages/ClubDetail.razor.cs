using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Security;

namespace Nova.UI.Features.Clubs.Pages;

/// <summary>Redirects a tenant-matching legacy Club detail route to the canonical Club overview.</summary>
/// <param name="authenticationStateProvider">The provider for the caller's current membership claims.</param>
/// <param name="navigationManager">The navigation manager used to replace or deny the legacy route.</param>
public partial class ClubDetail(
    AuthenticationStateProvider authenticationStateProvider,
    NavigationManager navigationManager)
{
    /// <summary>Gets or sets the club identifier supplied by the legacy route.</summary>
    [Parameter]
    public long ClubId { get; set; }

    /// <summary>Redirects matching membership to Overview and rejects cross-tenant legacy identifiers.</summary>
    /// <returns>A task that completes after the current authentication state is evaluated.</returns>
    protected override async Task OnInitializedAsync()
    {
        var principal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        var claim = principal.FindFirst(NovaClaimTypes.ClubId)?.Value;
        if (long.TryParse(claim, NumberStyles.Integer, CultureInfo.InvariantCulture, out var currentClubId)
            && currentClubId == ClubId)
        {
            navigationManager.NavigateTo(ClubRoutes.Overview, replace: true);
            return;
        }

        navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
    }
}
