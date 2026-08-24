using Nova.Shared.Features.Clubs;
using Nova.Shared.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

/// <summary>
/// Tests for <see cref="CreateClubInput"/> validation using DataAnnotations attributes.
///
/// Since CreateClubInput now uses explicit property syntax with attributes applied directly
/// to the properties, InputValidator.Validate() correctly discovers and applies all validation
/// rules ([Required], [NotWhitespace], [MaxLength]).
/// </summary>
public class CreateClubInputValidationTests
{
    [Fact]
    public void CreateClubInput_IsSealed() =>
        // Verify the record is sealed
        typeof(CreateClubInput).IsSealed.ShouldBeTrue();

    [Fact]
    public void CreateClubInput_HasNameProperty()
    {
        // Verify the record has the Name property
        var nameProperty = typeof(CreateClubInput).GetProperty("Name");
        nameProperty.ShouldNotBeNull();
        nameProperty!.PropertyType.ShouldBe(typeof(string));
    }

    [Fact]
    public void CreateClubInput_HasCityProperty()
    {
        // Verify the record has the City property
        var cityProperty = typeof(CreateClubInput).GetProperty("City");
        cityProperty.ShouldNotBeNull();
        cityProperty!.PropertyType.ShouldBe(typeof(string));
    }

    [Fact]
    public void CreateClubInput_HasStateProperty()
    {
        // Verify the record has the State property
        var stateProperty = typeof(CreateClubInput).GetProperty("State");
        stateProperty.ShouldNotBeNull();
        stateProperty!.PropertyType.ShouldBe(typeof(string));
    }

    [Fact]
    public void CreateClubInput_HasCrestContentProperty()
    {
        // Verify the record has the CrestContent property
        var crestContentProperty = typeof(CreateClubInput).GetProperty("CrestContent");
        crestContentProperty.ShouldNotBeNull();
        crestContentProperty!.PropertyType.ShouldBe(typeof(byte[]));
    }

    [Fact]
    public void CreateClubInput_HasCrestContentTypeProperty()
    {
        // Verify the record has the CrestContentType property
        var crestContentTypeProperty = typeof(CreateClubInput).GetProperty("CrestContentType");
        crestContentTypeProperty.ShouldNotBeNull();
        crestContentTypeProperty!.PropertyType.ShouldBe(typeof(string));
    }

    #region Validation Tests

    /// <summary>
    /// Valid input with all required properties set passes validation.
    /// </summary>
    [Fact]
    public void Validate_ReturnsEmpty_WithValidInput()
    {
        // Arrange
        var input = new CreateClubInput
        {
            Name = "Valid Club",
            City = "New York",
            State = "NY",
            CrestContent = TestImages.CreateJpeg(),
            CrestContentType = "image/jpeg"
        };

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldBeEmpty();
    }

    /// <summary>
    /// Null Name violates Required attribute.
    /// </summary>
    [Fact]
    public void Validate_ContainsNameError_WhenNameIsNull()
    {
        // Arrange
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type
        var input = new CreateClubInput
        {
            Name = null!,
            City = "New York",
            State = "NY",
            CrestContent = TestImages.CreateJpeg(),
            CrestContentType = "image/jpeg"
        };
#pragma warning restore CS8625

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldContainKey("Name");
        errors["Name"].ShouldNotBeEmpty();
    }

    /// <summary>
    /// Whitespace-only Name violates NotWhitespace attribute.
    /// </summary>
    [Fact]
    public void Validate_ContainsNameError_WhenNameIsWhitespace()
    {
        // Arrange
        var input = new CreateClubInput
        {
            Name = "   ",
            City = "New York",
            State = "NY",
            CrestContent = TestImages.CreateJpeg(),
            CrestContentType = "image/jpeg"
        };

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldContainKey("Name");
        errors["Name"].ShouldNotBeEmpty();
    }

    /// <summary>
    /// Name exceeding MaxLength(200) violates MaxLength attribute.
    /// </summary>
    [Fact]
    public void Validate_ContainsNameError_WhenNameExceedsMaxLength()
    {
        // Arrange
        var input = new CreateClubInput
        {
            Name = new string('a', 201),
            City = "New York",
            State = "NY",
            CrestContent = TestImages.CreateJpeg(),
            CrestContentType = "image/jpeg"
        };

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldContainKey("Name");
        errors["Name"].ShouldNotBeEmpty();
    }

    /// <summary>
    /// Null City violates Required attribute.
    /// </summary>
    [Fact]
    public void Validate_ContainsCityError_WhenCityIsNull()
    {
        // Arrange
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type
        var input = new CreateClubInput
        {
            Name = "Valid Club",
            City = null!,
            State = "NY",
            CrestContent = TestImages.CreateJpeg(),
            CrestContentType = "image/jpeg"
        };
#pragma warning restore CS8625

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldContainKey("City");
        errors["City"].ShouldNotBeEmpty();
    }

