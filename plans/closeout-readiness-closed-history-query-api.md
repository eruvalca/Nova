# Closeout Readiness and Closed-History Query APIs and Contracts

Deliver the read-only contracts, HTTP endpoints, and typed WASM client for closeout readiness
(summary + condition-keyed blockers with counts and assignment ids), the Overview-tab campaign
snapshot (closure fields added to the existing detail contract), recent campaign activity (bounded,
deterministically ordered lifecycle events), and proof that closed campaigns and player campaign
history remain readable by club staff. No mutations, no UI, no new entities or migrations.

Issue: https://github.com/eruvalca/Nova/issues/102 (sub-issue of epic #12). Sibling #104 owns the
close/reopen mutation slice in parallel; #101 owns the Closeout/Overview UI after this lands.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to
`Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue
with zero context); run the phase's **Verification Plan** and record the result before moving on.
When all phases are done, fill in **Final Recap** and **Deployment Plan**.

Repo conventions that govern this work (read before editing):

- `.github/instructions/api-endpoints.instructions.md` — route constants, MapGroup, static handlers,
  ToHttpResult, ProducesProblem metadata, antiforgery, [AsParameters] query binding, WASM payload rules.
- `.github/instructions/service-layer.instructions.md` — ServiceResult/ServiceProblem, dual-layer
  validation, DI registration, OneOf preference, source-generated logging.
- `.github/instructions/validation.instructions.md` — DataAnnotations on input records, InputValidator.
- `.github/instructions/ef-core-tenancy.instructions.md` — tenant-safe queries via `NovaReadDbContext`,
  club-scoped filters on every query.
- `.github/instructions/testing.instructions.md` — SQLite `TenancyTestHarness` (unit) vs Aspire Postgres
  (integration), MTP run commands, test naming (`Subject_Outcome_Condition`), Shouldly.
- `.github/instructions/functional-core.instructions.md` — `CampaignClosurePolicy` is the single source
  of readiness rules; this child never re-derives them.

Skills (step-by-step recipes): `add-api-endpoint`, `add-feature-slice`, `nova-testing`.

### Design decisions (confirmed with user 2026-08-18 — do not revisit)

1. **Snapshot = extended `CampaignDetailResult`.** Add `ClosedAt`, `ClosedByUserId`,
   `ClosedByDisplayName` to the existing detail contract and its endpoint (`GET /api/campaigns/{campaignId}`).
   No separate snapshot endpoint.
2. **Structured blocker ids come from the foundation.** Extend `CampaignCloseBlocked` with internal
   structured assignment-id collections populated by `CampaignClosurePolicy.Evaluate`. Never re-derive
   per-condition ids in the query service, and never parse them out of message strings.
3. **Readiness counts compose #11's contract.** The readiness query service injects
   `ICampaignPlacementQueryService` and embeds the returned `CampaignPlacementSummaryDto` verbatim.
4. **Activity bound = 50 newest events**, ordered `CreatedAt` desc then `CampaignLifecycleEventId` desc,
   each row: event id, event type, timestamp, actor user id, actor display name.

### Key assumptions (stated, not yet confirmed — flag if wrong)

- Auth for all new reads: `Policies.RequireClubMember` (matches every existing campaign read endpoint).
- Readiness is returned for Closed campaigns too (policy naturally yields zero blockers); presentation
  differences are #101's concern, not this child's.
- "Final outcomes" count shown on the Closeout screen is derived by the UI as
  `TotalCount - UndecidedCount`; the contract only embeds the five authoritative counts.
- Actor display name = `"{FirstName} {LastName}"` from the club-scoped users table, falling back to
  the stable `"Former member"` text when the actor row is missing (same pattern as
  `PlayerDetailQueryService`).
- New service/file names (see phases): `CampaignCloseoutQueryService` / `HttpCampaignCloseoutQueryService`
  / `CampaignCloseoutEndpointRouteBuilderExtensions` (the `Query`-less extension name is avoided
  deliberately so #104 can own close/reopen mutation mappings without a filename collision).
- `CampaignLifecycleEventEntity.CreatedById` is stamped by `TenantSaveChangesInterceptor` from the
  authenticated user (the explicit `default` in `CampaignLifecycleService` is overridden) — activity
  rows therefore carry real actor ids.

### Coordination with sibling slices

- #104 (close/reopen mutations) edits `CampaignEndpoints.cs`, `Nova/Program.cs`,
  `Nova.Client/Program.cs` in parallel. Keep additions in this child grouped (own region/order-stable
  positions), never touch close/reopen route constants, and expect trivial merge conflicts in those
  three shared files — resolve by keeping both sides' additions.
- #101 (UI) consumes these contracts later; do not change contract shapes after Phase 2 without a note.

### Acceptance criteria mapping

| # | Criterion | Where proven |
|---|-----------|--------------|
| 1 | Counts come from #11's summary contract; blockers come from the foundation, never recomputed | Phase 2 service composition + Phase 1 id-collection extension; unit tests |
| 2 | Every participant count accurate and independent of paging/filters | Phase 2 readiness DTO embeds `CampaignPlacementSummaryDto` (grouped whole-campaign query from #11); unit + HTTP tests |
| 3 | Closed campaigns and player history readable by evaluators and administrators; no cross-tenant disclosure | Phase 5 audit + tests |
| 4 | Recent activity bounded and deterministically ordered | Phase 3 (limit 50, CreatedAt desc, id desc) + client ordering validation |
| 5 | Contracts/endpoints/clients match repo route/validation/ProblemDetails conventions | Phases 2–4 follow instruction files; HTTP tests per endpoint |

---

## Phase 1: Foundation blocker-id exposure

Status: Complete

Suggested executor: orchestrator (tiny, policy-semantics-sensitive change)

- [x] In `Nova/Features/Campaigns/CampaignClosurePolicy.cs`: make the condition-key constants
      (`OutcomeBlockerKey`, `EligibilityBlockerKey`, `ArchivedTeamBlockerKey`) `internal const` instead
      of `private const`, and move the three literal values into a shared holder in `Nova.Shared`
      (`Nova.Shared/Features/Campaigns/CloseoutBlockerConditions.cs`:
      `Outcomes = "outcomes"`, `Eligibility = "eligibility"`, `ArchivedTeams = "archivedTeams"` as
      `public const string`), so policy and DTO mapping share one source of truth.
- [x] Extend `CampaignCloseBlocked` in `Nova/Features/Campaigns/CampaignClosurePolicy.cs` with three
      `internal IReadOnlyList<long> … { get; init; }` properties: `UndecidedAssignmentIds`,
      `IneligibleAssignmentIds`, `ArchivedTeamAssignmentIds`.
- [x] In `CampaignClosurePolicy.Evaluate`: capture the undecided assignment ids (currently only counted),
      and pass the already-computed `ineligibleAssignments` / `archivedTeamAssignments` arrays into the
      `CampaignCloseBlocked` initializer instead of only embedding them in message strings. Keep the
      human-readable messages, `Detail`, counts, and the `outcomes`/`eligibility`/`archivedTeams` dictionary
      keys byte-for-byte identical — this is additive exposure, not behavior change.
- [x] Extend `Nova.Unit.Tests/Campaigns/CampaignClosurePolicyTests.cs`: assert each id collection is
      empty when the condition passes and contains exactly the expected assignment ids (including
      undecided rows) when blocked; assert ordering is stable (ascending by assignment id, matching the
      current `Select` order); assert existing message text is unchanged.

### Verification Plan

- `dotnet build Nova.slnx` — zero errors.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignClosurePolicyTests"` — all green.

