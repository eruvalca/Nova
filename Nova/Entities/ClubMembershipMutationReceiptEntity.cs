using Nova.Entities.Base;

namespace Nova.Entities;

/// <summary>
/// Durable proof that one club-membership mutation committed. The immutable operation identifier
/// makes ambiguous-commit verification independent of subsequently mutable user state.
/// </summary>
public class ClubMembershipMutationReceiptEntity : BaseEntity, ITenantOwnedEntity
{
    /// <summary>Gets or sets the receipt identifier.</summary>
    public long ClubMembershipMutationReceiptId { get; set; }

    /// <summary>Gets or sets the stable identifier of the logical mutation.</summary>
    public required Guid OperationId { get; set; }

    /// <summary>Gets or sets the affected user identifier.</summary>
    public required long MemberUserId { get; set; }

    /// <summary>Gets or sets the mutation kind snapshot.</summary>
    public required string MutationKind { get; set; }

    /// <summary>Gets or sets the owning club identifier.</summary>
    public required long ClubId { get; set; }

    /// <summary>Gets or sets the owning club.</summary>
    public ClubEntity Club { get; set; } = null!;
}
