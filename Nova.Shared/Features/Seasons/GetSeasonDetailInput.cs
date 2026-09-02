using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Features.Seasons;

/// <summary>Describes a season-detail request with bounded campaign paging.</summary>
public sealed record GetSeasonDetailInput
{
    /// <summary>Gets the season identifier.</summary>
    [Range(1, long.MaxValue)]
    public required long SeasonId { get; init; }

    /// <summary>Gets the one-based campaign page number.</summary>
    [Range(1, int.MaxValue)]
    public int? CampaignPage { get; init; }

    /// <summary>Gets the number of campaigns returned per page.</summary>
    [Range(1, GetSeasonListInput.MaximumPageSize)]
    public int? CampaignPageSize { get; init; }
}