### Phase Summary

Complete. `CampaignClosurePolicy` now consumes the shared `CloseoutBlockerConditions` constants from
`Nova.Shared` and exposes three new internal `IReadOnlyList<long>` id collections
(`UndecidedAssignmentIds`, `IneligibleAssignmentIds`, `ArchivedTeamAssignmentIds`) on
`CampaignCloseBlocked`. `Evaluate` captures undecided ids (previously only counted) and passes all
three id arrays into the initializer; messages, `Detail`, counts, and dictionary keys are byte-for-byte
unchanged — additive exposure, not a behavior change. Tests assert empty collections on pass, exact
ascending ids on block (including undecided rows), and unchanged message text.
Verification: `dotnet build Nova.slnx` 0 errors; `--filter-class "*CampaignClosurePolicyTests"` 10/10 green.

---

## Phase 2: Closeout-readiness vertical slice (contract → service → endpoint → WASM client → tests)

Status: Complete

Suggested executor: sub-agent w/ smaller model (shapes fully fixed below; orchestrator reviews
contract fidelity before merge)

### Contract (fixed shape — implement exactly)

`Nova.Shared/Features/Campaigns/CampaignCloseoutContracts.cs`:

```csharp
/// Condition-keyed close blocker: the condition key, affected count, and the
/// affected campaign-assignment ids for the unresolved-player drill-down.
public sealed record CampaignCloseoutBlockerDto(
    string Condition,
    int Count,
    IReadOnlyList<long> AssignmentIds,
    string Message);

/// Authoritative closeout readiness: #11's placement summary plus the foundation
/// policy verdict and condition-keyed blockers. Counts and blockers are never
/// recomputed here.
public sealed record CampaignCloseoutReadinessDto(
    long CampaignId,
    CampaignStatus Status,
    bool IsReady,
    CampaignPlacementSummaryDto Summary,
    IReadOnlyList<CampaignCloseoutBlockerDto> Blockers);
```

