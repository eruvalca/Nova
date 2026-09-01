using Nova.Shared.Results;

namespace Nova.Shared.Features.Activity;

/// <summary>
/// Provides tenant-safe, role-shaped read access to the club activity feed. Implemented
/// server-side with direct database access and client-side over HTTP for WebAssembly components.
/// </summary>
public interface IClubActivityQueryService
{
    /// <summary>
    /// Retrieves one deterministic page of the caller's club activity feed, newest-first. Members
    /// see only publicly visible events; administrators additionally see admin-only events.
    /// </summary>
    /// <param name="input">The optional continuation cursor.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The activity page or a service problem.</returns>
    Task<ServiceResult<ClubActivityResult>> GetClubActivityAsync(
        GetClubActivityInput input,
        CancellationToken cancellationToken = default);
}
