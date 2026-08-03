using Nova.Shared.Campaigns;
using Nova.Shared.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Verifies campaign creation request validation without authorization or database access.
/// </summary>
public sealed class CreateCampaignInputValidationTests
{
    /// <summary>
    /// Verifies a request selecting one existing season passes structural validation.
    /// </summary>
    [Fact]
    public void Validate_ReturnsNoErrors_ForExistingSeasonChoice()
    {
        var errors = InputValidator.Validate(ValidInput() with { ExistingSeasonId = 42 });

        errors.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies a request defining one valid inline season passes structural validation.
    /// </summary>
    [Fact]
    public void Validate_ReturnsNoErrors_ForInlineSeasonChoice()
    {
        var errors = InputValidator.Validate(ValidInput() with
        {
            InlineSeason = new InlineSeasonInput
            {
                Name = "2026",
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2026, 12, 31)
            }
        });

        errors.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies omitting both season choices is rejected on both choice fields.
    /// </summary>
    [Fact]
    public void Validate_ReturnsChoiceErrors_WhenNoSeasonIsSelected()
    {
        var errors = InputValidator.Validate(ValidInput());

        errors.ShouldContainKey(nameof(CreateCampaignInput.ExistingSeasonId));
        errors.ShouldContainKey(nameof(CreateCampaignInput.InlineSeason));
    }

    /// <summary>
    /// Verifies selecting both season choices is rejected on both choice fields.
    /// </summary>
    [Fact]
    public void Validate_ReturnsChoiceErrors_WhenBothSeasonsAreSelected()
    {
        var errors = InputValidator.Validate(ValidInput() with
        {
            ExistingSeasonId = 42,
            InlineSeason = new InlineSeasonInput
            {
                Name = "2026",
                StartDate = new DateOnly(2026, 1, 1)
            }
        });

        errors.ShouldContainKey(nameof(CreateCampaignInput.ExistingSeasonId));
        errors.ShouldContainKey(nameof(CreateCampaignInput.InlineSeason));
    }

    /// <summary>
    /// Verifies an empty caller operation identifier is rejected.
    /// </summary>
    [Fact]
    public void Validate_ReturnsOperationIdError_WhenOperationIdIsEmpty()
    {
        var errors = InputValidator.Validate(ValidInput() with
        {
            OperationId = Guid.Empty,
            ExistingSeasonId = 42
        });

        errors.ShouldContainKey(nameof(CreateCampaignInput.OperationId));
    }

    /// <summary>
    /// Verifies campaign and inline-season end dates cannot precede their starts.
    /// </summary>
    [Fact]
    public void Validate_ReturnsDateErrors_WhenEndDatesPrecedeStarts()
    {
        var errors = InputValidator.Validate(ValidInput() with
        {
            PlannedEndDate = new DateOnly(2026, 5, 31),
            InlineSeason = new InlineSeasonInput
            {
                Name = "2026",
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2025, 12, 31)
            }
        });

        errors.ShouldContainKey(nameof(CreateCampaignInput.PlannedEndDate));
        errors.ShouldContainKey(
            $"{nameof(CreateCampaignInput.InlineSeason)}.{nameof(InlineSeasonInput.EndDate)}");
    }

    /// <summary>
    /// Verifies inline-season property annotations are included in parent validation results.
    /// </summary>
    [Fact]
    public void Validate_ReturnsQualifiedInlineSeasonErrors_WhenInlineSeasonFieldsAreInvalid()
    {
        var errors = InputValidator.Validate(ValidInput() with
        {
            InlineSeason = new InlineSeasonInput
            {
                Name = " ",
                StartDate = new DateOnly(2026, 1, 1)
            }
        });

        errors.ShouldContainKey(
            $"{nameof(CreateCampaignInput.InlineSeason)}.{nameof(InlineSeasonInput.Name)}");
    }

    /// <summary>
    /// Creates a structurally valid campaign request without a season choice.
    /// </summary>
    /// <returns>A campaign request ready for test-specific season selection.</returns>
    private static CreateCampaignInput ValidInput() => new()
    {
        OperationId = Guid.CreateVersion7(),
        Name = "Summer Tryouts",
        StartDate = new DateOnly(2026, 6, 1),
        PlannedEndDate = new DateOnly(2026, 6, 30)
    };
}
