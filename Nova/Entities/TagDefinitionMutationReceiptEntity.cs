using Nova.Entities.Base;
using Nova.Shared.Enums;

namespace Nova.Entities;

/// <summary>
/// Durable receipt proving that a specific tag-definition mutation committed, even when the tag
/// definition row itself is later updated or its lifecycle status changes before an ambiguous commit
/// is verified. Used to scope ambiguous-commit verification to the request that actually applied the
/// mutation instead of relying on the mutable tag-definition row.
/// </summary>
public class TagDefinitionMutationReceiptEntity : BaseEntity, ITenantOwnedEntity
{
    /// <summary>
    /// Gets or sets the mutation receipt identifier.
    /// </summary>
    public long TagDefinitionMutationReceiptId { get; set; } = default;

    /// <summary>
    /// Gets or sets the stable identifier for the mutation operation that wrote this receipt.
    /// </summary>
    public required Guid OperationId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the tag definition the mutation affected.
    /// </summary>
    public required long PlayerTagId { get; set; }

    /// <summary>
    /// Gets or sets the kind of tag-definition mutation this receipt records.
    /// </summary>
    public required TagDefinitionMutationType MutationType { get; set; }

    /// <summary>
    /// Gets or sets the club identifier owning this mutation receipt.
    /// </summary>
    public required long ClubId { get; set; }

    /// <summary>
    /// Gets or sets the club owning this mutation receipt.
    /// </summary>
    public ClubEntity Club { get; set; } = null!;
}
