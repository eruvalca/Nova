using Nova.Shared.Results;

namespace Nova.Shared.Account;

/// <summary>Lists club members and promotes a member to ClubAdmin.</summary>
public interface IClubMemberService
{
    /// <summary>Returns the other members of the current user's club (excludes the current user).</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The other club members, or a failure result.</returns>
    Task<ServiceResult<IReadOnlyList<ClubMemberDto>>> GetClubMembersAsync(CancellationToken cancellationToken);

    /// <summary>Promotes the specified member of the current user's club to ClubAdmin.</summary>
    /// <param name="targetUserId">The user to promote.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> on success, or a failure result.</returns>
    Task<ServiceResult<bool>> AssignClubAdminAsync(long targetUserId, CancellationToken cancellationToken);
}
