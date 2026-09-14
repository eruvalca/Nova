using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Provides written labels for the Place destination's eligibility, correction, and outcome states.
/// </summary>
/// <remarks>
/// Every state the Place surface renders carries words. Color is never the only expression of a state, and no
/// label is invented for a state the authoritative read did not report.
/// </remarks>
internal static class CampaignPlaceDisplay
{
    /// <summary>
    /// Labels one Active work classification in the foundation's own vocabulary.
    /// </summary>
    /// <param name="eligibility">The classification to label.</param>
    /// <returns>The written label.</returns>
    public static string EligibilityLabel(EffectivePlacementEligibility eligibility) => eligibility switch
    {
        EffectivePlacementEligibility.NeedsPlacement => "Needs placement",
        EffectivePlacementEligibility.OptionalReassignment => "Assigned this season",
        EffectivePlacementEligibility.Resolved => "Not selected",
        _ => "Unavailable"
    };

    /// <summary>
    /// Labels why a saved assignment cannot carry effective season membership.
    /// </summary>
    /// <param name="reason">The correction reason to label.</param>
    /// <returns>The written label, or an empty string when no correction applies.</returns>
    public static string CorrectionLabel(PlacementCorrectionReason reason) => reason switch
    {
        PlacementCorrectionReason.TeamUnavailable => "The saved team is no longer available in this club.",
        PlacementCorrectionReason.TeamArchived => "The saved team is archived.",
        PlacementCorrectionReason.TeamIncompatible => "The saved team is not compatible with this graduation year.",
        _ => string.Empty
    };

    /// <summary>
    /// Labels one campaign-local outcome, including the undecided placeholder.
    /// </summary>
    /// <param name="outcome">The outcome to label.</param>
    /// <returns>The written label.</returns>
    public static string OutcomeLabel(PlacementOutcome outcome) => outcome switch
    {
        PlacementOutcome.Assigned => "Assigned",
        PlacementOutcome.NotSelected => "Not selected",
        PlacementOutcome.Withdrawn => "Withdrawn",
        _ => "Undecided"
    };

    /// <summary>
    /// Formats a saved decision's attribution as a written actor and time statement.
    /// </summary>
    /// <param name="decision">The saved decision to describe.</param>
    /// <returns>The written attribution statement.</returns>
    public static string AttributionLabel(CampaignSavedPlacementDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return $"Last changed by {decision.ActorDisplayName} · {decision.RecordedAt.ToLocalTime():g}";
    }
}
