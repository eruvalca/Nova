using System.ComponentModel.DataAnnotations;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>Filters the authoritative current-season roster, never a caller-selected season.</summary>
public sealed record GetCurrentSeasonRosterInput : PlacementPageInput
{
    /// <summary>An optional tenant-visible team.</summary>
    [Range(1, long.MaxValue)]
    public long? TeamId { get; init; }
    /// <summary>An optional exact player graduation year.</summary>
    [Range(1, int.MaxValue)]
    public int? GraduationYear { get; init; }
    /// <summary>An optional literal player-name substring.</summary>
    [MaxLength(200)]
    public string? Search { get; init; }
}
