namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Represents the current season available while creating a campaign.
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
    /// Gets the club's current season, or null when inline first-season creation is available.
    /// </summary>
    public CampaignSeasonChoice? CurrentSeason { get; init; }

    /// <summary>
    /// Gets the current number of Active players.
    /// </summary>
    public required int ActivePlayerCount { get; init; }

    /// <summary>
    /// Gets the current number of Active teams.
    /// </summary>
    public required int ActiveTeamCount { get; init; }
}
