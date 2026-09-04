using Nova.Shared.Results;

namespace Nova.Shared.Features.Clubs;

/// <summary>Loads the signed-in member's current club identity without roster or administration data.</summary>
public interface IClubIdentityQueryService
{
    Task<ServiceResult<ClubIdentityResult>> GetCurrentAsync(CancellationToken cancellationToken = default);
}
