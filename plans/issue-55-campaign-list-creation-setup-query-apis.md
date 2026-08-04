# Issue #55 Campaign List and Creation Setup Query APIs

Add the read-only campaign query slice required by issue #55 and parent epic #9: a bounded,
season-grouped campaign list plus campaign-creation setup data, exposed through shared contracts,
tenant-safe server queries, authorized HTTP endpoints, and a typed WebAssembly client. This work
does not add persistence, mutations, Razor UI, dashboard rendering, or campaign-team relationships.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

Before implementation, invoke the repository skills that match the phase:

- `add-api-endpoint` for shared route constants, endpoint mapping, ProblemDetails, authorization,
  and WebAssembly client wiring.
- `nova-testing` before adding or running tests.

Follow these repository instructions throughout:

- `.github/instructions/csharp-conventions.instructions.md`
- `.github/instructions/service-layer.instructions.md`
- `.github/instructions/api-endpoints.instructions.md`
- `.github/instructions/ef-core-tenancy.instructions.md`
- `.github/instructions/testing.instructions.md`

### Confirmed Contract and Scope Decisions

- Add one boundary-crossing `ICampaignQueryService` with campaign-list and creation-setup methods.
  The server implementation is `CampaignQueryService`; the WebAssembly implementation is
  `HttpCampaignQueryService`.
- `GET /api/campaigns` accepts:
  - Optional case-insensitive `status` with allowed values `active` and `closed`; omission returns
    both statuses.
  - Optional nullable `limit`; omission uses 50, explicit values must be from 1 through 100.
  - Invalid explicit values are validation failures, not silently normalized or clamped.
- The campaign-list result contains season groups and `TotalCount`, where `TotalCount` is the
  number of matching campaigns before `Take(limit)`. Group only the bounded projected rows after
  materialization.
- Season groups are ordered by season start date descending, then season ID descending.
- Campaign rows are ordered by:
  1. status with Active before Closed,
  2. campaign start date descending,
  3. planned end date descending with null last,
  4. campaign name ascending,
  5. campaign ID descending.
- Each campaign row contains campaign ID, campaign name, start date, planned end date, status,
  participant count, and unresolved count. Each season group contains season ID, name, start date,
  and optional end date.
- Participant count includes every persisted `PlayerCampaignAssignmentEntity` for the campaign,
  regardless of the player's current lifecycle state. Unresolved count includes only assignments
  whose `PlacementOutcome` is `Undecided`.
- `GET /api/campaigns/creation-setup` returns:
  - the newest 100 tenant-visible season choices,
  - `TotalSeasonCount` before the bound,
  - the current count of Active players,
  - the current count of Active teams.
- Season choices are ordered by season start date descending, then season ID descending, and contain
  season ID, name, start date, and optional end date.
- Archived players and Archived teams do not contribute to setup counts. Teams are counted directly
  from persistent team lifecycle state; do not add or infer a campaign-team relationship.
- Both operations require an approved club member at middleware and service boundaries. Creation
  setup is intentionally readable by all approved club members, not only ClubAdmin users.
- Use `IDbContextFactory<NovaReadDbContext>`, tenant query filters, SQL-side filtering, ordering,
  counting, projection, and bounds. Do not load entity graphs or issue per-season/per-campaign
  queries.
- Extend `Nova.Shared/Campaigns/CampaignEndpoints.cs`; do not add a second campaign route-constant
  type. Use named routes `GetCampaignList` and `GetCampaignCreationSetup`.
- Keep the existing ClubAdmin-only `POST /api/campaigns` behavior unchanged. A separate query
  mapping may share `/api/campaigns` while applying `Policies.RequireClubMember` to the GET routes.
- The Active-filtered result is the reusable campaign-summary contract for dashboard epic #5.
- No EF model or migration changes are expected.
- Out of scope: campaign/season mutations, metadata correction, Razor UI, Playwright, dashboard
  cards/navigation, campaign workspace/detail queries, placement mutation, export, and any
  campaign-team persistence.

## Phase 1: Shared Query Contracts and Route Builders

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: sub-agent w/ smaller model

- [x] Add `GetCampaignListInput` under `Nova.Shared/Campaigns/` with documented constants for default
  limit 50 and maximum limit 100, a nullable case-insensitive status validation attribute, and a
  nullable `[Range]`-validated limit.
- [x] Add explicit-property DTO records for campaign summary rows, season groups, campaign-list
  results, season choices, and creation-setup results. Use required properties where a malformed
  success payload must not deserialize into a plausible default.
- [x] Add `ICampaignQueryService` returning
  `ServiceResult<CampaignListResult>` and `ServiceResult<CampaignCreationSetupResult>`.
- [x] Extend `CampaignEndpoints` with full and relative GET routes, route names, and a
  `GetCampaignListUrl` builder that emits only valid normalized status and limit query values.
  Use `/api/campaigns` for the list and `/api/campaigns/creation-setup` for setup.
