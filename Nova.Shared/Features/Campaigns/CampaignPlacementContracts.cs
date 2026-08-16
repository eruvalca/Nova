using Nova.Shared.Enums;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Bounded placement roster row for a campaign participant. Carries every persisted field needed to
/// render the row and to submit an <see cref="UpdateCampaignPlacementInput"/> without a per-row detail request.
/// </summary>
/// <param name="PlayerCampaignAssignmentId">The campaign-assignment identifier for this participant.</param>
/// <param name="PlayerId">The player identifier.</param>
/// <param name="DisplayName">The participant display name.</param>
/// <param name="GraduationYear">The participant graduation year.</param>
/// <param name="PlacementOutcome">The current placement outcome.</param>
/// <param name="Team">The optional assigned team summary.</param>
/// <param name="ConcurrencyToken">The optimistic-concurrency token required by <see cref="UpdateCampaignPlacementInput"/>.</param>
public sealed record CampaignPlacementRosterItem(
    long PlayerCampaignAssignmentId,
    long PlayerId,
    string DisplayName,
    int GraduationYear,
    PlacementOutcome PlacementOutcome,
    CampaignParticipantTeamSummaryDto? Team,
    Guid ConcurrencyToken);

/// <summary>
/// Authoritative whole-campaign placement outcome counts, independent of paging and filters.
/// </summary>
/// <param name="AssignedCount">The number of participants assigned to a team.</param>
/// <param name="NotSelectedCount">The number of participants not selected for a team.</param>
/// <param name="WithdrawnCount">The number of participants who withdrew.</param>
/// <param name="UndecidedCount">The number of participants whose placement is still undecided.</param>
/// <param name="TotalCount">The total number of participants in the campaign.</param>
public sealed record CampaignPlacementSummaryDto(
    int AssignedCount,
    int NotSelectedCount,
    int WithdrawnCount,
    int UndecidedCount,
    int TotalCount);
