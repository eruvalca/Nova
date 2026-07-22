using Nova.Shared.Enums;
using OneOf;

namespace Nova.Features.Campaigns;

/// <summary>
/// Reports that loaded campaign placement facts permit applying the requested mutation.
/// </summary>
internal readonly record struct PlacementMayApply;

/// <summary>
/// Reports that the placement belongs to a closed campaign.
/// </summary>
internal readonly record struct PlacementCampaignClosed;

/// <summary>
/// Reports that the placement belongs to an archived player.
/// </summary>
internal readonly record struct PlacementPlayerArchived;

/// <summary>
/// Reports that a requested team was unavailable in the current tenant.
/// </summary>
internal readonly record struct PlacementTeamUnavailable;

/// <summary>
/// Reports that the requested placement team is archived.
/// </summary>
internal readonly record struct PlacementTeamArchived;

/// <summary>
/// Reports that the player does not satisfy the requested team's graduation-year requirement.
/// </summary>
internal readonly record struct PlacementTeamIneligible;

/// <summary>
/// Captures the fresh lifecycle and eligibility facts required for a placement decision.
/// </summary>
/// <param name="CampaignStatus">The campaign lifecycle status.</param>
/// <param name="PlayerLifecycleStatus">The player lifecycle status.</param>
/// <param name="PlayerGraduationYear">The player's graduation year.</param>
/// <param name="TeamWasRequested">Whether the input requested an assigned team.</param>
/// <param name="TeamWasFound">Whether that team was visible in the current tenant.</param>
/// <param name="TeamLifecycleStatus">The requested team's lifecycle status when found.</param>
/// <param name="TeamGraduationYear">The requested team's graduation year when found.</param>
internal sealed record PlacementDecisionContext(
    CampaignStatus CampaignStatus,
    LifecycleStatus PlayerLifecycleStatus,
    int PlayerGraduationYear,
    bool TeamWasRequested,
    bool TeamWasFound,
    LifecycleStatus? TeamLifecycleStatus,
    int? TeamGraduationYear);

/// <summary>
/// Evaluates deterministic placement lifecycle and eligibility rules over freshly loaded facts.
/// </summary>
internal static class CampaignPlacementPolicy
{
    /// <summary>
    /// Determines whether the requested placement may be applied.
    /// </summary>
    /// <param name="context">The fresh campaign, player, and optional team facts.</param>
    /// <returns>An approval or the first rejection in existing placement precedence order.</returns>
    internal static OneOf<
        PlacementMayApply,
        PlacementCampaignClosed,
        PlacementPlayerArchived,
        PlacementTeamUnavailable,
        PlacementTeamArchived,
        PlacementTeamIneligible> Evaluate(PlacementDecisionContext context)
    {
        if (context.CampaignStatus == CampaignStatus.Closed)
        {
            return new PlacementCampaignClosed();
        }

        if (context.PlayerLifecycleStatus == LifecycleStatus.Archived)
        {
            return new PlacementPlayerArchived();
        }

        if (!context.TeamWasRequested)
        {
            return new PlacementMayApply();
        }

        if (!context.TeamWasFound)
        {
            return new PlacementTeamUnavailable();
        }

        if (context.TeamLifecycleStatus == LifecycleStatus.Archived)
        {
            return new PlacementTeamArchived();
        }

        return context.PlayerGraduationYear < context.TeamGraduationYear!.Value
            ? new PlacementTeamIneligible()
            : new PlacementMayApply();
    }
}