    /// <summary>
    /// Whitespace-only City violates NotWhitespace attribute.
    /// </summary>
    [Fact]
    public void Validate_ContainsCityError_WhenCityIsWhitespace()
    {
        // Arrange
        var input = new CreateClubInput
        {
            Name = "Valid Club",
            City = "   ",
            State = "NY",
            CrestContent = TestImages.CreateJpeg(),
            CrestContentType = "image/jpeg"
        };

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldContainKey("City");
        errors["City"].ShouldNotBeEmpty();
    }

    /// <summary>
    /// City exceeding MaxLength(100) violates MaxLength attribute.
    /// </summary>
    [Fact]
    public void Validate_ContainsCityError_WhenCityExceedsMaxLength()
    {
        // Arrange
        var input = new CreateClubInput
        {
            Name = "Valid Club",
            City = new string('a', 101),
            State = "NY",
            CrestContent = TestImages.CreateJpeg(),
            CrestContentType = "image/jpeg"
        };

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldContainKey("City");
        errors["City"].ShouldNotBeEmpty();
    }

    /// <summary>
    /// Null State violates Required attribute.
    /// </summary>
    [Fact]
    public void Validate_ContainsStateError_WhenStateIsNull()
    {
        // Arrange
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type
        var input = new CreateClubInput
        {
            Name = "Valid Club",
            City = "New York",
            State = null!,
            CrestContent = TestImages.CreateJpeg(),
            CrestContentType = "image/jpeg"
        };
#pragma warning restore CS8625

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldContainKey("State");
        errors["State"].ShouldNotBeEmpty();
    }

    /// <summary>
    /// Whitespace-only State violates NotWhitespace attribute.
    /// </summary>
    [Fact]
    public void Validate_ContainsStateError_WhenStateIsWhitespace()
    {
        // Arrange
        var input = new CreateClubInput
        {
            Name = "Valid Club",
            City = "New York",
            State = "   ",
            CrestContent = TestImages.CreateJpeg(),
            CrestContentType = "image/jpeg"
        };

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldContainKey("State");
        errors["State"].ShouldNotBeEmpty();
    }

    /// <summary>
    /// State exceeding MaxLength(100) violates MaxLength attribute.
    /// </summary>
    [Fact]
    public void Validate_ContainsStateError_WhenStateExceedsMaxLength()
    {
        // Arrange
        var input = new CreateClubInput
        {
            Name = "Valid Club",
            City = "New York",
            State = new string('a', 101),
            CrestContent = TestImages.CreateJpeg(),
            CrestContentType = "image/jpeg"
        };

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldContainKey("State");
        errors["State"].ShouldNotBeEmpty();
    }

    /// <summary>
    /// Null CrestContent violates Required attribute.
    /// </summary>
    [Fact]
    public void Validate_ContainsCrestContentError_WhenCrestContentIsNull()
    {
        // Arrange
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type
        var input = new CreateClubInput
        {
            Name = "Valid Club",
            City = "New York",
            State = "NY",
            CrestContent = null!,
            CrestContentType = "image/jpeg"
        };
#pragma warning restore CS8625

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldContainKey("CrestContent");
        errors["CrestContent"].ShouldNotBeEmpty();
    }

    /// <summary>
    /// Null CrestContentType violates Required attribute.
    /// </summary>
    [Fact]
    public void Validate_ContainsCrestContentTypeError_WhenCrestContentTypeIsNull()
    {
        // Arrange
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type
        var input = new CreateClubInput
        {
            Name = "Valid Club",
            City = "New York",
            State = "NY",
            CrestContent = TestImages.CreateJpeg(),
            CrestContentType = null!
        };
#pragma warning restore CS8625

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldContainKey("CrestContentType");
        errors["CrestContentType"].ShouldNotBeEmpty();
    }

    /// <summary>
    /// Whitespace-only CrestContentType violates NotWhitespace attribute.
    /// </summary>
    [Fact]
    public void Validate_ContainsCrestContentTypeError_WhenCrestContentTypeIsWhitespace()
    {
        // Arrange
        var input = new CreateClubInput
        {
            Name = "Valid Club",
            City = "New York",
            State = "NY",
            CrestContent = TestImages.CreateJpeg(),
            CrestContentType = "   "
        };

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldContainKey("CrestContentType");
        errors["CrestContentType"].ShouldNotBeEmpty();
    }

    /// <summary>
    /// Multiple validation errors are all reported.
    /// </summary>
    [Fact]
    public void Validate_ReportsMultipleErrors_WhenMultiplePropertiesAreInvalid()
    {
        // Arrange
        var input = new CreateClubInput
        {
            Name = "   ",  // Violates NotWhitespace
            City = new string('a', 101),  // Violates MaxLength
            State = "   ",  // Violates NotWhitespace
            CrestContent = [],
            CrestContentType = "   "
        };

        // Act
        var errors = InputValidator.Validate(input);

        // Assert
        errors.ShouldContainKey("Name");
        errors.ShouldContainKey("City");
        errors.ShouldContainKey("State");
        errors.ShouldContainKey("CrestContentType");
        errors.Count.ShouldBe(4);
    }

    #endregion
}
