using Nova.Shared.Results;

namespace Nova.Shared.Features.Players;

/// <summary>
/// Provides player-roster query operations for club members.
/// </summary>
public interface IPlayerService
{
    /// <summary>
    /// Retrieves a paged roster of active players for the requested club.
    /// </summary>
    /// <param name="input">The roster query input.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A paged roster on success, or a <see cref="ServiceProblem"/> when validation or authorization fails.
    /// </returns>
    Task<ServiceResult<PagedResult<PlayerListItem>>> GetPlayerRosterAsync(
        GetPlayerRosterInput input,
        CancellationToken cancellationToken = default);
}
