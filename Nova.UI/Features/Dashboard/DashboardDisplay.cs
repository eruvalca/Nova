using Nova.Shared.Enums;
using Nova.Shared.Features.Dashboard;
using Nova.UI.Features.Campaigns.Components;

namespace Nova.UI.Features.Dashboard;

/// <summary>Provides feature-local display helpers for structured dashboard activity.</summary>
internal static class DashboardDisplay
{
    /// <summary>Builds a complete, readable sentence for one role-shaped activity event.</summary>
    public static string ActivitySentence(DashboardActivityItemDto item)
        => (item.Kind, item.Context) switch
        {
            (DashboardActivityEventKind.CampaignDraftCreated, CampaignActivityContextDto context)
                => $"{context.ActorDisplayName} created Draft {context.CampaignName}.",
            (DashboardActivityEventKind.CampaignDraftDeleted, CampaignActivityContextDto context)
                => $"{context.ActorDisplayName} deleted Draft {context.CampaignName}.",
            (DashboardActivityEventKind.CampaignOpened, CampaignActivityContextDto context)
                => $"{context.ActorDisplayName} opened {context.CampaignName}.",
            (DashboardActivityEventKind.CampaignClosed, CampaignActivityContextDto context)
                => $"{context.ActorDisplayName} closed {context.CampaignName}.",
            (DashboardActivityEventKind.CampaignReopened, CampaignActivityContextDto context)
                => $"{context.ActorDisplayName} reopened {context.CampaignName}.",
            (DashboardActivityEventKind.PlacementAssigned, PlacementActivityContextDto context)
                => $"{context.ActorDisplayName} placed {context.PlayerDisplayName} on {PlacementLabel(context.Current)} in {context.CampaignName}.",
            (DashboardActivityEventKind.PlacementReassigned, PlacementActivityContextDto context)
                => $"{context.ActorDisplayName} moved {context.PlayerDisplayName} from {PlacementLabel(context.Previous)} to {PlacementLabel(context.Current)} in {context.CampaignName}.",
            (DashboardActivityEventKind.PlacementOutcomeChanged, PlacementActivityContextDto context)
                => $"{context.ActorDisplayName} changed {context.PlayerDisplayName}'s placement from {PlacementLabel(context.Previous)} to {PlacementLabel(context.Current)} in {context.CampaignName}.",
            (DashboardActivityEventKind.JoinRequestSubmitted, JoinRequestActivityContextDto context)
                => $"{context.RequesterDisplayName} submitted a request to join the club.",
            (DashboardActivityEventKind.JoinRequestCancelled, JoinRequestActivityContextDto context)
                => $"{context.RequesterDisplayName} cancelled their request to join the club.",
            (DashboardActivityEventKind.JoinRequestRejected, JoinRequestActivityContextDto context)
                => $"{context.ActorDisplayName} rejected {context.RequesterDisplayName}'s request to join.",
            (DashboardActivityEventKind.JoinRequestApproved, JoinRequestActivityContextDto context)
                => $"{context.ActorDisplayName} approved {context.RequesterDisplayName}'s request to join.",
            (DashboardActivityEventKind.MemberJoined, MembershipActivityContextDto context)
                => $"{context.MemberDisplayName} joined the club.",
            (DashboardActivityEventKind.MemberPromoted, MembershipActivityContextDto context)
                => $"{ActorName(context)} promoted {context.MemberDisplayName} to club administrator.",
            (DashboardActivityEventKind.MemberDemoted, MembershipActivityContextDto context)
                => $"{ActorName(context)} demoted {context.MemberDisplayName} from club administrator.",
            (DashboardActivityEventKind.MemberRemoved, MembershipActivityContextDto context)
                => $"{ActorName(context)} removed {context.MemberDisplayName} from the club.",
            (DashboardActivityEventKind.MemberLeft, MembershipActivityContextDto context)
                => $"{context.MemberDisplayName} left the club.",
            _ => "Club activity was recorded."
        };

    /// <summary>Formats an activity event timestamp for display.</summary>
    public static string FormatActivityDate(DateTimeOffset eventAt)
        => eventAt.ToString("MMM d, yyyy");

    /// <summary>Builds a readable placement-state label.</summary>
    private static string PlacementLabel(PlacementSnapshotDto snapshot)
        => snapshot.Outcome == PlacementOutcome.Assigned && !string.IsNullOrWhiteSpace(snapshot.TeamName)
            ? snapshot.TeamName
            : CampaignRosterDisplay.OutcomeLabel(snapshot.Outcome);

    /// <summary>Returns the distinct actor snapshot, or a neutral administrator label.</summary>
    private static string ActorName(MembershipActivityContextDto context)
        => string.IsNullOrWhiteSpace(context.ActorDisplayName)
            ? "A club administrator"
            : context.ActorDisplayName;
}
