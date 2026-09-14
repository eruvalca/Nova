using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// One Place queue row projected from whichever authoritative read the campaign lifecycle allows.
/// </summary>
/// <remarks>
/// An Active campaign renders <see cref="CampaignEffectivePlacementItem"/>, which carries eligibility,
/// correction evidence, the local concurrency token, and the effective season placement beside the
/// campaign-local decision. A Closed campaign renders <see cref="ClosedCampaignRosterItem"/>, which carries
/// only this campaign's immutable local outcome and its original attribution. Projecting both into one row
/// keeps the queue markup, ordering, and labels single, while the absent Active-only evidence stays
/// <see langword="null"/> rather than being invented for a Closed record.
/// </remarks>
public sealed record CampaignPlaceQueueRow
{
    /// <summary>The campaign-assignment identifier for this participant.</summary>
    public required long PlayerCampaignAssignmentId { get; init; }

    /// <summary>The player identifier used by player-detail links.</summary>
    public required long PlayerId { get; init; }

    /// <summary>The player's given name.</summary>
    public required string FirstName { get; init; }

    /// <summary>The player's family name.</summary>
    public required string LastName { get; init; }

    /// <summary>The player's graduation year, which is the hard team-compatibility rule.</summary>
    public required int GraduationYear { get; init; }

    /// <summary>The tryout number, or <see langword="null"/> when the participant has none.</summary>
    public int? TryoutNumber { get; init; }

    /// <summary>This campaign's saved decision, or <see langword="null"/> when nothing is saved.</summary>
    public CampaignSavedPlacementDecision? LocalDecision { get; init; }

    /// <summary>This campaign's team display evidence, including archived teams.</summary>
    public CampaignParticipantTeamSummaryDto? LocalTeam { get; init; }

    /// <summary>The effective season placement's team, or <see langword="null"/> when none applies.</summary>
    public CampaignParticipantTeamSummaryDto? EffectiveTeam { get; init; }

    /// <summary>The campaign that sourced the effective season placement, when one exists.</summary>
    public string? EffectiveSourceCampaignName { get; init; }

    /// <summary>The Active work classification, or <see langword="null"/> for a Closed immutable record.</summary>
    public EffectivePlacementEligibility? Eligibility { get; init; }

    /// <summary>Why a saved assignment cannot carry effective membership; <c>None</c> when it can.</summary>
    public PlacementCorrectionReason CorrectionReason { get; init; }

    /// <summary>The local mutation token, or <see langword="null"/> for a Closed immutable record.</summary>
    public Guid? ConcurrencyToken { get; init; }

    /// <summary>
    /// The player's lifecycle, or <see langword="null"/> for a Closed record whose read does not carry it.
    /// An archived player cannot receive a decision.
    /// </summary>
    public LifecycleStatus? PlayerLifecycleStatus { get; init; }

    /// <summary>Campaign-applied tags enriched by the read.</summary>
    public IReadOnlyList<CampaignParticipantTagSummaryDto> AppliedTags { get; init; } = [];

    /// <summary>The outcome that sourced the effective season placement, when one exists.</summary>
    public PlacementOutcome? EffectiveOutcome { get; init; }

    /// <summary>The participant's full display name.</summary>
    public string DisplayName => $"{FirstName} {LastName}";

    /// <summary>This campaign's written outcome; <c>Undecided</c> when no decision is saved.</summary>
    public PlacementOutcome LocalOutcome => LocalDecision?.Outcome ?? PlacementOutcome.Undecided;

    /// <summary>
    /// Projects an Active campaign row, preserving eligibility, correction evidence, and the local token.
    /// </summary>
    /// <param name="item">The effective-placement row to project.</param>
    /// <returns>The unified queue row.</returns>
    public static CampaignPlaceQueueRow FromActive(CampaignEffectivePlacementItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new CampaignPlaceQueueRow
        {
            PlayerCampaignAssignmentId = item.PlayerCampaignAssignmentId,
            PlayerId = item.PlayerId,
            FirstName = item.FirstName,
            LastName = item.LastName,
            GraduationYear = item.GraduationYear,
            TryoutNumber = item.TryoutNumber,
            LocalDecision = item.LocalDecision,
            LocalTeam = item.LocalTeam,
            EffectiveTeam = item.EffectiveTeam,
            EffectiveSourceCampaignName = item.EffectiveDecision?.CampaignName,
            EffectiveOutcome = item.EffectiveDecision?.Decision.Outcome,
            Eligibility = item.Eligibility,
            CorrectionReason = item.CorrectionReason,
            ConcurrencyToken = item.ConcurrencyToken,
            PlayerLifecycleStatus = item.PlayerLifecycleStatus,
            AppliedTags = item.AppliedTags
        };
    }

    /// <summary>
    /// Projects a Closed campaign row from its immutable local record, leaving every Active-only field absent.
    /// </summary>
    /// <param name="item">The Closed roster row to project.</param>
    /// <returns>The unified queue row.</returns>
    public static CampaignPlaceQueueRow FromClosed(ClosedCampaignRosterItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new CampaignPlaceQueueRow
        {
            PlayerCampaignAssignmentId = item.PlayerCampaignAssignmentId,
            PlayerId = item.PlayerId,
            FirstName = item.FirstName,
            LastName = item.LastName,
            GraduationYear = item.GraduationYear,
            TryoutNumber = item.TryoutNumber,
            LocalDecision = item.Source.Decision,
            LocalTeam = item.Source.Team,
            AppliedTags = item.AppliedTags
        };
    }
}
