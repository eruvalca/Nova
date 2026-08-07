using Nova.Shared.Results;

namespace Nova.Shared.Features.Teams;

/// <summary>
/// Provides tenant-safe read access to team detail and placement context.
/// </summary>
public interface ITeamDetailService
{
    /// <summary>
    /// Gets one team's permanent profile and bounded placement projections.
    /// </summary>
    /// <param name="teamId">The team identifier.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The team detail payload or a service problem.</returns>
    Task<ServiceResult<TeamDetailDto>> GetTeamDetailAsync(
        long teamId,
        CancellationToken cancellationToken = default);
}

