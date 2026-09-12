using System.ComponentModel.DataAnnotations;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>Requests the whole immutable Closed-campaign record as a CSV export.</summary>
public sealed record GetClosedCampaignRosterExportInput
{
    /// <summary>The Closed campaign to export. Draft, Active, and inaccessible campaigns are rejected.</summary>
    [Range(1, long.MaxValue)]
    public required long CampaignId { get; init; }
}
