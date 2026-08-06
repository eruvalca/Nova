using Nova.Shared.Features.Teams;
using Nova.Shared.Validation;
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
    public void Create_WithValidInput_ReturnsNoErrors()
        => InputValidator.Validate(ValidCreate()).ShouldBeEmpty();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankName_ReturnsError(string? name)
        => InputValidator.Validate(ValidCreate() with { Name = name! }).ShouldContainKey("Name");

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    public void Create_WithOutOfRangeGraduationYear_ReturnsError(int year)
        => InputValidator.Validate(ValidCreate() with { GraduationYear = year })
            .ShouldContainKey("GraduationYear");

    [Fact]
    public void Update_WithInvalidTeamId_ReturnsError()
        => InputValidator.Validate(ValidUpdate() with { TeamId = 0 }).ShouldContainKey("TeamId");

    [Fact]
    public void Update_WithOverlongName_ReturnsError()
        => InputValidator.Validate(ValidUpdate() with { Name = new string('x', 101) })
            .ShouldContainKey("Name");
}
