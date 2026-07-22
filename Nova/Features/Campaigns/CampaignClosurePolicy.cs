using Nova.Shared.Enums;
using OneOf;

namespace Nova.Features.Campaigns;

/// <summary>
/// Reports close blockers that prevent transitioning a campaign to closed.
/// </summary>
/// <param name="Detail">A human-readable summary of the blockers.</param>
/// <param name="Errors">A condition-keyed list of blocker messages.</param>
public readonly record struct CampaignCloseBlocked(
    string Detail,
    IReadOnlyDictionary<string, string[]> Errors)
{
    /// <summary>
    /// Gets the number of participation records without a final outcome.
    /// </summary>
    internal int UndecidedCount { get; init; }

    /// <summary>
    /// Gets the number of assigned participation records that fail team eligibility.
    /// </summary>
    internal int IneligibleCount { get; init; }

    /// <summary>
    /// Gets the number of assigned participation records that reference archived teams.
    /// </summary>
    internal int ArchivedTeamCount { get; init; }
}

/// <summary>
/// Reports that the current campaign assignment snapshot satisfies every closure rule.
/// </summary>
internal readonly record struct CampaignMayClose;

/// <summary>
/// Captures the assignment facts required to evaluate campaign closure.
/// </summary>
/// <param name="AssignmentId">The campaign participation identifier.</param>
/// <param name="Outcome">The placement outcome.</param>
/// <param name="PlayerGraduationYear">The player's graduation year.</param>
/// <param name="TeamId">The assigned team identifier when present.</param>
/// <param name="TeamGraduationYear">The assigned team's graduation year when present.</param>
/// <param name="TeamLifecycleStatus">The assigned team's lifecycle status when present.</param>
internal sealed record CampaignAssignmentClosureState(
    long AssignmentId,
    PlacementOutcome Outcome,
    int PlayerGraduationYear,
    long? TeamId,
    int? TeamGraduationYear,
    LifecycleStatus? TeamLifecycleStatus);

/// <summary>
/// Evaluates deterministic campaign closure rules over an immutable assignment snapshot.
/// </summary>
internal static class CampaignClosurePolicy
{
    private const string OutcomeBlockerKey = "outcomes";
    private const string EligibilityBlockerKey = "eligibility";
    private const string ArchivedTeamBlockerKey = "archivedTeams";

    /// <summary>
    /// Determines whether all campaign participation records satisfy closure requirements.
    /// </summary>
    /// <param name="assignmentStates">The current assignment facts loaded after acquiring the campaign mutation lock.</param>
    /// <returns>A close approval or all applicable blocker groups.</returns>
    internal static OneOf<CampaignMayClose, CampaignCloseBlocked> Evaluate(
        IReadOnlyCollection<CampaignAssignmentClosureState> assignmentStates)
    {
        Dictionary<string, string[]> blockers = [];
        var undecidedCount = assignmentStates.Count(state => state.Outcome == PlacementOutcome.Undecided);
        if (undecidedCount > 0)
        {
            blockers[OutcomeBlockerKey] =
            [
                $"Every participant must have a final outcome before closing. Found {undecidedCount} undecided participation record(s)."
            ];
        }

        var ineligibleAssignments = assignmentStates
            .Where(state => state.Outcome == PlacementOutcome.Assigned
                && (!state.TeamId.HasValue
                    || !state.TeamGraduationYear.HasValue
                    || state.PlayerGraduationYear < state.TeamGraduationYear.Value))
            .Select(state => state.AssignmentId)
            .ToArray();
        if (ineligibleAssignments.Length > 0)
        {
            blockers[EligibilityBlockerKey] =
            [
                $"Every assigned participant must remain eligible for their team. Ineligible assignment ids: {string.Join(", ", ineligibleAssignments)}."
            ];
        }

        var archivedTeamAssignments = assignmentStates
            .Where(state => state.Outcome == PlacementOutcome.Assigned
                && state.TeamLifecycleStatus == LifecycleStatus.Archived)
            .Select(state => state.AssignmentId)
            .ToArray();
        if (archivedTeamAssignments.Length > 0)
        {
            blockers[ArchivedTeamBlockerKey] =
            [
                $"Assigned participants cannot reference archived teams. Blocked assignment ids: {string.Join(", ", archivedTeamAssignments)}."
            ];
        }

        return blockers.Count == 0
            ? new CampaignMayClose()
            : new CampaignCloseBlocked(
                "Resolve all campaign close blockers before closing this campaign.",
                blockers)
            {
                UndecidedCount = undecidedCount,
                IneligibleCount = ineligibleAssignments.Length,
                ArchivedTeamCount = archivedTeamAssignments.Length
            };
    }
}
