using Nova.Shared.Results;

namespace Nova.Shared.Features.Teams;

/// <summary>
/// Provides administrator team creation and permanent-profile editing operations.
/// </summary>
public interface ITeamManagementService
{
    /// <summary>
    /// Creates an active team for the current club.
    /// </summary>
    /// <param name="input">The new team's profile.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The created team or a service problem.</returns>
    Task<ServiceResult<TeamDto>> CreateAsync(
        CreateTeamInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an active team's name and graduation-year cutoff.
    /// </summary>
    /// <param name="input">The requested team profile.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The updated team or a service problem, including structured eligibility blockers.</returns>
    Task<ServiceResult<TeamDto>> UpdateAsync(
        UpdateTeamInput input,
        CancellationToken cancellationToken = default);
}
