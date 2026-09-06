---
name: add-api-endpoint
description: >-
  Add, change, debug, review, or remove Nova minimal-API endpoints and HTTP client contracts.
  Covers shared routes, static handlers, ServiceResult conversion, validation, ProblemDetails,
  authorization, antiforgery, OpenAPI metadata, query binding, CreatedAtRoute, polymorphic JSON,
  and producer-to-client contract fidelity. Use for duplicate mutation routes or malformed success
  payloads. Full cross-tier features use add-feature-slice; persistence-only work uses
  add-domain-persistence; tests-only work uses nova-testing.
---

# Add API Endpoint

Use this skill when adding, changing, debugging, reviewing, or removing Nova minimal-API endpoints
and HTTP client contracts. For existing behavior, inspect the producer, consumers, and applicable
checklist steps without recreating unrelated endpoint structure.

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
7. Verify the endpoint uses the complete pattern before editing tests or callers. For
   `CreatedAtRoute`, add the real HTTP `201` + `Location` + follow test.
8. For contract changes, follow the
   [producer-to-UI contract check](../add-feature-slice/references/wasm-client.md#producer-to-ui-contract-check).
   Include valid and malformed payload cases in `nova-testing`; a client-only test does not prove
   the server emits the promised shape. Inspect sibling routes and clients for the same invariant.

## Required references

- [route-constants.md](references/route-constants.md) — route constants, URL builders, client usage, and MapGroup organization.
- [handlers-and-results.md](references/handlers-and-results.md) — static handlers, dependency injection, `ToHttpResult`, `Results<T>`, trace IDs, `WithName`, `CreatedAtRoute`, and complete example.
- [metadata-auth-antiforgery.md](references/metadata-auth-antiforgery.md) — `ProducesProblem`, antiforgery, and authorization.
- [validation-and-problemdetails.md](references/validation-and-problemdetails.md) — validation
  ProblemDetails JSON, .NET 10 automatic validation, `ProducesValidationProblem`,
  `DisableValidation`, manual validation, optional query binding, and enum query binding.
