using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Features.Seasons;

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
