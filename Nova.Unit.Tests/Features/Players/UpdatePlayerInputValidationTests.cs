using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Validation;
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
    public void ValidateWithValidInputReturnsNoErrors()
    {
        InputValidator.Validate(ValidInput()).ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateWithInvalidPlayerIdReturnsError(long id)
    {
        var input = ValidInput() with { PlayerId = id };
        var errors = InputValidator.Validate(input);
        errors.ShouldContainKey("PlayerId");
    }

    [Fact]
    public void ValidateWithValidPlayerIdReturnsNoError()
    {
        var input = ValidInput() with { PlayerId = long.MaxValue };
        InputValidator.Validate(input).ShouldBeEmpty();
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
        var input = ValidInput() with { FirstName = new string('a', 101) };
        InputValidator.Validate(input).ShouldContainKey("FirstName");
    }

    [Fact]
    public void ValidateWithLastNameExceedingMaxLengthReturnsError()
    {
        var input = ValidInput() with { LastName = new string('a', 101) };
        InputValidator.Validate(input).ShouldContainKey("LastName");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(1999)]
    [InlineData(2101)]
    public void ValidateWithOutOfRangeGraduationYearReturnsError(int year)
    {
        var input = ValidInput() with { GraduationYear = year };
        InputValidator.Validate(input).ShouldContainKey("GraduationYear");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(-1)]
    [InlineData(10000)]
    public void ValidateWithOutOfRangeJerseyNumberReturnsError(int jersey)
    {
        var input = ValidInput() with { JerseyNumber = jersey };
        InputValidator.Validate(input).ShouldContainKey("JerseyNumber");
    }
}
