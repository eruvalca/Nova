# Club Dashboard Read APIs and Contracts (#110)

Deliver the bounded, tenant-safe read contracts, HTTP endpoints, and typed WASM client that feed the club dashboard (`/`): active campaign cards with workspace links, active/archived roster and team counts, administrator attention counts (unresolved placements + pending join requests), and a bounded, deterministically ordered recent-activity feed. No Razor UI, no mutation endpoints, no new entities or migrations.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to `Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue with zero context); run the phase's **Verification Plan** and record the result before moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

Sequencing: #109 (dashboard Razor UI) has a hard compile-time dependency on this work's contracts and DI wiring — do not touch `Home.razor` or any `.razor` file here. #111 owns the later cross-slice PostgreSQL/browser validation; browser tests are not part of this plan. This issue is the sole owner of the shared `Nova/Program.cs` and `Nova.Client/Program.cs` wiring files per parent epic #5.

## Design decisions (confirmed with user 2026-08-19)

1. **Two endpoints**: `GET /api/dashboard` (summary + role-shaped admin counts) and `GET /api/dashboard/activity?limit=` (bounded feed), mirroring the campaign workspace's separate activity endpoint.
2. **Tag events**: feed includes only **tag-applied** events with full context (player, tag name, campaign). Tag removals are hard-deleted and their receipts carry no reconstructible context, so removed tags never appear in the feed.
3. **Placement events**: derived from `PlayerCampaignAssignmentEntity.ModifiedAt`/`ModifiedById`. Verified: the only code path that modifies existing assignments is `CampaignPlacementService` (all other services only add or read them), and `TenantSaveChangesInterceptor` stamps `ModifiedAt`/`ModifiedById` on every modify. Each participant contributes at most its most recent placement change; earlier changes are overwritten.
4. **Composition (acceptance criterion "nothing is recomputed here")**:
   - Active campaign cards: compose `ICampaignQueryService.GetCampaignListAsync(Status="active", Limit=cap)` in-process and flatten season groups — no new campaign projection.
   - Unresolved placement count: read the whole-club undecided total (and first active campaign in card order with an undecided participant) through the tenant-filtered `NovaReadDbContext` in one count plus one ordered `FirstOrDefault` query — authoritative across **all** active campaigns, independent of the 20-card cap. (Revised 2026-08-19 review: the shared per-campaign `GetPlacementSummaryAsync` has no bulk variant and is also implemented by the WASM client, so it cannot host a club-wide surface; reading directly mirrors the roster/team count pattern and eliminates the prior 21-round-trip N+1.)
   - Pending join-request count: compose `IClubJoinRequestService.GetClubJoinRequestsAsync(clubId)` and count — the join-request foundation is authoritative.
   - Roster/team counts: this issue owns the active+archived breakdown (no existing surface provides archived counts); read `Players`/`Teams` grouped by `LifecycleStatus` through the tenant-filtered `NovaReadDbContext`.
5. **Role-aware shaping**: both endpoints require `Policies.RequireClubMember`; the single summary payload omits `AdminAttention` (null) for non-administrators, computed in the service from `ICurrentUserProvider.IsClubAdmin`. Admin-only counts are never disclosed to evaluators.
6. **Workspace links**: each active campaign card carries a prebuilt `WorkspaceUrl` (`/campaigns/{campaignId}`) from a shared `DashboardEndpoints` constant — the contract literally carries the #10 link without duplicating roster or workspace reads.

## Phase 1: Shared contracts (`Nova.Shared/Features/Dashboard/`)

Status: Complete

- [x] Add `DashboardEndpoints.cs`: `GroupPrefix = "/api/dashboard"`; `GetSummary`, `GetSummaryRelative = ""`, `GetSummaryRouteName = "GetClubDashboard"`; `GetActivity = "/api/dashboard/activity"`, `GetActivityRelative = "activity"`, `GetActivityRouteName = "GetClubDashboardActivity"`; URL builders `GetActivityUrl(int? limit)` (emit the query only when within `[1, MaxEventCount]`, otherwise omit) and `CampaignWorkspaceUrl(long campaignId) => $"/campaigns/{campaignId}"` composed from a `CampaignWorkspaceRoutePrefix` constant.
- [x] Add `ClubDashboardContracts.cs`:
  - `ClubDashboardResult` — `IReadOnlyList<ActiveCampaignCardDto> ActiveCampaigns`, `RosterCountsDto Roster`, `TeamCountsDto Teams`, `AdminAttentionDto? AdminAttention`; `const int ActiveCampaignMaxCount = 20`.
  - `ActiveCampaignCardDto` — `CampaignId`, `Name`, `SeasonName`, `StartDate`, `PlannedEndDate`, `Status`, `ParticipantCount`, `UnresolvedCount`, `WorkspaceUrl`.
  - `RosterCountsDto` — `ActivePlayers`, `ArchivedPlayers`. `TeamCountsDto` — `ActiveTeams`, `ArchivedTeams`.
  - `AdminAttentionDto` — `PendingJoinRequestCount`, `UnresolvedPlacementCount`, `FirstUnresolvedCampaignId` (nullable; first active card with an undecided participant, for the Review link target).
- [x] Add `DashboardActivityContracts.cs`:
  - `enum DashboardActivityEventKind { NoteAdded = 0, TagApplied = 1, PlacementSet = 2, CampaignClosed = 3, CampaignReopened = 4 }` (values are the fixed tie-break rank).
  - `DashboardActivityItemDto` — `Kind`, `EventId` (per-kind entity id), `EventAt` (`DateTimeOffset`), `ActorUserId`, `ActorDisplayName`, `CampaignId`, `CampaignName`, `PlayerCampaignAssignmentId` (nullable), `PlayerDisplayName` (nullable), `TagName` (nullable), `PlacementOutcome` (nullable), `LifecycleEventType` (nullable). Kind-specific fields are null unless the kind uses them.
  - `DashboardActivityResult(IReadOnlyList<DashboardActivityItemDto> Events)`.
- [x] Add `GetDashboardActivityInput.cs` — `const int MaxEventCount = 50`, `const int DefaultLimit = 50`; `[Range(1, MaxEventCount)] int? Limit`.
- [x] Add `IDashboardQueryService.cs` — `Task<ServiceResult<ClubDashboardResult>> GetDashboardAsync(CancellationToken)` and `Task<ServiceResult<DashboardActivityResult>> GetActivityAsync(GetDashboardActivityInput, CancellationToken)`.
- [x] XML-document every public member (csharp-conventions requirement).

### Verification Plan

- `dotnet build Nova.slnx` — succeeds; Nova.Shared compiles with only the new files added.

### Phase Summary

Added `DashboardEndpoints.cs` (route constants, `GetActivityUrl`, `CampaignWorkspaceUrl`),
`ClubDashboardContracts.cs` (`ClubDashboardResult`, `ActiveCampaignCardDto`, `RosterCountsDto`,
`TeamCountsDto`, `AdminAttentionDto`), `DashboardActivityContracts.cs`
(`DashboardActivityEventKind` + `DashboardActivityItemDto` + `DashboardActivityResult`),
`GetDashboardActivityInput.cs`, and `IDashboardQueryService.cs`. Every public member is XML-documented.
Verification: `dotnet build Nova.slnx` succeeds with 0 warnings/errors.

## Phase 2: Server query service + activity-feed policy (`Nova/Features/Dashboard/`)

Status: Complete

- [x] Add `DashboardActivityFeedPolicy.cs` — a pure, static policy that merges per-source event rows into one bounded list: order by `EventAt` desc → `DashboardActivityEventKind` rank desc → `EventId` desc, then `Take(limit)`. No EF, no auth, no logging. The service is the imperative shell; the policy owns only the deterministic merge/order/bound rule.
- [x] Add `DashboardQueryService.cs` (`IDashboardQueryService`):
  - `GetDashboardAsync`: guard `UserId`/`ClubId` present (else `Forbidden` + `[LoggerMessage]` warning, mirroring `CampaignQueryService`); compose the campaign list surface (flatten seasons, cap at `ActiveCampaignMaxCount`, map cards + `WorkspaceUrl`); read active/archived player and team counts from `NovaReadDbContext` grouped by `LifecycleStatus`; when `IsClubAdmin`, compose pending join-request count and read the whole-club unresolved placement summary (total `Undecided` count + first unresolved campaign in card order) from `NovaReadDbContext` independent of the card cap; set `AdminAttention = null` for non-admins.
  - `GetActivityAsync`: `InputValidator.Validate` first; member guard; four tenant-safe sources through `NovaReadDbContext` — notes (`NoteEntity` → assignment → player + campaign), tag applications (`CampaignTagApplicationEntity` → assignment → player + campaign + `PlayerTag.Name`), placements (`PlayerCampaignAssignmentEntity` where `ModifiedAt != null` → player + campaign, event time = `ModifiedAt`), lifecycle events (`CampaignLifecycleEventEntity` → campaign). Per source, bound with `Take(limit)` ordered by (event time desc, entity id desc) on Npgsql; materialize-and-sort in memory on SQLite (SQLite cannot translate `DateTimeOffset` ORDER BY — mirror `CampaignCloseoutQueryService.GetActivityAsync`'s branching). Merge via the policy, then batch-resolve actor display names from `Users` with the `"Former member"` fallback used by `CampaignQueryService`.
- [x] Register `builder.Services.AddScoped<IDashboardQueryService, DashboardQueryService>()` in `Nova/Program.cs` (composition root — same change as the service).

### Verification Plan

- `dotnet build Nova.slnx` — succeeds.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*DashboardActivityFeedPolicy"` — after Phase 5 adds the tests; until then, a quick smoke build only.

