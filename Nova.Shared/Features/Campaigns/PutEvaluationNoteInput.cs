using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// The HTTP PUT body for editing an existing evaluation note. The note identifier travels in the
/// route so the body carries only the updated content.
/// </summary>
public sealed record PutEvaluationNoteInput
{
    /// <summary>The updated note content. Must be non-blank text.</summary>
    [Required, NotWhitespace, MaxLength(4000)]
    public required string Content { get; init; }
}
