using Nova.SharedKernel.Features.Seasons;
using Nova.SharedKernel.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Seasons;

/// <summary>Verifies structural validation for season mutation inputs.</summary>
public sealed class SeasonInputValidationTests
{
    /// <summary>Verifies empty operation IDs and blank names are rejected.</summary>
    [Fact]
    public void CreateSeasonInputRejectsInvalidFields()
    {
        var errors = InputValidator.Validate(new CreateSeasonInput
        {
            OperationId = Guid.Empty,
            Name = "  ",
            StartDate = new DateOnly(2026, 2, 1)
        });

        errors.ShouldContainKey(nameof(CreateSeasonInput.OperationId));
        errors.ShouldContainKey(nameof(CreateSeasonInput.Name));
    }

    /// <summary>Verifies an end date before the start date is rejected.</summary>
    [Fact]
    public void CreateSeasonInputRejectsInvertedDateRange()
    {
        var errors = InputValidator.Validate(new CreateSeasonInput
        {
            OperationId = Guid.NewGuid(),
            Name = "Season",
            StartDate = new DateOnly(2026, 2, 1),
            EndDate = new DateOnly(2026, 1, 31)
        });

        errors.ShouldContainKey(nameof(CreateSeasonInput.EndDate));
    }

    /// <summary>Verifies paging accepts defaults but rejects values outside the documented bounds.</summary>
    [Fact]
    public void PagingUsesOptionalDefaultsAndRejectsOversizedPage()
    {
        InputValidator.Validate(new GetSeasonListInput()).ShouldBeEmpty();
        var errors = InputValidator.Validate(new GetSeasonListInput
        {
            Page = 0,
            PageSize = GetSeasonListInput.MaximumPageSize + 1
        });
        errors.ShouldContainKey(nameof(GetSeasonListInput.Page));
        errors.ShouldContainKey(nameof(GetSeasonListInput.PageSize));
    }

    /// <summary>Verifies update and advancement inputs enforce tokens, IDs, names, and date ranges.</summary>
    [Fact]
    public void UpdateAndStartNextInputsRejectInvalidFields()
    {
        var updateErrors = InputValidator.Validate(new UpdateSeasonInput
        {
            ExpectedConcurrencyToken = Guid.Empty,
            Name = " ",
            StartDate = new DateOnly(2026, 2, 1),
            EndDate = new DateOnly(2026, 1, 31)
        });
        var startErrors = InputValidator.Validate(new StartNextSeasonInput
        {
            OperationId = Guid.Empty,
            ExpectedCurrentSeasonId = 0,
            Name = " ",
            StartDate = new DateOnly(2026, 2, 1),
            EndDate = new DateOnly(2026, 1, 31)
        });
        var updateDateErrors = InputValidator.Validate(new UpdateSeasonInput
        {
            ExpectedConcurrencyToken = Guid.NewGuid(),
            Name = "Season",
            StartDate = new DateOnly(2026, 2, 1),
            EndDate = new DateOnly(2026, 1, 31)
        });
        var startDateErrors = InputValidator.Validate(new StartNextSeasonInput
        {
            OperationId = Guid.NewGuid(),
            ExpectedCurrentSeasonId = 1,
            Name = "Season",
            StartDate = new DateOnly(2026, 2, 1),
            EndDate = new DateOnly(2026, 1, 31)
        });

        updateErrors.Keys.ShouldContain(nameof(UpdateSeasonInput.ExpectedConcurrencyToken), StringComparer.Ordinal);
        updateErrors.Keys.ShouldContain(nameof(UpdateSeasonInput.Name), StringComparer.Ordinal);
        startErrors.Keys.ShouldContain(nameof(StartNextSeasonInput.OperationId), StringComparer.Ordinal);
        startErrors.Keys.ShouldContain(nameof(StartNextSeasonInput.ExpectedCurrentSeasonId), StringComparer.Ordinal);
        startErrors.Keys.ShouldContain(nameof(StartNextSeasonInput.Name), StringComparer.Ordinal);
        updateDateErrors.Keys.ShouldContain(nameof(UpdateSeasonInput.EndDate), StringComparer.Ordinal);
        startDateErrors.Keys.ShouldContain(nameof(StartNextSeasonInput.EndDate), StringComparer.Ordinal);
    }
}
