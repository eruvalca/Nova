using System.ComponentModel.DataAnnotations;
using Nova.SharedKernel.Validation;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>A tab-retained logical mutation identity, reused with the exact original payload on recovery.</summary>
public abstract record EvaluationOperationInput
{
    /// <summary>The version-seven UUID created once before this operation is persisted and dispatched.</summary>
    [Required, NotEmptyGuid]
    public required Guid OperationId { get; init; }
}
