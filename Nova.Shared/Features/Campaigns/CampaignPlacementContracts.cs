using Nova.Shared.Enums;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Reports the concurrency token after a placement save succeeds. An identical save returns the submitted token; a meaningful mutation returns a replacement.
/// </summary>
/// <param name="ConcurrencyToken">The token callers must use for the next mutation.</param>
public readonly record struct PlacementMutationSuccess(Guid ConcurrencyToken);

/// <summary>
/// Bounded placement roster row for a campaign participant. Carries every persisted field needed to
/// render the row and to submit an <see cref="UpdateCampaignPlacementInput"/> without a per-row detail request.
/// </summary>
/// <param name="PlayerCampaignAssignmentId">The campaign-assignment identifier for this participant.</param>
/// <param name="PlayerId">The player identifier.</param>
/// <param name="DisplayName">The participant display name.</param>
/// <param name="FirstName">The participant first name, used to verify the server ordering contract.</param>
/// <param name="LastName">The participant last name, used to verify the server ordering contract.</param>
/// <param name="GraduationYear">The participant graduation year.</param>
/// <param name="PlacementOutcome">The campaign-local state; Undecided denotes participation without a saved decision.</param>
/// <param name="Team">The optional assigned team summary.</param>
/// <param name="ConcurrencyToken">The optimistic-concurrency token required by <see cref="UpdateCampaignPlacementInput"/>.</param>
public sealed record CampaignPlacementRosterItem(
    long PlayerCampaignAssignmentId,
    long PlayerId,
    string DisplayName,
    string FirstName,
    string LastName,
    int GraduationYear,
    PlacementOutcome PlacementOutcome,
    CampaignParticipantTeamSummaryDto? Team,
    Guid ConcurrencyToken)
{
    /// <summary>
    /// Gets this campaign's explicit saved decision, or null for participation without a decision.
    /// This is campaign-local history, not an effective-season roster projection.
    /// </summary>
    public CampaignSavedPlacementDecision? SavedDecision { get; init; }
}

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
