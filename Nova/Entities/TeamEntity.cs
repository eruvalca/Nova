using Nova.Entities.Base;

namespace Nova.Entities;

/// <summary>
/// Represents the Team Entity persisted in the database.
/// </summary>
public class TeamEntity : ArchivableEntity, ITenantOwnedEntity
{
    /// <summary>
    /// Gets or sets the Team Id.
    /// </summary>
    public long TeamId { get; set; } = default;
    /// <summary>
    /// Gets or sets the Name.
    /// </summary>
    public required string Name { get; set; }
    /// <summary>
    /// Gets or sets the Graduation Year.
    /// </summary>
    public required int GraduationYear { get; set; }

    /// <summary>
    /// Gets or sets the Player Assignments.
    /// </summary>
    public ICollection<PlayerCampaignAssignmentEntity> PlayerAssignments { get; set; } = [];

    /// <summary>
    /// Gets or sets the Club Id.
    /// </summary>
    public required long ClubId { get; set; }
    /// <summary>
    /// Gets or sets the Club.
    /// </summary>
    public ClubEntity Club { get; set; } = null!;
}
