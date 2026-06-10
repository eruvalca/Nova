namespace Nova.Entities.Base;

/// <summary>
/// Represents the Base Entity persisted in the database.
/// </summary>
public abstract class BaseEntity
{
    /// <summary>
    /// Gets or sets the Created At.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    /// <summary>
    /// Gets or sets the Created By Id.
    /// </summary>
    public required long CreatedById { get; set; }

    /// <summary>
    /// Gets or sets the Modified At.
    /// </summary>
    public DateTimeOffset? ModifiedAt { get; set; } = null;
    /// <summary>
    /// Gets or sets the Modified By Id.
    /// </summary>
    public long? ModifiedById { get; set; } = null;
}
