using Nova.Shared.Enums;

namespace Nova.Shared.Players;

/// <summary>
/// Represents the player detail payload including permanent profile fields, lifecycle status,
/// current traits, and campaign-grouped historical participation.
/// </summary>
/// <param name="PlayerId">The player identifier.</param>
/// <param name="FirstName">The player's first name.</param>
/// <param name="LastName">The player's last name.</param>
/// <param name="DateOfBirth">The player's date of birth.</param>
/// <param name="Gender">The optional gender value.</param>
/// <param name="GraduationYear">The player's graduation year.</param>
/// <param name="JerseyNumber">The optional jersey number.</param>
/// <param name="LifecycleStatus">The player lifecycle status.</param>
/// <param name="CurrentTraits">The deduplicated active-campaign traits currently applied to this player.</param>
/// <param name="CampaignHistory">The full campaign participation history for this player.</param>
public sealed record PlayerDetailDto(
    long PlayerId,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    Gender? Gender,
    int GraduationYear,
    int? JerseyNumber,
    LifecycleStatus LifecycleStatus,
    IReadOnlyList<PlayerCurrentTraitDto> CurrentTraits,
    IReadOnlyList<PlayerCampaignHistoryDto> CampaignHistory);

/// <summary>
/// Represents one deduplicated active-campaign trait currently applied to the player.
/// </summary>
/// <param name="PlayerTagId">The tag-definition identifier.</param>
/// <param name="Name">The tag-definition display name.</param>
/// <param name="Color">The tag-definition color token.</param>
public sealed record PlayerCurrentTraitDto(
    long PlayerTagId,
    string Name,
    string Color);

/// <summary>
/// Represents one campaign-participation history item for a player.
/// </summary>
/// <param name="PlayerCampaignAssignmentId">The participation identifier.</param>
/// <param name="CampaignId">The campaign identifier.</param>
/// <param name="CampaignName">The campaign name.</param>
/// <param name="CampaignStatus">The campaign lifecycle status.</param>
/// <param name="CampaignStartDate">The campaign start date.</param>
/// <param name="TryoutNumber">The campaign-scoped tryout number, when assigned.</param>
/// <param name="PlacementOutcome">The placement outcome for this participation.</param>
/// <param name="Team">The optional team summary when the player is assigned.</param>
/// <param name="Notes">Evaluation notes for this participation, newest first.</param>
/// <param name="TagApplications">Tag applications for this participation, newest first.</param>
public sealed record PlayerCampaignHistoryDto(
    long PlayerCampaignAssignmentId,
    long CampaignId,
    string CampaignName,
    CampaignStatus CampaignStatus,
    DateOnly CampaignStartDate,
    int? TryoutNumber,
    PlacementOutcome PlacementOutcome,
    PlayerTeamSummaryDto? Team,
    IReadOnlyList<PlayerEvaluationNoteDto> Notes,
    IReadOnlyList<PlayerTagApplicationDto> TagApplications);

/// <summary>
/// Represents a compact team summary linked to one campaign participation.
/// </summary>
/// <param name="TeamId">The team identifier.</param>
/// <param name="Name">The team name.</param>
/// <param name="GraduationYear">The team graduation year.</param>
/// <param name="LifecycleStatus">The team lifecycle status.</param>
public sealed record PlayerTeamSummaryDto(
    long TeamId,
    string Name,
    int GraduationYear,
    LifecycleStatus LifecycleStatus);

/// <summary>
/// Represents one evaluation note in player campaign history.
/// </summary>
/// <param name="NoteId">The note identifier.</param>
/// <param name="Content">The note text.</param>
/// <param name="AuthorUserId">The note author identifier.</param>
/// <param name="AuthorDisplayName">The note author display name, or fallback text when unavailable.</param>
/// <param name="CreatedAt">The note creation timestamp.</param>
public sealed record PlayerEvaluationNoteDto(
    long NoteId,
    string Content,
    long AuthorUserId,
    string AuthorDisplayName,
    DateTimeOffset CreatedAt);

/// <summary>
/// Represents one tag application in player campaign history.
/// </summary>
/// <param name="CampaignTagApplicationId">The campaign tag application identifier.</param>
/// <param name="PlayerTagId">The tag-definition identifier.</param>
/// <param name="TagName">The tag-definition name.</param>
/// <param name="TagColor">The tag-definition color token.</param>
/// <param name="IsTagArchived"><see langword="true"/> when the referenced tag definition is archived.</param>
/// <param name="ApplyingUserId">The applying user identifier.</param>
/// <param name="ApplyingUserDisplayName">The applying user display name, or fallback text when unavailable.</param>
/// <param name="AppliedAt">The application timestamp.</param>
public sealed record PlayerTagApplicationDto(
    long CampaignTagApplicationId,
    long PlayerTagId,
    string TagName,
    string TagColor,
    bool IsTagArchived,
    long ApplyingUserId,
    string ApplyingUserDisplayName,
    DateTimeOffset AppliedAt);
