using Nova.Entities.Base;
using Nova.Shared.Enums;

namespace Nova.Entities;

/// <summary>
/// Durable receipt proving that a specific evaluation-note mutation committed, even when the note
/// row itself is later edited or deleted before an ambiguous commit is verified. Used to scope
/// ambiguous-commit verification to the request that actually applied the mutation instead of
/// relying on the mutable note row.
/// </summary>
public class EvaluationNoteMutationReceiptEntity : BaseEntity, ITenantOwnedEntity
{
    /// <summary>
    /// Gets or sets the mutation receipt identifier.
    /// </summary>
    public long EvaluationNoteMutationReceiptId { get; set; } = default;

    /// <summary>
    /// Gets or sets the stable identifier for the mutation operation that wrote this receipt.
    /// </summary>
    public required Guid OperationId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the note the mutation affected.
    /// </summary>
    public required long NoteId { get; set; }

    /// <summary>
    /// Gets or sets the kind of evaluation-note mutation this receipt records.
    /// </summary>
    public required EvaluationNoteMutationType MutationType { get; set; }

    /// <summary>
    /// Gets or sets the club identifier owning this mutation receipt.
    /// </summary>
    public required long ClubId { get; set; }

    /// <summary>
    /// Gets or sets the club owning this mutation receipt.
    /// </summary>
    public ClubEntity Club { get; set; } = null!;
}
