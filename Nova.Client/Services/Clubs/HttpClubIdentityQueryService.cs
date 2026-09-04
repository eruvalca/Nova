using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;

namespace Nova.Client.Services.Clubs;

/// <summary>Loads the current club identity over HTTP for WebAssembly rendering.</summary>
public sealed class HttpClubIdentityQueryService(HttpClient http) : IClubIdentityQueryService
{
    public async Task<ServiceResult<ClubIdentityResult>> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(ClubEndpoints.GetCurrent, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<ClubIdentityResult>(
            "The server returned an invalid current club identity.",
            identity => identity is not null
                && identity.ClubId > 0
                && !string.IsNullOrWhiteSpace(identity.Name)
                && !string.IsNullOrWhiteSpace(identity.City)
                && !string.IsNullOrWhiteSpace(identity.State),
            cancellationToken);
    }
}
