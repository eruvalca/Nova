using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Features.Players;

/// <summary>
/// Validates DataAnnotations on <see cref="CreatePlayerInput"/> using <see cref="InputValidator"/>.
/// </summary>
public sealed class CreatePlayerInputValidationTests
{
    private static CreatePlayerInput ValidInput() => new()
    {
        FirstName = "Jordan",
        LastName = "Smith",
        DateOfBirth = new DateOnly(2010, 5, 15),
        GraduationYear = 2028
    };

    [Fact]
    public void ValidateWithValidInputReturnsNoErrors()
    {
        InputValidator.Validate(ValidInput()).ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateWithBlankFirstNameReturnsError(string? firstName)
    {
        var input = ValidInput() with { FirstName = firstName! };
        var errors = InputValidator.Validate(input);
        errors.ShouldContainKey("FirstName");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateWithBlankLastNameReturnsError(string? lastName)
    {
        var input = ValidInput() with { LastName = lastName! };
        var errors = InputValidator.Validate(input);
        errors.ShouldContainKey("LastName");
    }

    [Fact]
    public void ValidateWithFirstNameExceedingMaxLengthReturnsError()
    {
        var input = ValidInput() with { FirstName = new string('x', 101) };
        var errors = InputValidator.Validate(input);
        errors.ShouldContainKey("FirstName");
    }

    [Fact]
    public void ValidateWithLastNameExceedingMaxLengthReturnsError()
    {
        var input = ValidInput() with { LastName = new string('x', 101) };
        var errors = InputValidator.Validate(input);
        errors.ShouldContainKey("LastName");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(1999)]
    [InlineData(2101)]
    public void ValidateWithOutOfRangeGraduationYearReturnsError(int year)
    {
        var input = ValidInput() with { GraduationYear = year };
        var errors = InputValidator.Validate(input);
        errors.ShouldContainKey("GraduationYear");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(2000)]
    [InlineData(2050)]
    [InlineData(2100)]
    public void ValidateWithValidGraduationYearReturnsNoError(int year)
    {
        var input = ValidInput() with { GraduationYear = year };
        InputValidator.Validate(input).ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(-1)]
    [InlineData(10000)]
    public void ValidateWithOutOfRangeJerseyNumberReturnsError(int jersey)
    {
        var input = ValidInput() with { JerseyNumber = jersey };
        var errors = InputValidator.Validate(input);
        errors.ShouldContainKey("JerseyNumber");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(9999)]
    public void ValidateWithValidJerseyNumberReturnsNoError(int jersey)
    {
        var input = ValidInput() with { JerseyNumber = jersey };
        InputValidator.Validate(input).ShouldBeEmpty();
    }

    [Fact]
    public void ValidateWithNullGenderAndNullJerseyNumberReturnsNoErrors()
    {
        var input = ValidInput() with { Gender = null, JerseyNumber = null };
        InputValidator.Validate(input).ShouldBeEmpty();
    }

    [Fact]
    public void ValidateWithAllOptionalFieldsSetReturnsNoErrors()
    {
        var input = ValidInput() with { Gender = Gender.Male, JerseyNumber = 10 };
        InputValidator.Validate(input).ShouldBeEmpty();
    }
}
