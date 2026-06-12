using System.Net.Http.Json;
using Nova.Shared.Clubs;
using Nova.Shared.Results;

namespace Nova.Client.Services;

/// <summary>
/// WebAssembly client implementation of <see cref="IClubService"/> that calls the server's
/// minimal API endpoints over HTTP.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpClubService(HttpClient http) : IClubService
{
    /// <inheritdoc />
    public async Task<ServiceResult<ClubDto>> CreateClubAsync(
        CreateClubInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(ClubEndpoints.Create, input, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var club = await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken);
        return club!;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<ClubDto>>> SearchClubsAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        var url = ClubEndpoints.SearchUrl(query);
        using var response = await http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var clubs = await response.Content.ReadFromJsonAsync<List<ClubDto>>(cancellationToken);
        return (clubs ?? []).AsReadOnly();
    }
}
