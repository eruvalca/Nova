# Placement Roster and Summary Query API

Implements issue #86: a tenant-safe, bounded placement roster plus an authoritative whole-campaign
outcome summary for the workspace Placements tab and the #12 closeout presentation. This is a
read-only vertical slice: shared contracts → `NovaReadDbContext` query service → authorized GET
endpoints → typed WASM client → focused tests. No schema changes, no mutations, no Razor UI.

## Confirmed design decisions

- **Two endpoints**: `GET /api/campaigns/{campaignId}/placements` (paged roster) and
  `GET /api/campaigns/{campaignId}/placements/summary` (whole-campaign counts). Confirmed with the
  user so #12 can consume the summary directly without a second outcome-count query.
- **Deterministic ordering**: display name ascending (last name, then first name), then
  `PlayerCampaignAssignmentId` ascending as the stable tie-breaker. No sort options. Confirmed with
  the user.
- **Filters**: optional single `graduationYear` (matches the `[Grad year v]` select) and optional
  `unresolvedOnly` (maps to `PlacementOutcome.Undecided`; never redefined). Both compose with paging
  and never affect summary counts.
- **Paging limits** mirror the existing roster limits: page 1 default, page size 50 default, max 100.
- **Row contract** carries every persisted field needed to render and safely submit
  `UpdateCampaignPlacementInput`: assignment/player IDs, display name, graduation year, outcome,
  team summary, and the assignment concurrency token.
- **Team choices** reuse the existing #8 `ITeamRosterService` Active-team query — no new team
  endpoint or campaign-team persistence.
- Reuse `CampaignParticipantTeamSummaryDto` from #68 for the team summary (same `TeamId`+`TeamName`
  shape); #68 contracts remain untouched.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to
`Complete` and write its **Phase Summary**; run the phase's **Verification Plan** and record the
result before moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

Implementation recipes: invoke the `add-feature-slice` skill for the vertical-slice steps and
`nova-testing` for harness/run details. Always-on rules: `.github/instructions/` (api-endpoints,
service-layer, validation, testing, ef-core-tenancy, csharp-conventions).

## Phase 1: Shared contracts, route constants, and query-service interface

Status: Complete <!-- Not started | In progress | Complete -->

- [x] Add `GetCampaignPlacementRosterInput` (CampaignId, GraduationYear `int?`, UnresolvedOnly `bool?`, nullable Page/PageSize with roster-mirrored constants) with DataAnnotations per the validation rules.
- [x] Add `GetCampaignPlacementSummaryInput` (CampaignId only).
- [x] Add `CampaignPlacementRosterItem` (PlayerCampaignAssignmentId, PlayerId, DisplayName, GraduationYear, PlacementOutcome, `CampaignParticipantTeamSummaryDto?` Team, Guid ConcurrencyToken) and `CampaignPlacementSummaryDto` (AssignedCount, NotSelectedCount, WithdrawnCount, UndecidedCount, TotalCount).
- [x] Add `ICampaignPlacementQueryService` with `GetPlacementRosterAsync` and `GetPlacementSummaryAsync` returning `ServiceResult<T>`.
- [x] Extend `CampaignEndpoints` with placement roster + summary route constants, relative templates, route names, and URL builders that emit only values the input contract accepts.

### Verification Plan

- `dotnet build Nova.slnx` succeeds with no warnings from the new files.

### Phase Summary

Created 4 files in `Nova.Shared/Features/Campaigns/` (`GetCampaignPlacementRosterInput.cs`,
`GetCampaignPlacementSummaryInput.cs`, `CampaignPlacementContracts.cs`,
`ICampaignPlacementQueryService.cs`) and extended `CampaignEndpoints.cs` with the two route
constants/relative templates/route names and the two URL builders. Paging mirrors the #68 roster
limits (page 1/50 default, max 100) via nullable properties with initializers, coalesced in the
service. `unresolvedOnly` is bound as `bool?` so omission (no filter) is valid; only `true` filters.
Build succeeded with 0 warnings/errors.

## Phase 2: Server query service + authorized GET endpoints

Status: Complete <!-- Not started | In progress | Complete -->

