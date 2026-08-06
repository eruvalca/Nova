namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Contains the updated season metadata returned after a successful correction.
/// </summary>
/// <param name="SeasonId">The season identifier.</param>
/// <param name="Name">The corrected season display name.</param>
/// <param name="StartDate">The corrected season start date.</param>
/// <param name="EndDate">The corrected optional season end date.</param>
public sealed record UpdateSeasonMetadataResult(
    long SeasonId,
    string Name,
    DateOnly StartDate,
    DateOnly? EndDate);
