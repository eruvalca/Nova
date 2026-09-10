namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>
/// Reports the created evaluation note identifier.
/// </summary>
/// <param name="NoteId">The affected evaluation note identifier.</param>
/// <param name="Version">The resulting version, or the deleted version for a deletion.</param>
/// <param name="Receipt">Immutable proof of this operation.</param>
public readonly record struct EvaluationNoteMutationSuccess(long NoteId, Guid Version, EvaluationMutationReceipt Receipt);
