using System.ComponentModel.DataAnnotations;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>
/// Request input for a single campaign-detail query.
/// </summary>
public sealed record GetCampaignDetailInput
{
    /// <summary>
    /// The campaign identifier from the route.
    /// </summary>
    [Required]
    [Range(1, long.MaxValue)]
    public required long CampaignId { get; init; }
}
