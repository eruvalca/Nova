using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Request input for the authoritative whole-campaign placement summary.
/// </summary>
public sealed record GetCampaignPlacementSummaryInput
{
    /// <summary>
    /// The campaign identifier from the route.
    /// </summary>
    [Required]
    [Range(1, long.MaxValue)]
    public required long CampaignId { get; init; }
}
