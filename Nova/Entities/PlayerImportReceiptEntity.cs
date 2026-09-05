using Nova.Entities.Base;

namespace Nova.Entities;

/// <summary>Preserves immutable import commit proof independently of mutable or deleted aggregates.</summary>
public sealed class PlayerImportReceiptEntity : BaseEntity, ITenantOwnedEntity
{
    /// <summary>Gets or sets the generated receipt identifier.</summary>
    public long PlayerImportReceiptId { get; set; }
    /// <summary>Gets or sets the club snapshot; intentionally has no aggregate foreign key.</summary>
    public required long ClubId { get; set; }
    /// <summary>Gets or sets the server-issued preview operation identity.</summary>
    public required Guid OperationId { get; set; }
    /// <summary>Gets or sets the authorizing actor snapshot.</summary>
    public required long ActorUserId { get; set; }
    /// <summary>Gets or sets the hexadecimal digest of the original CSV bytes.</summary>
    public required string FileSha256 { get; set; }
    /// <summary>Gets or sets the original CSV byte count.</summary>
    public required int FileLength { get; set; }
    /// <summary>Gets or sets the hexadecimal digest of the exact opaque confirmation.</summary>
    public required string ConfirmationTokenSha256 { get; set; }
    /// <summary>Gets or sets the original completion serialized without source CSV values.</summary>
    public required string ResultJson { get; set; }
    /// <summary>Gets or sets the UTC completion time.</summary>
    public required DateTimeOffset CompletedAt { get; set; }
    /// <summary>Gets or sets the exclusive UTC recovery deadline.</summary>
    public required DateTimeOffset RecoveryExpiresAt { get; set; }
}
