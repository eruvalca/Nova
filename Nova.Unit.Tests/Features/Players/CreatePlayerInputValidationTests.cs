using Nova.Shared.Enums;
using Nova.Shared.Players;
using Nova.Shared.Validation;
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
    public void Validate_WithValidInput_ReturnsNoErrors()
    {
        InputValidator.Validate(ValidInput()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithBlankFirstName_ReturnsError(string? firstName)
    {
        var input = ValidInput() with { FirstName = firstName! };
        var errors = InputValidator.Validate(input);
        errors.ShouldContainKey("FirstName");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithBlankLastName_ReturnsError(string? lastName)
    {
        var input = ValidInput() with { LastName = lastName! };
        var errors = InputValidator.Validate(input);
        errors.ShouldContainKey("LastName");
    }

    [Fact]
    public void Validate_WithFirstNameExceedingMaxLength_ReturnsError()
    {
        var input = ValidInput() with { FirstName = new string('x', 101) };
        var errors = InputValidator.Validate(input);
        errors.ShouldContainKey("FirstName");
    }

    [Fact]
    public void Validate_WithLastNameExceedingMaxLength_ReturnsError()
    {
        var input = ValidInput() with { LastName = new string('x', 101) };
        var errors = InputValidator.Validate(input);
        errors.ShouldContainKey("LastName");
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    public void Validate_WithOutOfRangeGraduationYear_ReturnsError(int year)
    {
        var input = ValidInput() with { GraduationYear = year };
        var errors = InputValidator.Validate(input);
        errors.ShouldContainKey("GraduationYear");
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(2050)]
    [InlineData(2100)]
    public void Validate_WithValidGraduationYear_ReturnsNoError(int year)
    {
        var input = ValidInput() with { GraduationYear = year };
        InputValidator.Validate(input).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10000)]
    public void Validate_WithOutOfRangeJerseyNumber_ReturnsError(int jersey)
    {
        var input = ValidInput() with { JerseyNumber = jersey };
        var errors = InputValidator.Validate(input);
        errors.ShouldContainKey("JerseyNumber");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(9999)]
    public void Validate_WithValidJerseyNumber_ReturnsNoError(int jersey)
    {
        var input = ValidInput() with { JerseyNumber = jersey };
        InputValidator.Validate(input).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_WithNullGenderAndNullJerseyNumber_ReturnsNoErrors()
    {
        var input = ValidInput() with { Gender = null, JerseyNumber = null };
        InputValidator.Validate(input).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_WithAllOptionalFieldsSet_ReturnsNoErrors()
    {
        var input = ValidInput() with { Gender = Gender.Male, JerseyNumber = 10 };
        InputValidator.Validate(input).ShouldBeEmpty();
    }
}
