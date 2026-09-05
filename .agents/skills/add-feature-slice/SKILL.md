---
name: add-feature-slice
description: >-
  Build a Nova feature across shared input/contracts, server service, HTTP, WASM client, and tests.
  Use for an end-to-end mutation or bounded query, including recoverable stateful flows. For a
  single endpoint use add-api-endpoint; for UI-only work use add-blazor-ui; for persistence-only
  work use add-domain-persistence. Delegates those steps and harness selection to their skills.
---

# Add Feature Slice

Use this orchestrator when adding a complete Nova vertical slice that crosses the HTTP/WASM boundary. It owns the input/validation and service-layer recipes, then delegates detailed endpoint and test work to the dedicated skills.

Canonical examples: Clubs for mutations and
`Nova\Features\Campaigns\CampaignQueryService.cs` /
`Nova.Client\Services\Campaigns\HttpCampaignQueryService.cs` for a bounded read-only slice.

## When to use

- Add a new feature end to end: shared DTO/input, server service, API endpoint, WASM client service, and tests.
- Add a tenant-safe, bounded read-only query across the server and WebAssembly boundary.
- Add a new service contract that must be callable server-side and from WebAssembly.
- Scaffold a new feature folder by following the Clubs pattern.

## Ordered checklist

0. **Map consequential transitions, when applicable** — before implementing a recoverable command,
   contextual form validation, or async state whose authorization can change, use
   [stateful-transitions.md](references/stateful-transitions.md). Map ownership, entry points,
   effects, and proving tests in the existing task plan; skip this for ordinary CRUD.
1. **Domain/persistence or decision policy, when needed** — invoke `add-domain-persistence` for entity,
   EF configuration, migration, tenancy, lifecycle, concurrency, or a non-trivial deterministic
   business-rule matrix. Logic-only policy work does not require entity or migration changes.
2. **Input record + validation** — create `Nova.Shared\Features\{Feature}\{Name}Input.cs`; follow [input-and-validation.md](references/input-and-validation.md).
3. **Shared contract + server service** — add DTOs/interfaces in `Nova.Shared\Features\{Feature}\` and implement
   `Nova\Features\{Feature}\{Feature}Service.cs`; follow
   [service-result-patterns.md](references/service-result-patterns.md). Keep authorization, EF,
   locking, persistence, and logging in the service; compose a feature-local pure policy when the
   decision-boundary triggers apply. For provider-sensitive search or bounded query ordering, use
   [add-domain-persistence/query-construction.md](../add-domain-persistence/references/query-construction.md).
   For read-only slices, use `NovaReadDbContext`, project and bound in SQL, group only the bounded
   projection, share fixed bounds through the contract, and state whether separately queried totals
   are eventually consistent.
4. **Composition root** — register the server service in `Nova\Program.cs`; direct-construction unit tests do not verify DI registration.
5. **HTTP endpoint** — invoke `add-api-endpoint`; do not duplicate that skill's endpoint details here.
6. **WASM client service** — add `Nova.Client\Services\{Feature}\Http{Feature}Service.cs`; follow [wasm-client.md](references/wasm-client.md).
7. **UI (pages/components)** — invoke `add-blazor-ui` when the slice surfaces in the UI; it owns
   placement, the render-mode decision, lifecycle/persisted state, callbacks, and form wiring. Do not
   duplicate that skill's details here.
8. **Tests and closure** — invoke `nova-testing`; map the new behavior to tests as well as recording
   their execution. For a review finding or a complex stateful slice, use its
   [finding-closure and independent-review reference](../nova-testing/references/review-and-finding-closure.md).
