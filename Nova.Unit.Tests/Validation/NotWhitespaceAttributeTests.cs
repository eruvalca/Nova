using Nova.Shared.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Validation;

/// <summary>
/// Tests for <see cref="NotWhitespaceAttribute"/>: validation behavior for null, empty, whitespace-only,
/// and non-blank strings, as well as non-string values.
/// </summary>
public class NotWhitespaceAttributeTests
{
    private readonly NotWhitespaceAttribute _attribute = new();

    [Fact]
    public void IsValid_WithNull_ReturnsTrue()
    {
        // Arrange
        object? value = null;

        // Act
        var result = _attribute.IsValid(value);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void IsValid_WithEmptyString_ReturnsFalse()
    {
        // Arrange
        var value = "";

        // Act
        var result = _attribute.IsValid(value);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\t\n")]
    public void IsValid_WithWhitespaceOnly_ReturnsFalse(string value)
    {
        // Arrange & Act
        var result = _attribute.IsValid(value);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("hello")]
    [InlineData("  hello  ")]
    [InlineData("0")]
    [InlineData("123")]
    [InlineData(" \t world \n ")]
    public void IsValid_WithNonBlankString_ReturnsTrue(string value)
    {
        // Arrange & Act
        var result = _attribute.IsValid(value);

        // Assert
        result.ShouldBeTrue();
    }

    [Theory]
    [InlineData(42)]
    [InlineData(3.14)]
    [InlineData(true)]
    [InlineData(false)]
    public void IsValid_WithNonStringValue_ReturnsTrue(object value)
    {
        // Arrange & Act
        var result = _attribute.IsValid(value);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void IsValid_WithNonStringObject_ReturnsTrue()
    {
        // Arrange
        var value = new object();

        // Act
        var result = _attribute.IsValid(value);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void FormatErrorMessage_ReturnsMessageWithFieldName()
    {
        // Arrange
        var fieldName = "TestField";

        // Act
        var message = _attribute.FormatErrorMessage(fieldName);

        // Assert
        message.ShouldNotBeNullOrEmpty();
        message.ShouldContain(fieldName);
    }
}
