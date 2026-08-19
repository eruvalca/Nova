using Nova.Shared.Features.Tags;
using Nova.Shared.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Features.Tags;

/// <summary>
/// Verifies DataAnnotations on tag-definition inputs.
/// </summary>
public sealed class TagInputValidationTests
{
    private static CreateTagDefinitionInput ValidCreate() => new()
    {
        Name = "Forward",
        Color = "#1a2b3c"
    };

    private static UpdateTagDefinitionInput ValidUpdate() => new()
    {
        TagId = 1,
        Name = "Defender",
        Color = "#A1B2C3"
    };

    [Fact]
    public void Create_WithValidInput_ReturnsNoErrors()
        => InputValidator.Validate(ValidCreate()).ShouldBeEmpty();

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankName_ReturnsError(string? name)
        => InputValidator.Validate(ValidCreate() with { Name = name! }).ShouldContainKey("Name");

    [Fact]
    public void Create_WithOverlongName_ReturnsError()
        => InputValidator.Validate(ValidCreate() with { Name = new string('x', 101) })
            .ShouldContainKey("Name");

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#12345")]   // too short
    [InlineData("#1234567")] // too long
    [InlineData("1234567")]  // missing leading hash
    [InlineData("#GGGGGG")]  // non-hex characters
    [InlineData("#12345g")]  // trailing non-hex character
    public void Create_WithInvalidColor_ReturnsError(string? color)
        => InputValidator.Validate(ValidCreate() with { Color = color! }).ShouldContainKey("Color");

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("#a1b2c3")] // lowercase is valid
    [InlineData("#A1B2C3")] // uppercase is valid
    [InlineData("#09AfF0")] // mixed case is valid
    public void Create_WithValidColor_ReturnsNoErrors(string color)
        => InputValidator.Validate(ValidCreate() with { Color = color }).ShouldBeEmpty();

    [Fact]
    public void Update_WithValidInput_ReturnsNoErrors()
        => InputValidator.Validate(ValidUpdate()).ShouldBeEmpty();

    [Fact]
    public void Update_WithInvalidTagId_ReturnsError()
        => InputValidator.Validate(ValidUpdate() with { TagId = 0 }).ShouldContainKey("TagId");

    [Fact]
    public void Update_WithBlankName_ReturnsError()
        => InputValidator.Validate(ValidUpdate() with { Name = "  " }).ShouldContainKey("Name");

    [Fact]
    public void Update_WithInvalidColor_ReturnsError()
        => InputValidator.Validate(ValidUpdate() with { Color = "red" }).ShouldContainKey("Color");

    [Fact]
    public void GetList_WithValidInput_ReturnsNoErrors()
        => InputValidator.Validate(new GetTagDefinitionsInput { Search = "for", LifecycleStatus = "active" })
            .ShouldBeEmpty();

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("active")]
    [InlineData("archived")]
    [InlineData("all")]
    [InlineData("Active")]
    [InlineData("ARCHIVED")]
    public void GetList_WithValidLifecycleStatus_ReturnsNoErrors(string? status)
        => InputValidator.Validate(new GetTagDefinitionsInput { LifecycleStatus = status }).ShouldBeEmpty();

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("bogus")]
    [InlineData("   ")]
    public void GetList_WithInvalidLifecycleStatus_ReturnsError(string? status)
        => InputValidator.Validate(new GetTagDefinitionsInput { LifecycleStatus = status })
            .ShouldContainKey("LifecycleStatus");

    [Fact]
    public void GetList_WithOverlongSearch_ReturnsError()
        => InputValidator.Validate(new GetTagDefinitionsInput { Search = new string('x', 101) })
            .ShouldContainKey("Search");
}
