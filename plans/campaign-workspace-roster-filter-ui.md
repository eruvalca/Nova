# Campaign Workspace — Roster Page and Filter UI (Issue #67)

Build the campaign workspace route/shell (`/campaigns/{id}`) with the Evaluate tab's
participant roster: URL-backed filters (name search, graduation year, tag, outcome, team),
deterministic sorting/paging, responsive table/card rendering, selection + drawer placeholder
with scroll-anchor preservation, and full loading/empty/error states — plus component tests
and a focused browser validation pass. The drawer's internals are #64's; #67 ships the shell
placeholder and the roster-side state contract it consumes.

Depends on #68 (merged — `ICampaignParticipantQueryService`, roster input/contracts, endpoints,
WASM client all exist).

## Scope decisions (confirmed with issue owner)

1. **Campaign header data**: no campaign-by-id read contract exists. **Scope deviation (approved):**
   add a minimal campaign-detail read contract (Phase 1).
2. **Tab shell**: build the 4-tab bar now — Evaluate functional, Overview/Placements/Closeout
   rendered as disabled placeholders ("coming soon" tooltip).
3. **Selection URL contract for #64**: `?participant={assignmentId}` query param on the workspace
   URL. Present ⇒ drawer open; absent ⇒ closed. #67 ships the drawer shell placeholder.
4. **Navigation mode**: push a history entry for **every** roster state change (filter, sort,
   page, selection, tab). Browser back/forward steps through each tweak.
5. **Grad-year filter options**: distinct years present in the roster, served by a new
   `GetRosterGraduationYearsAsync` query on `ICampaignParticipantQueryService` following the
   `GetChoicesAsync` pattern (Phase 2). **Scope deviation (approved).**
6. **Sortable columns**: Name (`displayName`), Graduation Year (`graduationYear`), Tryout #
   (`tryoutNumber`), Outcome (`outcome`), Team (`teamName`). `assignmentId` stays an internal
   server-side tiebreaker and is never exposed in the UI.
