using System.Net.Http.Json;
using Nova.Shared.Players;
using Nova.Shared.Results;

namespace Nova.Client.Services;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="IPlayerDetailService"/>.
/// </summary>
/// <param name="http">The configured HTTP client.</param>
public sealed class HttpPlayerDetailService(HttpClient http) : IPlayerDetailService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PlayerDetailDto>> GetPlayerDetailAsync(long playerId, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(PlayerEndpoints.GetDetailUrl(playerId), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var detail = await response.Content.ReadFromJsonAsync<PlayerDetailDto>(cancellationToken);
        return detail!;
    }
}