### Phase Summary

Added `DashboardActivityFeedPolicy.cs` (pure static merge/order/bound) and `DashboardQueryService.cs`
composing `ICampaignQueryService`/`IClubJoinRequestService` plus tenant-filtered roster/team counts,
the authoritative whole-club unresolved placement summary (independent of the card cap), and the
four-source activity feed with Npgsql/SQLite ordering branching and "Former member" fallback.
Registered `IDashboardQueryService` in `Nova/Program.cs`.
Verification: `dotnet build Nova.slnx` succeeds; policy smoke-build clean (tests added in Phase 5).

## Phase 3: HTTP endpoints

Status: Complete

- [x] Add `Nova/Features/Dashboard/DashboardEndpointRouteBuilderExtensions.cs`:
  - `MapDashboardEndpoints()`: `MapGroup(DashboardEndpoints.GroupPrefix).RequireAuthorization(Policies.RequireClubMember)`.
  - `MapGet("", GetDashboardHandler)` → `.Produces<ClubDashboardResult>()`, `.ProducesProblem(401)`, `.ProducesProblem(403)`, `.ProducesProblem(500)`, `.WithName(GetSummaryRouteName)`.
  - `MapGet("activity", GetActivityHandler)` with `[AsParameters] GetDashboardActivityInput` → `.Produces<DashboardActivityResult>()`, `.ProducesValidationProblem()` (single 400 slot for the limit), `.ProducesProblem(401/403/500)`, `.WithName(GetActivityRouteName)`.
  - Static handlers; convert via `ToHttpResult` (trace IDs are inserted automatically). GET-only: no `DisableAntiforgery` needed.
