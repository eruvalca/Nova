using Nova.Entities.Base;
using Nova.Shared.Enums;

namespace Nova.Entities;

/// <summary>
/// Represents the Player Entity persisted in the database.
/// </summary>
public class PlayerEntity : ArchivableEntity, ITenantOwnedEntity
{
    /// <summary>
    /// Gets or sets the Player Id.
    /// </summary>
    public long PlayerId { get; set; } = default;
    /// <summary>
    /// Gets or sets the First Name.
    /// </summary>
    public required string FirstName { get; set; }
    /// <summary>
    /// Gets or sets the Last Name.
    /// </summary>
    public required string LastName { get; set; }
    /// <summary>
    /// Gets or sets the Date Of Birth.
    /// </summary>
    public string FullName => $"{FirstName} {LastName}";
    /// <summary>
    /// Gets or sets the Date Of Birth.
    /// </summary>
    public required DateOnly DateOfBirth { get; set; }
    /// <summary>
    /// Gets or sets the Primary Photo Blob Name.
    /// </summary>
    public string? PrimaryPhotoBlobName { get; set; }
    /// <summary>
    /// Gets or sets the Gender.
    /// </summary>
    public Gender? Gender { get; set; }
    /// <summary>
    /// Gets or sets the Jersey Number.
    /// </summary>
    public int? JerseyNumber { get; set; }
    /// <summary>
    /// Gets or sets the Graduation Year.
    /// </summary>
    public required int GraduationYear { get; set; }

    /// <summary>
    /// Gets or sets the Campaign Assignments.
    /// </summary>
    public ICollection<PlayerCampaignAssignmentEntity> CampaignAssignments { get; set; } = [];
    /// <summary>
    /// Gets or sets the Photos.
    /// </summary>
    public ICollection<PlayerPhotoEntity> Photos { get; set; } = [];

    /// <summary>
    /// Gets or sets the Club Id.
    /// </summary>
    public required long ClubId { get; set; }
    /// <summary>
    /// Gets or sets the Club.
    /// </summary>
    public ClubEntity Club { get; set; } = null!;
}
