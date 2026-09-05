using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using OneOf;

namespace Nova.Features.Campaigns;

/// <summary>
/// Reports that loaded campaign placement facts permit applying the requested mutation.
/// </summary>
/// <param name="IsNoOp">Whether the current campaign already records this exact decision.</param>
/// <param name="IsSupersession">Whether this save replaces an earlier campaign's effective decision.</param>
internal readonly record struct PlacementMayApply(bool IsNoOp = false, bool IsSupersession = false);

/// <summary>Rejects a mutation outside current-season opening order.</summary>
internal readonly record struct PlacementSeasonConflict;

/// <summary>Rejects editing a withdrawal in its owning campaign.</summary>
internal readonly record struct PlacementWithdrawalTerminal;

/// <summary>Requires administrator authority to supersede an earlier withdrawal.</summary>
internal readonly record struct PlacementWithdrawalRequiresAdmin;

/// <summary>Describes eligibility independently of whether a campaign has a local saved decision.</summary>
internal enum PlacementEligibility
{
    /// <summary>An active eligible player needs a decision.</summary>
    NeedsDecision,
    /// <summary>A valid effective assignment permits optional reassignment.</summary>
    OptionalReassignment,
    /// <summary>The owning campaign has a resolved teamless outcome.</summary>
    Resolved,
    /// <summary>The player is unavailable for ordinary placement.</summary>
    Unavailable,
}

/// <summary>
/// Reports that the placement belongs to a campaign that is not active.
/// </summary>
internal readonly record struct PlacementCampaignNotActive;

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
    int? TeamGraduationYear)
{
    /// <summary>Gets whether the target belongs to the club's authoritative current season.</summary>
    public bool IsCurrentSeason { get; init; } = true;
    /// <summary>Gets the target campaign identifier.</summary>
    public long CampaignId { get; init; }
    /// <summary>Gets the target season identifier.</summary>
    public long SeasonId { get; init; }
    /// <summary>Gets the target campaign's opening sequence.</summary>
    public long SeasonOpeningSequence { get; init; }
    /// <summary>Gets the latest saved same-season decision, including the target campaign when present.</summary>
    public CampaignSavedPlacementDecision? LatestDecision { get; init; }
    /// <summary>Gets whether the acting approved member is an administrator.</summary>
    public bool IsClubAdmin { get; init; }
    /// <summary>Gets the structurally validated requested explicit outcome.</summary>
    public PlacementOutcome RequestedOutcome { get; init; } = PlacementOutcome.NotSelected;
    /// <summary>Gets the structurally validated target team identifier.</summary>
    public long? RequestedTeamId { get; init; }
    /// <summary>Gets whether the latest assigned team is visible, active, and compatible.</summary>
    public bool EffectiveTeamIsValid { get; init; }
}

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
        PlacementCampaignNotActive,
        PlacementPlayerArchived,
        PlacementTeamUnavailable,
        PlacementTeamArchived,
        PlacementTeamIneligible,
        PlacementSeasonConflict,
        PlacementWithdrawalTerminal,
        PlacementWithdrawalRequiresAdmin> Evaluate(PlacementDecisionContext context)
    {
        if (context.CampaignStatus != CampaignStatus.Active)
        {
            return new PlacementCampaignNotActive();
        }

        if (context.PlayerLifecycleStatus == LifecycleStatus.Archived)
        {
            return new PlacementPlayerArchived();
        }

        var prior = ApplicableDecision(context);
        if (!context.IsCurrentSeason
            || prior is not null && (prior.SeasonOpeningSequence > context.SeasonOpeningSequence
                || prior.CampaignId != context.CampaignId && prior.SeasonOpeningSequence == context.SeasonOpeningSequence))
        {
            return new PlacementSeasonConflict();
        }

        var isLocal = prior?.CampaignId == context.CampaignId;
        if (isLocal && prior!.Outcome == context.RequestedOutcome && prior.TeamId == context.RequestedTeamId)
        {
            return new PlacementMayApply(IsNoOp: true);
        }

        if (prior?.Outcome == PlacementOutcome.Withdrawn)
        {
            if (isLocal)
            {
                return new PlacementWithdrawalTerminal();
            }

            if (!context.IsClubAdmin)
            {
                return new PlacementWithdrawalRequiresAdmin();
            }
        }

        if (!context.TeamWasRequested)
        {
            return new PlacementMayApply(IsSupersession: prior is not null && !isLocal);
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
            : new PlacementMayApply(IsSupersession: prior is not null && !isLocal);
    }

    /// <summary>Classifies participation without manufacturing a supplemental unresolved decision.</summary>
    /// <param name="context">The fresh season, player, and effective-decision facts.</param>
    /// <returns>The player's eligibility state for ordinary placement work.</returns>
    internal static PlacementEligibility GetEligibility(PlacementDecisionContext context)
    {
        if (!context.IsCurrentSeason || context.CampaignStatus != CampaignStatus.Active
            || context.PlayerLifecycleStatus != LifecycleStatus.Active)
        {
            return PlacementEligibility.Unavailable;
        }

        var prior = ApplicableDecision(context);
        return prior?.Outcome switch
        {
            PlacementOutcome.Withdrawn => PlacementEligibility.Unavailable,
            PlacementOutcome.Assigned when context.EffectiveTeamIsValid => PlacementEligibility.OptionalReassignment,
            PlacementOutcome.NotSelected when prior.CampaignId == context.CampaignId => PlacementEligibility.Resolved,
            _ => PlacementEligibility.NeedsDecision,
        };
    }

    /// <summary>Discards prior-season evidence and technical participation from decision semantics.</summary>
    /// <param name="context">The decision facts.</param>
    /// <returns>The applicable saved decision, if any.</returns>
    private static CampaignSavedPlacementDecision? ApplicableDecision(PlacementDecisionContext context)
        => context.LatestDecision is { } decision && decision.SeasonId == context.SeasonId
            && decision.Outcome != PlacementOutcome.Undecided ? decision : null;
}
