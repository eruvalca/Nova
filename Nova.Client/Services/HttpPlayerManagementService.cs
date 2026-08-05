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

        return await response.Content.ReadRequiredJsonAsync<PlayerDto>(
            "The server returned an invalid player response.",
            player => IsValidPlayer(player),
            cancellationToken);
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

        return await response.Content.ReadRequiredJsonAsync<PlayerDto>(
            "The server returned an invalid player response.",
            player => IsValidPlayer(player, input.PlayerId),
            cancellationToken);
    }

    /// <summary>
    /// Validates the portable invariants of a player success payload.
    /// </summary>
    /// <param name="player">The player to validate.</param>
    /// <param name="expectedPlayerId">The expected player identifier, when known.</param>
    /// <returns><see langword="true"/> when the player is structurally valid.</returns>
    private static bool IsValidPlayer(PlayerDto player, long? expectedPlayerId = null)
        => player is not null
            && player.PlayerId > 0
            && (expectedPlayerId is null || player.PlayerId == expectedPlayerId)
            && player.ClubId > 0
            && !string.IsNullOrWhiteSpace(player.FirstName)
            && !string.IsNullOrWhiteSpace(player.LastName);
}
