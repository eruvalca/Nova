using System.Net.Http.Json;
using Nova.Shared.Results;
using Nova.Shared.Teams;

namespace Nova.Client.Services;

/// <summary>
/// WebAssembly client implementation of <see cref="ITeamManagementService"/> that calls the
/// server's team-management endpoints over HTTP.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpTeamManagementService(HttpClient http) : ITeamManagementService
{
    /// <inheritdoc />
    public async Task<ServiceResult<TeamDto>> CreateAsync(
        CreateTeamInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(TeamEndpoints.Create, input, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var team = await response.Content.ReadFromJsonAsync<TeamDto>(cancellationToken);
        if (team is null)
        {
            return ServiceProblem.ServerError("The server returned an empty team response.");
        }

        return team;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<TeamDto>> UpdateAsync(
        UpdateTeamInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync(
            TeamEndpoints.UpdateUrl(input.TeamId),
            input,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var team = await response.Content.ReadFromJsonAsync<TeamDto>(cancellationToken);
        if (team is null)
        {
            return ServiceProblem.ServerError("The server returned an empty team response.");
        }

        return team;
    }
}
