using Microsoft.AspNetCore.Components;
using Nova.SharedKernel.Features.Campaigns;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>Renders already loaded effective-placement evidence without additional reads.</summary>
public partial class CampaignEffectivePlacementSummary
{
    /// <summary>The already loaded row; rendering never performs a history or placement read.</summary>
    [Parameter] public CampaignEffectivePlacementItem? Item { get; set; }

    private static string EligibilityLabel(EffectivePlacementEligibility value) => value switch
    {
        EffectivePlacementEligibility.NeedsPlacement => "Needs placement",
        EffectivePlacementEligibility.OptionalReassignment => "Optional reassignment",
        EffectivePlacementEligibility.Resolved => "Resolved",
        _ => "Unavailable",
    };
    private static string CorrectionLabel(PlacementCorrectionReason value) => value switch
    {
        PlacementCorrectionReason.TeamArchived => "Saved team is archived; correction required.",
        PlacementCorrectionReason.TeamIncompatible => "Saved team does not match the graduation year; correction required.",
        _ => "Saved team is unavailable; correction required.",
    };
}
