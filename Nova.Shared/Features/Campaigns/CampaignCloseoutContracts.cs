using Nova.Shared.Enums;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// One condition-keyed close blocker: the condition key, the affected participation count, and the
/// affected campaign-assignment ids that drive the unresolved-participant drill-down.
/// </summary>
/// <param name="Condition">The shared blocker condition key (one of <see cref="CloseoutBlockerConditions"/>).</param>
/// <param name="Count">The number of affected campaign assignments.</param>
/// <param name="AssignmentIds">The affected campaign-assignment identifiers, in stable policy order.</param>
/// <param name="Message">The human-readable blocker message produced by the foundation policy.</param>
public sealed record CampaignCloseoutBlockerDto(
    string Condition,
    int Count,
    IReadOnlyList<long> AssignmentIds,
    string Message);

/// <summary>
/// Authoritative closeout readiness for a campaign: the placement summary from the placement query
/// service plus the foundation policy verdict and its condition-keyed blockers. Counts and blockers
/// are never recomputed here.
/// </summary>
/// <param name="CampaignId">The campaign identifier.</param>
/// <param name="Status">The campaign lifecycle status at the time of the read.</param>
/// <param name="IsReady">Whether the foundation policy reports the campaign may close.</param>
/// <param name="Summary">The authoritative whole-campaign placement outcome summary.</param>
/// <param name="Blockers">The condition-keyed blockers when the campaign is not ready, otherwise empty.</param>
public sealed record CampaignCloseoutReadinessDto(
    long CampaignId,
    CampaignStatus Status,
    bool IsReady,
    CampaignPlacementSummaryDto Summary,
    IReadOnlyList<CampaignCloseoutBlockerDto> Blockers);
