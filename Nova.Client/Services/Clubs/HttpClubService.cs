using System.Net.Http.Headers;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;

namespace Nova.Client.Services.Clubs;

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
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(input.Name), "name");
        form.Add(new StringContent(input.City), "city");
        form.Add(new StringContent(input.State), "state");
        using var crestContent = new ByteArrayContent(input.CrestContent);
        crestContent.Headers.ContentType = new MediaTypeHeaderValue(input.CrestContentType);
        form.Add(crestContent, "crest", "crest");

        using var response = await http.PostAsync(ClubEndpoints.Create, form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<ClubDto>(
            "The server returned an invalid club response.",
            IsValidClub,
            cancellationToken);
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

        var result = await response.Content.ReadRequiredJsonAsync<List<ClubDto>>(
            "The server returned an invalid club list response.",
            clubs => clubs.All(IsValidClub),
            cancellationToken);
        return result.Match<ServiceResult<IReadOnlyList<ClubDto>>>(
            clubs => clubs.AsReadOnly(),
            problem => problem);
    }

    /// <summary>
    /// Validates the portable invariants of a club success payload.
    /// </summary>
    /// <param name="club">The club to validate.</param>
    /// <returns><see langword="true"/> when the club is structurally valid.</returns>
    private static bool IsValidClub(ClubDto club)
        => club is not null
            && club.ClubId > 0
            && !string.IsNullOrWhiteSpace(club.Name)
            && !string.IsNullOrWhiteSpace(club.City)
            && !string.IsNullOrWhiteSpace(club.State);
}
