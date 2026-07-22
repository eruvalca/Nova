using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Campaigns;

/// <summary>
/// Input model for adding a new evaluation note to a campaign participation record.
/// </summary>
public sealed record AddEvaluationNoteInput
{
    /// <summary>The campaign participation identifier the note belongs to.</summary>
    [Required, Range(1, long.MaxValue, ErrorMessage = "A valid campaign participation identifier is required.")]
    public required long PlayerCampaignAssignmentId { get; init; }

    /// <summary>The note content. Must be non-blank text.</summary>
    [Required, NotWhitespace, MaxLength(4000)]
    public required string Content { get; init; }
}
