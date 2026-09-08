using Nova.SharedKernel.Features.Teams;
using Nova.SharedKernel.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Features.Teams;

/// <summary>
/// Verifies DataAnnotations on team management inputs.
/// </summary>
public sealed class TeamInputValidationTests
{
    private static CreateTeamInput ValidCreate() => new()
    {
        Name = "U16 Red",
        GraduationYear = 2028
    };

    private static UpdateTeamInput ValidUpdate() => new()
    {
        TeamId = 1,
        Name = "U16 Blue",
        GraduationYear = 2029
    };

    [Fact]
    public void CreateWithValidInputReturnsNoErrors()
        => InputValidator.Validate(ValidCreate()).ShouldBeEmpty();

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateWithBlankNameReturnsError(string? name)
        => InputValidator.Validate(ValidCreate() with { Name = name! }).ShouldContainKey("Name");

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(1999)]
    [InlineData(2101)]
    public void CreateWithOutOfRangeGraduationYearReturnsError(int year)
        => InputValidator.Validate(ValidCreate() with { GraduationYear = year })
            .ShouldContainKey("GraduationYear");

    [Fact]
    public void UpdateWithInvalidTeamIdReturnsError()
        => InputValidator.Validate(ValidUpdate() with { TeamId = 0 }).ShouldContainKey("TeamId");

    [Fact]
    public void UpdateWithOverlongNameReturnsError()
        => InputValidator.Validate(ValidUpdate() with { Name = new string('x', 101) })
            .ShouldContainKey("Name");
}
