using Nova.Shared.Enums;
using Nova.Shared.Results;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Bounded roster row for a campaign participant.
/// </summary>
public sealed record CampaignParticipantRosterItem(
    long PlayerCampaignAssignmentId,
    long PlayerId,
    string DisplayName,
    int GraduationYear,
    int? TryoutNumber,
    PlacementOutcome PlacementOutcome,
    CampaignParticipantTeamSummaryDto? Team,
    IReadOnlyList<CampaignParticipantTagSummaryDto> AppliedTags);

/// <summary>
/// Lightweight team summary used in roster rows.
/// </summary>
public sealed record CampaignParticipantTeamSummaryDto(
    long TeamId,
    string TeamName);

/// <summary>
/// Lightweight tag summary used in roster rows and detail payloads.
/// </summary>
public sealed record CampaignParticipantTagSummaryDto(
    long PlayerTagId,
    string TagName,
    string TagColor,
    bool IsArchived);

/// <summary>
/// Full participant detail payload for a campaign assignment.
/// </summary>
public sealed record CampaignParticipantDetailDto(
    long PlayerCampaignAssignmentId,
    long PlayerId,
    string DisplayName,
    int GraduationYear,
    int? TryoutNumber,
    PlacementOutcome PlacementOutcome,
    CampaignParticipantTeamSummaryDto? Team,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt,
    CampaignStatus CampaignStatus,
    Guid ConcurrencyToken,
    IReadOnlyList<CampaignParticipantNoteDto> Notes,
    IReadOnlyList<CampaignParticipantTagApplicationDto> AppliedTags,
    CampaignParticipantCapabilitiesDto Capabilities);

/// <summary>
/// Note summary attached to a participant detail payload.
/// </summary>
public sealed record CampaignParticipantNoteDto(
    long NoteId,
    string Content,
    string AuthorDisplayName,
    DateTimeOffset CreatedAt);

/// <summary>
/// Tag application summary attached to a participant detail payload.
/// </summary>
public sealed record CampaignParticipantTagApplicationDto(
    long CampaignTagApplicationId,
    long PlayerTagId,
    string TagName,
    string TagColor,
    bool IsArchived,
    string ActorDisplayName,
    DateTimeOffset AppliedAt);

/// <summary>
/// Capability flags exposed to the caller for the participant detail view.
/// </summary>
public sealed record CampaignParticipantCapabilitiesDto(
    bool CanEditPlacement,
    bool CanAddNote,
    bool CanEditNote,
    bool CanDeleteNote,
    bool CanApplyTag,
    bool CanRemoveTagApplication,
    bool CanArchiveTagDefinitions);

