# Issue #31 Player Detail and Campaign History Query API

Implement the player detail read slice from issue #31 end to end: shared contracts, server query service, HTTP endpoint, typed WASM client, and focused tests for ordering, attribution fallback, authorization, tenancy, and error/result mapping.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to `Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue with zero context); run the phase's **Verification Plan** and record the result before moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

## Confirmed Decisions

- Implement the full issue scope in this worktree.
- Use the exact unresolved/deleted actor display fallback text `Former member`.
- Build campaign history from explicit `PlayerCampaignAssignmentEntity` membership only (never infer by date).
- Campaign group ordering is `StartDate` descending, then campaign ID descending.
- Current traits are the deduplicated union of Active-campaign tag applications, ordered by tag name ascending then definition ID ascending.
- Out-of-scope exclusions remain exactly those listed in issue #31.

## Phase 1: Add Shared Contracts and Endpoint Constants

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [x] Add player detail/history DTO contracts and endpoint constants under `Nova.Shared/Players/`.
- [x] Add/update the shared service interface for the new detail query surface and boundary result type.
- [x] Ensure route constants support server mapping and WASM client URL construction without inline literals.

### Verification Plan

- `dotnet build Nova.slnx` reports 0 errors.
- `dotnet test --project Nova.Unit.Tests --filter-class "*PlayerDetailQueryServiceTests"` discovers at least one test after Phase 2 scaffolding.

### Phase Summary

Added `Nova.Shared/Players/` contracts: `PlayerDetailDto` plus nested history/trait/note/tag/team records, `IPlayerDetailService`, and `PlayerEndpoints` route constants + URL builder. Contracts are boundary-safe (`ServiceResult<PlayerDetailDto>`) and shared by server endpoint and WASM client.

## Phase 2: Implement Server Query Service

Status: Complete

Suggested executor: orchestrator

- [x] Implement the player detail query service in `Nova/Features/Players/` using `IDbContextFactory<NovaReadDbContext>`.
- [x] Project permanent profile, lifecycle state, campaign-grouped participation context, notes, and tag application history with bounded tenant-safe queries (no entity-graph loading, no per-campaign query fan-out).
- [x] Resolve actor display names strictly within current-club users and apply `Former member` fallback when unresolved.
- [x] Compute and return current traits from Active campaigns only, with dedupe and deterministic ordering.
- [x] Map missing/cross-tenant player and authorization outcomes to repository-standard non-disclosing service results.
- [x] Register the service in `Nova/Program.cs`.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*PlayerDetailQueryServiceTests"` discovers tests and passes.
- `dotnet build Nova.slnx` reports 0 errors.

### Phase Summary

Implemented `PlayerDetailQueryService` with bounded projection queries over `NovaReadDbContext` (player core, assignments/campaign/team, notes, tag applications, actor-name lookup) and no per-campaign query fan-out. History ordering is `StartDate DESC, CampaignId DESC`; notes/tags are newest-first and actor attribution uses club-scoped resolution with `Former member` fallback. Current traits are deduped from Active campaigns and ordered by tag name then definition ID.

## Phase 3: Add HTTP Endpoint and WASM Client

Status: Complete

Suggested executor: orchestrator

- [x] Add `GET /api/players/{playerId}` mapping in the players endpoint group with authorization, OpenAPI metadata, and standard `ProblemDetails` behavior via `ToHttpResult`.
- [x] Ensure endpoint and client consume shared `Nova.Shared` route constants.
- [x] Implement/update the typed WASM client service and DI registration in `Nova.Client/Program.cs`.
- [x] Keep result mapping consistent for not-found, forbidden, validation/transport failures using existing `ServiceProblem` and HTTP response conversion patterns.

### Verification Plan

- `dotnet test --project Nova.Integration.Tests --filter-class "*PlayerDetailHttpTests"` discovers at least one test and all discovered tests pass.
- `dotnet build Nova.slnx` reports 0 errors.

### Phase Summary

Added `MapPlayerEndpoints()` with `RequireAuthorization(Policies.RequireClubMember)`, `ProducesProblem` metadata, named route, and static handler calling `ToHttpResult`. Added `HttpPlayerDetailService` using shared route constants and `ToServiceProblemAsync` failure deserialization. Registered server and WASM DI bindings and mapped the new endpoint in `Program.cs`.

## Phase 4: Add/Finalize Focused Coverage and Run Verification

Status: Complete

Suggested executor: sub-agent w/ smaller model

- [x] Add/extend focused unit tests for ordering, archived-history retention, missing-actor fallback, authorization, and tenancy isolation.
- [x] Add/extend focused integration tests for endpoint behavior and HTTP/service result mapping.
- [x] Add/extend focused client tests for typed WASM client mapping of service/transport failures.
- [x] Run issue-specified verification commands and confirm each filtered command discovers tests.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*PlayerDetailQueryServiceTests"` discovers at least one test and all discovered tests pass.
- `dotnet test --project Nova.Integration.Tests --filter-class "*PlayerDetailHttpTests"` discovers at least one test and all discovered tests pass.
- `dotnet build Nova.slnx` reports 0 errors.

### Phase Summary

Added `PlayerDetailQueryServiceTests` (unit, SQLite tenancy harness), `HttpPlayerDetailServiceTests` (typed WASM client), and `PlayerDetailHttpTests` (Aspire HTTP e2e). Verification completed with:
- `dotnet test --project Nova.Unit.Tests --filter-class "*PlayerDetailQueryServiceTests" --filter-class "*HttpPlayerDetailServiceTests"` (7 passed)
- `dotnet test --project Nova.Integration.Tests --filter-class "*PlayerDetailHttpTests"` (3 passed)
- `dotnet build Nova.slnx` (0 errors)

## Final Recap

Implemented issue #31 end to end. The system now exposes `GET /api/players/{playerId}` returning permanent player profile + lifecycle and full campaign-grouped history from explicit `PlayerCampaignAssignmentEntity` links, including ordered notes, ordered tag applications, archived-history preservation, tenant-safe actor attribution fallback, and active-campaign current-trait union. Shared contracts, server query service, endpoint wiring, WASM typed client, and focused unit/integration/client tests are all in place.

## Deployment Plan

1. Deploy the updated server and WASM client together so `Nova.Shared/Players` contracts, endpoint routes, and typed client stay version-aligned.
2. No migration or data backfill is required; this change is read-query and API/client wiring only.
3. Run the standard post-deploy smoke check against `GET /api/players/{playerId}` with a club member session and a cross-tenant session to confirm expected 200/404 behavior.
