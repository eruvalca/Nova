using Nova.Shared.Players;
using Nova.Shared.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Features.Players;

/// <summary>
/// Validates DataAnnotations on <see cref="UpdatePlayerInput"/> using <see cref="InputValidator"/>.
/// </summary>
public sealed class UpdatePlayerInputValidationTests
{
    private static UpdatePlayerInput ValidInput() => new()
    {
        PlayerId = 1,
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
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithInvalidPlayerId_ReturnsError(long id)
    {
        var input = ValidInput() with { PlayerId = id };
        var errors = InputValidator.Validate(input);
        errors.ShouldContainKey("PlayerId");
    }

    [Fact]
    public void Validate_WithValidPlayerId_ReturnsNoError()
    {
        var input = ValidInput() with { PlayerId = long.MaxValue };
        InputValidator.Validate(input).ShouldBeEmpty();
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
        var input = ValidInput() with { FirstName = new string('a', 101) };
        InputValidator.Validate(input).ShouldContainKey("FirstName");
    }

    [Fact]
    public void Validate_WithLastNameExceedingMaxLength_ReturnsError()
    {
        var input = ValidInput() with { LastName = new string('a', 101) };
        InputValidator.Validate(input).ShouldContainKey("LastName");
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    public void Validate_WithOutOfRangeGraduationYear_ReturnsError(int year)
    {
        var input = ValidInput() with { GraduationYear = year };
        InputValidator.Validate(input).ShouldContainKey("GraduationYear");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10000)]
    public void Validate_WithOutOfRangeJerseyNumber_ReturnsError(int jersey)
    {
        var input = ValidInput() with { JerseyNumber = jersey };
        InputValidator.Validate(input).ShouldContainKey("JerseyNumber");
    }
}
