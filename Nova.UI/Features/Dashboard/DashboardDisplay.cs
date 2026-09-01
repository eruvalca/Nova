using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;

namespace Nova.UI.Features.Dashboard;

/// <summary>
/// Provides feature-local display helpers for the club dashboard recent-activity feed.
/// </summary>
internal static class DashboardDisplay
{
    /// <summary>
    /// Maps a club activity event to its display verb phrase, using the family-shaped context for
    /// campaign, placement, membership, or role specifics.
    /// </summary>
    /// <param name="item">The activity event row.</param>
    /// <returns>The display verb phrase for the event.</returns>
    public static string ActivityVerb(ClubActivityItemDto item) => (item.Kind, item.Context) switch
    {
        (ActivityEventKind.CampaignDraftCreated, CampaignLifecycleContext c) => $"drafted {c.CampaignName}",
        (ActivityEventKind.CampaignDraftDeleted, CampaignLifecycleContext c) => $"deleted draft {c.CampaignName}",
        (ActivityEventKind.CampaignOpened, CampaignLifecycleContext c) => $"opened {c.CampaignName}",
        (ActivityEventKind.CampaignClosed, CampaignLifecycleContext c) => $"closed {c.CampaignName}",
        (ActivityEventKind.CampaignReopened, CampaignLifecycleContext c) => $"reopened {c.CampaignName}",
        (ActivityEventKind.PlacementAssigned, PlacementContext p) =>
            $"assigned {p.PlayerDisplayName} to {p.TeamName ?? "a team"} in {p.CampaignName}",
        (ActivityEventKind.PlacementNotSelected, PlacementContext p) =>
            $"marked {p.PlayerDisplayName} not selected for {p.CampaignName}",
        (ActivityEventKind.PlacementWithdrawn, PlacementContext p) =>
            $"withdrew {p.PlayerDisplayName}'s placement for {p.CampaignName}",
        (ActivityEventKind.PlacementReassigned, PlacementContext p) =>
            $"moved {p.PlayerDisplayName} to {p.TeamName ?? "a new team"} in {p.CampaignName}",
        (ActivityEventKind.PlacementOutcomeReplaced, PlacementContext p) =>
            $"updated {p.PlayerDisplayName}'s placement for {p.CampaignName}",
        (ActivityEventKind.PlacementSuperseded, PlacementContext p) =>
            $"superseded {p.PlayerDisplayName}'s placement for {p.CampaignName}",
        (ActivityEventKind.JoinRequestSubmitted, JoinRequestContext j) =>
            $"requested to join the club: {j.RequesterDisplayName}",
        (ActivityEventKind.JoinRequestCancelled, JoinRequestContext j) =>
            $"withdrew a join request: {j.RequesterDisplayName}",
        (ActivityEventKind.JoinRequestRejected, JoinRequestContext j) =>
            $"rejected {j.RequesterDisplayName}'s join request",
        (ActivityEventKind.MemberJoined, MembershipContext m) =>
            m.ApprovedByActorName is string approver
                ? $"approved {m.MemberDisplayName}'s membership"
                : $"{m.MemberDisplayName} joined the club",
        (ActivityEventKind.MemberRemoved, MembershipContext m) => $"removed {m.MemberDisplayName}",
        (ActivityEventKind.MemberLeft, MembershipContext m) => $"{m.MemberDisplayName} left the club",
        (ActivityEventKind.MemberPromoted, MemberRoleContext r) => $"promoted {r.MemberDisplayName} to {r.Role}",
        (ActivityEventKind.MemberDemoted, MemberRoleContext r) => $"demoted {r.MemberDisplayName}",
        _ => "updated the club"
    };

    /// <summary>
    /// Formats an activity event timestamp for display using the shared <c>"MMM d, yyyy"</c> format.
    /// </summary>
    /// <param name="eventAt">The event timestamp.</param>
    /// <returns>The formatted timestamp.</returns>
    public static string FormatActivityDate(DateTimeOffset eventAt)
        => eventAt.ToString("MMM d, yyyy");
}
