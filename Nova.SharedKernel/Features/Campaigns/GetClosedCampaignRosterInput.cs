using System.ComponentModel.DataAnnotations;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>Requests a page of the campaign-local Closed record, including archived participants.</summary>
public sealed record GetClosedCampaignRosterInput : PlacementPageInput
{
    /// <summary>The Closed campaign to read.</summary>
    [Range(1, long.MaxValue)]
    public required long CampaignId { get; init; }
}
