using Nova.SharedKernel.Results;

namespace Nova.SharedKernel.Features.Players;

/// <summary>
/// Provides the manual-intake enrollment preview for one club.
/// Implemented server-side with direct database access and client-side over HTTP for WebAssembly components.
/// </summary>
public interface IPlayerIntakeContextService
{
    /// <summary>
    /// Reads the club's current Active campaign so a manual intake board can state the enrollment
    /// consequence before commitment.
    /// </summary>
    /// <param name="input">The authenticated club to read.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>
    /// The current intake context with paired nulls when no campaign is Active, or a
    /// validation/authorization problem.
    /// </returns>
    Task<ServiceResult<PlayerIntakeContext>> GetPlayerIntakeContextAsync(
        GetPlayerIntakeContextInput input,
        CancellationToken cancellationToken = default);
}
