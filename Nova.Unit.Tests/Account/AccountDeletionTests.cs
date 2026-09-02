using Nova.Shared.Features.Account;
using Nova.Shared.Features.Clubs;
using Shouldly;

namespace Nova.Unit.Tests.Account;

/// <summary>
/// Tests for Phase 1 account deletion contracts:
/// - <see cref="AccountDeletionScenario"/> enum values
/// - <see cref="AccountDeletionPreviewDto"/> record equality and deconstruction
/// - <see cref="ClubMemberDto"/> record equality and deconstruction
/// - <see cref="ClubEndpoints"/> constants for member lifecycle routes
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
    public void ClubEndpoints_PromoteMemberUrl_EqualsExpectedValue()
    {
        // Arrange & Act
        var value = ClubEndpoints.PromoteMemberUrl(42);

        // Assert
        value.ShouldBe("/api/clubs/members/42/promote");
    }

    [Fact]
    public void ClubEndpoints_LeaveClub_EqualsExpectedValue()
    {
        // Arrange & Act
        var value = ClubEndpoints.LeaveClub;

        // Assert
        value.ShouldBe("/api/clubs/membership");
    }

    #endregion
}
