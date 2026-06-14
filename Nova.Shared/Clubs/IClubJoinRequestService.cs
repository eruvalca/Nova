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
    /// Gets the current user's most recent (non-cancelled) club join request, if one exists.
    /// The returned request may be in any non-cancelled status (Pending, Approved, or Rejected);
    /// cancelled requests are deleted and therefore never returned.
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

    /// <summary>
    /// Gets all pending join requests for the specified club. Caller must be a ClubAdmin of that club.
    /// </summary>
    /// <param name="clubId">The id of the club whose pending requests to list.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ServiceResult{TSuccess}"/> containing the pending <see cref="ClubJoinRequestDto"/> list (oldest first),
    /// or a <see cref="ServiceProblem"/> with <see cref="ServiceProblemKind.Forbidden"/> when the caller is not a ClubAdmin of the club.
    /// </returns>
    Task<ServiceResult<IReadOnlyList<ClubJoinRequestDto>>> GetClubJoinRequestsAsync(long clubId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves a pending join request. Caller must be the ClubAdmin of the request's club.
    /// Sets the request to Approved and assigns the requesting user to the club.
    /// </summary>
    /// <param name="requestId">The id of the join request to approve.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ServiceResult{TSuccess}"/> containing <see cref="Success"/> on success, or a <see cref="ServiceProblem"/>
    /// (Forbidden when not the owning ClubAdmin, NotFound when the request does not exist, Conflict when the request is not Pending).
    /// </returns>
    Task<ServiceResult<Success>> ApproveJoinRequestAsync(long requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects a pending join request. Caller must be the ClubAdmin of the request's club.
    /// Sets the request to Rejected; the requesting user's club membership is unchanged.
    /// </summary>
    /// <param name="requestId">The id of the join request to reject.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ServiceResult{TSuccess}"/> containing <see cref="Success"/> on success, or a <see cref="ServiceProblem"/>
    /// (Forbidden when not the owning ClubAdmin, NotFound when the request does not exist, Conflict when the request is not Pending).
    /// </returns>
    Task<ServiceResult<Success>> RejectJoinRequestAsync(long requestId, CancellationToken cancellationToken = default);
}
