# Player Archive/Restore API and Client Exposure

Implement issue #30 by exposing player archive/restore through shared service contracts, minimal API endpoints, and typed WASM client methods while preserving existing lifecycle invariants and history safety from #19.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to `Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue with zero context); run the phase's **Verification Plan** and record the result before moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

## Phase 1: Baseline and contract design

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [x] Inspect existing player lifecycle service, OneOf outcomes, existing tests, and route/client patterns to identify exact extension points.
- [x] Design shared archive/restore result contracts including structured archive blockers grouped by campaign with nested participation IDs.
- [x] Define service-boundary mapping plan from internal lifecycle outcomes to shared `ServiceResult`/`ServiceProblem` contracts without leaking internal marker types.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*ArchivalLifecycleServiceTests"` discovers and runs lifecycle-related unit coverage.

### Phase Summary

Identified existing lifecycle OneOf outcomes and route/client conventions, then chose a boundary-safe shared contract approach: expose player archive/restore via `IPlayerLifecycleService` returning shared `ServiceResult` and carry structured blockers through `ServiceProblem.Extensions` for HTTP/WASM round-tripping.

## Phase 2: Service-layer implementation

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [x] Add shared lifecycle contracts and `IPlayerLifecycleService` abstractions in `Nova.Shared` following validation/service-layer conventions.
- [x] Refactor/adapt `PlayerLifecycleService` to return shared service results and structured blockers at the boundary while preserving lock, transaction, tenant, and provenance behavior.
- [x] Keep redundant archive/restore transitions as conflicts and ensure restore only changes lifecycle state/provenance.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*ArchivalLifecycleServiceTests"` passes with updated result-shape assertions.

### Phase Summary

Added `IPlayerLifecycleService`, `PlayerArchiveBlocker`, and service boundary mapping to `ServiceResult`. Preserved lock + tenancy behavior and updated transaction execution to run within EF execution strategy. Archive/restore redundancy remains conflict-based; restore only updates lifecycle/provenance.

## Phase 3: HTTP endpoints and WASM client wiring

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [x] Add route constants and static minimal API handlers under player route groups for POST archive/restore mutations.
- [x] Map service results to HTTP `ProblemDetails` with trace IDs, authorization, antiforgery, and OpenAPI metadata per repo endpoint conventions.
- [x] Add typed WASM client methods + DI registration for archive/restore operations using shared contracts.

### Verification Plan

- `dotnet test --project Nova.Integration.Tests --filter-class "*PlayerLifecycleHttpTests"` discovers and runs endpoint/client boundary coverage.

### Phase Summary

Added player lifecycle endpoint map/extensions and shared route constants. Wired `ServiceResult` → HTTP with trace IDs plus extension payload preservation. Added WASM typed service (`HttpPlayerLifecycleService`) and DI registrations on server/client.

## Phase 4: Authorization, tenancy, and history-preservation coverage

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: sub-agent w/ smaller model

- [x] Add/extend tests for admin allowed, evaluator forbidden, cross-tenant not-found/non-disclosure semantics, and redundant transition conflicts.
- [x] Add/extend tests asserting archive/restore preserve participation, notes, tags, outcomes, teams, and campaign history rows.
- [x] Add/extend tests asserting restore clears archive provenance and does not retro-enroll campaigns created while archived.

### Verification Plan

- `dotnet test --project Nova.Integration.Tests --filter-class "*ArchivalLifecyclePostgresTests" --filter-class "*PlayerLifecycleHttpTests"` passes with at least one test discovered per filter.

### Phase Summary

Expanded unit/integration coverage for authorization, cross-tenant visibility constraints, conflict transitions, structured archive blockers, and history invariants. Added restore-no-retro-enroll assertions and HTTP endpoint behavior checks.

## Phase 5: End-to-end verification and handoff

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [x] Run all issue-specified verification commands and fix any regressions.
- [x] Update this plan with completed phase statuses and phase summaries.
- [x] Write final recap and deployment steps for zero-context handoff.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*ArchivalLifecycleServiceTests"`
- `dotnet test --project Nova.Integration.Tests --filter-class "*PlayerLifecycleHttpTests" --filter-class "*ArchivalLifecyclePostgresTests"`
- Confirm each filtered command discovers at least one test.
- `dotnet build Nova.slnx`

### Phase Summary

Completed targeted unit/integration verification and solution build after fixing the execution-strategy generic return typing in `PlayerLifecycleService`. All scoped commands completed successfully.

## Phase 6: Retry-safe player lifecycle mutations

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [x] Refactor player lifecycle execution so every execution-strategy attempt creates and disposes its own tenant-scoped `NovaDbContext`.
- [x] Preserve the transaction, advisory-lock, authorization, tenancy, and boundary-result behavior from the completed feature slice.
- [x] Add focused transient-failure regression coverage proving a retry observes persisted database state rather than stale tracked state.
- [x] Run targeted lifecycle tests and the solution build, then record results here.

### Verification Plan

- `dotnet test --project Nova.Integration.Tests --filter-class "*PlayerLifecycleRetryTests"` passes and discovers the retry regression test.
- `dotnet test --project Nova.Unit.Tests --filter-class "*ArchivalLifecycleServiceTests"` passes.
- `dotnet test --project Nova.Integration.Tests --filter-class "*PlayerLifecycleRetryTests" --filter-class "*PlayerLifecycleHttpTests" --filter-class "*ArchivalLifecyclePostgresTests"` passes.
- `dotnet build Nova.slnx` succeeds.

### Phase Summary

`PlayerLifecycleService` now uses one context only to obtain the provider execution strategy and creates a new tenant context inside every retried delegate invocation. Each attempt independently acquires the transaction and advisory lock, reloads persisted player state, saves, commits, and disposes. `PlayerLifecycleRetryTests` injects one transient Npgsql failure after the first update executes but before commit; the rollback is retried with a fresh context and archives successfully. Verification passed: 18 targeted unit tests, 13 targeted PostgreSQL/HTTP integration tests (including the retry regression), and `dotnet build Nova.slnx`.

## Final Recap

Implemented issue #30 as a full vertical slice. Player archive/restore is exposed through shared contracts (`IPlayerLifecycleService`), server minimal API endpoints, and WASM typed client methods. Structured archive blockers are returned as grouped campaign payloads and preserved end-to-end via `ServiceProblem.Extensions`/`ProblemDetails` extensions with trace IDs. Lifecycle mutations are retry-safe under Npgsql: every execution-strategy attempt uses a fresh tenant context so rolled-back tracked state cannot produce false conflicts. Unit and PostgreSQL integration coverage verify lifecycle invariants, history preservation, HTTP behavior, and transient retry behavior.

## Deployment Plan

1. Merge this branch into `main`.
2. Deploy the standard Nova application artifacts; no schema migration is required.
3. Smoke test player archive/restore with an administrator account.
4. Confirm non-admin and cross-tenant requests retain forbidden/not-found semantics.
5. Monitor lifecycle conflict and database retry telemetry after deployment for unexpected increases.
