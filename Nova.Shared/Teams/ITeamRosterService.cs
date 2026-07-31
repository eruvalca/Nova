using Nova.Shared.Results;

namespace Nova.Shared.Teams;

/// <summary>
/// Provides tenant-safe read access to the current club's team roster.
/// </summary>
public interface ITeamRosterService
{
    /// <summary>
    /// Retrieves teams matching the requested roster filters.
    /// </summary>
    /// <param name="input">The optional roster filters.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The matching teams or a service problem.</returns>
    Task<ServiceResult<IReadOnlyList<TeamRosterItem>>> GetRosterAsync(
        GetTeamRosterInput input,
        CancellationToken cancellationToken = default);
}
