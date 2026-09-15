using Nova.Entities.Base;

namespace Nova.Entities;

/// <summary>Proves a placement request committed even after a later decision replaces its token.</summary>
internal class PlacementMutationReceiptEntity : BaseEntity, ITenantOwnedEntity
{
    /// <summary>Gets or sets the receipt identifier.</summary>
    public long PlacementMutationReceiptId { get; set; }

    /// <summary>Gets or sets the stable logical request identifier.</summary>
    public required Guid OperationId { get; set; }

    /// <summary>Gets or sets the affected participation identifier snapshot.</summary>
    public required long PlayerCampaignAssignmentId { get; set; }

    /// <summary>Gets or sets the token originally returned by this request.</summary>
    public required Guid ConcurrencyToken { get; set; }

    /// <summary>Gets or sets the actor authorized to recover this operation.</summary>
    public long ActorUserId { get; set; }

    /// <summary>Gets or sets the exact request fingerprint.</summary>
    public required string RequestSha256 { get; set; }

    /// <summary>Gets or sets the immutable committed result or definitive rejection.</summary>
    public required string ResultJson { get; set; }

    /// <summary>Gets or sets the exclusive recovery deadline.</summary>
    public DateTimeOffset RecoveryExpiresAt { get; set; }

    /// <summary>Gets or sets the tenant snapshot without a foreign key so commit proof survives club deletion.</summary>
    public required long ClubId { get; set; }
}
