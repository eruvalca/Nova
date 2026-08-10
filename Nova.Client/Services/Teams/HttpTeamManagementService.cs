using System.Net.Http.Json;
using Nova.Shared.Enums;
using Nova.Shared.Features.Teams;
using Nova.Shared.Results;

namespace Nova.Client.Services.Teams;

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

        return await response.Content.ReadRequiredJsonAsync<TeamDto>(
            "The server returned an invalid team response.",
            team => IsValidTeam(team),
            cancellationToken);
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

        return await response.Content.ReadRequiredJsonAsync<TeamDto>(
            "The server returned an invalid team response.",
            team => IsValidTeam(team, input.TeamId),
            cancellationToken);
    }

    /// <summary>
    /// Validates the portable invariants of a team success payload.
    /// </summary>
    /// <param name="team">The team to validate.</param>
    /// <param name="expectedTeamId">The expected team identifier, when known.</param>
    /// <returns><see langword="true"/> when the team is structurally valid.</returns>
    private static bool IsValidTeam(TeamDto team, long? expectedTeamId = null)
        => team is not null
            && team.TeamId > 0
            && (expectedTeamId is null || team.TeamId == expectedTeamId)
            && team.ClubId > 0
            && !string.IsNullOrWhiteSpace(team.Name)
            && team.GraduationYear is >= 2000 and <= 2100
            && team.LifecycleStatus is LifecycleStatus.Active or LifecycleStatus.Archived;
}
