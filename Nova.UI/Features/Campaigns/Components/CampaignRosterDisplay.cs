using Nova.Shared.Enums;
using Nova.UI.Features.Players;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Provides shared display helpers for campaign roster outcomes and tag chips.
/// </summary>
internal static class CampaignRosterDisplay
{
    /// <summary>
    /// Maps a placement outcome to its Bootstrap badge classes.
    /// </summary>
    /// <param name="outcome">The placement outcome.</param>
    /// <returns>The badge class tokens.</returns>
    public static string OutcomeBadgeClass(PlacementOutcome outcome) => outcome switch
    {
        PlacementOutcome.Assigned => "text-bg-success",
        PlacementOutcome.NotSelected => "bg-warning text-dark",
        PlacementOutcome.Withdrawn => "text-bg-danger",
        _ => "text-bg-secondary"
    };

    /// <summary>
    /// Maps a placement outcome to its display label.
    /// </summary>
    /// <param name="outcome">The placement outcome.</param>
    /// <returns>The display label.</returns>
    public static string OutcomeLabel(PlacementOutcome outcome) => outcome switch
    {
        PlacementOutcome.Assigned => "Assigned",
        PlacementOutcome.NotSelected => "Not selected",
        PlacementOutcome.Withdrawn => "Withdrawn",
        _ => "Undecided"
    };

    /// <summary>
    /// Builds a safe inline badge style for a tag color.
    /// </summary>
    /// <param name="color">The tag color token.</param>
    /// <returns>The sanitized inline style string.</returns>
    public static string BuildTagStyle(string color) => PlayerTagStyle.BuildBadgeStyle(color);
}
