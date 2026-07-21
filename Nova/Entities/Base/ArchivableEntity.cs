using Nova.Shared.Enums;

namespace Nova.Entities.Base;

/// <summary>
/// Provides the shared lifecycle and archive provenance for records retained after archival.
/// </summary>
public abstract class ArchivableEntity : BaseEntity
{
    /// <summary>
    /// Gets or sets whether the record is active or retained only for history.
    /// </summary>
    public LifecycleStatus LifecycleStatus { get; set; } = LifecycleStatus.Active;

    /// <summary>
    /// Gets or sets when the record was archived.
    /// </summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who archived the record.
    /// </summary>
    public long? ArchivedById { get; set; }
}
