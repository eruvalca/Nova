using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Nova.Shared.Account;
using Nova.Shared.Clubs;
using Shouldly;

namespace Nova.Unit.Tests.Account;

/// <summary>
/// Tests for Phase 1 account deletion contracts:
/// - <see cref="AccountDeletionScenario"/> enum values
/// - <see cref="AccountDeletionPreviewDto"/> record equality and deconstruction
/// - <see cref="ClubMemberDto"/> record equality and deconstruction
/// - <see cref="AssignAdminInput"/> record equality, deconstruction, and validation
/// - <see cref="ClubEndpoints"/> constants for member and admin assignment routes
/// </summary>
public class AccountDeletionTests
{
    #region AccountDeletionScenario Enum Tests

    [Fact]
    public void AccountDeletionScenario_Value_NoClubOrNonAdmin()
    {
        // Arrange & Act
        var value = AccountDeletionScenario.NoClubOrNonAdmin;

        // Assert
        ((int)value).ShouldBe(0);
    }

    [Fact]
    public void AccountDeletionScenario_Value_OnlyClubMember()
    {
        // Arrange & Act
        var value = AccountDeletionScenario.OnlyClubMember;

        // Assert
        ((int)value).ShouldBe(1);
    }

    [Fact]
    public void AccountDeletionScenario_Value_SoleClubAdmin()
    {
        // Arrange & Act
        var value = AccountDeletionScenario.SoleClubAdmin;

        // Assert
        ((int)value).ShouldBe(2);
    }

    #endregion

    #region AccountDeletionPreviewDto Tests

    [Fact]
    public void AccountDeletionPreviewDto_EqualsOtherInstance_WithSameValues()
    {
        // Arrange
        var preview1 = new AccountDeletionPreviewDto(
            Scenario: AccountDeletionScenario.SoleClubAdmin,
            ClubName: "Manchester City",
            OtherMemberCount: 5);
        var preview2 = new AccountDeletionPreviewDto(
            Scenario: AccountDeletionScenario.SoleClubAdmin,
            ClubName: "Manchester City",
            OtherMemberCount: 5);

        // Act & Assert
        preview1.ShouldBe(preview2);
        (preview1 == preview2).ShouldBeTrue();
    }

    [Fact]
    public void AccountDeletionPreviewDto_NotEqualsOtherInstance_WithDifferentScenario()
    {
        // Arrange
        var preview1 = new AccountDeletionPreviewDto(
            Scenario: AccountDeletionScenario.SoleClubAdmin,
            ClubName: "Manchester City",
            OtherMemberCount: 5);
        var preview2 = new AccountDeletionPreviewDto(
            Scenario: AccountDeletionScenario.OnlyClubMember,
            ClubName: "Manchester City",
            OtherMemberCount: 5);

        // Act & Assert
        preview1.ShouldNotBe(preview2);
        (preview1 != preview2).ShouldBeTrue();
    }

    [Fact]
    public void AccountDeletionPreviewDto_NotEqualsOtherInstance_WithDifferentClubName()
    {
        // Arrange
        var preview1 = new AccountDeletionPreviewDto(
            Scenario: AccountDeletionScenario.SoleClubAdmin,
            ClubName: "Manchester City",
            OtherMemberCount: 5);
        var preview2 = new AccountDeletionPreviewDto(
            Scenario: AccountDeletionScenario.SoleClubAdmin,
            ClubName: "Manchester United",
            OtherMemberCount: 5);

        // Act & Assert
        preview1.ShouldNotBe(preview2);
        (preview1 != preview2).ShouldBeTrue();
    }

    [Fact]
    public void AccountDeletionPreviewDto_NotEqualsOtherInstance_WithDifferentOtherMemberCount()
    {
        // Arrange
        var preview1 = new AccountDeletionPreviewDto(
            Scenario: AccountDeletionScenario.SoleClubAdmin,
            ClubName: "Manchester City",
            OtherMemberCount: 5);
        var preview2 = new AccountDeletionPreviewDto(
            Scenario: AccountDeletionScenario.SoleClubAdmin,
            ClubName: "Manchester City",
            OtherMemberCount: 10);

        // Act & Assert
        preview1.ShouldNotBe(preview2);
        (preview1 != preview2).ShouldBeTrue();
    }

    [Fact]
    public void AccountDeletionPreviewDto_Deconstructs_Correctly()
    {
        // Arrange
        var preview = new AccountDeletionPreviewDto(
            Scenario: AccountDeletionScenario.OnlyClubMember,
            ClubName: "Liverpool FC",
            OtherMemberCount: 0);

        // Act
        var (scenario, clubName, otherMemberCount) = preview;

        // Assert
        scenario.ShouldBe(AccountDeletionScenario.OnlyClubMember);
        clubName.ShouldBe("Liverpool FC");
        otherMemberCount.ShouldBe(0);
    }

    [Fact]
    public void AccountDeletionPreviewDto_Deconstructs_WithNullValues()
    {
        // Arrange
        var preview = new AccountDeletionPreviewDto(
            Scenario: AccountDeletionScenario.NoClubOrNonAdmin,
            ClubName: null,
            OtherMemberCount: null);

        // Act
        var (scenario, clubName, otherMemberCount) = preview;

        // Assert
        scenario.ShouldBe(AccountDeletionScenario.NoClubOrNonAdmin);
        clubName.ShouldBeNull();
        otherMemberCount.ShouldBeNull();
    }

    #endregion

    #region ClubMemberDto Tests

