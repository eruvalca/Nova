using Nova.Shared.Enums;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Represents one campaign row in a season-grouped campaign list.
/// </summary>
public sealed record CampaignListItem
{
    /// <summary>
    /// Gets the campaign identifier.
    /// </summary>
    public required long CampaignId { get; init; }

    /// <summary>
    /// Gets the campaign name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the campaign start date.
    /// </summary>
    public required DateOnly StartDate { get; init; }

    /// <summary>
    /// Gets the optional planned end date.
    /// </summary>
    public DateOnly? PlannedEndDate { get; init; }

    /// <summary>
    /// Gets the campaign lifecycle status.
    /// </summary>
    public required CampaignStatus Status { get; init; }

    /// <summary>
    /// Gets the number of persisted campaign participants.
    /// </summary>
    public required int ParticipantCount { get; init; }

    /// <summary>
    /// Gets the number of participants whose placement remains undecided.
    /// </summary>
    public required int UnresolvedCount { get; init; }
}

/// <summary>
/// Represents one season group in the campaign list.
/// </summary>
public sealed record CampaignSeasonGroup
{
    /// <summary>
    /// Gets the season identifier.
    /// </summary>
    public required long SeasonId { get; init; }

    /// <summary>
    /// Gets the season name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the season start date.
    /// </summary>
    public required DateOnly StartDate { get; init; }

    /// <summary>
    /// Gets the optional season end date.
    /// </summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>
    /// Gets the token required to update this season's metadata.
    /// </summary>
    public required Guid ConcurrencyToken { get; init; }

    /// <summary>
    /// Gets the campaigns in this season group.
    /// </summary>
    public required IReadOnlyList<CampaignListItem> Campaigns { get; init; }
}

/// <summary>
/// Represents the bounded, season-grouped campaign list response.
/// </summary>
public sealed record CampaignListResult
{
    /// <summary>
    /// Gets the number of campaigns matching the optional status filter before bounding.
    /// </summary>
    public required int TotalCount { get; init; }

    /// <summary>
    /// Gets the season groups containing the bounded campaign rows.
    /// </summary>
    public required IReadOnlyList<CampaignSeasonGroup> Seasons { get; init; }
}
