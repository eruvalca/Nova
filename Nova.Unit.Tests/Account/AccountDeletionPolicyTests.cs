using Nova.Features.Account;
using Nova.Shared.Account;
using Shouldly;

namespace Nova.Unit.Tests.Account;

/// <summary>
/// Tests account-deletion classification without Identity, EF, DI, or mocks.
/// </summary>
public sealed class AccountDeletionPolicyTests
{
    /// <summary>
    /// Verifies unauthenticated and missing-user facts produce the neutral scenario.
    /// </summary>
    /// <param name="isAuthenticated">Whether the current user is authenticated.</param>
    /// <param name="userExists">Whether the Identity user exists.</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void Evaluate_ReturnsNoClubOrNonAdmin_WhenUserIsUnavailable(
        bool isAuthenticated,
        bool userExists)
    {
        var result = AccountDeletionPolicy.Evaluate(
            CreateFacts(isAuthenticated: isAuthenticated, userExists: userExists));

        result.ShouldBe(new AccountDeletionPreviewDto(AccountDeletionScenario.NoClubOrNonAdmin, null, null));
    }

    /// <summary>
    /// Verifies a non-administrator produces the neutral scenario.
    /// </summary>
    [Fact]
    public void Evaluate_ReturnsNoClubOrNonAdmin_WhenUserIsNotClubAdmin()
    {
        var result = AccountDeletionPolicy.Evaluate(CreateFacts(isClubAdmin: false));

        result.Scenario.ShouldBe(AccountDeletionScenario.NoClubOrNonAdmin);
    }

    /// <summary>
    /// Verifies an administrator without a club produces the neutral scenario.
    /// </summary>
    [Fact]
    public void Evaluate_ReturnsNoClubOrNonAdmin_WhenClubIsMissing()
    {
        var result = AccountDeletionPolicy.Evaluate(CreateFacts(clubId: null));

        result.Scenario.ShouldBe(AccountDeletionScenario.NoClubOrNonAdmin);
    }

    /// <summary>
    /// Verifies an only member is told that account deletion also deletes the club.
    /// </summary>
    [Fact]
    public void Evaluate_ReturnsOnlyClubMember_WhenTotalMemberCountIsOne()
    {
        var result = AccountDeletionPolicy.Evaluate(
            CreateFacts(totalMemberCount: 1, clubAdminCount: 1));

        result.ShouldBe(new AccountDeletionPreviewDto(AccountDeletionScenario.OnlyClubMember, "Club A", 0));
    }

    /// <summary>
    /// Verifies a sole administrator with other members must transfer administration first.
    /// </summary>
    [Fact]
    public void Evaluate_ReturnsSoleClubAdmin_WhenOtherMembersHaveNoOtherAdmin()
    {
        var result = AccountDeletionPolicy.Evaluate(
            CreateFacts(totalMemberCount: 4, clubAdminCount: 1));

        result.ShouldBe(new AccountDeletionPreviewDto(AccountDeletionScenario.SoleClubAdmin, "Club A", 3));
    }

    /// <summary>
    /// Verifies another administrator makes club-specific deletion handling unnecessary.
    /// </summary>
    [Fact]
    public void Evaluate_ReturnsNoClubOrNonAdmin_WhenMultipleAdminsExist()
    {
        var result = AccountDeletionPolicy.Evaluate(
            CreateFacts(totalMemberCount: 4, clubAdminCount: 2));

        result.ShouldBe(new AccountDeletionPreviewDto(AccountDeletionScenario.NoClubOrNonAdmin, null, null));
    }

    /// <summary>
    /// Creates valid account-deletion facts with overridable scenario inputs.
    /// </summary>
    /// <param name="isAuthenticated">Whether a current user identifier exists.</param>
    /// <param name="userExists">Whether the Identity user exists.</param>
    /// <param name="isClubAdmin">Whether the user is a club administrator.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="totalMemberCount">The current club member count.</param>
    /// <param name="clubAdminCount">The current club administrator count.</param>
    /// <returns>The requested account-deletion fact snapshot.</returns>
    private static AccountDeletionFacts CreateFacts(
        bool isAuthenticated = true,
        bool userExists = true,
        bool isClubAdmin = true,
        long? clubId = 100,
        int totalMemberCount = 3,
        int clubAdminCount = 2)
        => new(
            isAuthenticated,
            userExists,
            isClubAdmin,
            clubId,
            "Club A",
            totalMemberCount,
            clubAdminCount);
}
