using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Features.Seasons;

/// <summary>Describes an idempotent request to advance a club to a new current season.</summary>
public sealed record StartNextSeasonInput : IValidatableObject
{
    /// <summary>Gets the caller-generated idempotency identifier.</summary>
    [Required, NotEmptyGuid(ErrorMessage = "The operation identifier must not be empty.")]
    public required Guid OperationId { get; init; }

    /// <summary>Gets the current season identifier observed by the caller.</summary>
    [Range(1, long.MaxValue)]
    public required long ExpectedCurrentSeasonId { get; init; }

    /// <summary>Gets the new season name.</summary>
    [Required, NotWhitespace, MaxLength(100)]
    public required string Name { get; init; }

    /// <summary>Gets the new season start date.</summary>
    [Required, NotDefaultDateOnly(ErrorMessage = "The season start date is required.")]
    public required DateOnly StartDate { get; init; }

    /// <summary>Gets the optional new season end date.</summary>
    public DateOnly? EndDate { get; init; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => SeasonInputValidation.ValidateDates(StartDate, EndDate);
}
