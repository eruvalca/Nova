using System.ComponentModel.DataAnnotations;
using Nova.SharedKernel.Validation;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>Filters the Active campaign working set without changing its unfiltered section totals.</summary>
public sealed record GetCampaignEffectivePlacementsInput : CampaignRosterDiscoveryInput
{
    /// <summary>Filters campaign-local close blockers independently of effective placement eligibility.</summary>
    [NotWhitespace]
    [RegularExpression("(?i)^(outcomes|eligibility|archivedTeams)$")]
    public string? CloseoutBlocker { get; init; }

    /// <summary>An optional effective team filter.</summary>
    [Range(1, long.MaxValue)]
    public long? TeamId { get; init; }
    /// <summary>An optional exact player graduation year.</summary>
    [Range(1, int.MaxValue)]
    public int? GraduationYear { get; init; }
    /// <summary>An optional case-insensitive eligibility name; an unknown name selects all states.</summary>
    [MaxLength(40)]
    public string? Eligibility { get; init; }
}
