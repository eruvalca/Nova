using System.ComponentModel.DataAnnotations;
using Nova.SharedKernel.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Validation;

/// <summary>
/// Tests for <see cref="InputValidator"/>: validation of input objects using DataAnnotations attributes
/// and error projection into the Dictionary&lt;string, string[]&gt; format.
/// </summary>
public class InputValidatorTests
{
    /// <summary>
    /// Test input class with multiple validation constraints.
    /// Uses a class instead of a record because record positional parameter attributes
    /// have a known issue where they don't apply to properties in some C# versions.
    /// </summary>
    private sealed class TestInput
    {
        [Required]
        [NotWhitespace]
        [MaxLength(10)]
        public string Name { get; set; } = "";

        [Required]
        [Range(1, 100)]
        public int Age { get; set; }
    }

    [Fact]
    public void ValidateWithValidInputReturnsEmptyDictionary()
    {
        // Arrange
        var input = new TestInput { Name = "Alice", Age = 30 };

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void ValidateWithNullNameContainsNameError()
    {
        // Arrange
        var input = new TestInput { Name = null!, Age = 30 };

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldContainKey("Name");
        errors["Name"].ShouldNotBeEmpty();
        errors["Name"].ShouldContain(msg => msg.Contains("required", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateWithWhitespaceOnlyNameContainsNameError()
    {
        // Arrange
        var input = new TestInput { Name = "   ", Age = 30 };

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldContainKey("Name");
        errors["Name"].ShouldNotBeEmpty();
        // Should contain error from [NotWhitespace] - "The {0} field must not be empty or whitespace."
        errors["Name"].ShouldContain(msg => msg.Contains("must not be empty") || msg.Contains("whitespace") || msg.Contains("field"));
    }

    [Fact]
    public void ValidateWithNameExceedingMaxLengthContainsNameError()
    {
        // Arrange
        var input = new TestInput { Name = "TooLongString123", Age = 30 }; // 16 chars, max is 10

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldContainKey("Name");
        errors["Name"].ShouldNotBeEmpty();
        errors["Name"].ShouldContain(msg => msg.Contains("length", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateWithAgeOutOfRangeContainsAgeError()
    {
        // Arrange
        var input = new TestInput { Name = "Alice", Age = 0 };

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldContainKey("Age");
        errors["Age"].ShouldNotBeEmpty();
    }

    [Fact]
    public void ValidateWithMultipleViolationsContainsAllErrors()
    {
        // Arrange
        var input = new TestInput { Name = null!, Age = 0 };

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldContainKey("Name");
        errors.ShouldContainKey("Age");
        errors.Count.ShouldBe(2);
    }

    [Fact]
    public void ValidateWithNullInputThrowsArgumentNullException()
    {
        // Arrange & Act
        var ex = Should.Throw<ArgumentNullException>(() => InputValidator.Validate<TestInput>(null!));

        // Assert
        ex.ShouldNotBeNull();
        ex.ParamName.ShouldBe("input");
    }
}
