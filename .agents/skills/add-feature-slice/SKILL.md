---
name: add-feature-slice
description: >-
    Build, change, debug, or review a complete Nova feature across input/query contracts, services, HTTP endpoints, WASM clients, and tests.
    USE FOR: end-to-end features, cross-tier behavior changes, bounded read-only query APIs, shared input and service contracts, and consistent server/client behavior.
    DO NOT USE FOR: domain/persistence-only work (use add-domain-persistence), a single endpoint on an existing service (use add-api-endpoint), UI-only work such as adding a page or component (use add-blazor-ui), only writing/running tests (use nova-testing).
    INVOKES: add-domain-persistence (when schema/domain persistence changes), add-api-endpoint (endpoint step), add-blazor-ui (UI step), nova-testing (test step).
---

# Add Feature Slice

Use this orchestrator when adding or changing a complete Nova vertical slice across the HTTP/WASM
boundary, including diagnosis and review. It owns the input/validation and service-layer recipes,
then delegates detailed endpoint and test work. For existing slices, trace the affected behavior
through all tiers and apply the relevant steps without rebuilding unrelated structure.

Structural examples: Clubs for mutations and
`Nova\Features\Campaigns\CampaignQueryService.cs` /
`Nova.Client\Services\Campaigns\HttpCampaignQueryService.cs` for a bounded read-only slice.

## Bound the change

Before implementation, select the affected [transitions and boundaries](../nova-testing/references/transition-evidence.md).
State the expected outcome and where it must be proved; reuse that short list in the validation
record. A read-only slice does not acquire mutation/recovery work from this checklist.

## Ordered checklist

1. **Domain/persistence or decision policy, when needed** — invoke `add-domain-persistence` for entity,
   EF configuration, migration, tenancy, lifecycle, concurrency, or a non-trivial deterministic
   business-rule matrix. Logic-only policy work does not require entity or migration changes.
2. **Input record + validation** — create `Nova.SharedKernel\Features\{Feature}\{Name}Input.cs`; follow [input-and-validation.md](references/input-and-validation.md).
3. **Shared contract + server service** — add DTOs/interfaces in `Nova.SharedKernel\Features\{Feature}\` and implement
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
8. **Tests** — invoke `nova-testing`; do not duplicate that skill's test-suite details here.
9. **Verify the selected behavior across tiers** — use the
   [contract check](references/wasm-client.md#producer-to-ui-contract-check) for changed responses.
   Link the selected transition evidence in the single validation record. Completion, sibling-path
   review, review triage, and test gates are defined in [AGENTS.md](../../../AGENTS.md#completion-and-review).
