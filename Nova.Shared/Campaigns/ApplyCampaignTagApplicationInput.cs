using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Campaigns;

/// <summary>
/// Input model for applying a tag definition to a campaign participation.
/// </summary>
public sealed record ApplyCampaignTagApplicationInput
{
    /// <summary>The campaign participation identifier to tag.</summary>
    [Required]
    [Range(1, long.MaxValue)]
    public required long PlayerCampaignAssignmentId { get; init; }

    /// <summary>The tag-definition identifier to apply.</summary>
    [Required]
    [Range(1, long.MaxValue)]
    public required long PlayerTagId { get; init; }
}
