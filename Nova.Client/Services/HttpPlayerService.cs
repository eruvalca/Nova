using System.Net.Http.Json;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;

namespace Nova.Client.Services;

/// <summary>
/// WebAssembly client implementation of <see cref="IPlayerService"/> that calls player-roster APIs.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpPlayerService(HttpClient http) : IPlayerService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PagedResult<PlayerListItem>>> GetPlayerRosterAsync(
        GetPlayerRosterInput input,
        CancellationToken cancellationToken = default)
    {
        var url = GetPlayerRosterEndpoints.GetRosterUrl(
            input.ClubId,
            input.Search,
            input.LifecycleStatus,
            input.GraduationYear,
            input.PlayerTagId,
            input.SortBy,
            input.SortDirection,
            input.Page,
            input.PageSize);

        using var response = await http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var roster = await response.Content.ReadFromJsonAsync<PagedResult<PlayerListItem>>(cancellationToken);
        if (roster is null)
        {
            return ServiceProblem.ServerError("The server returned an empty roster payload.");
        }

        return roster;
    }
}
