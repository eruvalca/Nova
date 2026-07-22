using Nova.Entities.Base;

namespace Nova.Entities;

/// <summary>
/// Represents an evaluation note scoped to a single campaign participation record.
/// </summary>
public class NoteEntity : BaseEntity, ITenantOwnedEntity
{
    /// <summary>
    /// Gets or sets the Note Id.
    /// </summary>
    public long NoteId { get; set; } = default;

    /// <summary>
    /// Gets or sets the note content.
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Gets or sets the campaign participation this note belongs to.
    /// Player and campaign context is derived from the participation.
    /// </summary>
    public required long PlayerCampaignAssignmentId { get; set; }

    /// <summary>
    /// Gets or sets the campaign participation navigation.
    /// </summary>
    public PlayerCampaignAssignmentEntity PlayerCampaignAssignment { get; set; } = null!;

    /// <summary>
    /// Gets or sets the Club Id.
    /// </summary>
    public required long ClubId { get; set; }

    /// <summary>
    /// Gets or sets the Club.
    /// </summary>
    public ClubEntity Club { get; set; } = null!;
}
