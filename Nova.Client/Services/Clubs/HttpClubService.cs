using System.Net.Http.Headers;
using Nova.SharedKernel.Features.Clubs;
using Nova.SharedKernel.Results;

namespace Nova.Client.Services.Clubs;

/// <summary>
/// WebAssembly client implementation of <see cref="IClubService"/> that calls the server's
/// minimal API endpoints over HTTP.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
internal sealed class HttpClubService(HttpClient http) : IClubService
{
    /// <inheritdoc />
    public async Task<ServiceResult<ClubDto>> CreateClubAsync(
        CreateClubInput input,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        using var nameContent = new StringContent(input.Name);
        using var cityContent = new StringContent(input.City);
        using var stateContent = new StringContent(input.State);
        form.Add(nameContent, "name");
        form.Add(cityContent, "city");
        form.Add(stateContent, "state");
        using var crestContent = new ByteArrayContent(input.CrestContent);
        crestContent.Headers.ContentType = new MediaTypeHeaderValue(input.CrestContentType);
        form.Add(crestContent, "crest", "crest");

        using var response = await http.PostAsync(new Uri(ClubEndpoints.Create, UriKind.RelativeOrAbsolute), form, cancellationToken);
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
        using var response = await http.GetAsync(new Uri(url, UriKind.RelativeOrAbsolute), cancellationToken);
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
