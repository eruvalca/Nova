using Nova.Shared.Results;
using OneOf.Types;

namespace Nova.Shared.Features.Teams;

/// <summary>
/// Provides administrator-only team lifecycle mutations.
/// </summary>
public interface ITeamLifecycleService
{
    /// <summary>
    /// Archives a team when no active-campaign placement references it.
    /// </summary>
    /// <param name="teamId">The team identifier to archive.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A success result or a structured service problem.</returns>
    Task<ServiceResult<Success>> ArchiveAsync(
        long teamId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores an archived team to active use.
    /// </summary>
    /// <param name="teamId">The team identifier to restore.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A success result or a service problem.</returns>
    Task<ServiceResult<Success>> RestoreAsync(
        long teamId,
        CancellationToken cancellationToken = default);
}
