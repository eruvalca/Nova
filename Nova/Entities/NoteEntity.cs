using Nova.Entities.Base;

namespace Nova.Entities;

/// <summary>
/// Represents the Note Entity persisted in the database.
/// </summary>
public class NoteEntity : BaseEntity
{
    /// <summary>
    /// Gets or sets the Note Id.
    /// </summary>
    public long NoteId { get; set; } = default;
    /// <summary>
    /// Gets or sets the Content.
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// Gets or sets the Player Id.
    /// </summary>
    public required long PlayerId { get; set; }
    /// <summary>
    /// Gets or sets the Player.
    /// </summary>
    public PlayerEntity Player { get; set; } = null!;

    /// <summary>
    /// Gets or sets the Club Id.
    /// </summary>
    public required long ClubId { get; set; }
    /// <summary>
    /// Gets or sets the Club.
    /// </summary>
    public ClubEntity Club { get; set; } = null!;
}
