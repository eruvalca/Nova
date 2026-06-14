using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;
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
    public class TestInput
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
    public void Validate_WithValidInput_ReturnsEmptyDictionary()
    {
        // Arrange
        var input = new TestInput { Name = "Alice", Age = 30 };

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_WithNullName_ContainsNameError()
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
    public void Validate_WithWhitespaceOnlyName_ContainsNameError()
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
    public void Validate_WithNameExceedingMaxLength_ContainsNameError()
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
    public void Validate_WithAgeOutOfRange_ContainsAgeError()
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
    public void Validate_WithMultipleViolations_ContainsAllErrors()
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
    public void Validate_WithNullInput_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var ex = Should.Throw<ArgumentNullException>(() => InputValidator.Validate<TestInput>(null!));

        // Assert
        ex.ShouldNotBeNull();
        ex.ParamName.ShouldBe("input");
    }
}
