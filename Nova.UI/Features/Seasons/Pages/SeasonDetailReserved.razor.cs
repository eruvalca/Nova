using Microsoft.AspNetCore.Components;

namespace Nova.UI.Features.Seasons.Pages;

/// <summary>
/// Binds the reserved season-detail route while issue #259 builds the season record itself.
/// </summary>
public partial class SeasonDetailReserved
{
    /// <summary>Gets or sets the season identifier from the route.</summary>
    [Parameter]
    public long SeasonId { get; set; }
}
