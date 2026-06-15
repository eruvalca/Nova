using Nova.Shared.Results;

namespace Nova.Shared.Clubs;

/// <summary>
/// Provides club-detail read operations for the club detail page.
/// </summary>
public interface IClubDetailService
{
    /// <summary>
    /// Gets the details for the specified club, including its member roster and current-user context.
    /// </summary>
    /// <param name="clubId">The id of the club to load.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ServiceResult{TSuccess}"/> containing <see cref="ClubDetailDto"/> on success,
    /// or a <see cref="ServiceProblem"/> on failure (not found or forbidden).
    /// </returns>
    Task<ServiceResult<ClubDetailDto>> GetClubDetailAsync(long clubId, CancellationToken cancellationToken = default);
}
