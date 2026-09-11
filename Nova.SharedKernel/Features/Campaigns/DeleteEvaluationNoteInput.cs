using System.ComponentModel.DataAnnotations;
using Nova.SharedKernel.Validation;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>Deletes exactly the note version the author reviewed.</summary>
public sealed record DeleteEvaluationNoteInput : EvaluationOperationInput
{
    /// <summary>The note to delete.</summary>
    [Range(1, long.MaxValue)]
    public required long NoteId { get; init; }

    /// <summary>The last authoritatively read version.</summary>
    [NotEmptyGuid]
    public required Guid ExpectedVersion { get; init; }
}
