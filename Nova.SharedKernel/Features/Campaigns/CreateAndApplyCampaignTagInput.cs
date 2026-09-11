using System.ComponentModel.DataAnnotations;
using Nova.SharedKernel.Validation;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>Resolves a collaborative trait and applies it in one atomic operation.</summary>
public sealed record CreateAndApplyCampaignTagInput : EvaluationOperationInput
{
    /// <summary>The verified participant receiving the trait.</summary>
    [Range(1, long.MaxValue)]
    public required long PlayerCampaignAssignmentId { get; init; }

    /// <summary>The label entered by the evaluator; punctuation is significant.</summary>
    [Required, NotWhitespace, MaxLength(100)]
    public required string Label { get; init; }
}
