using System.Net.Http.Json;
using Nova.Shared.Results;
using Nova.Shared.Teams;

namespace Nova.Client.Services;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="ITeamDetailService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpTeamDetailService(HttpClient http) : ITeamDetailService
{
    /// <inheritdoc />
    public async Task<ServiceResult<TeamDetailDto>> GetTeamDetailAsync(
        long teamId,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(TeamEndpoints.GetDetailUrl(teamId), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var detail = await response.Content.ReadFromJsonAsync<TeamDetailDto>(cancellationToken);
        return detail is null
            ? ServiceProblem.ServerError("The server returned an empty team detail response.")
            : detail;
    }
}
