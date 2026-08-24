using Nova.Entities.Base;

namespace Nova.Entities;

/// <summary>
/// Represents the Club Crest Entity persisted in the database.
/// </summary>
public class ClubCrestEntity : BaseEntity, ITenantOwnedEntity
{
    /// <summary>
    /// Gets or sets the Club Crest Id.
    /// </summary>
    public long ClubCrestId { get; set; } = default;
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
    /// Gets or sets the Club Id that owns this crest.
    /// </summary>
    public required long ClubId { get; set; }
    /// <summary>
    /// Gets or sets the Club.
    /// </summary>
    public ClubEntity Club { get; set; } = null!;
}
