using Nova.Shared.Results;
using OneOf.Types;

namespace Nova.Shared.Clubs;

/// <summary>
/// Provides club join request operations for the current user. Implemented server-side with direct
/// database access and client-side over HTTP for WebAssembly components.
/// </summary>
public interface IClubJoinRequestService
{
    /// <summary>
    /// Gets the current user's pending club join request, if one exists.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ServiceResult{TSuccess}"/> containing the <see cref="ClubJoinRequestDto"/> when the
    /// user has a pending request, or a <see cref="ServiceProblem"/> with <see cref="ServiceProblemKind.NotFound"/>
    /// when no pending request exists.
    /// </returns>
    Task<ServiceResult<ClubJoinRequestDto>> GetCurrentUserPendingRequestAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a request for the current user to join the specified club.
    /// </summary>
    /// <param name="clubId">The id of the club to request joining.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ServiceResult{TSuccess}"/> containing the created <see cref="ClubJoinRequestDto"/> on success,
    /// or a <see cref="ServiceProblem"/> on failure (user already has a pending request, user already belongs to a club,
    /// or the target club does not exist).
    /// </returns>
    Task<ServiceResult<ClubJoinRequestDto>> CreateJoinRequestAsync(long clubId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels and deletes the specified pending join request owned by the current user.
    /// </summary>
    /// <param name="requestId">The id of the join request to cancel.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ServiceResult{TSuccess}"/> containing <see cref="Success"/> on success,
    /// or a <see cref="ServiceProblem"/> on failure (not found, or the request does not belong to the current user).
    /// </returns>
    Task<ServiceResult<Success>> CancelJoinRequestAsync(long requestId, CancellationToken cancellationToken = default);
}
