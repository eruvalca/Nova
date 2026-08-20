using Nova.Shared.Enums;
using Nova.Shared.Features.Dashboard;
using Nova.UI.Features.Campaigns.Components;

namespace Nova.UI.Features.Dashboard;

/// <summary>
/// Provides feature-local display helpers for the club dashboard recent-activity feed.
/// </summary>
internal static class DashboardDisplay
{
    /// <summary>
    /// Maps a dashboard activity event to its display verb phrase, including the player, tag, or
    /// placement outcome context specific to the event kind.
    /// </summary>
    /// <param name="item">The activity event row.</param>
    /// <returns>The display verb phrase for the event.</returns>
    public static string ActivityVerb(DashboardActivityItemDto item) => item.Kind switch
    {
        DashboardActivityEventKind.NoteAdded => $"added a note to {item.PlayerDisplayName}",
        DashboardActivityEventKind.TagApplied => $"applied tag \"{item.TagName}\" to {item.PlayerDisplayName}",
        DashboardActivityEventKind.PlacementSet =>
            $"set {item.PlayerDisplayName}'s placement to {CampaignRosterDisplay.OutcomeLabel(item.PlacementOutcome ?? PlacementOutcome.Undecided)}",
        DashboardActivityEventKind.CampaignClosed => "closed the campaign",
        DashboardActivityEventKind.CampaignReopened => "reopened the campaign",
        _ => "updated the campaign"
    };

    /// <summary>
    /// Formats an activity event timestamp for display using the shared <c>"MMM d, yyyy"</c> format.
    /// </summary>
    /// <param name="eventAt">The event timestamp.</param>
    /// <returns>The formatted timestamp.</returns>
    public static string FormatActivityDate(DateTimeOffset eventAt)
        => eventAt.ToString("MMM d, yyyy");
}
