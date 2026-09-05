using Nova.Entities.Base;

namespace Nova.Entities;

/// <summary>Proves a placement request committed even after a later decision replaces its token.</summary>
public class PlacementMutationReceiptEntity : BaseEntity, ITenantOwnedEntity
{
    /// <summary>Gets or sets the receipt identifier.</summary>
    public long PlacementMutationReceiptId { get; set; }

    /// <summary>Gets or sets the stable logical request identifier.</summary>
    public required Guid OperationId { get; set; }

    /// <summary>Gets or sets the affected participation identifier snapshot.</summary>
    public required long PlayerCampaignAssignmentId { get; set; }

    /// <summary>Gets or sets the token originally returned by this request.</summary>
    public required Guid ConcurrencyToken { get; set; }

    /// <summary>Gets or sets the tenant snapshot without a foreign key so commit proof survives club deletion.</summary>
    public required long ClubId { get; set; }
}
