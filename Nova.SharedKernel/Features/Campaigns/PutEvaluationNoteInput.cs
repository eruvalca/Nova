using System.ComponentModel.DataAnnotations;
using Nova.SharedKernel.Validation;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>
/// The HTTP PUT body for editing an existing evaluation note. The note identifier travels in the
/// route; the body carries the expected version, operation identity, and updated content.
/// </summary>
public sealed record PutEvaluationNoteInput : EvaluationOperationInput
{
    /// <summary>The authoritatively read note version.</summary>
    [NotEmptyGuid]
    public required Guid ExpectedVersion { get; init; }

    /// <summary>The updated note content. Must be non-blank text.</summary>
    [Required, NotWhitespace, MaxLength(4000)]
    public required string Content { get; init; }
}
