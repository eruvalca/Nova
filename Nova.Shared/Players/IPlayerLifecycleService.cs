using Nova.Shared.Results;
using OneOf.Types;

namespace Nova.Shared.Players;

/// <summary>
/// Provides player lifecycle archive and restore operations that are shared between server and WebAssembly clients.
/// </summary>
public interface IPlayerLifecycleService
{
    /// <summary>
    /// Archives a player when no undecided participation exists in active campaigns.
    /// </summary>
    /// <param name="playerId">The player identifier to archive.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A successful <see cref="Success"/> result when archived, or a <see cref="ServiceProblem"/> when
    /// forbidden, not found, or blocked by active undecided campaign participation.
    /// </returns>
    Task<ServiceResult<Success>> ArchiveAsync(long playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores an archived player to active status and clears archive provenance.
    /// </summary>
    /// <param name="playerId">The player identifier to restore.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A successful <see cref="Success"/> result when restored, or a <see cref="ServiceProblem"/> when
    /// forbidden, not found, or conflicting with current lifecycle state.
    /// </returns>
    Task<ServiceResult<Success>> RestoreAsync(long playerId, CancellationToken cancellationToken = default);
}
