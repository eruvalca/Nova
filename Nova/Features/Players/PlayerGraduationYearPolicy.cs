using Nova.Shared.Players;
using OneOf;

namespace Nova.Features.Players;

/// <summary>
/// Facts about a single Assigned placement used to evaluate graduation-year eligibility.
/// </summary>
internal sealed record AssignedPlacementFacts(
    long PlayerCampaignAssignmentId,
    long CampaignId,
    long TeamId,
    int TeamGraduationYear);

/// <summary>
/// Signals that the proposed graduation-year change would not invalidate any Assigned placement.
/// </summary>
internal readonly record struct GraduationYearMayChange;

/// <summary>
/// Signals that the proposed graduation-year change would invalidate one or more Assigned placements,
/// carrying structured data identifying each affected placement.
/// </summary>
/// <param name="Blockers">The placements that would become ineligible.</param>
internal sealed record GraduationYearEditBlocked(IReadOnlyList<GraduationYearBlockerItem> Blockers);

/// <summary>
/// Evaluates whether a proposed graduation-year value would invalidate any Active-campaign Assigned
/// placements. A placement is ineligible when the player's graduation year is less than the team's.
/// </summary>
internal static class PlayerGraduationYearPolicy
{
    /// <summary>
    /// Classifies each supplied placement against the proposed graduation year and returns either
    /// a go-ahead or a structured list of blockers.
    /// </summary>
    /// <param name="proposedGraduationYear">The graduation year the caller wants to set.</param>
    /// <param name="placements">The current Assigned placements in Active campaigns.</param>
    /// <returns>
    /// <see cref="GraduationYearMayChange"/> when no placement would become ineligible;
    /// <see cref="GraduationYearEditBlocked"/> with blocker details otherwise.
    /// </returns>
    public static OneOf<GraduationYearMayChange, GraduationYearEditBlocked> Evaluate(
        int proposedGraduationYear,
        IReadOnlyList<AssignedPlacementFacts> placements)
    {
        var blockers = placements
            .Where(p => proposedGraduationYear < p.TeamGraduationYear)
            .Select(p => new GraduationYearBlockerItem
            {
                PlayerCampaignAssignmentId = p.PlayerCampaignAssignmentId,
                CampaignId = p.CampaignId,
                TeamId = p.TeamId,
                TeamGraduationYear = p.TeamGraduationYear
            })
            .ToList();

        if (blockers.Count > 0)
        {
            return new GraduationYearEditBlocked(blockers);
        }

        return new GraduationYearMayChange();
    }
}
