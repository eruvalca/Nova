using Nova.Entities.Base;

namespace Nova.Entities;

/// <summary>
/// Durable receipt proving that a specific removal operation committed, even after the tag
/// application row itself is deleted. Used to scope ambiguous-commit verification to the
/// request that actually removed the row.
/// </summary>
public class CampaignTagApplicationRemovalReceiptEntity : BaseEntity, ITenantOwnedEntity
{
    /// <summary>
    /// Gets or sets the removal receipt identifier.
    /// </summary>
    public long CampaignTagApplicationRemovalReceiptId { get; set; } = default;

    /// <summary>
    /// Gets or sets the stable identifier for the removal operation that wrote this receipt.
    /// </summary>
    public required Guid RemovalOperationId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the campaign tag application that was removed.
    /// </summary>
    public required long CampaignTagApplicationId { get; set; }

    /// <summary>
    /// Gets or sets the club identifier owning this removal receipt.
    /// </summary>
    public required long ClubId { get; set; }

    /// <summary>
    /// Gets or sets the club owning this removal receipt.
    /// </summary>
    public ClubEntity Club { get; set; } = null!;
}