    [Fact]
    public void ClubMemberDto_EqualsOtherInstance_WithSameValues()
    {
        // Arrange
        var member1 = new ClubMemberDto(UserId: 42, FullName: "John Smith");
        var member2 = new ClubMemberDto(UserId: 42, FullName: "John Smith");

        // Act & Assert
        member1.ShouldBe(member2);
        (member1 == member2).ShouldBeTrue();
    }

    [Fact]
    public void ClubMemberDto_NotEqualsOtherInstance_WithDifferentUserId()
    {
        // Arrange
        var member1 = new ClubMemberDto(UserId: 42, FullName: "John Smith");
        var member2 = new ClubMemberDto(UserId: 43, FullName: "John Smith");

        // Act & Assert
        member1.ShouldNotBe(member2);
        (member1 != member2).ShouldBeTrue();
    }

    [Fact]
    public void ClubMemberDto_NotEqualsOtherInstance_WithDifferentFullName()
    {
        // Arrange
        var member1 = new ClubMemberDto(UserId: 42, FullName: "John Smith");
        var member2 = new ClubMemberDto(UserId: 42, FullName: "Jane Smith");

        // Act & Assert
        member1.ShouldNotBe(member2);
        (member1 != member2).ShouldBeTrue();
    }

    [Fact]
    public void ClubMemberDto_Deconstructs_Correctly()
    {
        // Arrange
        var member = new ClubMemberDto(UserId: 88, FullName: "Alice Johnson");

        // Act
        var (userId, fullName) = member;

        // Assert
        userId.ShouldBe(88);
        fullName.ShouldBe("Alice Johnson");
    }

    #endregion

    #region AssignAdminInput Tests

    [Fact]
    public void AssignAdminInput_EqualsOtherInstance_WithSameValues()
    {
        // Arrange
        var input1 = new AssignAdminInput { TargetUserId = 100 };
        var input2 = new AssignAdminInput { TargetUserId = 100 };

        // Act & Assert
        input1.ShouldBe(input2);
        (input1 == input2).ShouldBeTrue();
    }

    [Fact]
    public void AssignAdminInput_NotEqualsOtherInstance_WithDifferentTargetUserId()
    {
        // Arrange
        var input1 = new AssignAdminInput { TargetUserId = 100 };
        var input2 = new AssignAdminInput { TargetUserId = 200 };

        // Act & Assert
        input1.ShouldNotBe(input2);
        (input1 != input2).ShouldBeTrue();
    }

    [Fact]
    public void AssignAdminInput_ValidationPasses_WhenTargetUserIdIsZero_BecauseRequiredIsContractMarkerOnly()
    {
        // Arrange
        var input = new AssignAdminInput { TargetUserId = 0 };
        var context = new ValidationContext(input, null, null);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(input, context, results, validateAllProperties: true);

        // Assert
        // Note: [Required] on non-nullable long doesn't reject 0; it's a contract marker for API docs.
        // For phase 1 (contracts), we just verify it can be constructed and validated without exceptions.
        isValid.ShouldBeTrue();
        results.ShouldBeEmpty();
    }

    [Fact]
    public void AssignAdminInput_ValidationSucceeds_WhenTargetUserIdIsNonZero()
    {
        // Arrange
        var input = new AssignAdminInput { TargetUserId = 1 };
        var context = new ValidationContext(input, null, null);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(input, context, results, validateAllProperties: true);

        // Assert
        isValid.ShouldBeTrue();
        results.ShouldBeEmpty();
    }

    [Fact]
    public void AssignAdminInput_HasRequiredAttribute_OnTargetUserId()
    {
        // Arrange
        var property = typeof(AssignAdminInput).GetProperty(nameof(AssignAdminInput.TargetUserId));

        // Act
        var hasRequired = property?.GetCustomAttributes(typeof(RequiredAttribute), inherit: false).Any() ?? false;

        // Assert
        hasRequired.ShouldBeTrue();
    }

    [Fact]
    public void AssignAdminInput_ValidationSucceeds_WhenTargetUserIdIsLargePositive()
    {
        // Arrange
        var input = new AssignAdminInput { TargetUserId = 9876543210 };
        var context = new ValidationContext(input, null, null);
        var results = new List<ValidationResult>();

        // Act
        var isValid = Validator.TryValidateObject(input, context, results, validateAllProperties: true);

        // Assert
        isValid.ShouldBeTrue();
        results.ShouldBeEmpty();
    }

    #endregion

    #region ClubEndpoints Constants Tests

    [Fact]
    public void ClubEndpoints_GetMembersRelative_EqualsExpectedValue()
    {
        // Arrange & Act
        var value = ClubEndpoints.GetMembersRelative;

        // Assert
        value.ShouldBe("members");
    }

    [Fact]
    public void ClubEndpoints_GetMembers_EqualsExpectedValue()
    {
        // Arrange & Act
        var value = ClubEndpoints.GetMembers;

        // Assert
        value.ShouldBe("/api/clubs/members");
    }

    [Fact]
    public void ClubEndpoints_AssignAdminRelative_EqualsExpectedValue()
    {
        // Arrange & Act
        var value = ClubEndpoints.AssignAdminRelative;

        // Assert
        value.ShouldBe("assign-admin");
    }

    [Fact]
    public void ClubEndpoints_AssignAdmin_EqualsExpectedValue()
    {
        // Arrange & Act
        var value = ClubEndpoints.AssignAdmin;

        // Assert
        value.ShouldBe("/api/clubs/assign-admin");
    }

    #endregion
}