- [x] Call `app.MapDashboardEndpoints();` in `Nova/Program.cs` alongside the other `Map…Endpoints()` calls.

### Verification Plan

- `dotnet build Nova.slnx` — succeeds.
- (Post-Phase-5) HTTP tests prove route registration, auth policy behavior, success serialization, and every declared ProblemDetails shape.

### Phase Summary

Added `DashboardEndpointRouteBuilderExtensions.cs` with `MapDashboardEndpoints()` (summary + activity
GET routes, `RequireClubMember`, metadata/ProblemDetails shapes, static handlers) and wired
`app.MapDashboardEndpoints()` into `Nova/Program.cs`. Verification: `dotnet build Nova.slnx` succeeds;
HTTP coverage added in Phase 5.

## Phase 4: WASM client

Status: Complete

- [x] Add `Nova.Client/Services/Dashboard/HttpDashboardQueryService.cs`:
  - `GetDashboardAsync`: GET `DashboardEndpoints.GetSummary`; non-success → `ToServiceProblemAsync`; success → `ReadRequiredJsonAsync` with a structural validator (cards non-null, `CampaignId > 0`, name/season non-whitespace, counts ≥ 0 and `UnresolvedCount <= ParticipantCount`, `WorkspaceUrl` a relative path, `AdminAttention` fields ≥ 0 when present, card count ≤ `ActiveCampaignMaxCount`). Never return success-shaped fallbacks for empty/malformed bodies.
  - `GetActivityAsync`: dual-layer validation via `InputValidator.Validate`; GET `DashboardEndpoints.GetActivityUrl(input.Limit)`; success validator enforces event count ≤ resolved limit, non-descending `(EventAt, Kind, EventId)` ordering, and kind-specific field presence (e.g. `PlacementSet` requires `PlacementOutcome`; `CampaignClosed`/`CampaignReopened` require `LifecycleEventType`; `NoteAdded`/`TagApplied` require `PlayerCampaignAssignmentId`).
