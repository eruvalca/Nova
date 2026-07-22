---
name: add-feature-slice
description: >-
  Orchestrates building a full vertical feature slice in Nova: input record + validation, service (ServiceResult/ServiceProblem), HTTP endpoint, WASM client, tests.
  USE FOR: add a new feature, build a vertical slice end to end, new input record + service + endpoint, scaffold a feature, add a service that crosses the HTTP/WASM boundary.
  DO NOT USE FOR: domain/persistence-only work (use add-domain-persistence), a single endpoint on an existing service (use add-api-endpoint), only writing/running tests (use nova-testing).
  INVOKES: add-domain-persistence (when schema/domain persistence changes), add-api-endpoint (endpoint step), nova-testing (test step).
---

# Add Feature Slice

Use this orchestrator when adding a complete Nova vertical slice that crosses the HTTP/WASM boundary. It owns the input/validation and service-layer recipes, then delegates detailed endpoint and test work to the dedicated skills.

Canonical examples: `Nova.Shared\Clubs\CreateClubInput.cs`, `Nova.Shared\Clubs\ClubDto.cs`, `Nova.Shared\Clubs\IClubService.cs`, `Nova\Features\Clubs\ClubService.cs`, `Nova.Client\Services\HttpClubService.cs`, `Nova.Shared\Validation\InputValidator.cs`, `Nova.Shared\Validation\NotWhitespaceAttribute.cs`.

## When to use

- Add a new feature end to end: shared DTO/input, server service, API endpoint, WASM client service, and tests.
- Add a new service contract that must be callable server-side and from WebAssembly.
- Scaffold a new feature folder by following the Clubs pattern.

## Ordered checklist

1. **Domain/persistence or decision policy, when needed** — invoke `add-domain-persistence` for entity,
   EF configuration, migration, tenancy, lifecycle, concurrency, or a non-trivial deterministic
   business-rule matrix. Logic-only policy work does not require entity or migration changes.
2. **Input record + validation** — create `Nova.Shared\{Feature}\{Name}Input.cs`; follow [input-and-validation.md](references/input-and-validation.md).
3. **Shared contract + server service** — add DTOs/interfaces in `Nova.Shared\{Feature}\` and implement
   `Nova\Features\{Feature}\{Feature}Service.cs`; follow
   [service-result-patterns.md](references/service-result-patterns.md). Keep authorization, EF,
   locking, persistence, and logging in the service; compose a feature-local pure policy when the
   decision-boundary triggers apply.
4. **Composition root** — register the server service in `Nova\Program.cs`; direct-construction unit tests do not verify DI registration.
5. **HTTP endpoint** — invoke `add-api-endpoint`; do not duplicate that skill's endpoint details here.
6. **WASM client service** — add `Nova.Client\Services\Http{Feature}Service.cs`; follow [wasm-client.md](references/wasm-client.md).
7. **Tests** — invoke `nova-testing`; do not duplicate that skill's test-suite details here.
