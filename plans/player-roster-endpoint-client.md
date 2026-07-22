# Issue 32: Player Roster Endpoint and Client

Implement backend and WASM client support for querying a club player roster with filtering, sorting, and pagination, following Nova endpoint/service/result conventions.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to `Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue with zero context); run the phase's **Verification Plan** and record the result before moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

## Phase 1: Baseline and Pattern Alignment

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [x] Locate and align with existing players feature conventions across `Nova.Shared`, `Nova`, and `Nova.Client`.
- [x] Identify existing paged DTO/result type to reuse for roster responses.
- [x] Identify existing validation and ServiceResult-to-HTTP conversion patterns for query endpoints.

### Verification Plan

- Inspect existing player/list endpoints and confirm the same route, handler, and result conversion patterns will be followed.
- Confirm an existing reusable paged response DTO (or document that a new equivalent must be introduced).

### Phase Summary

Mapped the roster slice to existing Nova conventions (`MapGroup`, static handlers, `ToHttpResult`, dual-layer validation). Confirmed no reusable generic paging contract existed in `Nova.Shared`, so a new shared `PagedResult<TItem>` contract was added.

## Phase 2: Shared Contracts and Server Endpoint Surface

Status: Complete

Suggested executor: sub-agent w/ smaller model

- [x] Add `Nova.Shared/Features/Players/GetPlayerRosterEndpoints.cs` with route constants for `/api/clubs/{clubId:long}/players/roster`.
- [x] Add `Nova.Shared/Features/Players/GetPlayerRosterInput.cs` with required `ClubId`, optional `Search`, `SortBy`, `SortDirection`, `Page` (default 1), and `PageSize` (default 20).
- [x] Add endpoint mapping/handler in `Nova/Features/Players/PlayerRosterEndpointRouteBuilderExtensions.cs` using existing API conventions for validation and problem details.

### Verification Plan

- Build: `dotnet build Nova/Nova.csproj`
- Confirm endpoint metadata reflects expected route, auth/antiforgery conventions, and problem response wiring used by neighboring player endpoints.

### Phase Summary

Added shared route/input contracts under `Nova.Shared/Features/Players` and mapped a new minimal GET endpoint at `/api/clubs/{clubId:long}/players/roster` with club-member authorization, validation-problem metadata, and `ServiceResult` HTTP conversion.

## Phase 3: Service + Query Logic + Client Wiring

Status: Complete

Suggested executor: orchestrator

- [x] Extend `IPlayerService` with a roster query method returning the shared paged roster shape.
- [x] Implement service logic in `PlayerService` with:
- [x] Active-members-only filtering.
- [x] Case-insensitive contains `Search` matching.
- [x] Default sort `displayName asc`, plus `displayName|joinedAt` and `asc|desc`.
- [x] Pagination defaults (`page=1`, `pageSize=20`) and max `pageSize=100`.
- [x] Add `Nova.Client/Services/HttpPlayerService` method to call roster endpoint and return the shared result type.

### Verification Plan

- Build: `dotnet build Nova.Client/Nova.Client.csproj`
- Run targeted server/service tests for the roster query behavior and confirm expected filtering/sorting/pagination outcomes.

### Phase Summary

Implemented server `PlayerService` with tenant/club authorization and the agreed filter/sort/paging behavior, wired server DI + endpoint mapping in `Nova/Program.cs`, and added WASM client wiring via `HttpPlayerService` plus `Nova.Client/Program.cs` DI registration.

## Phase 4: Tests, Integration Checks, and Completion

Status: Complete

Suggested executor: sub-agent w/ smaller model

- [x] Add targeted automated tests for input validation, endpoint behavior, and service query semantics.
- [x] Ensure problem details + trace/validation behavior matches existing conventions.
- [x] Run focused test commands for changed areas and resolve failures.
- [x] Update this plan with completed phase summaries, final recap, and deployment steps.

### Verification Plan

- Test: `dotnet test Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter "FullyQualifiedName~PlayerRoster|FullyQualifiedName~GetPlayerRoster"`
- If endpoint integration tests are touched: `dotnet test Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter "FullyQualifiedName~PlayerRoster|FullyQualifiedName~GetPlayerRoster"`

### Phase Summary

Added focused unit coverage for shared contracts (`GetPlayerRosterContractTests`), server roster behavior (`PlayerServiceTests`), and WASM client HTTP wiring (`HttpPlayerServiceTests`). Verified by targeted unit tests plus `Nova` and `Nova.Client` builds.

## Final Recap

Added an end-to-end player roster slice across shared contracts, server endpoint/service, and WASM client:

- New shared feature contracts in `Nova.Shared/Features/Players`: endpoint constants + URL builder, validated query input, player list item DTO, and `IPlayerService`.
- New shared `PagedResult<TItem>` in `Nova.Shared/Results`.
- New server roster API mapping (`PlayerRosterEndpointRouteBuilderExtensions`) exposed at `/api/clubs/{clubId:long}/players/roster`.
- New server `PlayerService` implementing active-only roster filtering, case-insensitive contains search, sortable `displayName|joinedAt` with `asc|desc`, and paging defaults/cap (`1/20`, max `100`).
- New WASM client `HttpPlayerService` plus DI registration in both server and client `Program.cs`.
- Added targeted tests covering contracts, service behavior, and client endpoint calls.

## Deployment Plan

1. Deploy this branch through the normal CI pipeline.
2. Ensure the server and client artifacts are published together (the new client service depends on the new server route contract).
3. After deployment, smoke-test `GET /api/clubs/{clubId}/players/roster` as an authenticated club member with:
   - default query (no params),
   - `search`,
   - `sortBy=joinedAt&sortDirection=desc`,
   - `page`/`pageSize` variations.
4. Confirm the endpoint returns paged payloads and expected 403 behavior for users outside the requested club.
