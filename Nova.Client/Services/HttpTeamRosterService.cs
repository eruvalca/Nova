using System.Net.Http.Json;
using Nova.Shared.Results;
using Nova.Shared.Teams;
using Nova.Shared.Validation;

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
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        using var response = await http.GetAsync(
            TeamRosterEndpoints.GetRosterUrl(input.Search, input.LifecycleStatus, input.GraduationYear),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var result = await response.Content.ReadRequiredJsonAsync<List<TeamRosterItem>>(
            "The server returned an invalid team roster response.",
            teams => teams.All(IsValidTeam),
            cancellationToken);
        return result.Match<ServiceResult<IReadOnlyList<TeamRosterItem>>>(
            teams => teams.AsReadOnly(),
            problem => problem);
    }

    /// <summary>
    /// Validates the portable invariants of a team-roster row.
    /// </summary>
    /// <param name="team">The team row to validate.</param>
    /// <returns><see langword="true"/> when the row is structurally valid.</returns>
    private static bool IsValidTeam(TeamRosterItem team)
        => team is not null
            && team.TeamId > 0
            && !string.IsNullOrWhiteSpace(team.Name)
            && team.ActivePlacementCount >= 0;
}
