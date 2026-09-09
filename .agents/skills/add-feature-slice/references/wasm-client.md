# WASM Client Service Recipe

After `add-api-endpoint` defines route constants and maps the endpoint, add a WebAssembly HTTP client
service in `Nova.Client\Services\{Feature}\Http{Feature}Service.cs`. The service should implement the shared
`I{Feature}Service` interface from `Nova.SharedKernel\Features\{Feature}\`, use `HttpClient`, and return the same
`ServiceResult<T>` contract as the server service.

Canonical files:

- `Nova.Client\Services\Campaigns\HttpCampaignCreationService.cs`
- `Nova.Client\Services\Campaigns\HttpCampaignQueryService.cs`
- `Nova.SharedKernel\Results\HttpSuccessContentExtensions.cs`
- `Nova.SharedKernel\Features\Campaigns\CampaignEndpoints.cs`
- `Nova.SharedKernel\Features\Campaigns\ICampaignQueryService.cs`

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
- Enforce required JSON field presence with C# `required` or `[JsonRequired]` (use
  `[property: JsonRequired]` on positional record parameters). A missing enum or count can otherwise
  deserialize to a valid zero and pass value validation. Test omission separately from legitimate
  zero values. Presence checks do not reject explicit JSON `null`; guard required nested collections
  and elements explicitly and test nulls separately.
- Validate portable protocol invariants such as positive IDs, shared bounds, ordering keys, and count
  relationships guaranteed by the consistency contract. Do not compare separately queried totals
  with returned rows, or reproduce database-collated string ordering client-side; an ID tie-breaker
  remains safe when names are exactly equal.
- Register the HTTP implementation for WebAssembly DI wherever the feature's client services are registered.

For mutations supporting recovery across HTTP requests, preserve the same operation ID and request
payload on retry, including original upload bytes and confirmation token when present. Check the
returned operation identity as well as payload invariants. Follow
[client retries and reviewed batches](../../add-domain-persistence/references/retrying-mutations-and-locks.md#client-retries-and-reviewed-batches)
for the server receipt contract; `HttpPlayerImportService` is the multipart example. A cancelled
request or lost response leaves the commit outcome unknown until recovery succeeds.

## Producer-to-UI contract check

For a changed response or stricter client validation, inspect the whole chain before writing the
regression: service/query producer → shared DTO and JSON serialization → HTTP client → rendered
consumer. State which guarantees come from one snapshot and which totals are eventually consistent.

1. Check required field presence, explicit nulls, identity, count relationships, shared limits, and
   portable ordering against the producer's actual query/transaction. Do not invent a client-only
   invariant or silently weaken a promised server guarantee.
2. Verify the endpoint serializes a populated valid response through the production DTOs. Exercise
   omission/malformed input and declared failure shapes at HTTP boundaries when applicable.
3. Exercise the client with a populated valid response and missing required fields, nested nulls,
   malformed JSON, invalid relationships, and limit edges. Reject invalid success bodies as a
   protocol failure; retain the legitimate zero/empty cases.
4. Verify the UI consumes the same guarantees: complete bounded previews, visible truncation, and
   receipt-based committed counts. Prove recovery from a rejected payload when the UI offers retry.

For bounded opening previews, inspect `CampaignQueryService.cs`, `CampaignOpeningContracts.cs`,
`HttpCampaignQueryService.cs`, and `CampaignEntry.razor(.cs)` together. The focused evidence is
`CampaignOpeningHttpTests.CampaignOpeningReadinessReturnsBoundedActiveTeamPreviewAsync` for real HTTP,
`HttpCampaignQueryServiceTests.GetOpeningReadinessAsyncRequiresCompleteBoundedPreviewAsync` for zero,
singleton, and capped client bounds, and
`CampaignEntryTests.CampaignEntryUsesCountAwareReadinessLabels` for rendered count wording.
These tests prove their named contracts; add the missing boundary evidence for the current change.

## Canonical example

```csharp
using System.Net.Http.Json;
using Nova.SharedKernel.Features.Clubs;
using Nova.SharedKernel.Results;

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
