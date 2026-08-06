using Nova.Shared.Features.Campaigns;
using Nova.Shared.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Verifies UpdateSeasonMetadataInput structural validation without authorization or database access.
/// </summary>
public sealed class UpdateSeasonMetadataInputValidationTests
{
    /// <summary>
    /// Verifies a fully valid input without an end date passes structural validation.
    /// </summary>
    [Fact]
    public void Validate_ReturnsNoErrors_ForValidInputWithoutEndDate()
    {
        var errors = InputValidator.Validate(ValidInput());

        errors.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies a fully valid input with an end date passes structural validation.
    /// </summary>
    [Fact]
    public void Validate_ReturnsNoErrors_ForValidInputWithEndDate()
    {
        var errors = InputValidator.Validate(ValidInput() with
        {
            EndDate = new DateOnly(2026, 12, 31)
        });

        errors.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies a zero SeasonId is rejected.
    /// </summary>
    [Fact]
    public void Validate_ReturnsError_WhenSeasonIdIsZero()
    {
        var errors = InputValidator.Validate(ValidInput() with { SeasonId = 0 });

        errors.ShouldContainKey(nameof(UpdateSeasonMetadataInput.SeasonId));
    }

    /// <summary>
    /// Verifies a blank name is rejected.
    /// </summary>
    [Fact]
    public void Validate_ReturnsError_WhenNameIsBlank()
    {
        var errors = InputValidator.Validate(ValidInput() with { Name = "   " });

        errors.ShouldContainKey(nameof(UpdateSeasonMetadataInput.Name));
    }

    /// <summary>
    /// Verifies a default start date is rejected.
    /// </summary>
    [Fact]
    public void Validate_ReturnsError_WhenStartDateIsDefault()
    {
        var errors = InputValidator.Validate(ValidInput() with { StartDate = default });

        errors.ShouldContainKey(nameof(UpdateSeasonMetadataInput.StartDate));
    }

    /// <summary>
    /// Verifies an end date before the start date is rejected.
    /// </summary>
    [Fact]
    public void Validate_ReturnsError_WhenEndDateIsBeforeStartDate()
    {
        var errors = InputValidator.Validate(ValidInput() with
        {
            StartDate = new DateOnly(2026, 6, 1),
            EndDate = new DateOnly(2026, 5, 31)
        });

        errors.ShouldContainKey(nameof(UpdateSeasonMetadataInput.EndDate));
    }

    private static UpdateSeasonMetadataInput ValidInput() => new()
    {
        SeasonId = 1,
        Name = "2026 Season",
        StartDate = new DateOnly(2026, 1, 1)
    };
}