- [x] Add focused contract tests for omitted values, case-insensitive accepted statuses, rejected
  statuses, limit bounds, and exact URL encoding/normalization. Follow existing input-validation
  and endpoint URL-builder test patterns.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*GetCampaignListInput*" --filter-class "*CampaignEndpoints*"` — discovers the new contract tests and all discovered tests pass.
- `dotnet build Nova.Shared/Nova.Shared.csproj` — succeeds with no errors.

### Phase Summary

Implemented the shared list/setup contracts, validation, route constants, URL builder, service boundary,
and contract tests. The list uses `/api/campaigns`; setup uses `/api/campaigns/creation-setup`.

## Phase 2: Tenant-Safe Campaign Query Service

Status: Complete

Suggested executor: sub-agent w/ smaller model

- [x] Add `Nova/Features/Campaigns/CampaignQueryService.cs` using
  `IDbContextFactory<NovaReadDbContext>`, `ICurrentUserProvider`, and source-generated warning
  logging for rejected callers.
- [x] In both methods, reject callers lacking a user ID or club ID with the repository-standard
  approved-member `Forbidden` result before creating a context.
- [x] In the list method, run `InputValidator.Validate(input)` before authorization or database work,
  parse the validated optional status case-insensitively, and coalesce omitted limit to 50.
- [x] Build one tenant-filtered campaign base query for the optional status. Execute one
  `CountAsync` for `TotalCount`, then one ordered, bounded, projection-only query for the result rows.
  Project season metadata and correlated assignment counts in SQL; do not use `Include`.
- [x] Express planned-end null-last ordering explicitly so SQLite and PostgreSQL select the same
  bounded rows. Apply every ordering key before `Take(limit)`.
- [x] Group only the bounded projected rows in memory, preserving the database order exactly. Do not
  re-sort with a key sequence that differs from the bounded SQL query.
- [x] In the setup method, query `TotalSeasonCount`, the newest 100 projected season choices, the
  Active-player count, and the Active-team count. Keep all four operations bounded/fixed-count and
  free from entity loading or query fan-out.
- [x] Register `ICampaignQueryService` to `CampaignQueryService` in `Nova/Program.cs`.
- [x] Add `CampaignQueryServiceTests` with the SQLite tenancy harness covering:
  - missing membership returns Forbidden with no query,
  - status omitted returns Active and Closed,
  - case-insensitive Active and Closed filtering,
  - total count is computed before the requested limit,
  - newest season grouping and the complete deterministic campaign ordering,
  - null planned-end ordering,
  - participant and Undecided counts,
  - archived players remain included in persisted campaign participant/unresolved counts,
  - list results are tenant-isolated,
  - setup season ordering, 100-choice bound, and total season count,
  - setup counts only Active players and Active teams,
  - setup results are tenant-isolated,
  - an approved non-admin member can call both service methods.
- [x] Cover query shape through the single projection query, SQL-side `Take(100)`/`Take(limit)`, and
  bounded-result service tests. The SQLite tenancy harness does not expose command interception;
  the integration projection/serialization tests additionally exercise the provider-generated queries.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*CampaignQueryServiceTests"` — discovers all service, tenancy, ordering, count, bound, and query-shape tests and they pass.
- `dotnet build Nova/Nova.csproj` — succeeds with no errors.

### Phase Summary

Implemented tenant-filtered, read-only projections with deterministic ordering, bounded results,
correlated assignment counts, live setup lifecycle counts, defensive approved-member authorization,
and source-generated forbidden-access logging. No entity or migration changes were required.

## Phase 3: Authorized HTTP Query Endpoints

Status: Complete

Suggested executor: sub-agent w/ smaller model

- [x] Add `CampaignQueryEndpointRouteBuilderExtensions` under `Nova/Features/Campaigns/` with a
  campaign query `MapGroup`, static handlers, and `Policies.RequireClubMember`.
- [x] Map `GET /api/campaigns` with `[AsParameters] GetCampaignListInput`, automatic validation,
  `CampaignListResult` success metadata, 400 validation, 401, 403, and 500 ProblemDetails metadata,
  and the shared `GetCampaignList` route name.
- [x] Map `GET /api/campaigns/creation-setup` with `CampaignCreationSetupResult` success metadata,
  401, 403, and 500 ProblemDetails metadata, and the shared `GetCampaignCreationSetup` route name.
- [x] Convert both service results through `ToHttpResult` so every ProblemDetails response retains
  the W3C trace ID.
- [x] Map the query endpoints in `Nova/Program.cs` without changing the authorization or behavior of
  the existing campaign creation endpoint.
- [x] Add focused Aspire HTTP tests in `Nova.Integration.Tests/Http/CampaignQueryHttpTests.cs`
  covering:
  - both routes reject anonymous callers,
  - an approved non-admin member receives 200 from both routes,
  - an invalid explicit status and invalid explicit limit return validation ProblemDetails with a
    trace ID,
  - omitted status/limit bind successfully,
  - Active and Closed filters serialize the expected grouped result,
  - campaign counts and setup counts serialize correctly,
  - a second club cannot observe the first club's campaigns, seasons, players, or teams.

