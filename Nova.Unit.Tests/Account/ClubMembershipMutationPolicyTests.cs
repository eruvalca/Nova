using Nova.Features.Account;
using Shouldly;

namespace Nova.Unit.Tests.Account;

public class ClubMembershipMutationPolicyTests
{
    [Theory]
    [InlineData(false, typeof(MembershipMutationMayApply))]
    [InlineData(true, typeof(MembershipMutationNoOp))]
    public void Promote_ReturnsExpectedDecision(bool isAdministrator, Type expected)
        => OutcomeType(ClubMembershipMutationPolicy.Promote(isAdministrator)).ShouldBe(expected);

    [Theory]
    [InlineData(false, 1, typeof(MembershipMutationNoOp))]
    [InlineData(true, 1, typeof(SoleAdministratorConflict))]
    [InlineData(true, 2, typeof(MembershipMutationMayApply))]
    public void Demote_ReturnsExpectedDecision(bool isAdministrator, int count, Type expected)
        => OutcomeType(ClubMembershipMutationPolicy.Demote(isAdministrator, count)).ShouldBe(expected);

    [Theory]
    [InlineData(10, 10, typeof(UseLeaveEndpointConflict))]
    [InlineData(10, 11, typeof(MembershipMutationMayApply))]
    public void Remove_ReturnsExpectedDecision(long actorUserId, long targetUserId, Type expected)
        => OutcomeType(ClubMembershipMutationPolicy.Remove(actorUserId, targetUserId)).ShouldBe(expected);

    [Theory]
    [InlineData(false, 0, 1, typeof(FinalMemberConflict))]
    [InlineData(true, 1, 2, typeof(SoleAdministratorConflict))]
    [InlineData(true, 2, 2, typeof(MembershipMutationMayApply))]
    [InlineData(false, 1, 2, typeof(MembershipMutationMayApply))]
    public void Leave_ReturnsExpectedDecision(bool isAdministrator, int admins, int members, Type expected)
        => OutcomeType(ClubMembershipMutationPolicy.Leave(isAdministrator, admins, members)).ShouldBe(expected);

    private static Type OutcomeType(
        OneOf.OneOf<MembershipMutationMayApply, MembershipMutationNoOp, SoleAdministratorConflict, FinalMemberConflict, UseLeaveEndpointConflict> outcome)
        => outcome.Match(
            mayApply => mayApply.GetType(),
            noOp => noOp.GetType(),
            soleAdministrator => soleAdministrator.GetType(),
            finalMember => finalMember.GetType(),
            useLeaveEndpoint => useLeaveEndpoint.GetType());
}
