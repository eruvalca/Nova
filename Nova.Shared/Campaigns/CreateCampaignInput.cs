using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Campaigns;

/// <summary>
/// Describes an idempotent campaign creation request using either an existing or inline-created season.
/// </summary>
public sealed record CreateCampaignInput : IValidatableObject
{
    /// <summary>
    /// Gets the caller-generated identifier that makes repeated submissions idempotent.
    /// </summary>
    [Required, NotEmptyGuid(ErrorMessage = "The operation identifier must not be empty.")]
    public required Guid OperationId { get; init; }

    /// <summary>
    /// Gets the campaign display name.
    /// </summary>
    [Required, NotWhitespace, MaxLength(100)]
    public required string Name { get; init; }

    /// <summary>
    /// Gets the campaign start date.
    /// </summary>
    [Required, NotDefaultDateOnly(ErrorMessage = "The campaign start date is required.")]
    public required DateOnly StartDate { get; init; }

    /// <summary>
    /// Gets the optional planned campaign end date. This metadata does not close the campaign.
    /// </summary>
    public DateOnly? PlannedEndDate { get; init; }

    /// <summary>
    /// Gets the existing season identifier when the campaign uses a persisted season.
    /// </summary>
    [Range(1, long.MaxValue)]
    public long? ExistingSeasonId { get; init; }

    /// <summary>
    /// Gets the inline season definition when a new season should be created atomically.
    /// </summary>
    public InlineSeasonInput? InlineSeason { get; init; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PlannedEndDate < StartDate)
        {
            yield return new ValidationResult(
                "The planned campaign end date cannot be before the campaign start date.",
                [nameof(PlannedEndDate)]);
        }

        if (ExistingSeasonId.HasValue == (InlineSeason is not null))
        {
            yield return new ValidationResult(
                "Specify exactly one season choice: an existing season or inline season data.",
                [nameof(ExistingSeasonId), nameof(InlineSeason)]);
        }

        if (InlineSeason is not null)
        {
            foreach (var result in ValidateInlineSeason(InlineSeason))
            {
                yield return result;
            }
        }
    }

    /// <summary>
    /// Runs the inline season's annotations and prefixes its member names for the parent input.
    /// </summary>
    /// <param name="inlineSeason">The inline season to validate.</param>
    /// <returns>The inline season validation failures with parent-qualified member names.</returns>
    private static IEnumerable<ValidationResult> ValidateInlineSeason(InlineSeasonInput inlineSeason)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            inlineSeason,
            new ValidationContext(inlineSeason),
            results,
            validateAllProperties: true);

        foreach (var result in results)
        {
            var memberNames = result.MemberNames
                .Select(memberName => $"{nameof(InlineSeason)}.{memberName}")
                .ToArray();
            yield return new ValidationResult(result.ErrorMessage, memberNames);
        }
    }
}
