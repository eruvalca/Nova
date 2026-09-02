using OneOf;

namespace Nova.Features.Account;

/// <summary>Indicates that a club-membership mutation should be applied.</summary>
internal readonly record struct MembershipMutationMayApply;

/// <summary>Indicates that the requested club-membership state already exists.</summary>
internal readonly record struct MembershipMutationNoOp;

/// <summary>Indicates that a mutation would remove the club's sole administrator.</summary>
internal readonly record struct SoleAdministratorConflict;

/// <summary>Indicates that a leave would remove the club's final member.</summary>
internal readonly record struct FinalMemberConflict;

/// <summary>Indicates that administrative self-removal must use the leave endpoint.</summary>
internal readonly record struct UseLeaveEndpointConflict;

/// <summary>Pure decisions for serialized club-membership mutations.</summary>
internal static class ClubMembershipMutationPolicy
{
    /// <summary>Decides whether promoting a member changes their current role.</summary>
    /// <param name="targetIsAdministrator">Whether the target already has the administrator role.</param>
    /// <returns>An exhaustive domain outcome for the promotion.</returns>
    internal static OneOf<MembershipMutationMayApply, MembershipMutationNoOp, SoleAdministratorConflict, FinalMemberConflict, UseLeaveEndpointConflict> Promote(
        bool targetIsAdministrator)
        => targetIsAdministrator ? new MembershipMutationNoOp() : new MembershipMutationMayApply();

    /// <summary>Decides whether an administrator may be demoted.</summary>
    /// <param name="targetIsAdministrator">Whether the target has the administrator role.</param>
    /// <param name="administratorCount">The current number of club administrators.</param>
    /// <returns>An exhaustive domain outcome for the demotion.</returns>
    internal static OneOf<MembershipMutationMayApply, MembershipMutationNoOp, SoleAdministratorConflict, FinalMemberConflict, UseLeaveEndpointConflict> Demote(
        bool targetIsAdministrator,
        int administratorCount)
        => !targetIsAdministrator
            ? new MembershipMutationNoOp()
            : administratorCount <= 1
                ? new SoleAdministratorConflict()
                : new MembershipMutationMayApply();

    /// <summary>Decides whether an administrator may remove the target member.</summary>
    /// <param name="actorUserId">The acting administrator's user identifier.</param>
    /// <param name="targetUserId">The target member's user identifier.</param>
    /// <returns>An exhaustive domain outcome for the removal.</returns>
    internal static OneOf<MembershipMutationMayApply, MembershipMutationNoOp, SoleAdministratorConflict, FinalMemberConflict, UseLeaveEndpointConflict> Remove(
        long actorUserId,
        long targetUserId)
        => actorUserId == targetUserId ? new UseLeaveEndpointConflict() : new MembershipMutationMayApply();

    /// <summary>Decides whether a member may voluntarily leave the club.</summary>
    /// <param name="actorIsAdministrator">Whether the leaving member is an administrator.</param>
    /// <param name="administratorCount">The current number of club administrators.</param>
    /// <param name="memberCount">The current number of club members.</param>
    /// <returns>An exhaustive domain outcome for the leave.</returns>
    internal static OneOf<MembershipMutationMayApply, MembershipMutationNoOp, SoleAdministratorConflict, FinalMemberConflict, UseLeaveEndpointConflict> Leave(
        bool actorIsAdministrator,
        int administratorCount,
        int memberCount)
        => memberCount <= 1
            ? new FinalMemberConflict()
            : actorIsAdministrator && administratorCount <= 1
                ? new SoleAdministratorConflict()
                : new MembershipMutationMayApply();
}