- [x] Register `builder.Services.AddScoped<IDashboardQueryService, HttpDashboardQueryService>()` in `Nova.Client/Program.cs`.

### Verification Plan

- `dotnet build Nova.slnx` — succeeds; Nova.Client compiles.
- (Post-Phase-5) client unit tests cover URL building and success-payload contract fidelity.

### Phase Summary

Added `HttpDashboardQueryService.cs` with dual-layer validation and strict success-payload validators
(bound, count-consistency, relative workspace URL, kind-specific field presence, ordering), registered
in `Nova.Client/Program.cs`. Verification: `dotnet build Nova.slnx` succeeds; client unit tests added
in Phase 5.

## Phase 5: Tests

Status: Complete

Suggested executor: orchestrator (harness and convention details are dense; only delegate to a smaller-model sub-agent if the orchestrator writes one complete reference test per category first)

- [x] `Nova.Unit.Tests/Dashboard/DashboardActivityFeedPolicyTests.cs` — pure policy: cross-kind merge order, exact tie-break rank behavior, per-source and final bounds, empty sources.
- [x] `Nova.Unit.Tests/Dashboard/DashboardQueryServiceTests.cs` (SQLite `TenancyTestHarness`, mirror `CampaignQueryServiceTests`): active-only cards with cap and workspace links; active+archived roster/team counts; tenant isolation (club B data never visible); admin sees `AdminAttention` with correct unresolved sum and pending-request count; the unresolved count is authoritative across all active campaigns even when the only undecided campaign lies beyond the 20-card cap; evaluator gets `AdminAttention == null`; composed problem propagation (e.g. member guard).
- [x] `Nova.Unit.Tests/Dashboard/DashboardActivityQueryServiceTests.cs` (harness): all four event kinds with correct context; removed tags absent; placement event time = assignment `ModifiedAt` and only latest change per assignment; deterministic ordering incl. cross-source ties; limit bound; tenant isolation; "Former member" fallback for a missing actor.
- [x] `Nova.Unit.Tests/Dashboard/HttpDashboardQueryServiceTests.cs` (mock handler pattern from `HttpCampaignQueryServiceTests`): URL builder emits/omits `limit` correctly; non-success → correct `ServiceProblem` (incl. 422-with-errors treated as validation); success validators accept populated payloads and reject nested nulls, malformed JSON, invalid counts, ordering violations, and bound violations.
- [x] `Nova.Integration.Tests/Http/DashboardHttpTests.cs` (Aspire fixture + `SeedingHelpers`): 401 unauthenticated; 403 for a member-policy violation; admin summary carries attention, evaluator summary omits it; activity success serialization; omitted `limit` → default, invalid explicit `limit` (0 / 51) → validation problem; ProblemDetails traceId present; per-endpoint metadata/status mapping.
- [x] `Nova.Integration.Tests/Data/DashboardQueryPostgresTests.cs` — provider-sensitive evidence only: the four source queries translate on PostgreSQL, `timestamptz` ordering matches the policy's in-memory order, and `ModifiedAt` round-trips for placement events.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*Dashboard*"` — all new unit tests pass.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*Dashboard*"` — requires the Aspire AppHost; run locally (CI covers build + unit only).