Input `Nova.Shared/Features/Campaigns/GetCampaignCloseoutReadinessInput.cs` — mirror
`GetCampaignPlacementSummaryInput`: `[Required] [Range(1, long.MaxValue)] required long CampaignId`.

Interface `Nova.Shared/Features/Campaigns/ICampaignCloseoutQueryService.cs`:

```csharp
Task<ServiceResult<CampaignCloseoutReadinessDto>> GetCloseoutReadinessAsync(
    GetCampaignCloseoutReadinessInput input, CancellationToken cancellationToken = default);
Task<ServiceResult<CampaignActivityResult>> GetActivityAsync(
    GetCampaignActivityInput input, CancellationToken cancellationToken = default);
```

- [x] Add `CloseoutBlockerConditions`, `CampaignCloseoutBlockerDto`, `CampaignCloseoutReadinessDto`
      (in the new `CampaignCloseoutContracts.cs`), `GetCampaignCloseoutReadinessInput`,
      `GetCampaignActivityInput` (see Phase 3 for its shape), and `ICampaignCloseoutQueryService`
      to `Nova.Shared`. XML-doc every public member.
- [x] Add route constants to `Nova.Shared/Features/Campaigns/CampaignEndpoints.cs`:
      `GetCampaignCloseoutReadiness = $"{GroupPrefix}/{{campaignId:long}}/closeout-readiness"`,
      `GetCampaignCloseoutReadinessRelative = "{campaignId:long}/closeout-readiness"`,
      `GetCampaignCloseoutReadinessRouteName = "GetCampaignCloseoutReadiness"`, URL builder
      `GetCampaignCloseoutReadinessUrl(long campaignId)`; `GetCampaignActivity`,
      `GetCampaignActivityRelative = "{campaignId:long}/activity"`,
      `GetCampaignActivityRouteName = "GetCampaignActivity"`, URL builder
      `GetCampaignActivityUrl(GetCampaignActivityInput input)` (emits only values the input contract
      accepts; omit `limit` when null).
- [x] Server service `Nova/Features/Campaigns/CampaignCloseoutQueryService.cs`
      (`IDbContextFactory<NovaReadDbContext>`, `ICurrentUserProvider`, `ICampaignPlacementQueryService`,
      `ILogger<CampaignCloseoutQueryService>`):
      - `GetCloseoutReadinessAsync`: validate via `InputValidator.Validate` → require signed-in user +
        club scope (`ServiceProblem.Forbidden`, logged) → tenant-safe existence check
        (`db.Campaigns.Any(c => c.ClubId == clubId && c.CampaignId == id)` → `NotFound`) → **compose**
        `ICampaignPlacementQueryService.GetPlacementSummaryAsync(new GetCampaignPlacementSummaryInput { CampaignId = id })`
        and propagate any problem it returns → project `CampaignAssignmentClosureState` rows with the
        exact `CampaignLifecycleService.CloseAsync` projection (tenant-scoped) →
        `CampaignClosurePolicy.Evaluate` → map `CampaignMayClose` to `IsReady = true, Blockers = []`
        and `CampaignCloseBlocked` to one `CampaignCloseoutBlockerDto` per `Errors` key using the shared
        condition constants, the matching internal id collection, and the message. Include
        `campaign.Status` from the existence check's projection.
      - Source-generated `[LoggerMessage]` for the no-club-scope rejection (Warning), mirroring
        `CampaignPlacementQueryService`.
