using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;

namespace Nova.Client.Services.Clubs;

/// <summary>Loads the current club identity over HTTP for WebAssembly rendering.</summary>
/// <param name="http">The HTTP client for the authenticated server API.</param>
public sealed class HttpClubIdentityQueryService(HttpClient http) : IClubIdentityQueryService
{
    /// <inheritdoc />
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
                && identity.Name.Length <= 200
                && !string.IsNullOrWhiteSpace(identity.City)
                && identity.City.Length <= 100
                && !string.IsNullOrWhiteSpace(identity.State)
                && identity.State.Length <= 100,
            cancellationToken);
    }
}
