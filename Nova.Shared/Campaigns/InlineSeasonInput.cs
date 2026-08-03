using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Campaigns;

/// <summary>
/// Describes a season to create atomically with a campaign.
/// </summary>
public sealed record InlineSeasonInput : IValidatableObject
{
    /// <summary>
    /// Gets the season display name.
    /// </summary>
    [Required, NotWhitespace, MaxLength(100)]
    public required string Name { get; init; }

    /// <summary>
    /// Gets the first date in the season.
    /// </summary>
    [Required]
    public required DateOnly StartDate { get; init; }

    /// <summary>
    /// Gets the optional final date in the season.
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
