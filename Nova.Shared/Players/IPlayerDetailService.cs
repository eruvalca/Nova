using Nova.Shared.Results;

namespace Nova.Shared.Players;

/// <summary>
/// Provides read operations for player detail and campaign history payloads.
/// </summary>
public interface IPlayerDetailService
{
    /// <summary>
    /// Gets the detail payload for one player in the current tenant context.
    /// </summary>
    /// <param name="playerId">The player identifier to load.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ServiceResult{TSuccess}"/> containing <see cref="PlayerDetailDto"/> on success,
    /// or a <see cref="ServiceProblem"/> on failure.
    /// </returns>
    Task<ServiceResult<PlayerDetailDto>> GetPlayerDetailAsync(long playerId, CancellationToken cancellationToken = default);
}
