---
name: add-api-endpoint
description: >-
  Add, change, or remove a Nova minimal-API endpoint and its shared route/WASM client contract.
  Covers handlers, ProblemDetails, authorization, metadata, validation/binding, Location headers,
  and real HTTP verification. Use add-feature-slice for a whole feature and nova-testing for
  test-only work.
---

# Add API Endpoint

Use this skill when adding or changing Nova minimal-API endpoints that are shared between the server and the Blazor WebAssembly client.

## Canonical Nova examples

- Routes: `Nova.Shared\Features\Clubs\ClubEndpoints.cs`
- Mapping/handlers: `Nova\Features\Clubs\ClubEndpointRouteBuilderExtensions.cs`
- WASM client: `Nova.Client\Services\Clubs\HttpClubService.cs`
- ToHttpResult: `Nova\Features\Shared\ServiceResultExtensions.cs`
- Created resource contract: `Nova.Shared\Features\Teams\TeamEndpoints.cs`,
  `Nova\Features\Teams\TeamManagementEndpointRouteBuilderExtensions.cs`,
  `Nova.Integration.Tests\Http\TeamManagementHttpTests.cs`
- Dead endpoint removal: the removed team graduation-year route, which duplicated normal team update.

## Checklist

1. **Prove the endpoint is needed** — search existing mutations, routes, clients, callers, and tests.
   Do not create a duplicate mutation surface. For removal, use the end-to-end cleanup checklist in
   [route-constants.md](references/route-constants.md).
2. Define shared route constants and URL builders in `Nova.Shared` — see [route-constants.md](references/route-constants.md).
3. Map endpoints with `MapGroup`, static handlers, DI parameters, `ToHttpResult`, and `WithName` — see [handlers-and-results.md](references/handlers-and-results.md).
4. Add response metadata, authorization, and antiforgery handling — see [metadata-auth-antiforgery.md](references/metadata-auth-antiforgery.md).
5. Apply endpoint-layer validation, validation ProblemDetails rules, optional `[AsParameters]` query
   binding, and enum query binding — see
   [validation-and-problemdetails.md](references/validation-and-problemdetails.md).
6. Wire the WASM client to consume shared route constants and deserialize failures with `ToServiceProblemAsync()`.
7. Use `nova-testing` for real HTTP boundary coverage of routing, policies, validation, and response
   contracts. For `CreatedAtRoute`, assert `201`, `Location`, and a successful GET after following it.
   For a behavioral finding, use its
   [sibling-closure and independent-review brief](../nova-testing/references/review-and-finding-closure.md).

## Required references

- [route-constants.md](references/route-constants.md) — route constants, URL builders, client usage, and MapGroup organization.
- [handlers-and-results.md](references/handlers-and-results.md) — static handlers, dependency injection, `ToHttpResult`, `Results<T>`, trace IDs, `WithName`, `CreatedAtRoute`, and complete example.
- [metadata-auth-antiforgery.md](references/metadata-auth-antiforgery.md) — `ProducesProblem`, antiforgery, and authorization.
- [validation-and-problemdetails.md](references/validation-and-problemdetails.md) — validation
  ProblemDetails JSON, .NET 10 automatic validation, `ProducesValidationProblem`,
  `DisableValidation`, manual validation, optional query binding, and enum query binding.