- [x] Add `CampaignPlacementQueryService` (Nova/Features/Campaigns/) using `IDbContextFactory<NovaReadDbContext>` + `ICurrentUserProvider`: dual-layer validation, club-scope authorization (any approved member), campaign existence check → non-disclosing `NotFound`, projection-only bounded SQL with `Skip`/`Take` before materialization, deterministic display-name ordering with assignment-ID tie-break, provider-safe patterns only where needed.
- [x] Summary computed in **one** grouped SQL statement over the whole campaign (per-outcome counts + derived total), independent of filters/paging.
- [x] Add `CampaignPlacementEndpointRouteBuilderExtensions` with `MapGroup(CampaignEndpoints.GroupPrefix).RequireAuthorization(Policies.RequireClubMember)`, static handlers, `[AsParameters]` binding, `ToHttpResult`, and full OpenAPI/ProblemDetails metadata (`Produces`, `ProducesValidationProblem`, `ProducesProblem` 401/403/404/500, `WithName`).
- [x] Register `ICampaignPlacementQueryService` (scoped) and call `app.MapCampaignPlacementEndpoints()` in `Nova/Program.cs`.

### Verification Plan

- `dotnet build Nova.slnx` succeeds.
- OpenAPI sanity: start `dotnet run --project Nova.AppHost` and confirm both routes appear under `/openapi` with declared 401/403/404 responses (or rely on the integration tests in Phase 5 that prove registration).

### Phase Summary

Created `CampaignPlacementQueryService` (read-only, projection-only SQL, deterministic
last-name/first-name/assignment-id ordering, bounded `Skip`/`Take` in SQL, overflow guard mirroring
the #68 roster service) and `CampaignPlacementEndpointRouteBuilderExtensions` (two `MapGet` routes
under the campaign group with `RequireClubMember`, full ProblemDetails metadata, static handlers).
Registered scoped `ICampaignPlacementQueryService` and mapped endpoints in `Nova/Program.cs`. Build
passed with 0 warnings. OpenAPI registration is proven by Phase 5 HTTP tests instead of a manual
AppHost run.

## Phase 3: Typed WASM client

Status: Complete <!-- Not started | In progress | Complete -->

- [x] Add `HttpCampaignPlacementQueryService` (Nova.Client/Services/Campaigns/) implementing the shared interface: client-side `InputValidator`, shared URL constants, `ToServiceProblemAsync` on failures, `ReadRequiredJsonAsync` with structural success-payload validation (populated rows, paging invariants, count bounds, token presence).
- [x] Register `ICampaignPlacementQueryService` → HTTP implementation in `Nova.Client/Program.cs`.

### Verification Plan

- `dotnet build Nova.slnx` succeeds.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*HttpCampaignPlacementQueryServiceTests"` passes (after Phase 4).

### Phase Summary

Created `HttpCampaignPlacementQueryService` implementing the shared interface with structural
success-payload validation: paging invariants, row shape (positive IDs, non-blank display name,
outcome/team relationship, non-empty concurrency token), filter fidelity (graduation year,
unresolved-only), and summary count consistency (non-negative counts, total = sum of counts).
Registered in `Nova.Client/Program.cs`. Build passed with 0 warnings.

## Phase 4: Unit tests (SQLite tenancy harness + client tests)

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: sub-agent with a smaller model (well-specified once Phases 1–3 exist).

- [x] `CampaignPlacementQueryServiceTests` (Nova.Unit.Tests/Campaigns/): summary accuracy over mixed outcomes; filter composition (`graduationYear` + `unresolvedOnly`); paging bounds and default page size; deterministic ordering incl. assignment-ID tie-break; concurrency-token presence; member authorization; no-club/user forbidden; cross-tenant campaign → NotFound; tenant isolation of rows.
- [x] `CampaignPlacementInputValidationTests`: invalid page/pageSize/graduationYear values rejected; omission of optional filters accepted.
- [x] `HttpCampaignPlacementQueryServiceTests`: URL composition, validation short-circuit, problem deserialization (404/403/validation), success-body structural validation (populated payload, malformed/empty → ServerError).

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignPlacement*"` — all pass.

### Phase Summary

Wrote the three test files directly (the work was tightly coupled to the contracts written in
Phases 1–3, so no sub-agent delegation). 66 placement-focused tests pass and the full unit suite
(1419 tests) passes with no regressions. Two initial client-test failures were test-data mistakes
(an `Assigned` row against `unresolvedOnly=true`; URL expectation missing the default
`page=1&pageSize=50` that the initializer-backed builder emits, matching the #68 builder
precedent) — fixed in the tests, not the production code.

