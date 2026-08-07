using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Describes a request to correct an Active campaign's metadata without affecting roster enrollment.
/// </summary>
public sealed record UpdateCampaignMetadataInput : IValidatableObject
{
    /// <summary>
    /// Gets the identifier of the campaign to update.
    /// </summary>
    [Range(1, long.MaxValue, ErrorMessage = "A valid campaign identifier is required.")]
    public required long CampaignId { get; init; }

    /// <summary>
    /// Gets the corrected campaign display name.
    /// </summary>
    [Required, NotWhitespace, MaxLength(100)]
    public required string Name { get; init; }

    /// <summary>
    /// Gets the identifier of the season the campaign belongs to.
    /// </summary>
    [Range(1, long.MaxValue, ErrorMessage = "A valid season identifier is required.")]
    public required long SeasonId { get; init; }

    /// <summary>
    /// Gets the corrected campaign start date.
    /// </summary>
    [Required, NotDefaultDateOnly(ErrorMessage = "The campaign start date is required.")]
    public required DateOnly StartDate { get; init; }

    /// <summary>
    /// Gets the optional corrected planned end date. This metadata does not close the campaign.
    /// </summary>
    public DateOnly? PlannedEndDate { get; init; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PlannedEndDate < StartDate)
        {
            yield return new ValidationResult(
                "The planned campaign end date cannot be before the campaign start date.",
                [nameof(PlannedEndDate)]);
        }
    }
}
