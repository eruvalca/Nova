using Nova.Shared.Features.Campaigns;
using Nova.Shared.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Verifies UpdateCampaignMetadataInput structural validation without authorization or database access.
/// </summary>
public sealed class UpdateCampaignMetadataInputValidationTests
{
    /// <summary>
    /// Verifies a fully valid input passes structural validation.
    /// </summary>
    [Fact]
    public void Validate_ReturnsNoErrors_ForValidInput()
    {
        var errors = InputValidator.Validate(ValidInput());

        errors.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies a valid input with a planned end date passes structural validation.
    /// </summary>
    [Fact]
    public void Validate_ReturnsNoErrors_ForValidInputWithPlannedEndDate()
    {
        var errors = InputValidator.Validate(ValidInput() with
        {
            PlannedEndDate = new DateOnly(2026, 9, 30)
        });

        errors.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies a zero CampaignId is rejected.
    /// </summary>
    [Fact]
    public void Validate_ReturnsError_WhenCampaignIdIsZero()
    {
        var errors = InputValidator.Validate(ValidInput() with { CampaignId = 0 });

        errors.ShouldContainKey(nameof(UpdateCampaignMetadataInput.CampaignId));
    }

    /// <summary>
    /// Verifies a zero SeasonId is rejected.
    /// </summary>
    [Fact]
    public void Validate_ReturnsError_WhenSeasonIdIsZero()
    {
        var errors = InputValidator.Validate(ValidInput() with { SeasonId = 0 });

        errors.ShouldContainKey(nameof(UpdateCampaignMetadataInput.SeasonId));
    }

    /// <summary>
    /// Verifies a blank campaign name is rejected.
    /// </summary>
    [Fact]
    public void Validate_ReturnsError_WhenNameIsBlank()
    {
        var errors = InputValidator.Validate(ValidInput() with { Name = "   " });

        errors.ShouldContainKey(nameof(UpdateCampaignMetadataInput.Name));
    }

    /// <summary>
    /// Verifies a default start date is rejected.
    /// </summary>
    [Fact]
    public void Validate_ReturnsError_WhenStartDateIsDefault()
    {
        var errors = InputValidator.Validate(ValidInput() with { StartDate = default });

        errors.ShouldContainKey(nameof(UpdateCampaignMetadataInput.StartDate));
    }

    /// <summary>
    /// Verifies a planned end date before the start date is rejected.
    /// </summary>
    [Fact]
    public void Validate_ReturnsError_WhenPlannedEndDateIsBeforeStartDate()
    {
        var errors = InputValidator.Validate(ValidInput() with
        {
            StartDate = new DateOnly(2026, 6, 1),
            PlannedEndDate = new DateOnly(2026, 5, 31)
        });

        errors.ShouldContainKey(nameof(UpdateCampaignMetadataInput.PlannedEndDate));
    }

    private static UpdateCampaignMetadataInput ValidInput() => new()
    {
        CampaignId = 1,
        Name = "Fall Tryouts",
        SeasonId = 10,
        StartDate = new DateOnly(2026, 6, 1)
    };
}
