using Nova.Shared.Enums;
using Nova.Shared.Features.Dashboard;

namespace Nova.Features.Dashboard;

/// <summary>
/// Pure, deterministic merge policy for the club dashboard recent-activity feed. It owns only the
/// cross-source ordering and bound rule: <see cref="DashboardActivityEventRow.EventAt"/> descending,
/// then <see cref="DashboardActivityEventKind"/> rank descending, then <see cref="DashboardActivityEventRow.EventId"/>
/// descending, bounded by the requested limit.
/// </summary>
internal static class DashboardActivityFeedPolicy
{
    /// <summary>
    /// Merges per-source activity rows into one bounded, deterministically ordered list.
    /// </summary>
    /// <param name="rows">The concatenated per-source event rows.</param>
    /// <param name="limit">The maximum number of rows to return.</param>
    /// <returns>The bounded rows in newest-first deterministic order.</returns>
    public static IReadOnlyList<DashboardActivityEventRow> OrderAndBound(
        IReadOnlyList<DashboardActivityEventRow> rows,
        int limit)
        => rows
            .OrderByDescending(row => row.EventAt)
            .ThenByDescending(row => (int)row.Kind)
            .ThenByDescending(row => row.EventId)
            .Take(limit)
            .ToList()
            .AsReadOnly();
}

/// <summary>
/// One merged activity event row carrying the full context needed to build the final DTO, before
/// actor display-name resolution. The service projects each source into this shape; the policy only
/// reads the three ordering keys.
/// </summary>
/// <param name="Kind">The event kind, whose numeric value is the fixed tie-break rank.</param>
/// <param name="EventId">The per-kind entity identifier used as the final ordering tie-break.</param>
/// <param name="EventAt">When the event occurred.</param>
/// <param name="ActorUserId">The identifier of the user who performed the action.</param>
/// <param name="CampaignId">The campaign identifier the event belongs to.</param>
/// <param name="CampaignName">The campaign name the event belongs to.</param>
/// <param name="PlayerCampaignAssignmentId">The participant assignment identifier, when player-scoped.</param>
/// <param name="PlayerDisplayName">The participant display name, when player-scoped.</param>
/// <param name="TagName">The applied tag name, when a tag event.</param>
/// <param name="PlacementOutcome">The placement outcome, when a placement event.</param>
/// <param name="LifecycleEventType">The lifecycle transition type, when a lifecycle event.</param>
internal sealed record DashboardActivityEventRow(
    DashboardActivityEventKind Kind,
    long EventId,
    DateTimeOffset EventAt,
    long ActorUserId,
    long CampaignId,
    string CampaignName,
    long? PlayerCampaignAssignmentId,
    string? PlayerDisplayName,
    string? TagName,
    PlacementOutcome? PlacementOutcome,
    CampaignLifecycleEventType? LifecycleEventType);
