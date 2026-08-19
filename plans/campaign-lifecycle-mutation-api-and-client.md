# Campaign Close and Reopen Mutation API and Client

Expose the completed `CampaignLifecycleService` close and reopen operations (issue #104, parent epic #12)
through administrator-only HTTP endpoints (`POST /api/campaigns/{campaignId}/close` and
`POST /api/campaigns/{campaignId}/reopen`) and a typed WASM client, with exhaustive ProblemDetails
mapping, condition-keyed blocker propagation, and full unit + Aspire integration HTTP test coverage.
No entities, migrations, queries, Razor UI, or policy changes — the service, closure policy, and result
unions are reused as-is. This slice mirrors the completed placement mutation slice (#85, PR #97,
`plans/campaign-placement-mutation-api-and-client.md`) step for step.

Decisions confirmed with the requester:

- Routes: `POST /api/campaigns/{campaignId:long}/close` and `POST /api/campaigns/{campaignId:long}/reopen`;
  route-only campaign id, no request body (tag/team archive-restore precedent).
- Success: **204 No Content**; cross-tier contract is `ServiceResult<Success>` (team-lifecycle precedent).
- Close blockers map to **409 Conflict** carrying the condition-keyed `CampaignCloseBlocked.Errors`
  groups (`outcomes` / `eligibility` / `archivedTeams`, with counts and assignment ids) in the
  structured `errors` extension — the `ServiceProblem.Conflict(detail, errors)` precedent; never recalculated.
- `CampaignLifecycleService` explicitly implements the new shared `ICampaignLifecycleService`
  (SSR-prerender readiness for #101's workspace composition); endpoint handlers call the concrete
  OneOf-returning service directly — no second mutation service.
- Aspire integration HTTP tests are in scope and run locally against the AppHost before merge.
- A PR for #104 is opened on completion.
- **N/A from issue boilerplate:** "malformed payloads" and "route/body mismatch" tests do not apply —
  the endpoints accept no request body (team-lifecycle precedent: no `.ProducesValidationProblem()`,
  no body binding, no 400 slot beyond framework-level handling).

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to
`Complete` and write its **Phase Summary**; run the phase's **Verification Plan** and record the result
before moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

Known landmine (read before Phase 5): `CampaignLifecycleService` calls `BeginTransactionAsync` directly.
The placement slice proved this throws `InvalidOperationException` on the real
`NpgsqlRetryingExecutionStrategy` (Aspire's `EnrichNpgsqlDbContext` enables retries), while the SQLite
unit harness and fixture contexts do not configure the strategy — it only surfaces through the real
HTTP path. Phase 5 contains the documented contingency.

## Phase 1: Shared contracts and route constants

Status: Complete <!-- Not started | In progress | Complete -->

- [x] Create `Nova.Shared/Features/Campaigns/ICampaignLifecycleService.cs` — the cross-tier WASM
      contract: `Task<ServiceResult<Success>> CloseAsync(long campaignId, CancellationToken = default)`
      and `Task<ServiceResult<Success>> ReopenAsync(long campaignId, CancellationToken = default)`.
- [x] Add to `Nova.Shared/Features/Campaigns/CampaignEndpoints.cs`, following the existing
      full-URL / relative / route-name / URL-builder shape:
      - `Close` (`{GroupPrefix}/{campaignId:long}/close`), `CloseRelative` (`{campaignId:long}/close`),
        `CloseRouteName` (`CloseCampaign`), and `CloseUrl(long campaignId)` builder.
      - `Reopen`, `ReopenRelative` (`{campaignId:long}/reopen`), `ReopenRouteName` (`ReopenCampaign`),
        and `ReopenUrl(long campaignId)` builder.
- [x] No new DTOs — 204 success means no success payload type is needed.

### Verification Plan

- `dotnet build Nova.slnx` succeeds.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignLifecycleServiceTests"` still passes (service untouched).

### Phase Summary

- `ICampaignLifecycleService` added as the cross-tier WASM contract returning `ServiceResult<Success>` for both operations.
- `CampaignEndpoints` gained the close/reopen full-URL, relative, route-name constants and the `CloseUrl`/`ReopenUrl` builders; routes agreed with the requester (`POST /api/campaigns/{campaignId:long}/close` and `/reopen`).
- No DTOs added (204 success carries no payload).
- Verification: solution build succeeded (0 warnings/0 errors); `CampaignLifecycleServiceTests` (8 tests) still passed.

## Phase 2: Server endpoint mapping, handlers, and registration

Status: Complete <!-- Not started | In progress | Complete -->

- [x] Create `Nova/Features/Campaigns/CampaignLifecycleEndpointRouteBuilderExtensions.cs`:
      - `extension(IEndpointRouteBuilder endpoints)` block with `MapCampaignLifecycleEndpoints()`
        mapping `MapPost(CampaignEndpoints.CloseRelative, ...)` and
        `MapPost(CampaignEndpoints.ReopenRelative, ...)` under
        `MapGroup(CampaignEndpoints.GroupPrefix).RequireAuthorization(Policies.RequireClubAdmin)`,
        with `.Produces(StatusCodes.Status204NoContent)`, `.ProducesProblem(401/403/404/409/500)`
        (no `.ProducesValidationProblem()` — no request body), `.DisableAntiforgery()`, and
        `.WithName(...)` on each.
      - Static handlers injecting the concrete `CampaignLifecycleService` + `CancellationToken`
        (placement precedent).
      - Feature-local `ToHttpResult()` conversion covering every union case exhaustively:
        - `CampaignCloseResult`: `Success` → `TypedResults.NoContent()`; `NotFound` → non-disclosing
          `ServiceProblem.NotFound()`; `LifecycleForbidden` → `ServiceProblem.Forbidden(detail)`;
          `CampaignCloseBlocked` → `ServiceProblem.Conflict(blocked.Detail, blocked.Errors)`;
          `LifecycleConflict` → `ServiceProblem.Conflict(detail)`.
        - Reopen `OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>`: the same mapping
          minus the blocker case.
- [x] Make `CampaignLifecycleService` explicitly implement `ICampaignLifecycleService`, mapping its
      OneOf results to `ServiceResult<Success>` with the same conversions (placement precedent).
- [x] Register `app.MapCampaignLifecycleEndpoints();` in `Nova/Program.cs` next to the other campaign
      mappings, and add the forwarding registration
      `builder.Services.AddScoped<ICampaignLifecycleService>(services => services.GetRequiredService<CampaignLifecycleService>());`
      next to the existing `CampaignLifecycleService` registration.

### Verification Plan

- `dotnet build Nova.slnx` succeeds.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignLifecycleEndpointTests"` passes (Phase 4 tests).

### Phase Summary

- Endpoint builder created with two `MapPost` routes under the shared campaign group (`RequireClubAdmin` at group level), full `Produces*` metadata (204/401/403/404/409/500), `DisableAntiforgery()`, and the shared route names; static handlers call the concrete `CampaignLifecycleService`.
- Two feature-local `ToHttpResult()` extensions convert `CampaignCloseResult` (5 cases) and the reopen `OneOf` (4 cases) exhaustively; close-blocked maps its condition-keyed `Errors` via `ServiceProblem.Conflict(detail, errors)`.
- `CampaignLifecycleService` now explicitly implements `ICampaignLifecycleService`; the forwarding DI registration and `app.MapCampaignLifecycleEndpoints()` were wired in `Nova/Program.cs`.
- Verification: solution build succeeded (0 warnings/0 errors).

## Phase 3: WASM client and DI registration

Status: Complete <!-- Not started | In progress | Complete -->

- [x] Create `Nova.Client/Services/Campaigns/HttpCampaignLifecycleService.cs` implementing
      `ICampaignLifecycleService`:
      - `http.PostAsync(CampaignEndpoints.CloseUrl(campaignId), content: null, cancellationToken)`
        and the same for `ReopenUrl`.
      - Failure → `response.ToServiceProblemAsync(cancellationToken)` (preserves structured errors,
        including the 409 `errors` extension).
      - Success → `new Success()` (204 carries no body — no success-shaped fallbacks).
- [x] Register `builder.Services.AddScoped<ICampaignLifecycleService, HttpCampaignLifecycleService>();`
      in `Nova.Client/Program.cs` next to the other campaign services.

### Verification Plan

- `dotnet build Nova.slnx` succeeds.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*HttpCampaignLifecycleServiceTests"` passes (Phase 4 tests).

### Phase Summary

- `HttpCampaignLifecycleService` POSTs to the shared `CloseUrl`/`ReopenUrl` builders with no body, converts failures via `ToServiceProblemAsync` (structured 409 errors preserved), and returns `new Success()` on 204 — no success-shaped fallbacks.
- Registered against `ICampaignLifecycleService` in `Nova.Client/Program.cs`.
- Verification: solution build succeeded (0 warnings/0 errors).

## Phase 4: Unit tests — endpoint metadata, result conversion, WASM client

Status: Complete <!-- Not started | In progress | Complete -->

- [x] Create `Nova.Unit.Tests/Campaigns/CampaignLifecycleEndpointTests.cs`:
      - Both routes are registered at the exact shared templates, each carrying `RequireClubAdmin`
        authorization metadata, `IAntiforgeryMetadata` with `RequiresValidation == false`, the POST
        verb, and the shared route name (placement endpoint-test pattern).
      - `ToHttpResult` conversion executed against a `DefaultHttpContext` (pattern of
        `CampaignPlacementEndpointTests.ExecuteAsync`) for **every** case:
        - Close: success → 204 with empty body; not-found → 404 without disclosure detail;
          forbidden → 403 with the service detail; blocked → 409 whose `errors` extension contains
          the `outcomes` / `eligibility` / `archivedTeams` keys with the policy messages;
          lifecycle conflict → 409 with the conflict detail.
        - Reopen: success / not-found / forbidden / conflict — same four mappings.
- [x] Create `Nova.Unit.Tests/Campaigns/HttpCampaignLifecycleServiceTests.cs`:
      - Success POSTs to the built close/reopen URLs with `HttpMethod.Post` and returns `Success` on 204.
      - 403, non-disclosing 404, 409-with-errors, and 409-without-errors ProblemDetails round-trip to
        the matching `ServiceProblemKind`, preserving the structured error groups.
- [x] Explicitly cover the `CampaignLifecycleService` → `ICampaignLifecycleService` explicit-interface
      mapping (OneOf → `ServiceResult`) if not already covered by the existing lifecycle service tests.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignLifecycleEndpointTests"` passes.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*HttpCampaignLifecycleServiceTests"` passes.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full unit suite stays green.

### Phase Summary

- `CampaignLifecycleEndpointTests` (10 tests): both routes registered at the shared templates with `RequireClubAdmin` + disabled antiforgery + POST + route name; `ToHttpResult` conversion executed against an isolated `DefaultHttpContext` for all five close cases and all four reopen cases (204 empty body, non-disclosing 404, 403/409 with details, and the close-blocked 409 carrying `outcomes`/`eligibility`/`archivedTeams`).
- `HttpCampaignLifecycleServiceTests` (6 tests): POST to the shared URL builders, 204 → `Success`, and forbidden/not-found/conflict-with-errors/conflict-without-errors ProblemDetails round-trips.
- `CampaignLifecycleServiceTests` extended with 4 explicit-interface mapping tests (OneOf → `ServiceResult`) covering success, close-blockers → `Conflict` with structured errors, forbidden/not-found/already-closed, and reopen success/conflict/forbidden/not-found.
- Full unit suite green: 1527 passed, 0 failed.

## Phase 5: Aspire integration HTTP tests (run locally)

Status: Complete <!-- Not started | In progress | Complete -->

- [x] Create `Nova.Integration.Tests/Http/CampaignLifecycleHttpTests.cs`
      (`[Collection(NovaAppHostCollection.Name)]`, `NovaAppHostFixture` via primary constructor,
      `using static SeedingHelpers`; seeding stays feature-local in the class):
      - Anonymous caller → 401 on both endpoints.
      - Authenticated club member (non-admin) → 403 on both endpoints.
      - Club admin close success: fully-decided campaign → 204, and the persisted row is `Closed` with
        `ClosedAt` and `ClosedById` recorded plus a `Closed` lifecycle event (transactional, audit actor).
      - Blocked close: undecided participant; ineligible Assigned (missing/younger team); Assigned to an
        archived team → 409 with `errors` keys `outcomes` / `eligibility` / `archivedTeams` carrying the
        counts and assignment ids; the campaign remains Active.
      - Cross-tenant campaign id → 404 (non-disclosing).
      - Already-closed close → 409; already-active reopen → 409.
      - Club admin reopen success: Closed campaign → 204, row back to `Active` with closure metadata
        cleared and a `Reopened` lifecycle event persisted.
- [x] **Contingency — expected foundation defect:** `CampaignLifecycleService.CloseAsync/ReopenAsync`
      call `BeginTransactionAsync` directly; on the real `NpgsqlRetryingExecutionStrategy` (Aspire
      `EnrichNpgsqlDbContext` enables retries) this throws `InvalidOperationException`, exactly as the
      placement slice discovered. If the real HTTP path hits this, refactor the service's transaction
      blocks to the canonical `CreateExecutionStrategy().ExecuteAsync` pattern with a fresh tenant
      context per attempt (peers: `TeamLifecycleService`, `TagDefinitionLifecycleService`,
      `CampaignPlacementService`) — service-only, no policy or persistence changes — and re-run the
      existing lifecycle unit/Postgres tests.

### Verification Plan

- `dotnet build Nova.slnx` succeeds.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignLifecycleHttpTests"` passes against the Aspire AppHost (starts it if not running).
- Re-run regression classes: `CampaignLifecyclePostgresTests` and `CampaignTagApplicationHttpTests`.

### Phase Summary

- **Foundation defect found and fixed**: the real HTTP path surfaced
  `InvalidOperationException: The configured execution strategy 'NpgsqlRetryingExecutionStrategy' does not support user-initiated transactions` on the service's direct `BeginTransactionAsync` (the SQLite unit harness and fixture contexts do not configure the strategy). Refactored `CloseAsync`/`ReopenAsync` to the repo's canonical `CreateExecutionStrategy().ExecuteAsync` pattern with a fresh tenant context per attempt and commit verification (`VerifyClosureCommittedAsync`/`VerifyReopenCommittedAsync`) that reconstructs success after an ambiguous commit — no policy, persistence, or schema changes.
- `CampaignLifecycleHttpTests` (7 tests): anonymous 401, member 403 (both endpoints), admin close 204 + persisted `Closed`/`ClosedAt`/`ClosedById` + `Closed` event, blocked close 409 with `outcomes`/`eligibility`/`archivedTeams` keys + counts/assignment ids and campaign still `Active`, cross-tenant 404 (non-disclosing), already-closed close 409 + already-active reopen 409, admin reopen 204 + closure metadata cleared + `Reopened` event.
- Regression runs: `CampaignLifecyclePostgresTests` 8 passed, `CampaignTagApplicationHttpTests` 17 passed.

## Phase 6: Formatting, full validation, acceptance walk, and PR

Status: Complete <!-- Not started | In progress | Complete -->

- [x] `dotnet format Nova.slnx` (apply), then `dotnet format Nova.slnx --verify-no-changes` passes.
- [x] `dotnet build Nova.slnx` clean.
- [x] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` full suite green.
- [x] Walk the six issue acceptance criteria line by line and record evidence in the Final Recap.
- [x] Commit the change to `eruvalca-campaign-close-and-reopen-mutation-api-a` with the
      Co-authored-by trailer.
- [x] Open a PR for issue #104.

### Verification Plan

- `dotnet format Nova.slnx --verify-no-changes` exits 0.
- Full unit suite exits 0; the Phase 5 integration class exits 0.
- `git status` shows only the intended files.

### Phase Summary

- `dotnet format Nova.slnx --verify-no-changes` exited 0 ("Formatted 0 of 597 files"); solution build clean (0 warnings/0 errors); full unit suite 1527 passed.
- The Phase 5 integration class (`CampaignLifecycleHttpTests`) passed 7/7 against the Aspire AppHost.
- `git status` shows only the intended files (6 new source/test files, 5 modified files, plus this plan).
- Committed with the Co-authored-by trailer; PR opened for issue #104.

## Final Recap

Issue #104 (sub-issue of #12) is complete: the completed `CampaignLifecycleService` close and reopen
operations are now exposed through administrator-only HTTP endpoints and a typed WASM client.

- **HTTP**: `POST /api/campaigns/{campaignId:long}/close` and `/reopen` under the shared campaign
  group with `RequireClubAdmin`, antiforgery disabled for the WASM client, full `Produces*` metadata
  (204/401/403/404/409/500), and route names. The `CampaignCloseResult` OneOf and reopen OneOf are
  converted to HTTP exhaustively: success → 204; not-found → non-disclosing 404; forbidden → 403 with
  the service detail; close-blocked → 409 whose `errors` extension carries the condition-keyed
  `outcomes`/`eligibility`/`archivedTeams` groups; lifecycle conflict → 409.
- **Shared**: `ICampaignLifecycleService` added as the cross-tier contract; close/reopen route
  constants and URL builders added to `CampaignEndpoints`. `CampaignLifecycleService` explicitly
  implements `ICampaignLifecycleService` (mapping OneOf → `ServiceResult`) and is registered for
  server-side prerender.
- **WASM**: `HttpCampaignLifecycleService` POSTs to the shared URLs with no body, preserves structured
  problems via `ToServiceProblemAsync`, and returns `new Success()` on 204 — no success-shaped fallbacks.
  Registered in `Nova.Client/Program.cs`.
- **Foundation defect fixed**: the lifecycle service's direct `BeginTransactionAsync` threw on the
  retrying PostgreSQL provider; refactored to the canonical `CreateExecutionStrategy().ExecuteAsync`
  pattern with a fresh tenant context per attempt and commit verification (no policy/persistence changes).
- **Tests**: 20 new unit tests (endpoint metadata + exhaustive conversion + client contract + explicit
  interface mapping) and 7 new Aspire integration HTTP tests. Full unit suite 1527 passed; integration
  classes `CampaignLifecycleHttpTests` (7), `CampaignLifecyclePostgresTests` (8), and
  `CampaignTagApplicationHttpTests` (17) all passed locally.

Acceptance criteria evidence:
1. Only club administrators can close or reopen — group-level `RequireClubAdmin` policy plus the
   service's in-tier admin guard; anonymous 401 and member 403 integration tests on both endpoints.
2. Close returns actionable, condition-keyed blockers without recalculating readiness — the service
   reuses `CampaignClosurePolicy.Evaluate` and the endpoint maps `CampaignCloseBlocked.Errors` verbatim
   into the 409 `errors` extension; blocked-close unit + integration tests assert `outcomes` /
   `eligibility` / `archivedTeams` with counts and assignment ids.
3. Closing is transactional — a 204 close means the row is `Closed` with `ClosedAt` + `ClosedById`
   plus a `Closed` lifecycle event in the same transaction (integration test); a blocked close leaves
   the campaign `Active` with no lifecycle event (integration test).
4. Reopening records an auditable action and returns forbidden for non-administrators — reopen appends
   a `Reopened` lifecycle event and clears closure metadata (integration test); member 403 (integration test).
5. Archived/unavailable/cross-tenant/already-transitioned requests preserve foundation behavior via
   standard ProblemDetails — archived-team blocker → 409 `archivedTeams`, cross-tenant id → non-disclosing
   404, already-closed close → 409, already-active reopen → 409 through real HTTP.
6. Endpoint and client code handle all service results exhaustively and do not invoke or duplicate
   policy logic — `ToHttpResult` covers all 5 close and 4 reopen cases (unit-tested), the client maps
   every problem via `ToServiceProblemAsync`, and the closure policy remains service-internal.

## Deployment Plan

1. Merge `eruvalca-campaign-close-and-reopen-mutation-api-a` into `main` via PR.
2. CI runs build + unit tests automatically; the integration HTTP suite was verified locally against
   the Aspire AppHost and requires no additional environment beyond Docker.
3. No migrations, configuration, or environment changes ship with this work. The endpoints are live
   once the server deploys; there is no client-side UI in this slice, so no rollout ordering applies.
4. Monitor `CampaignId=... lifecycle changed to Closed/Active by UserId=...` and
   `Campaign close blocked for CampaignId=...` structured logs for administrator close/reopen activity
   after rollout.
