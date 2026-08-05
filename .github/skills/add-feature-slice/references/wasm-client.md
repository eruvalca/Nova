# WASM Client Service Recipe

After `add-api-endpoint` defines route constants and maps the endpoint, add a WebAssembly HTTP client
service in `Nova.Client\Services\Http{Feature}Service.cs`. The service should implement the shared
`I{Feature}Service` interface from `Nova.Shared\{Feature}\`, use `HttpClient`, and return the same
`ServiceResult<T>` contract as the server service.

Canonical files:

- `Nova.Client\Services\HttpCampaignCreationService.cs`
- `Nova.Client\Services\HttpCampaignQueryService.cs`
- `Nova.Client\Services\HttpSuccessContentExtensions.cs`
- `Nova.Shared\Campaigns\CampaignEndpoints.cs`
- `Nova.Shared\Campaigns\ICampaignQueryService.cs`

## Pattern

- Use endpoint route constants/builders from the shared `{Feature}Endpoints` type so client and server routes stay synchronized.
- Validate shared input before calling a URL builder that normalizes or omits invalid values; invalid
  caller input must not silently become a default request.
- Use `PostAsJsonAsync` / `GetAsync` and pass the `CancellationToken`.
- On non-success status codes, call `response.ToServiceProblemAsync(cancellationToken)`.
- On success, use `ReadRequiredJsonAsync` to deserialize and validate the required body. A
  successfully deserialized empty collection (`[]`) is valid when the contract permits it. The
  helper maps an empty body, JSON `null`, malformed JSON, or a contract-invalid payload to
  `ServiceProblem.ServerError`; never disguise those failures with `[]`, `default`, or `!`.
- C# `required` checks property presence during deserialization but does not reject explicit JSON
  `null`; guard required nested collections and elements explicitly.
- Validate portable protocol invariants such as positive IDs, shared bounds, ordering keys, and count
  relationships guaranteed by the consistency contract. Do not compare separately queried totals
  with returned rows, or reproduce database-collated string ordering client-side; an ID tie-breaker
  remains safe when names are exactly equal.
- Register the HTTP implementation for WebAssembly DI wherever the feature's client services are registered.

## Canonical example

```csharp
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

        return await response.Content.ReadRequiredJsonAsync<ClubDto>(
            "The server returned an invalid club response.",
            club => club.ClubId > 0 && !string.IsNullOrWhiteSpace(club.Name),
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
            clubs => clubs.All(club => club is not null && club.ClubId > 0),
            cancellationToken);
        return result.Match<ServiceResult<IReadOnlyList<ClubDto>>>(
            clubs => clubs.AsReadOnly(),
            problem => problem);
    }
}
```
