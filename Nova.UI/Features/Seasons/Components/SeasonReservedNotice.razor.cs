using Microsoft.AspNetCore.Components;

namespace Nova.UI.Features.Seasons.Components;

/// <summary>
/// Presents an honest "not available here yet" notice for a Seasons destination owned by a later slice.
/// </summary>
public partial class SeasonReservedNotice
{
    /// <summary>Gets or sets the destination heading.</summary>
    [Parameter, EditorRequired]
    public required string Title { get; set; }

    /// <summary>Gets or sets the explanation naming the slice that owns the destination.</summary>
    [Parameter, EditorRequired]
    public required string Explanation { get; set; }
}
