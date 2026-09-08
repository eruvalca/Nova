using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Validation;
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
    public void CreateWithValidInputReturnsNoErrors()
        => InputValidator.Validate(ValidCreate()).ShouldBeEmpty();

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateWithBlankNameReturnsError(string? name)
        => InputValidator.Validate(ValidCreate() with { Name = name! }).ShouldContainKey("Name");

    [Fact]
    public void CreateWithOverlongNameReturnsError()
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
    public void CreateWithInvalidColorReturnsError(string? color)
        => InputValidator.Validate(ValidCreate() with { Color = color! }).ShouldContainKey("Color");

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("#a1b2c3")] // lowercase is valid
    [InlineData("#A1B2C3")] // uppercase is valid
    [InlineData("#09AfF0")] // mixed case is valid
    public void CreateWithValidColorReturnsNoErrors(string color)
        => InputValidator.Validate(ValidCreate() with { Color = color }).ShouldBeEmpty();

    [Fact]
    public void UpdateWithValidInputReturnsNoErrors()
        => InputValidator.Validate(ValidUpdate()).ShouldBeEmpty();

    [Fact]
    public void UpdateWithInvalidTagIdReturnsError()
        => InputValidator.Validate(ValidUpdate() with { TagId = 0 }).ShouldContainKey("TagId");

    [Fact]
    public void UpdateWithBlankNameReturnsError()
        => InputValidator.Validate(ValidUpdate() with { Name = "  " }).ShouldContainKey("Name");

    [Fact]
    public void UpdateWithInvalidColorReturnsError()
        => InputValidator.Validate(ValidUpdate() with { Color = "red" }).ShouldContainKey("Color");

    [Fact]
    public void GetListWithValidInputReturnsNoErrors()
        => InputValidator.Validate(new GetTagDefinitionsInput { Search = "for", LifecycleStatus = "active" })
            .ShouldBeEmpty();

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("active")]
    [InlineData("archived")]
    [InlineData("all")]
    [InlineData("Active")]
    [InlineData("ARCHIVED")]
    public void GetListWithValidLifecycleStatusReturnsNoErrors(string? status)
        => InputValidator.Validate(new GetTagDefinitionsInput { LifecycleStatus = status }).ShouldBeEmpty();

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("bogus")]
    [InlineData("   ")]
    public void GetListWithInvalidLifecycleStatusReturnsError(string? status)
        => InputValidator.Validate(new GetTagDefinitionsInput { LifecycleStatus = status })
            .ShouldContainKey("LifecycleStatus");

    [Fact]
    public void GetListWithOverlongSearchReturnsError()
        => InputValidator.Validate(new GetTagDefinitionsInput { Search = new string('x', 101) })
            .ShouldContainKey("Search");
}
