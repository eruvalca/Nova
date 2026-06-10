namespace Nova.Entities.Base;

/// <summary>
/// Marks an entity as owned by a club (tenant). Entities implementing this interface
/// are automatically covered by the tenant global query filter.
/// </summary>
public interface ITenantOwnedEntity
{
    /// <summary>
    /// Gets or sets the Club Id that owns this entity.
    /// </summary>
    long ClubId { get; set; }
}
