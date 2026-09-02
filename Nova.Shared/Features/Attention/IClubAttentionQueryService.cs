using Nova.Shared.Results;

namespace Nova.Shared.Features.Attention;

/// <summary>
/// Provides administrator-only read access to the two independent club attention regions
/// (pending join requests and campaigns needing placement). Implemented server-side with direct
/// database access and client-side over HTTP for WebAssembly components.
/// </summary>
public interface IClubAttentionQueryService
{
    /// <summary>
    /// Retrieves the club attention projection for the caller's club. Administrators receive both
    /// regions; each region reports its own failure without affecting the other.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The attention projection or a service problem.</returns>
    Task<ServiceResult<ClubAttentionResult>> GetClubAttentionAsync(
        CancellationToken cancellationToken = default);
}
