using System.Net.Http.Json;
using Nova.Shared.Results;
using Nova.Shared.Teams;
using OneOf.Types;

namespace Nova.Client.Services;

/// <summary>
/// WebAssembly client implementation of <see cref="ITeamLifecycleService"/> that calls team lifecycle endpoints.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpTeamLifecycleService(HttpClient http) : ITeamLifecycleService
{
    /// <inheritdoc />
    public Task<ServiceResult<Success>> ArchiveAsync(
        long teamId,
        CancellationToken cancellationToken = default)
        => SendMutationAsync(TeamEndpoints.ArchiveUrl(teamId), cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<Success>> RestoreAsync(
        long teamId,
        CancellationToken cancellationToken = default)
        => SendMutationAsync(TeamEndpoints.RestoreUrl(teamId), cancellationToken);

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> UpdateGraduationYearAsync(
        UpdateTeamGraduationYearInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync(
            TeamEndpoints.UpdateGraduationYearUrl(input.TeamId),
            input,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return new Success();
    }

    private async Task<ServiceResult<Success>> SendMutationAsync(
        string requestUri,
        CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync(requestUri, content: null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return new Success();
    }
}
