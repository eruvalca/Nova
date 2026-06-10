using Nova.Entities.Base;

namespace Nova.Entities;

/// <summary>
/// Represents the Player Photo Entity persisted in the database.
/// </summary>
public class PlayerPhotoEntity : BaseEntity, ITenantOwnedEntity
{
    /// <summary>
    /// Gets or sets the Player Photo Id.
    /// </summary>
    public long PlayerPhotoId { get; set; } = default;
    /// <summary>
    /// Gets or sets the Original Blob Name.
    /// </summary>
    public required string OriginalBlobName { get; set; }
    /// <summary>
    /// Gets or sets the Small Blob Name.
    /// </summary>
    public string? SmallBlobName { get; set; }
    /// <summary>
    /// Gets or sets the Medium Blob Name.
    /// </summary>
    public string? MediumBlobName { get; set; }
    /// <summary>
    /// Gets or sets the Large Blob Name.
    /// </summary>
    public string? LargeBlobName { get; set; }
    /// <summary>
    /// Gets or sets the Content Type.
    /// </summary>
    public string? ContentType { get; set; }

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
