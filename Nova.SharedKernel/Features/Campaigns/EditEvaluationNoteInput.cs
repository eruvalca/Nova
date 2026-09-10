using System.ComponentModel.DataAnnotations;
using Nova.SharedKernel.Validation;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>
/// Input model for editing an existing evaluation note.
/// </summary>
public sealed record EditEvaluationNoteInput : EvaluationOperationInput
{
    /// <summary>The identifier of the note to edit.</summary>
    [Required, Range(1, long.MaxValue, ErrorMessage = "A valid note identifier is required.")]
    public required long NoteId { get; init; }

    /// <summary>The authoritatively read version being edited.</summary>
    [NotEmptyGuid]
    public required Guid ExpectedVersion { get; init; }

    /// <summary>The updated note content. Must be non-blank text.</summary>
    [Required, NotWhitespace, MaxLength(4000)]
    public required string Content { get; init; }
}
