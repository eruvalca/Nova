using Nova.SharedKernel.Validation;
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
    public void IsValidWithNullReturnsTrue()
    {
        // Arrange
        object? value = null;

        // Act
        var result = _attribute.IsValid(value);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void IsValidWithEmptyStringReturnsFalse()
    {
        // Arrange
        var value = "";

        // Act
        var result = _attribute.IsValid(value);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\t\n")]
    public void IsValidWithWhitespaceOnlyReturnsFalse(string value)
    {
        // Arrange & Act
        var result = _attribute.IsValid(value);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("a")]
    [InlineData("hello")]
    [InlineData("  hello  ")]
    [InlineData("0")]
    [InlineData("123")]
    [InlineData(" \t world \n ")]
    public void IsValidWithNonBlankStringReturnsTrue(string value)
    {
        // Arrange & Act
        var result = _attribute.IsValid(value);

        // Assert
        result.ShouldBeTrue();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(42)]
    [InlineData(3.14)]
    [InlineData(true)]
    [InlineData(false)]
    public void IsValidWithNonStringValueReturnsTrue(object value)
    {
        // Arrange & Act
        var result = _attribute.IsValid(value);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void IsValidWithNonStringObjectReturnsTrue()
    {
        // Arrange
        var value = new object();

        // Act
        var result = _attribute.IsValid(value);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void FormatErrorMessageReturnsMessageWithFieldName()
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
