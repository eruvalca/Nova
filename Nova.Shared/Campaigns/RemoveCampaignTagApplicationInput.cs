using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Campaigns;

/// <summary>
/// Input model for removing a campaign tag application.
/// </summary>
public sealed record RemoveCampaignTagApplicationInput
{
    /// <summary>The campaign tag application identifier to remove.</summary>
    [Required]
    [Range(1, long.MaxValue)]
    public required long CampaignTagApplicationId { get; init; }
}
