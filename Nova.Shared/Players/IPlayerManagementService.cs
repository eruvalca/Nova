using Nova.Shared.Results;

namespace Nova.Shared.Players;

/// <summary>
/// Provides player creation and permanent-profile editing operations.
/// Implemented server-side with direct database access and client-side over HTTP for WebAssembly components.
/// </summary>
public interface IPlayerManagementService
{
    /// <summary>
    /// Creates a new Active player and atomically enrolls that player into every currently Active
    /// campaign in the current club.
    /// </summary>
    /// <param name="input">The player profile details.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ServiceResult{TSuccess}"/> containing the created <see cref="PlayerDto"/> on success,
    /// or a <see cref="ServiceProblem"/> on failure (validation errors, forbidden, or server error).
    /// </returns>
    Task<ServiceResult<PlayerDto>> CreateAsync(CreatePlayerInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a player's permanent profile fields. Blocks graduation-year changes that would
    /// invalidate any Active-campaign Assigned placement and returns structured blocker information.
    /// </summary>
    /// <param name="input">The updated profile fields.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ServiceResult{TSuccess}"/> containing the updated <see cref="PlayerDto"/> on success,
    /// or a <see cref="ServiceProblem"/> on failure (validation errors, forbidden, not found,
    /// conflict with archived status or blocked graduation-year change, or server error).
    /// </returns>
    Task<ServiceResult<PlayerDto>> UpdateAsync(UpdatePlayerInput input, CancellationToken cancellationToken = default);
}
