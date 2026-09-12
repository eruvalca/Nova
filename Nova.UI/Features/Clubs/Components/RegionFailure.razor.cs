
using Microsoft.AspNetCore.Components;

namespace Nova.UI.Features.Clubs.Components;

/// <summary>Displays a recoverable regional error with an enhanced retry action.</summary>
public partial class RegionFailure
{
    /// <summary>Gets or sets the unavailable-region message.</summary>
    [Parameter, EditorRequired]
    public required string Message { get; set; }

    /// <summary>Gets or sets the callback that retries only the failed region.</summary>
    [Parameter, EditorRequired]
    public required EventCallback Retry { get; set; }

    /// <summary>
    /// Gets or sets the route a retry follows when no interactive circuit is attached, such as during
    /// prerender or with scripting disabled. Defaults to the club overview for the surfaces that already
    /// host this region; a caller on another route must pass its own destination so a non-interactive
    /// retry re-enters the failed page instead of leaving it.
    /// </summary>
    [Parameter]
    public string FallbackRoute { get; set; } = Nova.SharedKernel.Features.Clubs.ClubRoutes.Overview;
}