- [x] Server endpoint `Nova/Features/Campaigns/CampaignCloseoutEndpointRouteBuilderExtensions.cs`
      (C# 14 `extension` style, mirror `CampaignPlacementEndpointRouteBuilderExtensions`): `MapGroup`
      under `CampaignEndpoints.GroupPrefix`, `.RequireAuthorization()`, `MapGet` readiness and activity
      routes with `RequireClubMember`, `.WithName`, `Produces<T>`, `ProducesValidationProblem()`,
      `ProducesProblem` for 401/403/404/500; static handlers inject the input `[AsParameters]` and
      `ICampaignCloseoutQueryService`, return `result.ToHttpResult()`.
- [x] Register server service in `Nova/Program.cs` (`AddScoped<ICampaignCloseoutQueryService,
      CampaignCloseoutQueryService>()` next to the other campaign query registrations) and call
      `app.MapCampaignCloseoutEndpoints();` beside the other campaign mappings.
- [x] WASM client `Nova.Client/Services/Campaigns/HttpCampaignCloseoutQueryService.cs`: validate input,
      GET via the shared URL builders, `ToServiceProblemAsync` on non-success, and
      `ReadRequiredJsonAsync<CampaignCloseoutReadinessDto>` with a strict validator:
      `CampaignId > 0`, `Summary` non-null and passing the #11 summary invariants
      (`Total == Assigned + NotSelected + Withdrawn + Undecided`, all `>= 0`), `Blockers` non-null with
      no null rows, per-blocker `Count >= 0`, `AssignmentIds` non-null/non-negative/unique,
      `Condition` one of the three shared constants, `IsReady == (Blockers.Count == 0)`,
      The client deliberately does NOT enforce `outcomes` blocker `Count == Summary.UndecidedCount`:
      the summary and the blocker ids come from separate reads, so a concurrent placement mutation can
      make them briefly disagree (see `.github/instructions/ef-core-tenancy.instructions.md`). Empty,
      null, or malformed success bodies → `ServiceProblem.ServerError` via `ReadRequiredJsonAsync`.
- [x] Register WASM client in `Nova.Client/Program.cs` (`AddScoped<ICampaignCloseoutQueryService,
      HttpCampaignCloseoutQueryService>()`).
- [x] Unit tests `Nova.Unit.Tests/Campaigns/CampaignCloseoutQueryServiceTests.cs` (SQLite
      `TenancyTestHarness`): may-close readiness (verdict true, zero blockers, summary counts equal
      #11's grouped query result); undecided-only, eligibility-only, archived-team-only, and
      multi-condition blocked cases with exact per-condition counts and assignment-id sets; input
      validation; not-found; forbidden without club scope; cross-tenant campaign invisible (other club
      cannot read); Closed campaign returns `IsReady` with zero blockers; summary composition proven by
      seeding assignments independently of any roster paging state.
- [x] Unit tests `Nova.Unit.Tests/Campaigns/HttpCampaignCloseoutQueryServiceTests.cs`: populated payload
      accepted; explicit nested nulls, malformed JSON, negative counts/ids, duplicate assignment ids,
      unknown condition key, `IsReady`/blockers inconsistency, and summary total mismatch each yield
      the expected `ServiceProblem`; non-success statuses map via `ToServiceProblemAsync`.
- [x] Integration tests `Nova.Integration.Tests/Http/CampaignCloseoutHttpTests.cs` (AppHost fixture):
      route registration + 200 success shape; 401 unauthenticated; 403 ordinary member vs 404
      cross-tenant (never discloses); blocked campaign readiness carries ids that match seeded rows;
      Closed-campaign readiness readable by evaluator and administrator; every declared ProblemDetails
      status asserted.

### Verification Plan

- `dotnet build Nova.slnx`
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignCloseout*"` and
  `--filter-class "*CampaignClosurePolicyTests"` — all green.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignCloseoutHttpTests"` — all green (requires local Aspire AppHost).

### Phase Summary

Complete. Added `CloseoutBlockerConditions`, `CampaignCloseoutBlockerDto`, `CampaignCloseoutReadinessDto`,
`GetCampaignCloseoutReadinessInput`, `ICampaignCloseoutQueryService`, and the readiness route
constants/builders. Server `CampaignCloseoutQueryService.GetCloseoutReadinessAsync` validates, authorizes
(signed-in + club scope, logged), performs a tenant-safe existence check, composes
`ICampaignPlacementQueryService.GetPlacementSummaryAsync` verbatim, projects tenant-scoped
`CampaignAssignmentClosureState` rows (ordered by assignment id for deterministic blocker ids), evaluates
`CampaignClosurePolicy`, and maps the verdict to the DTO using the shared constants. Endpoint
`CampaignCloseoutEndpointRouteBuilderExtensions` maps readiness+activity with the full metadata block.
WASM `HttpCampaignCloseoutQueryService` validates input and strictly validates success payloads. DI
registered in both `Program.cs`. Unit, HTTP, and Postgres integration tests all green.

---

## Phase 3: Recent-activity vertical slice

Status: Complete

Suggested executor: sub-agent w/ smaller model (start after Phase 2 lands to avoid shared-file edit
races in `CampaignEndpoints.cs`, `Nova/Program.cs`, `Nova.Client/Program.cs`)

### Contract (fixed shape)

`Nova.Shared/Features/Campaigns/CampaignActivityContracts.cs`:

```csharp
/// One append-only lifecycle event in a campaign's bounded activity feed.
public sealed record CampaignActivityItemDto(
    long CampaignLifecycleEventId,
    CampaignLifecycleEventType EventType,
    DateTimeOffset CreatedAt,
    long ActorUserId,
    string ActorDisplayName);

/// Bounded, deterministically ordered recent lifecycle activity for one campaign.
public sealed record CampaignActivityResult(
    IReadOnlyList<CampaignActivityItemDto> Events);
```

Input `GetCampaignActivityInput`: `[Required] [Range(1, long.MaxValue)] required long CampaignId`;
`int? Limit` (`[Range(1, CampaignActivityResult.MaxEventCount)]`); `public const int MaxEventCount = 50`,
`DefaultLimit = 50` (put the consts on the input or result record — pick `GetCampaignActivityInput` to
match `GetCampaignPlacementRosterInput` precedent).

- [x] Add the contracts and input to `Nova.Shared` (interface method already declared in Phase 2).
- [x] `CampaignCloseoutQueryService.GetActivityAsync`: validate → authorize (signed-in + club scope,
      logged) → tenant-safe existence check → query `db.CampaignLifecycleEvents` where
      `ClubId == clubId && CampaignId == input.CampaignId`, `OrderByDescending(CreatedAt)
      .ThenByDescending(CampaignLifecycleEventId)`, `Take(limit)` → resolve actor display names with the
      `PlayerDetailQueryService` pattern (club-scoped `db.Users` lookup, `"{FirstName} {LastName}"`,
      fallback `"Former member"`) → map to `CampaignActivityItemDto` list → wrap in
      `CampaignActivityResult`.
- [x] Map the activity GET route in `CampaignCloseoutEndpointRouteBuilderExtensions` (Phase 2 file) with
      the same metadata block as readiness.
- [x] `HttpCampaignCloseoutQueryService.GetActivityAsync`: validate input, GET via URL builder,
      `ReadRequiredJsonAsync<CampaignActivityResult>` validator: `Events` non-null, count `<=` requested
      limit (default 50), every row has `CampaignLifecycleEventId > 0`, valid
      `CampaignLifecycleEventType` (Closed/Reopened), `ActorUserId > 0`, non-whitespace
      `ActorDisplayName`, and adjacent rows satisfy the portable ordering check — equal `CreatedAt`
      ⇒ non-increasing `CampaignLifecycleEventId` (mirror `HttpCampaignPlacementQueryService.IsOrdered`
      precedent; `CreatedAt` desc itself is ordinal-comparable).
- [x] Unit tests `Nova.Unit.Tests/Campaigns/CampaignActivityQueryServiceTests.cs`: bound honored
      (seed 60 events, expect 50 newest), deterministic ordering incl. equal-timestamp id tie-break,
      actor display-name resolution + missing-actor fallback, tenant isolation (other club's campaign
      invisible), not-found, forbidden without club scope, input validation, Closed campaign readable.
- [x] Unit tests `Nova.Unit.Tests/Campaigns/HttpCampaignActivityQueryServiceTests.cs`: populated payload,
      nested nulls, malformed JSON, zero/negative event id, invalid enum value, ordering violation,
      over-bound payload → each the expected `ServiceProblem`/server-error.
- [x] Integration tests (extend `CampaignCloseoutHttpTests.cs` or add
      `CampaignActivityHttpTests.cs`): 200 bounded ordered shape after seeding close+reopen events via
      `CampaignLifecycleService`, 401/403/404 behavior, evaluator readability.

### Verification Plan

- `dotnet build Nova.slnx`
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignActivity*"` — all green.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignCloseoutHttpTests"` and `"*CampaignActivityHttpTests"` — all green.

### Phase Summary

Complete. Added `CampaignActivityItemDto`, `CampaignActivityResult`, `GetCampaignActivityInput`
(`MaxEventCount=50`, `DefaultLimit=50`), and the activity route constants/builders (limit omitted when
null). `CampaignCloseoutQueryService.GetActivityAsync` validates, authorizes, performs a tenant-safe
existence check, and queries bounded events ordered `CreatedAt` desc then id desc — SQL-side for
PostgreSQL, with an isolated in-memory fallback for SQLite (documented in code, since SQLite cannot
translate `DateTimeOffset` ORDER BY). Actor display names resolve club-scoped with the `"Former member"` fallback.
WASM client validates bound/ordering/rows. Unit, HTTP, and Postgres integration tests all green
(including close+reopen ordering seeded via `CampaignLifecycleService`).

---

## Phase 4: Overview snapshot — extend campaign detail with closure fields

Status: Complete

Suggested executor: sub-agent w/ smaller model (mechanical, shape fixed)

- [x] Extend `Nova.Shared/Features/Campaigns/CampaignDetailResult.cs` with
      `DateTimeOffset? ClosedAt { get; init; }`, `long? ClosedByUserId { get; init; }`,
      `string? ClosedByDisplayName { get; init; }` (nullable, non-required — additive).
- [x] `Nova/Features/Campaigns/CampaignQueryService.GetCampaignDetailAsync`: include `ClosedAt` and
      `ClosedById` in the existing projection and resolve `ClosedByDisplayName` via a second club-scoped
      `db.Users` lookup (fallback `"Former member"`), reusing the `PlayerDetailQueryService` name pattern.
- [x] `Nova.Client/Services/Campaigns/HttpCampaignQueryService.IsValidCampaignDetail`: add closure
      invariants — `Status == Closed` ⇒ `ClosedAt != null && ClosedByUserId > 0 && non-empty
      ClosedByDisplayName`; `Status == Active` ⇒ `ClosedAt == null && ClosedByUserId == null`.
- [x] Update `Nova.Unit.Tests/Campaigns/CampaignQueryServiceTests.cs` (detail cases) and
      `Nova.Unit.Tests/Campaigns/HttpCampaignQueryServiceTests.cs`: closed campaign returns populated
      closure fields with resolved display name; active campaign returns nulls; missing actor row falls
      back to `"Former member"`; tenant isolation unchanged; client validator accepts the valid closed
      payload and rejects Active-with-ClosedAt / Closed-without-ClosedAt payloads.
- [x] Integration: extend the existing campaign-detail HTTP tests (or `CampaignCloseoutHttpTests.cs`)
      asserting a Closed campaign's detail response carries `closedAt` and `closedByUserId` after a
      close via `CampaignLifecycleService`.

### Verification Plan

- `dotnet build Nova.slnx`
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignQueryServiceTests"` and `--filter-class "*HttpCampaignQueryServiceTests"` — all green.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignDetailHttpTests"` — all green.

### Phase Summary

Complete. `CampaignDetailResult` gained nullable `ClosedAt`, `ClosedByUserId`, `ClosedByDisplayName`.
`CampaignQueryService.GetCampaignDetailAsync` now projects `ClosedAt`/`ClosedById` and resolves
`ClosedByDisplayName` via a second club-scoped `db.Users` lookup (`"Former member"` fallback, mirroring the
`PlayerDetailQueryService` name pattern). `HttpCampaignQueryService.IsValidCampaignDetail` enforces
`Closed ⇒ ClosedAt != null && ClosedByUserId > 0 && non-empty ClosedByDisplayName` and
`Active ⇒ ClosedAt == null && ClosedByUserId == null`. Unit + HTTP tests cover closed-populated,
active-nulls, missing-actor `"Former member"` fallback, tenant isolation, and client rejections; the
`CampaignCloseoutHttpTests` integration asserts a Closed detail response carries `closedAt` and
`closedByUserId` after a close via `CampaignLifecycleService`.

---

## Phase 5: Closed-history readability audit and coverage

Status: Complete

Suggested executor: orchestrator (cross-cutting judgment; do not delegate)

Goal: prove acceptance criterion 3 without adding endpoints. The audit established (2026-08-18) that no
campaign **read** path gates on `CampaignStatus.Closed` — closed-gates exist only in the mutation
services (`CampaignPlacementPolicy`, `CampaignMetadataService`, `CampaignTagApplicationService`,
`EvaluationNoteService`), which is the intended freeze. This phase proves the reads keep working and
guards against future regressions.

- [x] Re-verify the audit on the current branch: grep `Nova/Features/Campaigns/*.cs` and
      `Nova/Features/Players/*.cs` for `Status == CampaignStatus.Closed` — every hit must be in a
      mutation service/policy, never in a query path. Record the finding in the Phase Summary.
- [x] Unit tests: for each of detail, placement roster, placement summary, closeout readiness, activity,
      and player detail — seed one Closed campaign plus one Active campaign, then assert an evaluator
      (`fixture.UseUser` with club-member claims) and an administrator can read the Closed campaign's
      data with identical shapes to the Active one, and that another club's Closed campaign is
      invisible (404) and never appears in lists/player history.
- [x] Player history coverage: seed a player participating in a Closed campaign and assert
      `PlayerDetailQueryService` returns the `PlayerCampaignHistoryDto` row with
      `CampaignStatus == Closed` and its outcome/team/notes intact (evaluator + admin).
- [x] Campaign list coverage: assert `GetCampaignListAsync` with `status=closed` returns the Closed
      campaign (and only tenant-visible ones), ordered per the existing list contract.
- [x] Integration tests (Postgres): one evaluator-scoped HTTP pass reading closed-campaign detail,
      roster, summary, readiness, and activity end to end; one admin pass; one cross-tenant pass
      asserting 404 with no data disclosure; extend `PlayerDetailHttpTests` for closed-campaign history.

### Verification Plan

- `dotnet build Nova.slnx`
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` (full project) — all green.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` (full project) — all green (local Aspire).

### Phase Summary

Complete. Audit re-verified on the current branch: every `Status == CampaignStatus.Closed` hit in
`Nova/Features/Campaigns/*.cs` and `Nova/Features/Players/*.cs` lives in a mutation service/policy
(`CampaignLifecycleService`, `CampaignMetadataService`, `CampaignPlacementPolicy`,
`CampaignTagApplicationService`, `EvaluationNoteService`) — none in a query path. Added
`ClosedCampaignReadabilityTests` proving an evaluator and an administrator read a Closed campaign with
identical shapes across detail, placement roster, placement summary, closeout readiness, activity, and
player detail, and that another club's Closed campaign returns 404 and never appears in lists/history.
Extended `PlayerDetailHttpTests` to assert the closed campaign history row carries
`CampaignStatus == Closed` with outcome intact, and covered evaluator/admin/cross-tenant over HTTP in
`CampaignCloseoutHttpTests`.

---

## Phase 6: Final gate — format, full suites, PR

Status: Complete

Suggested executor: orchestrator

- [x] `dotnet format Nova.slnx` (apply) then `dotnet format Nova.slnx --verify-no-changes` — clean.
- [x] `dotnet build Nova.slnx` — zero warnings/errors.
- [x] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full green.
- [x] `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — full green locally
      (CI only runs build + unit tests; integration must be proven before merge).
- [x] Do NOT run `Nova.Browser.Tests` additions here (UI is #101; this child has no UI surface).
- [x] Cross-check issue acceptance criteria 1–5 against the delivered contracts/tests; tick the
      mapping table at the top of this plan.
- [x] Update the "Current readiness" section of epic #12 (via a comment on #12, not an edit of the
      issue body) summarizing what this child delivered and that #101 can start composing the clients.
- [x] Open PR from `eruvalca-closeout-readiness-closed-history-query` to `main` with issue link
      `Closes #102` (or `Refs #102` if the epic tracks closure), body summarizing the four read
      surfaces, and the standard Co-authored-by trailer.

### Verification Plan

- All commands above pass on the final commit; PR CI (build + unit tests) green.

### Phase Summary

Complete. `dotnet format Nova.slnx` applied and `--verify-no-changes` clean (0 of 606 files changed).
`dotnet build Nova.slnx` 0 warnings/0 errors. Full unit suite 1577/1577 green; full integration suite
276/276 green (Aspire PostgreSQL). Browser tests intentionally not run (no UI scope). Acceptance criteria
1–5 cross-checked against delivered contracts/tests. Epic #12 comment posted and PR opened to `main` with
`Closes #102`.

---

## Final Recap

Delivered the four read-only surfaces of issue #102 end to end with no mutations, no UI, and no new
entities/migrations:

1. **Foundation blocker-id exposure** — shared `CloseoutBlockerConditions` constants plus internal
   `CampaignCloseBlocked` id collections populated by `CampaignClosurePolicy.Evaluate` (additive; messages
   unchanged).
2. **Closeout readiness** — `CampaignCloseoutReadinessDto`/`CampaignCloseoutBlockerDto` contract,
   `CampaignCloseoutQueryService` composing `ICampaignPlacementQueryService` and the closure policy,
   `GET /api/campaigns/{campaignId}/closeout-readiness`, WASM `HttpCampaignCloseoutQueryService`, DI, and
   full unit/HTTP/Postgres coverage.
3. **Recent activity** — bounded (50), deterministically ordered (CreatedAt desc then id desc)
   `CampaignActivityResult` at `GET /api/campaigns/{campaignId}/activity`, with actor display-name
   resolution and an isolated SQLite fallback for the PostgreSQL ordering path.
4. **Overview snapshot** — `ClosedAt`/`ClosedByUserId`/`ClosedByDisplayName` added to the existing
   `CampaignDetailResult` and endpoint, with client closure invariants.

Phase 5 re-verified the closed-history readability audit (all closed-gates live in mutation
services/policies, never query paths) and added evaluator/admin/cross-tenant unit + Postgres coverage,
including player campaign history and `status=closed` list readability. All repo instruction files and
skills (`add-feature-slice`, `add-api-endpoint`, `nova-testing`) were followed.

## Deployment Plan

1. Merge the PR for issue #102 to `main` (CI runs build + unit tests only).
2. Integration (Postgres/Aspire) and browser suites are proven locally before merge; no new
   infrastructure, entities, or migrations are introduced.
3. Deploy through the existing pipeline (no schema change, no feature flag required).
4. After merge, #101 (Closeout/Overview UI) can start composing `ICampaignCloseoutQueryService`,
   `CampaignDetailResult` closure fields, and the activity contract. #104 (close/reopen mutations)
   continues in parallel and should keep its additions; the three shared files (`CampaignEndpoints.cs`,
   `Nova/Program.cs`, `Nova.Client/Program.cs`) were edited with grouped, order-stable additions and no
   close/reopen route constants were touched.
