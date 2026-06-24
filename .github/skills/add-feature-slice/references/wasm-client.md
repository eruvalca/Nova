# WASM Client Service Recipe

After `add-api-endpoint` defines route constants and maps the endpoint, add a WebAssembly HTTP client
service in `Nova.Client\Services\Http{Feature}Service.cs`. The service should implement the shared
`I{Feature}Service` interface from `Nova.Shared\{Feature}\`, use `HttpClient`, and return the same
`ServiceResult<T>` contract as the server service.

Canonical files:

- `Nova.Client\Services\HttpClubService.cs`
- `Nova.Shared\Clubs\ClubEndpoints.cs`
- `Nova.Shared\Clubs\IClubService.cs`

## Pattern

- Use endpoint route constants/builders from the shared `{Feature}Endpoints` type so client and server routes stay synchronized.
- Use `PostAsJsonAsync` / `GetAsync` and pass the `CancellationToken`.
- On non-success status codes, call `response.ToServiceProblemAsync(cancellationToken)`.
- On success, deserialize the DTO with `ReadFromJsonAsync<T>(cancellationToken)` and return it.
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
```

