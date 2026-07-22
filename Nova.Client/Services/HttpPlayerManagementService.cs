using System.Net.Http.Json;
using Nova.Shared.Players;
using Nova.Shared.Results;

namespace Nova.Client.Services;

/// <summary>
/// WebAssembly client implementation of <see cref="IPlayerManagementService"/> that calls the
/// server's minimal API endpoints over HTTP.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpPlayerManagementService(HttpClient http) : IPlayerManagementService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PlayerDto>> CreateAsync(
        CreatePlayerInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(PlayerEndpoints.Create, input, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var player = await response.Content.ReadFromJsonAsync<PlayerDto>(cancellationToken);
        return player!;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PlayerDto>> UpdateAsync(
        UpdatePlayerInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync(
            PlayerEndpoints.UpdateUrl(input.PlayerId),
            input,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var player = await response.Content.ReadFromJsonAsync<PlayerDto>(cancellationToken);
        return player!;
    }
}
