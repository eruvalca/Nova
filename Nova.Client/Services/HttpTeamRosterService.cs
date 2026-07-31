using System.Net.Http.Json;
using Nova.Shared.Results;
using Nova.Shared.Teams;

namespace Nova.Client.Services;

/// <summary>
/// WebAssembly client implementation of <see cref="ITeamRosterService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpTeamRosterService(HttpClient http) : ITeamRosterService
{
    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<TeamRosterItem>>> GetRosterAsync(
        GetTeamRosterInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(
            TeamRosterEndpoints.GetRosterUrl(input.Search, input.LifecycleStatus, input.GraduationYear),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var teams = await response.Content.ReadFromJsonAsync<List<TeamRosterItem>>(cancellationToken);
        return teams is null
            ? ServiceProblem.ServerError("The server returned an empty team roster response.")
            : teams.AsReadOnly();
    }
}
