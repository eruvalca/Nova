using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using OneOf.Types;

namespace Nova.Client.Services.Players;

/// <summary>
/// WebAssembly client implementation of <see cref="IPlayerLifecycleService"/> that calls player lifecycle minimal API endpoints.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
internal sealed class HttpPlayerLifecycleService(HttpClient http) : IPlayerLifecycleService
{
    /// <inheritdoc />
    public async Task<ServiceResult<Success>> ArchiveAsync(
        long playerId,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsync(new Uri(PlayerEndpoints.ArchiveUrl(playerId), UriKind.RelativeOrAbsolute), content: null, cancellationToken);
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
        using var response = await http.PostAsync(new Uri(PlayerEndpoints.RestoreUrl(playerId), UriKind.RelativeOrAbsolute), content: null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return new Success();
    }
}
