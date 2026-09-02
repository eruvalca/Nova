using Nova.Shared.Enums;

namespace Nova.Shared.Features.Seasons;

/// <summary>Represents season metadata and currentness.</summary>
public sealed record SeasonSummary
{
    /// <summary>Gets the season identifier.</summary>
    public required long SeasonId { get; init; }

    /// <summary>Gets the season name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the season start date.</summary>
    public required DateOnly StartDate { get; init; }

    /// <summary>Gets the optional season end date.</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>Gets a value indicating whether this is the club's current season.</summary>
    public required bool IsCurrent { get; init; }

    /// <summary>Gets the token required for the next metadata write.</summary>
    public required Guid ConcurrencyToken { get; init; }
}

/// <summary>Represents one bounded campaign-history row in season detail.</summary>
public sealed record SeasonCampaignSummary
{
    /// <summary>Gets the campaign identifier.</summary>
    public required long CampaignId { get; init; }

    /// <summary>Gets the campaign name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the campaign lifecycle status.</summary>
    public required CampaignStatus Status { get; init; }

    /// <summary>Gets the campaign start date.</summary>
    public required DateOnly StartDate { get; init; }

    /// <summary>Gets the optional campaign end date.</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>Gets the persisted campaign participant count.</summary>
    public required int ParticipantCount { get; init; }
}

/// <summary>Represents one eventually-consistent page of seasons.</summary>
public sealed record SeasonPageResult
{
    /// <summary>Gets the returned seasons.</summary>
    public required IReadOnlyList<SeasonSummary> Items { get; init; }

    /// <summary>Gets the one-based page number.</summary>
    public required int Page { get; init; }

    /// <summary>Gets the page size.</summary>
    public required int PageSize { get; init; }

    /// <summary>Gets the eventually-consistent total season count.</summary>
    public required int TotalCount { get; init; }
}

/// <summary>Represents season metadata and a bounded campaign-history page.</summary>
public sealed record SeasonDetailResult
{
    /// <summary>Gets the season metadata.</summary>
    public required SeasonSummary Season { get; init; }

    /// <summary>Gets the returned campaign rows.</summary>
    public required IReadOnlyList<SeasonCampaignSummary> Campaigns { get; init; }

    /// <summary>Gets the one-based campaign page number.</summary>
    public required int CampaignPage { get; init; }

    /// <summary>Gets the campaign page size.</summary>
    public required int CampaignPageSize { get; init; }

    /// <summary>Gets the eventually-consistent total campaign count.</summary>
    public required int CampaignTotalCount { get; init; }
}

/// <summary>Represents a successful atomic season advancement.</summary>
public sealed record StartNextSeasonResult
{
    /// <summary>Gets the season that was current before advancement.</summary>
    public required long PreviousSeasonId { get; init; }

    /// <summary>Gets the newly established current season.</summary>
    public required SeasonSummary CurrentSeason { get; init; }
}
