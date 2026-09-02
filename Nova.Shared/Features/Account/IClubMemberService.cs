using Nova.Shared.Results;

namespace Nova.Shared.Features.Account;

/// <summary>Lists club members and manages club membership lifecycle.</summary>
public interface IClubMemberService
{
    /// <summary>Returns the other members of the current user's club (excludes the current user).</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The other club members, or a failure result.</returns>
    Task<ServiceResult<IReadOnlyList<ClubMemberDto>>> GetClubMembersAsync(CancellationToken cancellationToken);

    /// <summary>Promotes a member of the current user's club to ClubAdmin.</summary>
    Task<ServiceResult<OneOf.Types.Success>> PromoteMemberAsync(ClubMemberMutationInput input, CancellationToken cancellationToken = default);

    /// <summary>Demotes a ClubAdmin in the current user's club.</summary>
    Task<ServiceResult<OneOf.Types.Success>> DemoteMemberAsync(ClubMemberMutationInput input, CancellationToken cancellationToken = default);

    /// <summary>Removes another member from the current user's club.</summary>
    Task<ServiceResult<OneOf.Types.Success>> RemoveMemberAsync(ClubMemberMutationInput input, CancellationToken cancellationToken = default);

    /// <summary>Leaves the current user's club.</summary>
    Task<ServiceResult<OneOf.Types.Success>> LeaveClubAsync(CancellationToken cancellationToken = default);
}
