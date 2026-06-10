using Nova.Entities.Base;

namespace Nova.Entities;

/// <summary>
/// Represents the Nova User Photo Entity persisted in the database.
/// </summary>
public class NovaUserPhotoEntity : BaseEntity
{
    /// <summary>
    /// Gets or sets the Nova User Photo Id.
    /// </summary>
    public long NovaUserPhotoId { get; set; } = default;
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
    /// Gets or sets the Nova User Id.
    /// </summary>
    public required long NovaUserId { get; set; }
    /// <summary>
    /// Gets or sets the Nova User.
    /// </summary>
    public NovaUserEntity NovaUser { get; set; } = null!;
}
