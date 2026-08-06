using Nova.Shared.Features.Teams;
using OneOf;

namespace Nova.Features.Teams;

/// <summary>
/// Facts about a single Assigned placement on a team, used to evaluate whether a proposed team
/// graduation year would leave that placement eligible.
/// </summary>
/// <param name="PlayerCampaignAssignmentId">The placement identifier.</param>
/// <param name="CampaignId">The campaign the placement belongs to.</param>
/// <param name="PlayerId">The placed player identifier.</param>
/// <param name="PlayerGraduationYear">The placed player's graduation year.</param>
internal sealed record TeamAssignedPlacementFacts(
    long PlayerCampaignAssignmentId,
    long CampaignId,
    long PlayerId,
    int PlayerGraduationYear);

/// <summary>
/// Signals that the proposed team graduation year would not invalidate any Assigned placement.
/// </summary>
internal readonly record struct TeamGraduationYearMayChange;

/// <summary>
/// Signals that the proposed team graduation year would invalidate one or more Assigned placements,
/// carrying structured data identifying each affected placement.
/// </summary>
/// <param name="Blockers">The placements that would become ineligible.</param>
internal sealed record TeamGraduationYearEditBlocked(
    IReadOnlyList<TeamGraduationYearBlockerItem> Blockers);

/// <summary>
/// Evaluates whether a proposed team graduation year would invalidate any Active-campaign Assigned
/// placement. A team's graduation year is the <em>minimum</em> eligible player graduation year, so a
/// placement is ineligible when the player's graduation year is less than the team's.
/// </summary>
internal static class TeamGraduationYearPolicy
{
    /// <summary>
    /// Classifies each supplied placement against the proposed team graduation year and returns
    /// either a go-ahead or a structured list of blockers.
    /// </summary>
    /// <param name="proposedGraduationYear">The graduation year the caller wants to set on the team.</param>
    /// <param name="placements">The team's current Assigned placements in Active campaigns.</param>
    /// <returns>
    /// <see cref="TeamGraduationYearMayChange"/> when no placement would become ineligible;
    /// <see cref="TeamGraduationYearEditBlocked"/> with blocker details otherwise.
    /// </returns>
    public static OneOf<TeamGraduationYearMayChange, TeamGraduationYearEditBlocked> Evaluate(
        int proposedGraduationYear,
        IReadOnlyList<TeamAssignedPlacementFacts> placements)
    {
        ArgumentNullException.ThrowIfNull(placements);

        var blockers = placements
            .Where(placement => placement.PlayerGraduationYear < proposedGraduationYear)
            .OrderBy(placement => placement.PlayerCampaignAssignmentId)
            .Select(placement => new TeamGraduationYearBlockerItem
            {
                PlayerCampaignAssignmentId = placement.PlayerCampaignAssignmentId,
                CampaignId = placement.CampaignId,
                PlayerId = placement.PlayerId,
                PlayerGraduationYear = placement.PlayerGraduationYear
            })
            .ToList()
            .AsReadOnly();

        if (blockers.Count > 0)
        {
            return new TeamGraduationYearEditBlocked(blockers);
        }

        return new TeamGraduationYearMayChange();
    }
}
