namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Reports the created evaluation note identifier.
/// </summary>
/// <param name="NoteId">The created evaluation note identifier.</param>
public readonly record struct EvaluationNoteMutationSuccess(long NoteId);
