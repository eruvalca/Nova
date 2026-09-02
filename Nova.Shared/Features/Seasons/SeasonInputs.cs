using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Features.Seasons;

/// <summary>Describes an idempotent request to create a club's first current season.</summary>
public sealed record CreateSeasonInput : IValidatableObject
{
    /// <summary>Gets the caller-generated idempotency identifier.</summary>
    [Required, NotEmptyGuid(ErrorMessage = "The operation identifier must not be empty.")]
    public required Guid OperationId { get; init; }

    /// <summary>Gets the season name.</summary>
    [Required, NotWhitespace, MaxLength(100)]
    public required string Name { get; init; }

    /// <summary>Gets the season start date.</summary>
    [Required, NotDefaultDateOnly(ErrorMessage = "The season start date is required.")]
    public required DateOnly StartDate { get; init; }

    /// <summary>Gets the optional season end date.</summary>
    public DateOnly? EndDate { get; init; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => SeasonInputValidation.ValidateDates(StartDate, EndDate);
}

/// <summary>Describes an administrator request to update season metadata.</summary>
public sealed record UpdateSeasonInput : IValidatableObject
{
    /// <summary>Gets the concurrency token observed by the caller.</summary>
    [Required, NotEmptyGuid(ErrorMessage = "The expected concurrency token must not be empty.")]
    public required Guid ExpectedConcurrencyToken { get; init; }

    /// <summary>Gets the season name.</summary>
    [Required, NotWhitespace, MaxLength(100)]
    public required string Name { get; init; }

    /// <summary>Gets the season start date.</summary>
    [Required, NotDefaultDateOnly(ErrorMessage = "The season start date is required.")]
    public required DateOnly StartDate { get; init; }

    /// <summary>Gets the optional season end date.</summary>
    public DateOnly? EndDate { get; init; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        => SeasonInputValidation.ValidateDates(StartDate, EndDate);
}

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

/// <summary>Describes a bounded season-list request.</summary>
public sealed record GetSeasonListInput
{
    /// <summary>Gets the default page number.</summary>
    public const int DefaultPage = 1;

    /// <summary>Gets the default page size.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Gets the maximum page size.</summary>
    public const int MaximumPageSize = 50;

    /// <summary>Gets the one-based page number.</summary>
    [Range(1, int.MaxValue)]
    public int? Page { get; init; }

    /// <summary>Gets the number of seasons returned per page.</summary>
    [Range(1, MaximumPageSize)]
    public int? PageSize { get; init; }
}

/// <summary>Describes a season-detail request with bounded campaign paging.</summary>
public sealed record GetSeasonDetailInput
{
    /// <summary>Gets the season identifier.</summary>
    [Range(1, long.MaxValue)]
    public required long SeasonId { get; init; }

    /// <summary>Gets the one-based campaign page number.</summary>
    [Range(1, int.MaxValue)]
    public int? CampaignPage { get; init; }

    /// <summary>Gets the number of campaigns returned per page.</summary>
    [Range(1, GetSeasonListInput.MaximumPageSize)]
    public int? CampaignPageSize { get; init; }
}

/// <summary>Provides shared structural validation for season date ranges.</summary>
internal static class SeasonInputValidation
{
    /// <summary>Validates that a finite season does not end before it starts.</summary>
    /// <param name="startDate">The requested start date.</param>
    /// <param name="endDate">The requested optional end date.</param>
    /// <returns>The structural validation failures.</returns>
    public static IEnumerable<ValidationResult> ValidateDates(DateOnly startDate, DateOnly? endDate)
    {
        if (endDate < startDate)
        {
            yield return new ValidationResult(
                "The season end date cannot be before the season start date.",
                [nameof(CreateSeasonInput.EndDate)]);
        }
    }
}