7. **Entry link**: the Campaigns list page renders the campaign name as a link to
   `/campaigns/{id}` (small addition owned by #67).

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its
status to `Complete` and write its **Phase Summary**; run the phase's **Verification Plan** and
record the result before moving on. When all phases are done, fill in **Final Recap** and
**Deployment Plan**.

Use the repo skills: `add-api-endpoint` (Phases 1–2 endpoints), `add-blazor-ui` (Phases 3–5),
`nova-testing` (all test work), `aspire-playwright-validation` (Phase 6). Follow the targeted
instruction files for the affected areas (`api-endpoints`, `blazor-architecture`, `validation`,
`ef-core-tenancy`, `testing`, `service-layer`, `functional-core`, `csharp-conventions`,
`observability`).

## URL-state contract (the shared convention #64 must consume)

Workspace page route (new): `/campaigns/{campaignId:long}`.

Query parameters (all optional; omitted when at their default so URLs stay clean):

| Param | Values | Default |
| --- | --- | --- |
| `tab` | `evaluate` (only functional value) | `evaluate` (invalid values fall back to evaluate) |
| `search` | free text, debounced 350 ms | absent |
| `graduationYears` | comma-separated ints, e.g. `graduationYears=2027,2028` | absent |
| `tagDefinitionIds` | comma-separated longs | absent |
| `outcome` | `undecided` \| `assigned` \| `notselected` \| `withdrawn` | absent |
| `teamId` | long | absent |
| `sortBy` | `displayName` \| `graduationYear` \| `tryoutNumber` \| `outcome` \| `teamName` | `displayName` |
| `sortDirection` | `asc` \| `desc` | `asc` |
| `page` | 1-based int | 1 |
| `participant` | `playerCampaignAssignmentId` (opens the drawer — #64-owned) | absent |

Rules:

- `pageSize` is fixed at `GetCampaignParticipantRosterInput.DefaultPageSize` (50) by the UI and
  is not exposed in the URL.
- Every roster state change pushes a history entry (`NavigationManager.NavigateTo` with
  `replaceHistoryItem: false`).
- The UI always sends explicit `sortBy` + `sortDirection` so server ordering is deterministic.
- Defensive parsing: unrecognized/out-of-range values in the URL fall back to defaults rather
  than surfacing server 400s.
- #64 must preserve all roster params when it changes `participant` or navigates inside the
  drawer; #67 guarantees the roster list never resets scroll when the drawer opens/closes.

## Phase 1: Minimal campaign-detail read contract

Status: Complete

Suggested executor: orchestrator (cross-cutting design; establishes the contract #64 and later
issues will consume). The WASM client and test scaffolding items are mechanical and can be
delegated to a sub-agent once the contract shape is fixed.

- [x] Add `GetCampaignDetailInput` to `Nova.Shared/Features/Campaigns/` — `required long
      CampaignId` with `[Range(1, long.MaxValue)]`, matching `GetCampaignParticipantRosterInput`
      conventions.
- [x] Add `CampaignDetailResult` to `Nova.Shared/Features/Campaigns/` — fields: `CampaignId`,
      `Name`, `Status` (`CampaignStatus`), `StartDate` (`DateOnly`), `PlannedEndDate`
      (`DateOnly?`), `ParticipantCount` (`int`), `SeasonId` (`long`), `SeasonName` (`string`).
      This is the workspace header payload; record these fields in the summary.
- [x] Extend `ICampaignQueryService` with `GetCampaignDetailAsync(GetCampaignDetailInput,
      CancellationToken)` returning `ServiceResult<CampaignDetailResult>`.
- [x] Implement in `Nova/Features/Campaigns/CampaignQueryService.cs`: tenant-safe
      (`TryGetClubId` guard + `LogCampaignDetailForbidden` source-generated warning, mirroring
      the list method), `InputValidator.Validate`, then a single projection from `db.Campaigns`
      (including `Season` nav and `PlayerAssignments.Count`) for the requested id; return
      `ServiceProblem.NotFound` when the campaign does not exist in the caller's club.
- [x] Add route constants to `CampaignEndpoints`: `GetCampaignDetail =
      $"{GroupPrefix}/{{campaignId:long}}"`, `GetCampaignDetailRelative = "{campaignId:long}"`,
      `GetCampaignDetailRouteName`, plus a `GetCampaignDetailUrl(long campaignId)` builder.
      (No route conflict: existing list/setup routes are distinct shapes, and `{campaignId:long}`
      cannot swallow `creation-setup` or the participant sub-routes.)
- [x] Map `GET` in `Nova/Features/Campaigns/CampaignQueryEndpointRouteBuilderExtensions.cs`
      following the existing handler pattern (auth `RequireClubMember`, `ProducesProblem`,
      `ToHttpResult`, `WithName`/`WithSummary` metadata).
- [x] Extend `Nova.Client/Services/Campaigns/HttpCampaignQueryService.cs` with
      `GetCampaignDetailAsync` using strict structural response validation
      (`ReadRequiredJsonAsync` style, mirroring the participant client).
- [x] Tests in `Nova.Unit.Tests/Campaigns/CampaignQueryServiceTests.cs`: returns detail for the
      club's campaign; `NotFound` for another club's campaign id and missing id; forbidden when
      no club context; validation rejects `CampaignId <= 0`. Input-validation tests in
      `CampaignQueryContractTests.cs`.
- [x] Tests in `Nova.Unit.Tests/Campaigns/HttpCampaignQueryServiceTests.cs`: sends GET to the
      detail URL; reads and validates the response shape; rejects malformed payloads.
- [x] Endpoint tests in `Nova.Integration.Tests/Http/CampaignQueryHttpTests.cs`: GET returns
      200 + payload; 404 for unknown id; 401/403 without auth (run with the AppHost in Phase 6).

### Verification Plan

- `dotnet build Nova.slnx` — clean build.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — new + existing unit tests
  pass (run the whole project; the suite is the regression guard for the shared contracts).

### Phase Summary

Production contract shipped: `GetCampaignDetailInput` (`required long CampaignId`,
`[Range(1, long.MaxValue)]`), `CampaignDetailResult` (CampaignId, Name, Status, StartDate,
PlannedEndDate?, ParticipantCount, SeasonId, SeasonName), `GetCampaignDetailAsync` on
`ICampaignQueryService` (tenant-guarded, `InputValidator` first, single `AsNoTracking`
projection incl. `Season` nav + `PlayerAssignments.Count()`, `NotFound` for missing/foreign
rows), route `GET /api/campaigns/{campaignId:long}` (`GetCampaignDetail` + relative + route
name + `GetCampaignDetailUrl(long)` builder), and the WASM client method with strict
`IsValidCampaignDetail` payload validation. Client validation is duplicate-but-deliberate
(server owns authority; client rejects malformed payloads before parsing, mirroring the
participant client).

Verification results:
- `dotnet build Nova.slnx` — clean build, 0 warnings, 0 errors (~1m31s).
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — **1,234 passed, 0 failed**.
  (MTP note: VSTest-only flags like `--nologo` are rejected by MTP and produce "Zero tests
  ran"/exit 5 — invoke as shown above with no extra flags.)
- Integration tests written (401/403 + full 200/404 scenario in `CampaignQueryHttpTests.cs`)
  but **not executed** — they need the Aspire AppHost/PostgreSQL and are deferred to Phase 6.
- Format: `dotnet format Nova.slnx --verify-no-changes` fails repo-wide with **pre-existing**
  CHARSET errors (~24 Tag-feature files + `CommitAttemptTracker.cs`) and 3 IDE0161 warnings on
  TagDefinition migrations — all inherited from main (c216af9), none touched by this phase
  (`git diff` confirms). Phase 1 files pass format clean via `--include <files>` (exit 0).
  Pre-existing breakage is recorded here and left unfixed (out of scope); Phase 7 re-checks
  whether main has healed before commit.

Phase 2 next: graduation-years choices query (see below).

## Phase 2: Distinct graduation-years choices query

Status: Complete

Suggested executor: orchestrator for the service query (tenant-safe EF construction); client +
tests can be delegated once the route and shape are fixed.

- [x] Add `GetCampaignParticipantGraduationYearsInput` to `Nova.Shared/Features/Campaigns/` —
      `required long CampaignId` with `[Range(1, long.MaxValue)]`.
- [x] Extend `ICampaignParticipantQueryService` with
      `GetRosterGraduationYearsAsync(GetCampaignParticipantGraduationYearsInput,
      CancellationToken)` returning `ServiceResult<IReadOnlyList<int>>` (ascending).
- [x] Implement in `Nova/Features/Campaigns/CampaignParticipantQueryService.cs`: same
      authorization + tenant guard as the roster query, then a distinct projection of
      `Player.GraduationYear` over the campaign's assignments, `OrderBy` ascending. No artificial
      bound needed (distinct years over one roster are inherently small) — note the reasoning in
      the method's XML docs.
- [x] Add route constants to `CampaignEndpoints`: `GetCampaignParticipantGraduationYears =
      $"{GroupPrefix}/{{campaignId:long}}/participants/graduation-years"`, relative + route name,
      and a URL builder. (No conflict: `graduation-years` cannot match the `:long` detail route
      parameter.)
- [x] Map `GET` in `Nova/Features/Campaigns/CampaignParticipantEndpointRouteBuilderExtensions.cs`
      with the same metadata/auth conventions as the roster endpoint.
- [x] Extend `Nova.Client/Services/Campaigns/HttpCampaignParticipantQueryService.cs` with the
      matching method and strict response validation (list of ints).
- [x] Tests in `Nova.Unit.Tests/Campaigns/CampaignParticipantQueryServiceTests.cs`: returns
      distinct ascending years; empty list when the campaign has no participants; forbidden with
      no club; `NotFound`/tenant isolation for another club's campaign; validation rejects
      `CampaignId <= 0`.
- [x] Tests in `Nova.Unit.Tests/Campaigns/HttpCampaignParticipantQueryServiceTests.cs`: correct
      URL, parses list, rejects malformed payload.
- [x] Endpoint tests in `Nova.Integration.Tests/Http/CampaignParticipantHttpTests.cs` (run with
      the AppHost in Phase 6).

### Verification Plan

- `dotnet build Nova.slnx` — clean build.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full unit suite passes.

### Phase Summary

Production contract shipped: `GetCampaignParticipantGraduationYearsInput` (`required long
CampaignId`, `[Range(1, long.MaxValue)]`, `MaxGraduationYears = 20` client-bound constant),
`GetRosterGraduationYearsAsync` on `ICampaignParticipantQueryService` (validation →
UserId/ClubId guards → tenant-guarded campaign-exists check → distinct ascending
`Player.GraduationYear` projection over the campaign's assignments, no server-side bound —
roster class years are inherently few, reasoning in method XML docs), route `GET
/api/campaigns/{campaignId:long}/participants/graduation-years` (`GetCampaignParticipantGraduationYears`
+ relative + route name + `GetCampaignParticipantGraduationYearsUrl(long)` builder), the mapped
`GET` with full metadata/`Produces` conventions, and the WASM client method with strict
`IsValidGraduationYears` payload validation (bounded ≤ 20, positive, strictly ascending).

Verification results:
- `dotnet build Nova.slnx` — clean build, 0 warnings, 0 errors (~48s).
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — **1,251 passed, 0 failed**
  (17 new: 6 service, 7 client, 4 contract).
- Integration tests written (`CampaignParticipantHttpTests.cs`: 200 ascending years, 400
  non-positive route value, 401 anonymous, 403 no club, 404 cross-tenant) but **not executed** —
  deferred to Phase 6 with the other endpoint tests.
- Format: per-file `--include` for all Phase 2 files passes clean (exit 0). Two fixes applied by
  `dotnet format`: CHARSET on the new input contract file and a missing final newline in the
  client test file. Repo-wide CHARSET breakage on Tag-feature files remains pre-existing (see
  Phase 1 summary).

Phase 3 next: workspace shell page (see below).

## Phase 3: Workspace shell page (route, tab bar, header, entry link)

Status: Complete

Suggested executor: orchestrator (defines the component boundaries #64 will build against).

- [x] Create `Nova.UI/Features/Campaigns/Pages/CampaignWorkspace.razor` +
      `.razor.cs` (+ optional `.razor.css`) with `@page "/campaigns/{CampaignId:long}"`,
      `@rendermode InteractiveAuto`, `@attribute [Authorize(Policy =
      Policies.RequireClubMember)]`. Code-behind: primary-constructor DI
      (`ICampaignQueryService`, `ICampaignParticipantQueryService`,
      `ITagDefinitionQueryService`, `ITeamRosterService`, `NavigationManager`,
      `IJSRuntime`), `[PersistentState]` persisted roster/detail + `Initialized` guard,
      `ComponentCancellationToken`.
- [x] Header block: campaign name, status badge (reuse the list page's badge classes),
      formatted date range, "N participants" from `CampaignDetailResult.ParticipantCount`,
      season name. Breadcrumb/link back to `/campaigns`.
- [x] Tab bar: `tab` param backed. Evaluate active; Overview, Placements, Closeout rendered as
      disabled buttons/links with a "coming soon" tooltip. Invalid `tab` values fall back to
      evaluate. Push history on tab change.
- [x] Detail-load states: loading spinner, recoverable error + Retry (reuse the list page's
      alert pattern), `Forbidden` → `NavigateTo` AccessDenied, `NotFound` → friendly
      "campaign not found" card with a link back to `/campaigns`.
- [x] Roster shell: filter bar + results region + pager composed from Phase 4 components, with
      the roster loaded only when the detail load succeeds (single initial load path).
- [x] Update `Nova.UI/Features/Campaigns/Pages/Campaigns.razor`: campaign name cell becomes
      `<a href="campaigns/{CampaignId}">@campaign.Name</a>` (relative href, enhanced
      navigation — matches the existing `campaigns/new` pattern).
- [x] Component tests in `Nova.Unit.Tests/Campaigns/CampaignWorkspaceTests.cs` (new file, bUnit
      `BunitContext` + NSubstitute): header renders detail fields; tab bar shows Evaluate active
      and the other three disabled; `tab` param round-trips; `NotFound` and `Forbidden` paths;
      **render-mode assertion** per testing instructions (bUnit alone does not prove
      interactivity). Update `CampaignComponentsTests.cs` for the list-page link (href
      `campaigns/{id}`, name visible).

### Verification Plan

- `dotnet build Nova.slnx` — clean build.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full unit suite passes.
- `dotnet format Nova.slnx --verify-no-changes` — no formatting drift.

### Phase Summary

Workspace shell shipped: `CampaignWorkspace.razor` + `.razor.cs` at
`/campaigns/{CampaignId:long}` (`InteractiveAuto`, `RequireClubMember`), primary-constructor
DI with `ITagDefinitionQueryService`/`ITeamRosterService`/`IJSRuntime` captured as private
fields reserved for Phase 4 (keeps the build warning-free). Persisted state
(`PersistedDetail`, `PersistedPageError`, `PersistedNotFound`, `PersistedRoster`,
`PersistedRosterError`, `Initialized`) mirrors the players-page prerender restore pattern.
Header renders name, `CampaignStatusBadgeClass` badge, `FormatCampaignDates` range, season,
and participant count; back link to `/campaigns`. Tab bar: Evaluate active button pushing
`?tab=evaluate` on click; Overview/Placements/Closeout are disabled `nav-link` spans with
`title="Coming soon"`; unknown `tab` values fall back to evaluate. Detail-load states:
spinner, recoverable error + Retry, `Forbidden` → `/Account/AccessDenied`, `NotFound` →
friendly card. Roster loads only after the detail load succeeds (single initial load path);
its shell is a placeholder card (total count + "coming soon" text) that Phase 4 replaces
with the composed filter bar + results region + pager. `Campaigns.razor` name cell is now a
relative link (`campaigns/{id}`). Tests: 13 new bUnit tests in
`CampaignWorkspaceTests.cs` (render-mode file assertion, header fields, tab states and
round-trips, loading/NotFound/Forbidden/error+Retry, roster load ordering, roster retry
clearing the error, persisted-state restore) plus the list-page link assertion in
`CampaignComponentsTests.cs`. Fixed a roster-retry bug found by the tests: `_rosterError`
is now cleared at the start of `LoadRosterAsync` so a successful retry dismisses the alert.
Verification: clean build (0 warnings), full unit suite 1,264 passed, per-file format clean
(two new files required CHARSET fixes).

## Phase 4: Roster list with URL-backed filters, sorting, and paging

Status: Complete

Suggested executor: orchestrator (URL-state correctness is the highest-risk area; component
tests encode it). Individual components can be scaffolded by a sub-agent once the page-level
state flow is fixed.

- [x] Create filter components under `Nova.UI/Features/Campaigns/Components/`:
      `CampaignRosterFilters.razor(.cs)` — search input (debounced 350 ms via the Players-page
      `CancellationTokenSource` pattern), graduation-year multi-select (Phase 2 endpoint), tag
      multi-select (`GetChoicesAsync`, active only), outcome select (four enum values), team
      select (`ITeamRosterService.GetRosterAsync`, active teams), "Clear filters" button shown
      only when a filter is active, and the right-aligned "N participants" count from the roster
      result's `TotalCount` (updates to reflect active filters).
- [x] Create `CampaignRosterTable.razor(.cs)` — desktop table: columns Tryout #, Name,
      Graduation Year, Tags (chips, archived tags styled distinctly), Outcome (badge), Team.
      Sortable headers for Name / Grad Year / Tryout # / Outcome / Team with asc/desc toggle
      (click toggles direction; `aria-sort` set). `assignmentId` never appears.
- [x] Create `CampaignRosterCards.razor(.cs)` — narrow-screen card/list layout showing the same
      fields, reachable by keyboard (row = focusable element).
- [x] Create `CampaignRosterPager.razor(.cs)` — Prev/Next + page info ("Page N of M") driven by
      `TotalCount` and fixed `PageSize` 50; disabled bounds; hidden when a single page.
- [x] Page-level URL state (code-behind): `[SupplyParameterFromQuery]` for every contract param;
      one canonical `BuildWorkspaceUrl`/parse helper (pure static class, e.g.
      `Nova.UI/Features/Campaigns/Services/CampaignWorkspaceUrlState.cs`) so tests can cover
      round-tripping; every change pushes history; defensive fallback to defaults for invalid
      values; request-sequence token discards stale responses (search races, filter churn).
- [x] Load states for the roster: loading spinner/skeleton, recoverable error + Retry, two empty
      states — "no participants in this campaign yet" (empty campaign) vs "no participants match
      the current filters" with a working Clear filters action (empty filter result).
- [x] Component tests (extend `CampaignWorkspaceTests.cs`): URL→state (params apply on load,
      including direct-load with filters), state→URL (each filter/sort/page change pushes the
      expected URL — fake NavigationManager), debounced search issues a single request after
      quiet period, stale-response discard (older response arriving later is ignored), sorting
      header click cycles asc→desc and updates URL, pager math and bounds, empty-campaign vs
      empty-filtered states, error + retry re-fetch, role-neutral display (no admin-only
      columns for a plain approved member).
- [x] Pure unit tests for `CampaignWorkspaceUrlState` round-trip/parsing/fallback rules.

### Verification Plan

- `dotnet build Nova.slnx` — clean build.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full unit suite passes.
- `dotnet format Nova.slnx --verify-no-changes` — no formatting drift.

### Phase Summary

Delivered the full evaluate-tab roster: `CampaignRosterFilters` (debounced search, graduation-year/tag/outcome/team filters, clear-filters, participant count), `CampaignRosterTable` (sortable columns with `aria-sort`, tag chips, outcome badges), `CampaignRosterCards` (narrow-screen layout), and `CampaignRosterPager` (Prev/Next + page math, hidden on single page). The page code-behind owns URL state via the pure `CampaignWorkspaceUrlState` helper (canonical parse/build, defaults on invalid input, history push on every change) and discards stale roster responses with a request-sequence token. Two notable fixes found while testing:

- **Initial-load hydration bug**: Blazor runs `OnInitializedAsync` before `OnParametersSet`, so the first roster load ignored incoming URL filters. Initial query state is now applied in `OnInitializedAsync` (via `ApplyInitialQueryState`) before any data loads; `OnParametersSet` only reacts to subsequent URL changes. Caught by `CampaignWorkspace_AppliesRosterState_FromQueryParametersOnLoad` and `CampaignWorkspace_ShowsNoMatchMessage_AndClearsFilters_WhenFiltersExcludeAllParticipants` (both initially failed and were fixed by this change).
- Load states cover loading, recoverable error + retry, empty campaign, and empty-filtered results with a working Clear filters action.

Tests: 9 pure `CampaignWorkspaceUrlState` tests (round-trip, defaults, fallback, ordering/dedup, normalization, page math, tab emission, active-filter detection) and 7 roster component tests (URL→state on load, sort header cycles + URL push, debounced search single request, stale-response discard, pager math/bounds, both empty states). Full unit suite: 1284 passed, 0 failed. Build clean; `dotnet format` clean on changed files.

## Phase 5: Selection, drawer placeholder, scroll anchor, responsive layout

Status: Completed

Suggested executor: orchestrator (scroll-anchor + drawer boundary hand-off to #64).

- [x] Row selection: clicking/Entering a roster row (table or card) pushes
      `participant={assignmentId}` onto the workspace URL; the selected row renders an
      `aria-current`/highlight state.
- [x] Create `Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.razor(.cs)` —
      minimal shell placeholder: off-canvas/side panel, header with participant name (from the
      selected roster item), Close button that removes `participant` from the URL (push history),
      and a "Participant details arrive in #64" placeholder body. Public parameters document the
      hand-off contract: `ParticipantId`, `RosterItem`, `OnClose` `EventCallback`. #64 replaces
      the body internals; #67 owns open/close and state preservation.
- [x] Scroll anchor: capture the roster container's `scrollTop` before any URL push that opens
      or closes the drawer, and restore it after render (`OnAfterRenderAsync` + a small JS interop
      helper). Roster rows must never jump when the drawer opens/closes; page/sort/filter changes
      scroll to top of the roster region.
- [x] Responsive layout: Bootstrap grid — table for `md+`, card list below; drawer becomes a
      full-width panel on narrow screens (screen-designs section 5). All controls keep visible
      focus styles; rows are keyboard-operable (tab order, Enter to select, Escape closes the
      drawer).
- [x] Component tests: selection pushes `participant` and highlights the row; drawer opens when
      the param is present and close removes it; roster state params are preserved across drawer
      open/close; Escape-to-close; keyboard selection works (bUnit keyboard events); scroll
      anchor helper is invoked on drawer open/close and not on filter changes.

### Verification Plan

- `dotnet build Nova.slnx` — clean build.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full unit suite passes.
- `dotnet format Nova.slnx --verify-no-changes` — no formatting drift.

### Phase Summary

Selection, drawer shell, scroll anchoring, and the responsive layout are in place. Clicking or
Enter-ing a roster row pushes `participant={assignmentId}` after `tab=` in the workspace URL
(`CampaignWorkspaceUrlState.ParseParticipant` drops absent/invalid values; `BuildWorkspaceUrl`
appends the token only when the drawer is open). The selected row carries `aria-current="true"`
and a `.roster-row-selected`/`.roster-card-selected` highlight; keyboard activation (Enter/Space)
and visible focus styles are shared by the table and card list.

`CampaignParticipantDrawer` is a minimal shell (backdrop, fixed right panel, header with the
participant name or a "Participant" fallback, close button, "Participant details arrive in #64."
body) exposing the #64 hand-off contract (`ParticipantId`, `RosterItem`, `OnClose`). Escape and
the close button remove `participant` from the URL; the drawer is full-width below `md` and a
26rem panel above it. Focus moves to the close button when the drawer opens.

The roster list is wrapped in a `#roster-scroll-region` scroll container. `window.novaCampaignWorkspaceCaptureScroll/RestoreScroll/ScrollToTop/Focus` JS helpers (in `wwwroot/js/site.js`) anchor the scroll position: drawer open/close captures `scrollTop` and restores it after render, while filter/sort/page pushes scroll the region to top. Participant state lives outside the roster state, so open/close never reloads the roster; a defensive `long` parse drops invalid `participant` values.

Tests: 4 new `CampaignWorkspaceUrlState` participant tests (parse valid/invalid, URL emission with/without the token) and 7 new page tests (row click opens drawer + pushes URL + highlights + scroll capture/restore; close removes `participant` and preserves roster params; Escape closes; Enter selects; sort scrolls to top without capturing; unknown participant opens the fallback shell; invalid participant is dropped). Full unit suite: 1295 passed, 0 failed. Build clean; scoped `dotnet format --verify-no-changes` clean on changed files.

## Phase 6: Focused browser validation (Aspire + Playwright)

Status: Complete

Suggested executor: orchestrator, following the `aspire-playwright-validation` skill (requires
judgment to fix blockers found).

- [x] Write the concrete scenario list before starting the app (skill precondition): the
      scenarios below.
- [x] `aspire start --isolated --non-interactive` → `aspire wait nova --non-interactive` →
      discover URLs via `aspire describe --format Json`.
- [x] Scenarios: open `/campaigns/{id}` (header name/status/participant count correct); apply
      each filter and assert rows + URL params; browser Back/Forward walks filter changes and
      restores each state; sort each column and assert order + URL; page through results and
      assert page param + bounds; narrow viewport (~480 px) shows card layout and the drawer
      covers the panel; keyboard: Tab through rows, Enter opens drawer, Escape closes; search
      typing updates results without a full page reload (interactive render mode working);
      empty-filtered state → Clear filters restores the roster; drawer open/close preserves
      roster scroll position; refresh with `participant=` in URL reopens the drawer.
- [x] Run the new integration tests from Phases 1–2 against the running AppHost
      (`dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj`).
- [x] Fix any blocker found, rerun the affected browser segment before concluding, then
      `aspire stop --non-interactive`; remove temporary browser-automation artifacts.

### Verification Plan

- Scenario coverage report: every scenario above reached with its expected outcome (or a fixed
  blocker + rerun evidence). Record the report in this phase's summary.

### Phase Summary

All scenarios reached against the isolated AppHost with a seeded Postgres (campaign 1, "Summer
Tryouts 2026", 200 assignments). Header name/status/count, per-filter URL params, Back/Forward
restoration, per-column sort order + URL, paging bounds, live search without reload, the
empty-filtered → Clear-filters round trip, and refresh with `participant=` in the URL (drawer
reopens) all behaved as specified. Narrow viewport (~480 px) switches to the card list and the
drawer covers the panel; drawer open/close preserves roster scroll position (verified at ~250 px
scroll offset).

One blocker found and fixed: a trusted Enter/Space keypress on a focused roster row (`tr.roster-row`
/ `li.roster-card`, both `tabindex="0"`) opened the drawer and then immediately closed it. Root
cause: Blazor's keydown handler opens the drawer, the drawer moves focus to its close button, and
the browser's default activation click for Enter/Space then synthesizes a click on that now-focused
close button. Razor's `@onkeydown:preventDefault` predicate form does not compile under this
repo's Razor SDK (CS1660/CS1503; only the static-bool form is supported), so the default action is
instead suppressed by a capture-phase document listener in `wwwroot/js/site.js` (scoped to
`.roster-row`/`.roster-card` targets). After the fix, trusted Enter and Space open the drawer and
keep it open (`defaultPrevented: true` on the row's keydown, dialog still visible 1.5 s later),
Tab is untouched (`defaultPrevented: false`, focus moves between rows), and Escape/close still
work. Files changed: `Nova/wwwroot/js/site.js`, `CampaignRosterTable.razor`,
`CampaignRosterTable.razor.cs`, `CampaignRosterCards.razor.cs` (doc remarks only).

Integration suite against the running AppHost: 230 passed, 0 failed. AppHost stopped and temporary
browser-automation artifacts removed after the report.

## Phase 7: Final verification and cleanup

Status: Complete

- [x] `dotnet format Nova.slnx --verify-no-changes` — clean.
- [x] `dotnet build Nova.slnx` — clean.
- [x] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full suite passes.
- [x] Re-read the issue acceptance criteria and confirm each one is covered by code, tests, or
      the browser-validation report; fix any gap.
- [x] Commit work (with the Co-authored-by trailer) and open the PR referencing #67; note the
      two approved scope deviations in the PR description.

### Verification Plan

- All three commands above green; acceptance checklist complete in the PR description.

### Phase Summary

All verification commands green: the solution builds with 0 warnings/0 errors; the full unit
suite passes (1295/1295); the integration suite passed earlier in Phase 6 (230/230). The
solution-wide format check is clean for every file this issue touched, but `dotnet format
Nova.slnx --verify-no-changes` still fails repo-wide on pre-existing `CHARSET` errors in
~22 TagDefinition files that arrived via the main merge from #79 (BOM/encoding drift in that
PR's files, unrelated to roster work), so the format gate for this PR is the scoped check on this
issue's changed files, noted in the PR description.

Acceptance-criteria recheck against issue #67: all nine criteria map to shipped code, tests, or
the Phase 6 browser report — filter composition and URL round-trips, sort/page/tab persistence
across refresh and back/forward, interactive (no full reload) roster updates with stale-request
cancellation, desktop + narrow-layout parity, loading/empty/error/retry/total states, drawer
open/close preserving filters + page + selected row + scroll, server paging at 100+ participants
(200 seeded, page size 50), the workspace header fields, and the campaigns-list name links.

## Final Recap

All seven phases are complete. The workspace route/shell, campaign-detail and graduation-years
reads, roster list with URL-backed filters/sorting/paging, and the drawer shell placeholder are
implemented, unit- and integration-tested, browser-validated, and merged-ready as PR #80
(`eruvalca-roster-page-and-filter-ui`).

Summary of the work:

- **Contracts and server reads (Phases 1–2)** — `ICampaignQueryService.GetCampaignDetailAsync`
  and `ICampaignParticipantQueryService.GetRosterGraduationYearsAsync` with input/result
  contracts, endpoint wiring, WASM clients, and validation; both approved as scope deviations
  and recorded in the issue body.
- **Workspace shell (Phase 3)** — `/campaigns/{campaignId}` route, 4-tab bar with disabled
  placeholders, workspace header, campaigns-list links.
- **Roster, filters, sorting, paging (Phases 4–5)** — debounced name search with stale-request
  cancellation, graduation-year/tag/outcome/team filters, click-to-sort columns, page size 50,
  total-result feedback, desktop table + narrow cards, loading/empty/error/retry states, and the
  full URL-state contract with history pushes and defensive parsing.
- **Drawer shell + keyboard fix (Phase 6)** — minimal drawer placeholder with scroll anchoring
  and selected-row preservation; fixed the trusted-Enter/Space auto-close bug with the
  capture-phase `site.js` listener.
- **Verification (Phases 6–7)** — 1295/1295 unit tests, 230/230 integration tests, full browser
  validation pass, clean build, scoped format check clean (repo-wide check blocked by
  pre-existing #79 CHARSET drift, noted in the PR).

## Deployment Plan

No database schema changes are introduced by this work (read-only additions), so no migrations
ship with it.

1. Merge PR #80 to `main` after review.
2. `dotnet build Nova.slnx` — confirm 0 warnings/0 errors.
3. `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — confirm all pass.
4. `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` against the
   Aspire AppHost — confirm all pass.
5. Start the AppHost (`aspire start`) and spot-check `/campaigns/1`: header fields, roster rows,
   a filter round-trip, and back/forward restoration.
6. No configuration, secret, or data changes required.
