using Nova.Features.Account;
using Shouldly;

namespace Nova.Unit.Tests.Account;

public class ClubMembershipMutationPolicyTests
{
    [Theory]
    [InlineData(false, "Apply")]
    [InlineData(true, "NoOp")]
    public void Promote_ReturnsExpectedDecision(bool isAdministrator, string expected)
        => ClubMembershipMutationPolicy.Promote(isAdministrator).ToString().ShouldBe(expected);

    [Theory]
    [InlineData(false, 1, "NoOp")]
    [InlineData(true, 1, "SoleAdministrator")]
    [InlineData(true, 2, "Apply")]
    public void Demote_ReturnsExpectedDecision(bool isAdministrator, int count, string expected)
        => ClubMembershipMutationPolicy.Demote(isAdministrator, count).ToString().ShouldBe(expected);

    [Theory]
    [InlineData(false, 0, 1, "FinalMember")]
    [InlineData(true, 1, 2, "SoleAdministrator")]
    [InlineData(true, 2, 2, "Apply")]
    [InlineData(false, 1, 2, "Apply")]
    public void Leave_ReturnsExpectedDecision(bool isAdministrator, int admins, int members, string expected)
        => ClubMembershipMutationPolicy.Leave(isAdministrator, admins, members).ToString().ShouldBe(expected);
}
