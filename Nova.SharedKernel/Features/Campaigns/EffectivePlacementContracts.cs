using System.Text.Json.Serialization;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Results;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>The ordinary placement work classification, independent of override authority.</summary>
public enum EffectivePlacementEligibility
{
    /// <summary>The active player needs a valid decision.</summary>
    NeedsPlacement,
    /// <summary>A valid season assignment permits optional reassignment.</summary>
    OptionalReassignment,
    /// <summary>A local NotSelected decision resolves the campaign.</summary>
    Resolved,
    /// <summary>The player is archived or withdrawn.</summary>
    Unavailable,
}

/// <summary>Explains why a saved assignment cannot carry effective roster membership.</summary>
public enum PlacementCorrectionReason
{
    /// <summary>No invalid assigned team.</summary>
    None,
    /// <summary>The saved team is not visible in the tenant.</summary>
    TeamUnavailable,
    /// <summary>The saved team is archived.</summary>
    TeamArchived,
    /// <summary>The player does not satisfy the team's graduation year.</summary>
    TeamIncompatible,
}

/// <summary>Identity of the season used by a roster response.</summary>
public sealed record PlacementSeasonIdentity(long SeasonId, string Name);

/// <summary>Identity and lifecycle of a campaign-specific read.</summary>
public sealed record PlacementCampaignIdentity(long CampaignId, string Name,
    [property: JsonRequired] CampaignStatus Status, PlacementSeasonIdentity Season);

/// <summary>A saved decision's source identity and team evidence, even when its team is invalid.</summary>
public sealed record PlacementDecisionSource(
    CampaignSavedPlacementDecision Decision,
    string CampaignName,
    CampaignParticipantTeamSummaryDto? Team);

/// <summary>One player on a valid effective current-season team.</summary>
public sealed record CurrentSeasonRosterItem(
    long PlayerId, string FirstName, string LastName, int GraduationYear,
    PlacementDecisionSource Source);

/// <summary>Season identity and membership share one response snapshot. An absent season has an empty page.</summary>
public sealed record CurrentSeasonRosterResult(
    [property: JsonRequired] PlacementSeasonIdentity? Season,
    PagedResult<CurrentSeasonRosterItem> Roster);

/// <summary>One campaign participant with separately owned local and effective decision evidence.</summary>
public sealed record CampaignEffectivePlacementItem(
    long PlayerCampaignAssignmentId, long PlayerId, string FirstName, string LastName,
    int GraduationYear, int? TryoutNumber, [property: JsonRequired] LifecycleStatus PlayerLifecycleStatus,
    Guid ConcurrencyToken,
    [property: JsonRequired] CampaignSavedPlacementDecision? LocalDecision,
    [property: JsonRequired] PlacementDecisionSource? EffectiveDecision,
    [property: JsonRequired] CampaignParticipantTeamSummaryDto? EffectiveTeam,
    [property: JsonRequired] EffectivePlacementEligibility Eligibility,
    [property: JsonRequired] PlacementCorrectionReason CorrectionReason)
{
    /// <summary>The saved campaign-local team's display evidence, including archived teams.</summary>
    [JsonRequired]
    public CampaignParticipantTeamSummaryDto? LocalTeam { get; init; }

    /// <summary>Campaign-applied tags enriched within the response snapshot.</summary>
    [JsonRequired]
    public IReadOnlyList<CampaignParticipantTagSummaryDto> AppliedTags { get; init; } = [];
}

/// <summary>Unfiltered whole-campaign work counts from one aggregate statement.</summary>
public sealed record EffectivePlacementCounts(
    [property: JsonRequired] int NeedsPlacement,
    [property: JsonRequired] int OptionalReassignment,
    [property: JsonRequired] int Resolved,
    [property: JsonRequired] int Unavailable);

/// <summary>The Active campaign identity, working page, and unfiltered counts share one response snapshot.</summary>
public sealed record CampaignEffectivePlacementsResult(
    PlacementCampaignIdentity Campaign,
    EffectivePlacementCounts Counts,
    PagedResult<CampaignEffectivePlacementItem> Participants);

/// <summary>One immutable campaign-local outcome with its original decision attribution.</summary>
public sealed record ClosedCampaignRosterItem(
    long PlayerCampaignAssignmentId, long PlayerId, string FirstName, string LastName,
    int GraduationYear, int? TryoutNumber, PlacementDecisionSource Source)
{
    /// <summary>The player's present archive context; archival never removes the saved outcome.</summary>
    [JsonRequired]
    public LifecycleStatus PlayerLifecycleStatus { get; init; }

    /// <summary>The saved team's present archive context, or null for non-assignment outcomes.</summary>
    [JsonRequired]
    public LifecycleStatus? TeamLifecycleStatus { get; init; }

    /// <summary>Campaign-applied tags enriched within the response snapshot.</summary>
    [JsonRequired]
    public IReadOnlyList<CampaignParticipantTagSummaryDto> AppliedTags { get; init; } = [];
}

/// <summary>A consistent Closed lifecycle and local-outcome page; later campaigns never contribute rows.</summary>
public sealed record ClosedCampaignRosterResult(
    PlacementCampaignIdentity Campaign,
    PagedResult<ClosedCampaignRosterItem> Participants)
{
    /// <summary>Unfiltered local outcome totals from the same snapshot as the page.</summary>
    [JsonRequired]
    public CampaignPlacementSummaryDto Summary { get; init; } = null!;

    /// <summary>The latest durable close event, including the original actor name after departure.</summary>
    [JsonRequired]
    public CampaignActivityItemDto ClosingEvent { get; init; } = null!;

    /// <summary>The whole campaign's participant count before discovery filters or paging.</summary>
    [JsonRequired]
    public int ParticipantCount { get; init; }
}
