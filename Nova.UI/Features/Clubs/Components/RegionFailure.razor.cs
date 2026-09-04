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
}
