using Nova.Shared.Players;
using Nova.Shared.Results;
using OneOf.Types;

namespace Nova.Client.Services;

/// <summary>
/// WebAssembly client implementation of <see cref="IPlayerLifecycleService"/> that calls player lifecycle minimal API endpoints.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpPlayerLifecycleService(HttpClient http) : IPlayerLifecycleService
{
    /// <inheritdoc />
    public async Task<ServiceResult<Success>> ArchiveAsync(
        long playerId,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsync(PlayerEndpoints.ArchiveUrl(playerId), content: null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return new Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> RestoreAsync(
        long playerId,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsync(PlayerEndpoints.RestoreUrl(playerId), content: null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return new Success();
    }
}
