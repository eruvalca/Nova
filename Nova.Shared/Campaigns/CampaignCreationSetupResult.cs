namespace Nova.Shared.Campaigns;

/// <summary>
/// Represents a season choice available while creating a campaign.
/// </summary>
public sealed record CampaignSeasonChoice
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
}

/// <summary>
/// Represents the live setup data needed before creating a campaign.
/// </summary>
public sealed record CampaignCreationSetupResult
{
    /// <summary>
    /// Gets the newest tenant seasons returned as choices.
    /// </summary>
    public required IReadOnlyList<CampaignSeasonChoice> Seasons { get; init; }

    /// <summary>
    /// Gets the total number of tenant seasons before the choice bound.
    /// </summary>
    public required int TotalSeasonCount { get; init; }

    /// <summary>
    /// Gets the current number of Active players.
    /// </summary>
    public required int ActivePlayerCount { get; init; }

    /// <summary>
    /// Gets the current number of Active teams.
    /// </summary>
    public required int ActiveTeamCount { get; init; }
}
