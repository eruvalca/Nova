using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;

namespace Nova.Features.Activity;

/// <summary>
/// Identifies the structured-detail family a kind maps onto.
/// </summary>
internal enum ActivityEventFamily
{
    /// <summary>
    /// Campaign lifecycle (draft, open, close, reopen).
    /// </summary>
    CampaignLifecycle = 0,

    /// <summary>
    /// Placement decisions and changes.
    /// </summary>
    Placement = 1,

    /// <summary>
    /// Join request submission, cancellation, and rejection.
    /// </summary>
    JoinRequest = 2,

    /// <summary>
    /// Club membership changes.
    /// </summary>
    Membership = 3,

    /// <summary>
    /// Member role promotions and demotions.
    /// </summary>
    MemberRole = 4,
}

/// <summary>
/// Evaluates the deterministic kind-to-family and kind-to-visibility rules of the activity
/// foundation, then classifies placement transitions into event kinds.
/// </summary>
internal static class ActivityEventPolicy
{
    /// <summary>
    /// Gets the family a kind belongs to.
    /// </summary>
    /// <param name="kind">The event kind.</param>
    /// <returns>The family the kind belongs to.</returns>
    internal static ActivityEventFamily FamilyFor(ActivityEventKind kind) => kind switch
    {
        ActivityEventKind.CampaignDraftCreated => ActivityEventFamily.CampaignLifecycle,
        ActivityEventKind.CampaignDraftDeleted => ActivityEventFamily.CampaignLifecycle,
        ActivityEventKind.CampaignOpened => ActivityEventFamily.CampaignLifecycle,
        ActivityEventKind.CampaignClosed => ActivityEventFamily.CampaignLifecycle,
        ActivityEventKind.CampaignReopened => ActivityEventFamily.CampaignLifecycle,
        ActivityEventKind.PlacementAssigned => ActivityEventFamily.Placement,
        ActivityEventKind.PlacementNotSelected => ActivityEventFamily.Placement,
        ActivityEventKind.PlacementWithdrawn => ActivityEventFamily.Placement,
        ActivityEventKind.PlacementReassigned => ActivityEventFamily.Placement,
        ActivityEventKind.PlacementOutcomeReplaced => ActivityEventFamily.Placement,
        ActivityEventKind.PlacementSuperseded => ActivityEventFamily.Placement,
        ActivityEventKind.JoinRequestSubmitted => ActivityEventFamily.JoinRequest,
        ActivityEventKind.JoinRequestCancelled => ActivityEventFamily.JoinRequest,
        ActivityEventKind.JoinRequestRejected => ActivityEventFamily.JoinRequest,
        ActivityEventKind.MemberJoined => ActivityEventFamily.Membership,
        ActivityEventKind.MemberRemoved => ActivityEventFamily.Membership,
        ActivityEventKind.MemberLeft => ActivityEventFamily.Membership,
        ActivityEventKind.MemberPromoted => ActivityEventFamily.MemberRole,
        ActivityEventKind.MemberDemoted => ActivityEventFamily.MemberRole,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unrecognized activity event kind."),
    };

    /// <summary>
    /// Determines whether a kind is visible only to club administrators. Draft events and
    /// unresolved join-request events are administrator-only (per the confirmed attention brief);
    /// every other event is visible to all club members.
    /// </summary>
    /// <param name="kind">The event kind.</param>
    /// <returns>True when only administrators may see the event.</returns>
    internal static bool IsAdminOnly(ActivityEventKind kind) => kind switch
    {
        ActivityEventKind.CampaignDraftCreated => true,
        ActivityEventKind.CampaignDraftDeleted => true,
        ActivityEventKind.JoinRequestSubmitted => true,
        ActivityEventKind.JoinRequestCancelled => true,
        ActivityEventKind.JoinRequestRejected => true,
        _ => false,
    };

    /// <summary>
    /// Classifies a placement transition into the event kind to append, or returns null when the
    /// save carries no meaningful placement change (no event is emitted). Supersession is not
    /// classified here: it is emitted explicitly by the campaign foundation when a later
    /// assignment replaces an earlier one.
    /// </summary>
    /// <param name="previousOutcome">The outcome before the save (null when no prior outcome).</param>
    /// <param name="previousTeamId">The team before the save (null when no prior team).</param>
    /// <param name="outcome">The outcome after the save.</param>
    /// <param name="teamId">The team after the save (null when no team).</param>
    /// <returns>The kind to append, or null when nothing meaningful changed.</returns>
    internal static ActivityEventKind? ClassifyPlacementTransition(
        PlacementOutcome? previousOutcome,
        long? previousTeamId,
        PlacementOutcome outcome,
        long? teamId)
    {
        if (previousOutcome == outcome && previousTeamId == teamId)
        {
            return null;
        }

        return outcome switch
        {
            PlacementOutcome.NotSelected => ActivityEventKind.PlacementNotSelected,
            PlacementOutcome.Withdrawn => ActivityEventKind.PlacementWithdrawn,
            PlacementOutcome.Assigned => (previousOutcome!.GetValueOrDefault(), previousTeamId, teamId) switch
            {
                (PlacementOutcome.Assigned, long prevTeam, var newTeam) when prevTeam != newTeam
                    && newTeam.HasValue => ActivityEventKind.PlacementReassigned,
                (PlacementOutcome.Assigned, _, _) => null,
                (PlacementOutcome.Undecided, _, _) => ActivityEventKind.PlacementAssigned,
                (var prev, _, _) when !Enum.IsDefined(prev) => ActivityEventKind.PlacementAssigned,
                _ => ActivityEventKind.PlacementOutcomeReplaced,
            },
            _ => null,
        };
    }

    /// <summary>
    /// Validates that a context record belongs to the family of the kind it annotates. The feed
    /// projection uses this to reject malformed persisted payloads instead of rendering them.
    /// </summary>
    /// <param name="kind">The event kind.</param>
    /// <param name="context">The annotated context record.</param>
    /// <returns>True when the context family matches the kind family.</returns>
    internal static bool ContextMatchesKind(ActivityEventKind kind, ClubActivityContext context)
    {
        var family = FamilyFor(kind);
        return (family, context) switch
        {
            (ActivityEventFamily.CampaignLifecycle, CampaignLifecycleContext) => true,
            (ActivityEventFamily.Placement, PlacementContext) => true,
            (ActivityEventFamily.JoinRequest, JoinRequestContext) => true,
            (ActivityEventFamily.Membership, MembershipContext) => true,
            (ActivityEventFamily.MemberRole, MemberRoleContext) => true,
            _ => false,
        };
    }
}