## Phase 5: Integration HTTP tests (Aspire/PostgreSQL)

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: sub-agent with a smaller model (well-specified once Phases 1–3 exist).

- [x] `CampaignPlacementHttpTests` (Nova.Integration.Tests/Http/): member 200 + payload shape for roster and summary; unauthenticated 401; non-member/cross-tenant 403 and non-disclosing 404 with traceId; invalid explicit query values → validation problem; summary unaffected by roster filters; deterministic ordering and paging across pages.

### Verification Plan

- Start `dotnet run --project Nova.AppHost`, then
  `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignPlacementHttpTests"` — all pass.

### Phase Summary

Wrote `CampaignPlacementHttpTests` (9 tests) following the `CampaignParticipantHttpTests` pattern.
No manual AppHost start was needed — `NovaAppHostFixture` launches the AppHost in-process via
`DistributedApplicationTestingBuilder`. All 9 tests passed in 1m 02s against real PostgreSQL 18,
proving route registration, `RequireClubMember` middleware (401/403), non-disclosing cross-tenant
404s with traceId, deterministic cross-page ordering on the real provider, filter composition, and
summary independence.

## Phase 6: Final verification

Status: Complete <!-- Not started | In progress | Complete -->

- [x] `dotnet format Nova.slnx --verify-no-changes` clean (apply with `dotnet format Nova.slnx` if needed).
- [x] Full unit suite: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` passes.
- [x] Full integration suite against the AppHost: `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` passes.

### Verification Plan

- All three commands above succeed; no pre-existing tests regress.

### Phase Summary

- Unit suite: 1419 passed, 0 failed.
- Integration suite: 243 passed, 0 failed (Aspire AppHost + PostgreSQL 18, ~3 min).
- `dotnet format --verify-no-changes` reports zero issues in every file this work touched. The
  command still exits non-zero because of pre-existing `CHARSET` errors in Tag-feature and
  migration files created by parallel sibling sessions (issue #86 has parallel children) —
  deliberately not fixed here.

## Final Recap

Implemented the issue #86 placement roster and summary query slice end to end:

- **Shared contracts** (`Nova.Shared/Features/Campaigns/`): `GetCampaignPlacementRosterInput`
  (graduationYear + unresolvedOnly filters, nullable page/pageSize mirroring the #68 roster
  limits), `GetCampaignPlacementSummaryInput`, `CampaignPlacementRosterItem` (assignment/player
  IDs, display name, graduation year, outcome, team summary, concurrency token),
  `CampaignPlacementSummaryDto` (four outcome counts + total), `ICampaignPlacementQueryService`,
  and placement route constants + URL builders in `CampaignEndpoints`.
- **Server** (`Nova/Features/Campaigns/`): `CampaignPlacementQueryService` — dual-layer
  validation, signed-in + club-scope authorization, non-disclosing NotFound for invisible
  campaigns, projection-only `NovaReadDbContext` SQL with `Skip`/`Take` in SQL, deterministic
  display-name ordering (last, first, then assignment id), and a single grouped SQL statement for
  whole-campaign summary counts. `CampaignPlacementEndpointRouteBuilderExtensions` maps
  `GET /api/campaigns/{campaignId}/placements` and `GET .../placements/summary` under
  `RequireClubMember` with full OpenAPI/ProblemDetails metadata; both wired in `Nova/Program.cs`.
- **WASM client** (`Nova.Client/Services/Campaigns/`): `HttpCampaignPlacementQueryService` with
  client-side validation, shared route constants, problem deserialization, and strict
  success-payload structural validation; registered in `Nova.Client/Program.cs`.
- **Tests**: 66 unit tests (service over the SQLite tenancy harness, input validation, WASM client)
  + 9 PostgreSQL HTTP integration tests covering auth, tenancy, validation, ordering, paging,
  filter composition, and summary independence.

#68 contracts are untouched and backward compatible; team choices intentionally reuse the #8
`ITeamRosterService`. No schema changes, no mutations, no UI — as scoped.

## Deployment Plan

- Standard service deployment; no schema changes, migrations, or infrastructure work required.
- `Nova/Program.cs` registers the new scoped service and maps the two GET routes; the WASM client
  registration is inert until a UI consumer (Placements tab, issue #11) or closeout (#12)
  injects `ICampaignPlacementQueryService`.
- CI runs build + unit tests only; the integration suite was run locally and passes.
