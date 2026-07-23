---
name: add-api-endpoint
description: >-
  Recipe for adding a minimal-API HTTP endpoint in Nova with shared route constants,
  MapGroup mapping, static handlers, ServiceResult-to-HTTP conversion, validation,
  ProblemDetails, metadata, authorization, antiforgery, OpenAPI, and WASM client wiring.
  USE FOR: add an API endpoint, map a new route, new MapPost/MapGet handler, wire a WASM client call to a server endpoint, ProblemDetails/ToHttpResult, ProducesProblem, antiforgery on WASM endpoints, optional AsParameters query binding, enum query binding, CreatedAtRoute.
  DO NOT USE FOR: building a full feature from scratch (use add-feature-slice), domain/persistence-only work (use add-domain-persistence), service-layer result types only, writing tests (use nova-testing).
---

# Add API Endpoint

Use this skill when adding or changing Nova minimal-API endpoints that are shared between the server and the Blazor WebAssembly client.

## Canonical Nova examples

- Routes: `Nova.Shared\Clubs\ClubEndpoints.cs`
- Mapping/handlers: `Nova\Features\Clubs\ClubEndpointRouteBuilderExtensions.cs`
- WASM client: `Nova.Client\Services\HttpClubService.cs`
- ToHttpResult: `Nova\Features\Shared\ServiceResultExtensions.cs`

## Checklist

1. Define shared route constants and URL builders in `Nova.Shared` — see [route-constants.md](references/route-constants.md).
2. Map endpoints with `MapGroup`, static handlers, DI parameters, `ToHttpResult`, and `WithName` — see [handlers-and-results.md](references/handlers-and-results.md).
3. Add response metadata, authorization, and antiforgery handling — see [metadata-auth-antiforgery.md](references/metadata-auth-antiforgery.md).
4. Apply endpoint-layer validation, validation ProblemDetails rules, optional `[AsParameters]` query
   binding, and enum query binding — see
   [validation-and-problemdetails.md](references/validation-and-problemdetails.md).
5. Wire the WASM client to consume shared route constants and deserialize failures with `ToServiceProblemAsync()`.
6. Verify the endpoint uses the complete pattern before editing tests or callers.

## Required references

- [route-constants.md](references/route-constants.md) — route constants, URL builders, client usage, and MapGroup organization.
- [handlers-and-results.md](references/handlers-and-results.md) — static handlers, dependency injection, `ToHttpResult`, `Results<T>`, trace IDs, `WithName`, `CreatedAtRoute`, and complete example.
- [metadata-auth-antiforgery.md](references/metadata-auth-antiforgery.md) — `ProducesProblem`, antiforgery, and authorization.
- [validation-and-problemdetails.md](references/validation-and-problemdetails.md) — validation
  ProblemDetails JSON, .NET 10 automatic validation, `ProducesValidationProblem`,
  `DisableValidation`, manual validation, optional query binding, and enum query binding.
