using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Describes a request to correct a season's metadata without deleting linked campaigns or closing the season.
/// </summary>
public sealed record UpdateSeasonMetadataInput : IValidatableObject
{
    /// <summary>
    /// Gets the identifier of the season to update.
    /// </summary>
    [Range(1, long.MaxValue, ErrorMessage = "A valid season identifier is required.")]
    public required long SeasonId { get; init; }

    /// <summary>
    /// Gets the corrected season display name.
    /// </summary>
    [Required, NotWhitespace, MaxLength(100)]
    public required string Name { get; init; }

    /// <summary>
    /// Gets the corrected season start date.
    /// </summary>
    [Required, NotDefaultDateOnly(ErrorMessage = "The season start date is required.")]
    public required DateOnly StartDate { get; init; }

    /// <summary>
    /// Gets the optional corrected season end date.
    /// </summary>
    public DateOnly? EndDate { get; init; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndDate < StartDate)
        {
            yield return new ValidationResult(
                "The season end date cannot be before the season start date.",
                [nameof(EndDate)]);
        }
    }
}
