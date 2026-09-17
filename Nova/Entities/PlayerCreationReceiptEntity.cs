using Nova.Entities.Base;

namespace Nova.Entities;

/// <summary>Durable player creation proof with tenant and actor snapshots and no aggregate foreign keys.</summary>
internal sealed class PlayerCreationReceiptEntity : BaseEntity, ITenantOwnedEntity
{
    /// <summary>Gets or sets the storage identity.</summary>
    public long PlayerCreationReceiptId { get; set; }
    /// <summary>Gets or sets the originating tenant snapshot.</summary>
    public required long ClubId { get; set; }
    /// <summary>Gets or sets the originating actor snapshot.</summary>
    public required long ActorUserId { get; set; }
    /// <summary>Gets or sets the original client operation identity.</summary>
    public required Guid OperationId { get; set; }
    /// <summary>Gets or sets the digest of the exact original typed creation request.</summary>
    public required string RequestSha256 { get; set; }
    /// <summary>Gets or sets the immutable outcome envelope containing the typed result or definitive rejection.</summary>
    public required string ResultJson { get; set; }
    /// <summary>Gets or sets the exclusive recovery deadline.</summary>
    public required DateTimeOffset RecoveryExpiresAt { get; set; }
}
