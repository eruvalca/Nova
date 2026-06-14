using Nova.Entities;
using Nova.Shared.Account;
using Shouldly;

namespace Nova.Unit.Tests.Account;

/// <summary>
/// Tests for <see cref="NovaUserEntity.FullName"/> property and related mapping.
/// Note: The ToClubMemberDto extension is internal and tested indirectly through ClubMemberService.
/// </summary>
public class NovaUserEntityExtensionsTests
{
    [Fact]
    public void NovaUserEntity_FullName_CombinesFirstAndLastName()
    {
        // Arrange
        var user = new NovaUserEntity
        {
            Id = 42,
            FirstName = "John",
            LastName = "Doe",
            UserName = "john.doe@example.com",
            Email = "john.doe@example.com",
            ClubId = 1
        };

        // Act
        var fullName = user.FullName;

        // Assert
        fullName.ShouldBe("John Doe");
    }

    [Fact]
    public void NovaUserEntity_FullName_WithMultipleWords()
    {
        // Arrange
        var user = new NovaUserEntity
        {
            Id = 1,
            FirstName = "Mary Jane",
            LastName = "Watson-Parker",
            UserName = "mary@example.com",
            Email = "mary@example.com"
        };

        // Act
        var fullName = user.FullName;

        // Assert
        fullName.ShouldBe("Mary Jane Watson-Parker");
    }

    [Fact]
    public void ClubMemberDto_Construction_VerifiesStructure()
    {
        // Arrange
        var dto = new ClubMemberDto(UserId: 555, FullName: "Test User");

        // Act & Assert
        dto.UserId.ShouldBe(555);
        dto.FullName.ShouldBe("Test User");
    }

    [Fact]
    public void ClubMemberDto_Deconstructs_Correctly()
    {
        // Arrange
        var dto = new ClubMemberDto(UserId: 100, FullName: "Jane Smith");

        // Act
        var (userId, fullName) = dto;

        // Assert
        userId.ShouldBe(100);
        fullName.ShouldBe("Jane Smith");
    }
}

