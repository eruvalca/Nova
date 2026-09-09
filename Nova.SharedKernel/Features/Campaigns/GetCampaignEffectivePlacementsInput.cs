using System.ComponentModel.DataAnnotations;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>Filters the Active campaign working set without changing its unfiltered section totals.</summary>
public sealed record GetCampaignEffectivePlacementsInput : PlacementPageInput
{
    /// <summary>The campaign whose participants are requested.</summary>
    [Range(1, long.MaxValue)]
    public required long CampaignId { get; init; }
    /// <summary>An optional effective team filter.</summary>
    [Range(1, long.MaxValue)]
    public long? TeamId { get; init; }
    /// <summary>An optional exact player graduation year.</summary>
    [Range(1, int.MaxValue)]
    public int? GraduationYear { get; init; }
    /// <summary>A literal name substring or exact numeric tryout number.</summary>
    [MaxLength(200)]
    public string? Search { get; init; }
    /// <summary>An optional case-insensitive eligibility name; an unknown name selects all states.</summary>
    [MaxLength(40)]
    public string? Eligibility { get; init; }
}
