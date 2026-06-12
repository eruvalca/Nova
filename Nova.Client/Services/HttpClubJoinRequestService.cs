using System.Net.Http.Json;
using Nova.Shared.Clubs;
using Nova.Shared.Results;
using OneOf.Types;

namespace Nova.Client.Services;

/// <summary>
/// WebAssembly client implementation of <see cref="IClubJoinRequestService"/> that calls the server's
/// minimal API endpoints over HTTP.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpClubJoinRequestService(HttpClient http) : IClubJoinRequestService
{
    /// <inheritdoc />
    public async Task<ServiceResult<ClubJoinRequestDto>> GetCurrentUserPendingRequestAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(ClubEndpoints.PendingRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var dto = await response.Content.ReadFromJsonAsync<ClubJoinRequestDto>(cancellationToken);
        return dto!;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ClubJoinRequestDto>> CreateJoinRequestAsync(
        long clubId,
        CancellationToken cancellationToken = default)
    {
        var url = ClubEndpoints.CreateJoinRequestUrl(clubId);
        // POST with empty body — clubId is in the route
        using var response = await http.PostAsJsonAsync(url, new { }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var dto = await response.Content.ReadFromJsonAsync<ClubJoinRequestDto>(cancellationToken);
        return dto!;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> CancelJoinRequestAsync(
        long requestId,
        CancellationToken cancellationToken = default)
    {
        var url = ClubEndpoints.CancelJoinRequestUrl(requestId);
        using var response = await http.DeleteAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return new Success();
    }
}
