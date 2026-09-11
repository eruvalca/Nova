using System.ComponentModel.DataAnnotations;
using Nova.SharedKernel.Validation;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>A tab-retained logical mutation identity, reused with the exact original payload on recovery.</summary>
/// <remarks>
/// Retain the original operation after a problem unless <see cref="EvaluationMutationRejection.IsNotCommitted"/>
/// confirms its durable rejection. Authorization denial, expiry and a generic conflict cannot establish
/// whether an earlier attempt committed. A rejected receipt prevents delayed requests from later executing
/// the same operation; corrected input uses a new identity only after definitive settlement.
/// </remarks>
public abstract record EvaluationOperationInput
{
    /// <summary>The version-seven UUID created once before this operation is persisted and dispatched.</summary>
    [Required, NotEmptyGuid]
    public required Guid OperationId { get; init; }
}