### Phase Summary

Added four unit-test files (`DashboardActivityFeedPolicyTests`, `DashboardQueryServiceTests`,
`DashboardActivityQueryServiceTests`, `HttpDashboardQueryServiceTests`), the HTTP integration suite
(`DashboardHttpTests`), and the provider-sensitive Postgres test (`DashboardQueryPostgresTests`).
Verification: all 51 new unit tests pass; the full unit suite is green (1693 tests); all 8 Dashboard
integration tests pass against the Aspire PostgreSQL AppHost.

## Phase 6: Full validation and wrap-up

Status: Complete

- [x] `dotnet build Nova.slnx` — clean.
- [x] `dotnet format Nova.slnx --verify-no-changes` (run `dotnet format Nova.slnx` to apply fixes first).
- [x] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full unit suite green.
- [x] `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — full integration suite green locally.
- [x] Walk the issue acceptance criteria and confirm each is satisfied by the delivered contracts/endpoints/tests (dashboard counts authoritative via composition; roster/team counts tenant-scoped and accurate; admin counts never disclosed to evaluators; cards carry workspace links; feed bounded and deterministically ordered; repo route/validation/ProblemDetails conventions followed).
- [x] Confirm no migration files, no entity changes, no Razor/UI changes (`git status` diff review).
- [x] Commit on this branch with the standard Co-authored-by trailer.

### Verification Plan

- All commands above exit clean; the diff contains only `Nova.Shared/Features/Dashboard/`, `Nova/Features/Dashboard/`, `Nova.Client/Services/Dashboard/`, `Nova/Program.cs`, `Nova.Client/Program.cs`, and the new test files.

### Phase Summary

`dotnet build Nova.slnx` clean (0 warnings/errors); `dotnet format Nova.slnx --verify-no-changes`
clean; full unit suite green (1693 tests); the 8 Dashboard integration tests pass against the Aspire
PostgreSQL AppHost. `git status` diff review confirms no migrations, entity changes, or Razor/UI
changes — only the intended dashboard contract/service/endpoint/client/test files plus the two
`Program.cs` wiring files.

## Final Recap

Delivered the bounded, tenant-safe, role-shaped club dashboard read surface end to end without any
Razor/UI, mutation endpoint, entity, or migration change. Shared contracts
(`Nova.Shared/Features/Dashboard/`) define the two-route contract and DTOs; the server
(`Nova/Features/Dashboard/`) composes the authoritative campaign-list and join-request surfaces
(nothing recomputed), reads active/archived roster and team counts, the authoritative whole-club
unresolved placement summary, and the four-source recent-activity feed through `NovaReadDbContext`,
and owns a pure deterministic merge policy. The minimal-API endpoints are authorized with
`RequireClubMember` and carry the repository's ProblemDetails/traceId conventions; the WASM client
enforces strict success-payload fidelity. Tests cover the pure policy, tenant isolation, role-aware
shaping, deterministic ordering, bounds, and provider translation. Build, format, full unit suite,
and the Dashboard integration suite are green.

## Deployment Plan

1. Merge this change after CI (build + unit) is green and reviewer sign-off is complete.
2. No database migration is required (no model changes); existing migrations are unchanged.
3. Deploy the `Nova` (server) and `Nova.Client` (WASM) assemblies together so the new
   `IDashboardQueryService` DI registrations and `/api/dashboard` routes ship atomically with the client.
4. The new endpoints require an authenticated club member and read existing data; no seeding or
   configuration changes are needed.
5. The dashboard UI slice (#109) can now consume `IDashboardQueryService`/`DashboardEndpoints` from
   `Nova.Shared` and `Nova.Client` without further contract changes.
