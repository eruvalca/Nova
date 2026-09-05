using Nova.Shared.Results;

namespace Nova.Shared.Features.Clubs;

/// <summary>Loads the signed-in member's current club identity without roster or administration data.</summary>
public interface IClubIdentityQueryService
{
    /// <summary>Gets the signed-in member's current club identity without roster or administration data.</summary>
    /// <param name="cancellationToken">Propagates cancellation to the underlying query.</param>
    /// <returns>The signed-in member's club identity or a service problem.</returns>
    Task<ServiceResult<ClubIdentityResult>> GetCurrentAsync(CancellationToken cancellationToken = default);
}
