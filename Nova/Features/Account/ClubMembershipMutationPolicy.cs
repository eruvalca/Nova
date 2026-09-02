namespace Nova.Features.Account;

/// <summary>Pure decisions for serialized club-membership mutations.</summary>
internal static class ClubMembershipMutationPolicy
{
    internal enum Decision
    {
        Apply,
        NoOp,
        SoleAdministrator,
        FinalMember,
        UseLeaveEndpoint,
    }

    internal static Decision Promote(bool targetIsAdministrator)
        => targetIsAdministrator ? Decision.NoOp : Decision.Apply;

    internal static Decision Demote(bool targetIsAdministrator, int administratorCount)
        => !targetIsAdministrator
            ? Decision.NoOp
            : administratorCount <= 1
                ? Decision.SoleAdministrator
                : Decision.Apply;

    internal static Decision Remove(long actorUserId, long targetUserId)
        => actorUserId == targetUserId ? Decision.UseLeaveEndpoint : Decision.Apply;

    internal static Decision Leave(bool actorIsAdministrator, int administratorCount, int memberCount)
        => memberCount <= 1
            ? Decision.FinalMember
            : actorIsAdministrator && administratorCount <= 1
                ? Decision.SoleAdministrator
                : Decision.Apply;
}
