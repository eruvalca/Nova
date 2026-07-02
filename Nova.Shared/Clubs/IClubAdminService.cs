using Nova.Shared.Results;

namespace Nova.Shared.Clubs;

/// <summary>
/// Provides club-administration read operations for the club admin page.
/// </summary>
/// <remarks>
/// This service is consumed only by the server-rendered <c>ClubAdmin</c> page (static SSR),
/// so — unlike <c>IClubMemberService</c> — there is no WASM <c>HttpClient</c> client
/// implementation by design.
/// </remarks>
public interface IClubAdminService
{
    /// <summary>
    /// Gets the club-admin summary for the specified club.
    /// </summary>
    /// <param name="clubId">The id of the club to load.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ServiceResult{TSuccess}"/> containing a <see cref="ClubAdminSummaryDto"/> on success,
    /// or a <see cref="ServiceProblem"/> on failure.
    /// </returns>
    Task<ServiceResult<ClubAdminSummaryDto>> GetClubAdminSummaryAsync(long clubId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the full roster for the specified club, including the current user and club-admin flags.
    /// </summary>
    /// <param name="clubId">The id of the club to load.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ServiceResult{TSuccess}"/> containing an <see cref="IReadOnlyList{T}"/> of
    /// <see cref="ClubMemberDetailDto"/> on success, or a <see cref="ServiceProblem"/> on failure.
    /// </returns>
    Task<ServiceResult<IReadOnlyList<ClubMemberDetailDto>>> GetClubRosterAsync(long clubId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the ClubAdmin role from a target member of the caller's club.
    /// </summary>
    /// <param name="input">The target member to demote.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ServiceResult{TSuccess}"/> containing <c>true</c> when demotion succeeds,
    /// or a <see cref="ServiceProblem"/> when the caller is unauthorized, the target is invalid,
    /// or the club would be left without any administrators.
    /// </returns>
    Task<ServiceResult<bool>> DemoteClubAdminAsync(DemoteAdminInput input, CancellationToken cancellationToken = default);
}
