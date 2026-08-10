using Nova.Shared.Enums;
using Nova.Shared.Results;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Bounded roster row for a campaign participant.
/// </summary>
/// <param name="PlayerCampaignAssignmentId">The campaign-assignment identifier for this participant.</param>
/// <param name="PlayerId">The player identifier.</param>
/// <param name="DisplayName">The participant display name.</param>
/// <param name="GraduationYear">The participant graduation year.</param>
/// <param name="TryoutNumber">The optional tryout number.</param>
/// <param name="PlacementOutcome">The current placement outcome.</param>
/// <param name="Team">The optional assigned team summary.</param>
/// <param name="AppliedTags">The tags applied to this participant.</param>
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
/// <param name="TeamId">The team identifier.</param>
/// <param name="TeamName">The team display name.</param>
public sealed record CampaignParticipantTeamSummaryDto(
    long TeamId,
    string TeamName);

/// <summary>
/// Lightweight tag summary used in roster rows and detail payloads.
/// </summary>
/// <param name="PlayerTagId">The tag-definition identifier.</param>
/// <param name="TagName">The tag display name.</param>
/// <param name="TagColor">The tag color token.</param>
/// <param name="IsArchived">Whether the tag definition is archived.</param>
public sealed record CampaignParticipantTagSummaryDto(
    long PlayerTagId,
    string TagName,
    string TagColor,
    bool IsArchived);

/// <summary>
/// Full participant detail payload for a campaign assignment.
/// </summary>
/// <param name="PlayerCampaignAssignmentId">The campaign-assignment identifier for this participant.</param>
/// <param name="PlayerId">The player identifier.</param>
/// <param name="DisplayName">The participant display name.</param>
/// <param name="GraduationYear">The participant graduation year.</param>
/// <param name="TryoutNumber">The optional tryout number.</param>
/// <param name="PlacementOutcome">The current placement outcome.</param>
/// <param name="Team">The optional assigned team summary.</param>
/// <param name="CreatedAt">When the campaign assignment was created.</param>
/// <param name="ModifiedAt">When the campaign assignment was last modified, when applicable.</param>
/// <param name="CampaignStatus">The campaign lifecycle status.</param>
/// <param name="ConcurrencyToken">The optimistic-concurrency token for the assignment.</param>
/// <param name="Notes">The participant notes in the detail payload.</param>
/// <param name="AppliedTags">The tag applications in the detail payload.</param>
/// <param name="Capabilities">The caller capabilities for this participant detail view.</param>
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
/// <param name="NoteId">The note identifier.</param>
/// <param name="Content">The note content.</param>
/// <param name="AuthorDisplayName">The display name of the note author.</param>
/// <param name="CreatedAt">When the note was created.</param>
/// <param name="CanEdit">Whether the caller can edit this note.</param>
/// <param name="CanDelete">Whether the caller can delete this note.</param>
public sealed record CampaignParticipantNoteDto(
    long NoteId,
    string Content,
    string AuthorDisplayName,
    DateTimeOffset CreatedAt,
    bool CanEdit,
    bool CanDelete);

/// <summary>
/// Tag application summary attached to a participant detail payload.
/// </summary>
/// <param name="CampaignTagApplicationId">The tag-application identifier.</param>
/// <param name="PlayerTagId">The tag-definition identifier.</param>
/// <param name="TagName">The tag display name.</param>
/// <param name="TagColor">The tag color token.</param>
/// <param name="IsArchived">Whether the tag definition is archived.</param>
/// <param name="ActorDisplayName">The display name of the actor who applied the tag.</param>
/// <param name="AppliedAt">When the tag was applied.</param>
/// <param name="CanRemove">Whether the caller can remove this tag application.</param>
public sealed record CampaignParticipantTagApplicationDto(
    long CampaignTagApplicationId,
    long PlayerTagId,
    string TagName,
    string TagColor,
    bool IsArchived,
    string ActorDisplayName,
    DateTimeOffset AppliedAt,
    bool CanRemove);

/// <summary>
/// Capability flags exposed to the caller for the participant detail view.
/// </summary>
/// <param name="CanEditPlacement">Whether the caller can edit the placement outcome.</param>
/// <param name="CanAddNote">Whether the caller can add a note.</param>
/// <param name="CanApplyTag">Whether the caller can apply a tag.</param>
/// <param name="CanArchiveTagDefinitions">Whether the caller can archive tag definitions.</param>
public sealed record CampaignParticipantCapabilitiesDto(
    bool CanEditPlacement,
    bool CanAddNote,
    bool CanApplyTag,
    bool CanArchiveTagDefinitions);