### Verification Plan

- `dotnet test --project Nova.Integration.Tests --filter-class "*CampaignQueryHttpTests"` — discovers the endpoint tests and all discovered tests pass.
- `dotnet build Nova/Nova.csproj` — succeeds with no errors.

### Phase Summary

Added the authorized GET query group, response/problem metadata, shared route names, trace-preserving
result conversion, and HTTP coverage for authorization, validation, serialization, counts, and tenancy.
The existing ClubAdmin-only POST campaign endpoint remains unchanged.

## Phase 4: Typed WebAssembly Campaign Query Client

Status: Complete

Suggested executor: sub-agent w/ smaller model

- [x] Add `Nova.Client/Services/HttpCampaignQueryService.cs` implementing both query methods through
  the shared route constants and URL builder.
- [x] Deserialize required success bodies explicitly. Convert non-success responses with
  `ToServiceProblemAsync`; convert empty, JSON `null`, malformed, or structurally invalid 2xx
  payloads to `ServiceProblem.ServerError`.
- [x] Validate success payload invariants without over-constraining legitimate empty states:
  counts are non-negative, returned row count does not exceed the requested/default limit,
  `TotalCount` is not smaller than returned campaign rows, `TotalSeasonCount` is not smaller than
  returned season choices, IDs are positive, names are non-whitespace, dates are intrinsically
  ordered, season groups and campaigns retain the contracted deterministic order, and setup returns
  at most 100 choices.
- [x] Register `ICampaignQueryService` to `HttpCampaignQueryService` in `Nova.Client/Program.cs`.
- [x] Add `HttpCampaignQueryServiceTests` covering exact list query construction, omitted filters,
  setup route use, successful empty and populated responses, ProblemDetails mapping, and rejection
  of empty, null, malformed, incomplete, negative-count, over-limit, and incorrectly ordered success
  payloads.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*HttpCampaignQueryServiceTests"` — discovers the client contract tests and all discovered tests pass.
- `dotnet build Nova.Client/Nova.Client.csproj` — succeeds with no errors.

### Phase Summary

Implemented the typed WebAssembly client, shared route usage, strict success-payload validation,
ProblemDetails conversion, and malformed/invalid response coverage. Client registration is wired in
the WASM composition root.

## Phase 5: Cross-Surface Verification and Handoff

Status: Complete

Suggested executor: sub-agent w/ smaller model

- [x] Search all new symbols and routes to confirm shared contracts are used by server and client,
  both DI containers are wired, both endpoint mappings are registered, and no inline duplicate route
  strings exist.
- [x] Confirm no entity, configuration, migration, campaign mutation, Razor component, dashboard, or
  campaign-team relationship changed.
- [x] Run all campaign query contract, service, endpoint, and client tests together and confirm each
  filter discovers tests.
- [x] Run the complete unit and integration projects to catch shared route, DI, serialization,
  authorization, and tenancy regressions.
- [x] Build the complete solution and inspect `git diff --check`.
- [x] Review the final diff against issue #55 and parent #9 acceptance criteria, especially approved
  member setup access, Active-dashboard reuse, SQL bounds, deterministic ordering, live lifecycle
  counts, and the absence of N+1 queries.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*GetCampaignListInput*" --filter-class "*CampaignEndpoints*" --filter-class "*CampaignQueryServiceTests" --filter-class "*HttpCampaignQueryServiceTests"` — all focused unit tests are discovered and pass.
- `dotnet test --project Nova.Integration.Tests --filter-class "*CampaignQueryHttpTests"` — all focused HTTP tests are discovered and pass.
- `dotnet test --project Nova.Unit.Tests` — the complete unit suite passes.
- `dotnet test --project Nova.Integration.Tests` — the complete Aspire/PostgreSQL integration suite passes.
- `dotnet build Nova.slnx` — succeeds with no new warnings or errors.
- `git diff --check` — reports no whitespace errors.

### Phase Summary

Focused campaign tests pass (13 unit, 3 integration), the full delegated suites pass (791 unit,
135 integration), the solution build succeeds, and `git diff --check` is clean. Existing NuGet
vulnerability warnings remain unrelated to this change.

## Final Recap

Issue #55 is implemented as a read-only vertical query slice. Shared contracts and routes are used
by the server endpoints and typed WASM client; the server enforces approved-member access, tenant
filters, deterministic ordering, SQL-side bounds, and live lifecycle counts. Focused and complete
unit/integration verification passed. No persistence, migration, mutation, or UI work was added.

## Deployment Plan

Deploy the normal Nova solution without migration steps. The new GET routes are deployed with the
server and client binaries; existing campaign creation authorization is unchanged. After deployment,
verify an approved member can load `/api/campaigns` and `/api/campaigns/creation-setup`, while
anonymous and cross-club requests remain denied or isolated.
