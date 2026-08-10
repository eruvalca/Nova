using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Request input for a single campaign-participant detail query.
/// </summary>
public sealed record GetCampaignParticipantDetailInput
{
    /// <summary>
    /// The campaign identifier from the route.
    /// </summary>
    [Required]
    [Range(1, long.MaxValue)]
    public required long CampaignId { get; init; }

    /// <summary>
    /// The participant assignment identifier from the route.
    /// </summary>
    [Required]
    [Range(1, long.MaxValue)]
    public required long PlayerCampaignAssignmentId { get; init; }
}
