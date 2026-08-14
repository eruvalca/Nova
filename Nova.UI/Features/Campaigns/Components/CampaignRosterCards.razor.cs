using Microsoft.AspNetCore.Components;
using Nova.Shared.Features.Campaigns;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Renders the narrow-screen campaign roster card list.
/// </summary>
public partial class CampaignRosterCards
{
    /// <summary>
    /// Gets or sets the roster rows to display.
    /// </summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<CampaignParticipantRosterItem> Items { get; set; } = [];
}
