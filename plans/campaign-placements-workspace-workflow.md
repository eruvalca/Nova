# Campaign Placements Workspace Workflow (Issue #87)

Replace the disabled Placements placeholder in the campaign workspace with the responsive
campaign-placement workspace: a `tab=placements` URL state, graduation-year and unresolved-only
filters, live authoritative summary counts, a wide-screen table and narrow-screen card equivalent
with per-row outcome/team editing, concurrency-safe save/conflict recovery, and read-only result
views for Closed campaigns and non-administrators. This child owns Razor composition, URL state,
interaction state, refresh/retry UX, and focused component/browser coverage only — server placement
rules and campaign close/reopen behavior remain out of scope.

## Prerequisites (all merged — verified on `main`)

- #85 placement mutation API and client (commit `1335dd6`), #86 placement roster/summary query API
  (commit `7da5951`), #10 campaign workspace shell, #68 participant contracts, #8 team roster,
  #14/#17/#19 placement foundations. No schema, server, or contract changes are expected in this work.

## Confirmed design decisions

1. **Team eligibility presentation**: the Assigned team select lists all Active teams; teams whose
   cutoff year exceeds the player's graduation year render **disabled with a muted "ineligible"
   label** (confirmed with the user). Filtering is guidance only — the mutation service stays
   authoritative.
2. **Conflict recovery scope**: any save conflict shows an actionable warning and a
   **"Close and reload"** action that **discards ALL unsaved row drafts**, reloads the campaign
   detail, roster, and summary from the server, and re-enables editing only after the reload
   completes (confirmed with the user). A conflict never silently overwrites a newer placement.
3. **Paging**: the placements roster pages at the endpoint default (50) with pager controls
   mirroring the Evaluate roster (confirmed with the user).
4. **Row save UX**: edits stay local until an explicit per-row Save; each dirty row shows dirty /
   saving / saved / validation-error / conflict states; duplicate submission per row is prevented;
   no implicit multi-row transaction (issue text).
5. **Success handling**: adopt the returned `PlacementMutationSuccess.ConcurrencyToken`, refresh only
   the authoritative summary, and remove the row from the unresolved-only view when the new outcome
   leaves `Undecided`. Sibling rows' unsaved drafts survive (issue text).
6. **Narrow screens**: card equivalent per row, following the Evaluate roster table/cards pattern.
7. **Non-admin members on an Active campaign**: same static result view as Closed, with a muted
   read-only note; mutation controls are never rendered for them.
8. **URL parameter names**: placements state uses `tab=placements` plus `placementGraduationYear`,
   `unresolvedOnly`, and `placementPage` — distinct from the Evaluate roster params so each tab's
   URL carries only its own state and history entries stay unambiguous. Unknown tab tokens still
   fall back to `evaluate`.
9. **Filter choices reuse existing services**: graduation-year choices from
   `ICampaignParticipantQueryService.GetParticipantGraduationYearsAsync`; team choices from
   `ITeamRosterService` with `GetTeamRosterInput.LifecycleStatus = "active"`.
10. **Component split**: a new `CampaignPlacementsPanel` component owns the placements region
    (filter bar, summary footer, table/cards, pager, and the per-row edit state machine); the
    `CampaignWorkspace` page keeps owning URL state, tab switching, and role/status derivation.
11. **Player detail navigation**: the player name in each row links to
    `/players/{playerId}?returnUrl=<current placements workspace URL>` so back navigation restores
    the tab and filters (PlayerDetail already normalizes `returnUrl`).
12. **Edge case — archived current team**: when a row's currently assigned team is absent from the
    Active team choices, render it as a disabled "current team" option so editing the row never
    silently clears it.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to
`Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to
continue with zero context); run the phase's **Verification Plan** and record the result before
moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

Recipes: invoke `add-blazor-ui` for every Razor step (placement, lifecycle, persisted state,
`EventCallback`/binding) and `nova-testing` for bUnit/browser test work; use
`aspire-playwright-validation` only for one-off acceptance passes that should not become committed
regression coverage. Always-on rules: `.github/instructions/` — `blazor-architecture`,
`testing`, `csharp-conventions`, `validation` (client-side `InputValidator` mirroring the shared
input annotations), `service-layer` (problem-shape mapping), and `observability` where ProblemDetails
trace IDs surface. All UI lives in `Nova.UI/Features/Campaigns/`; all data access goes through the
existing typed services — never `DbContext` or `HttpContext` from components.

Known environment caveats: `dotnet format Nova.slnx --verify-no-changes` may still exit non-zero on
pre-existing `CHARSET` errors in sibling-session files (Tag feature/migrations, noted in the #86
plan). Scope format verification to the files this work touches with `--include` and record the
result.

## Phase 1: URL state and tab bar enablement

Status: Not started <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator (establishes the URL/tab contract Phases 2–4 build on).

- [ ] Extend `CampaignWorkspaceUrlState` (Nova.UI/Features/Campaigns/Services/): add `PlacementsTab`
      token and `ValidTabs`; add a `CampaignWorkspacePlacementState` record (GraduationYear `int?`,
      UnresolvedOnly `bool`, Page `int`) with defensive `ParsePlacement(...)` (invalid year/bool/page
      fall back to defaults) and `BuildPlacementQueryString(...)` that omits defaults; add placement
      URL building that appends only placement params + `tab=placements`.
- [ ] In `CampaignWorkspace.razor.cs`: add `[SupplyParameterFromQuery]` properties for
      `placementGraduationYear`, `unresolvedOnly`, `placementPage`; replace the hard-coded
      `_activeTab = EvaluateTabName` fallback with tab-query normalization
      (`evaluate`/`placements` valid, unknown → `evaluate`) without altering roster parsing; add
      `SelectPlacementsTabAsync` that navigates to the placements URL; compute
      `_canEditPlacements = detail.Status == CampaignStatus.Active && user.IsInRole(Roles.ClubAdmin)`
      from `AuthenticationStateProvider` (repo precedent: `Campaigns.razor.cs` /
      `Players.razor.cs`).
- [ ] In `CampaignWorkspace.razor`: replace the disabled `<span>` Placements tab with a real
      button (`role="tab"`, `aria-selected`, `@onclick="SelectPlacementsTabAsync"`); keep Evaluate as
      default; render the placements region only when `_activeTab == PlacementsTabName`; leave the
      Evaluate region untouched.
- [ ] Update `CampaignWorkspaceTests.RegisterServices` to register substitutes for
      `ICampaignPlacementQueryService` and `ICampaignPlacementService` (and any auth test double the
      workspace now needs) so existing tests keep rendering.
- [ ] bUnit: new `CampaignWorkspaceUrlStateTests` (parse/build round-trips, invalid tokens, page
      bounds, placements-vs-roster param isolation); extend `CampaignWorkspaceTests` — tab switching
      pushes the placements URL, direct `?tab=placements` loads activate the tab, unknown tab falls
      back to Evaluate, Evaluate URL behavior is unchanged.

### Verification Plan

- `dotnet build Nova.slnx` — clean build.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignWorkspace*"` — all pass.

### Phase Summary

_(write when phase completes)_

## Phase 2: Read-only placements region (data loading and rendering)

Status: Not started <!-- Not started | In progress | Complete -->

Suggested executor: sub-agent with a smaller model (well-specified once Phase 1 exists).

- [ ] Add `CampaignPlacementsPanel.razor` / `.razor.cs` / `.razor.css` in
      `Nova.UI/Features/Campaigns/Components/`: primary-constructor DI for
      `ICampaignPlacementQueryService`, `ITeamRosterService`, `ICampaignParticipantQueryService`;
      public `[Parameter]`s (CampaignId, CampaignStatus, CanEditPlacements, placement state) and
      `EventCallback`s for filter/page changes back to the page (page updates the URL).
- [ ] Load the paged roster (`GetPlacementRosterAsync`), summary (`GetPlacementSummaryAsync`),
      Active team choices (`ITeamRosterService`, `LifecycleStatus="active"`), and graduation-year
      choices; guard duplicate startup fetches with the `[PersistentState]` Initialized pattern and
      keep explicit reload helpers for user-triggered refresh; flow `ComponentCancellationToken`
      everywhere.
- [ ] Render loading, empty (with clear-filters affordance when filtered), and error-with-Retry
      states; reuse the pager pattern (Page/PageSize/TotalCount) per confirmed decision 3.
- [ ] Filter bar: graduation-year select + "Unresolved only" checkbox; changes raise the
      EventCallback and reset the page to 1.
- [ ] Summary footer from `CampaignPlacementSummaryDto`: "`{n} assigned | {n} not selected |
      {n} withdrawn | {n} undecided`" with `role="status" aria-live="polite"`.
- [ ] Wide table (`d-none d-md-block`-style split) and narrow card equivalent: player name link to
      `/players/{playerId}?returnUrl=...` (confirmed decision 11), graduation year, outcome, team.
      Read-only rendering = static text, no selects.
- [ ] Read-only indicators: Closed campaigns show a muted "Placements are frozen" banner; Active +
      non-admin shows a muted read-only note (confirmed decision 7).
- [ ] bUnit: loading/empty/error/retry; summary counts render from the DTO; read-only static
      rendering (Closed and non-admin); player link carries the returnUrl; narrow cards render.

### Verification Plan

- `dotnet build Nova.slnx` — clean build.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignPlacementsPanel*"` — all pass.

### Phase Summary

_(write when phase completes)_

## Phase 3: Per-row edit state machine

Status: Not started <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator (cross-cutting state machine the conflict phase extends).

- [ ] Add a row-draft model (draft outcome/teamId, snapshot of the loaded row, current
      `ConcurrencyToken`, dirty/saving/conflict flags); derive dirty by comparing draft to snapshot.
- [ ] Per-row Outcome select (Undecided / Assigned / Not selected / Withdrawn) and Team select
      (format `{Team} - cutoff {year}`): Team select enabled and required only when
      Outcome = Assigned; changing away from Assigned clears the draft team; ineligible teams render
      disabled with the "ineligible" label (confirmed decision 1); a currently assigned team missing
      from Active choices renders as a disabled "current team" option (confirmed decision 12).
- [ ] Per-row Save button (visible when dirty, disabled while saving); client-side validation before
      submission via `InputValidator.Validate<UpdateCampaignPlacementInput>`-shaped checks (team
      required for Assigned) with inline per-row validation messages.
- [ ] Submit via `ICampaignPlacementService.UpdatePlacementAsync` with the row's token; map problem
      kinds distinctly: validation (400) → inline row error, forbidden/not-found → row-level
      actionable message, conflict (409) → Phase 4 path; per-row duplicate-submission guard.
- [ ] On success: adopt the returned token, mark the row saved (status message preserved across the
      summary refresh and cleared at the next intentional action), refresh ONLY the summary, and
      remove the row from the unresolved-only view when the new outcome leaves `Undecided`
      (confirmed decision 5); sibling drafts untouched.
- [ ] Accessibility: labelled selects per row (`Outcome for {name}`, `Team for {name}`), visible
      focus, `role="status"` save feedback, `role="alert"` row errors.
- [ ] bUnit state-transition matrix: dirty→saving→saved; validation error renders and blocks submit;
      team required/cleared behavior; "ineligible" disabled labels; token adoption after success;
      unresolved-only row removal; duplicate-submission prevention while saving.

### Verification Plan

- `dotnet build Nova.slnx` — clean build.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignPlacementsPanel*"` — all pass (extended).

### Phase Summary

_(write when phase completes)_

## Phase 4: Conflict and Closed-transition recovery

Status: Not started <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator.

- [ ] On mutation conflict (409): render the actionable warning ("The placement was changed by
      someone else.") with a **Close and reload** button; block further submissions until the reload
      completes; the reload discards ALL drafts and refreshes campaign detail + roster + summary
      (confirmed decision 2); adopt server values for every row afterward.
- [ ] Closed-while-open transition: when a rejected save or a reloaded detail shows
      `CampaignStatus == Closed`, clear all drafts and transition the panel to read-only with the
      frozen banner (issue requirement).
- [ ] Distinguish conflict from other problem kinds so validation/forbidden/not-found never trigger
      a full discard-and-reload.
- [ ] Accessibility: warning region `role="alert" aria-live="assertive"` with focus moved to it;
      success/failure announcements via `role="status"`.
- [ ] bUnit: conflict clears drafts and disables saves until reload completes; reload repopulates
      rows with server values; Closed transition renders read-only and clears drafts; focus lands on
      the warning region.

### Verification Plan

- `dotnet build Nova.slnx` — clean build.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignPlacementsPanel*"` — all pass (extended).

### Phase Summary

_(write when phase completes)_

## Phase 5: Browser scenarios (Aspire + Playwright)

Status: Not started <!-- Not started | In progress | Complete -->

Suggested executor: sub-agent with a smaller model (well-specified once Phases 1–4 exist).

- [ ] Add `PlacementSeed` (`Nova.Browser.Tests/`) seeding an admin + approved evaluator (real
      Identity HTTP flow), an Active campaign with ~60 participants of mixed graduation years and
      outcomes (mostly `Undecided`), 3–4 active teams with different cutoff years, and a Closed
      campaign with placements; if a reusable team-insert helper is warranted, add it to
      `Nova.Integration.Tests/Http/SeedingHelpers.cs` (never copy seeding helpers per file).
- [ ] `CampaignPlacementBrowserTests` — primary administrator workflow: open
      `/campaigns/{id}?tab=placements`, enable "Unresolved only", assign an eligible team to a row,
      save, assert the saved status, the updated summary counts, and the row's removal from the
      unresolved-only view; assert URL/history round-trip (tab + filters restored on reload/back).
- [ ] Non-admin approved member scenario: placements tab renders the static result view with no
      enabled mutation controls.
- [ ] Closed campaign scenario: frozen banner and static rows for the admin as well.
- [ ] Accessibility regression assertions (row control touch targets ≥24×24, contrast) in the
      exercised scenario, per the browser-suite conventions; remember the SSR-prerendered-row
      click-through/hydration helpers from `CampaignEvaluationBrowserTests`.

### Verification Plan

- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --filter-class "*CampaignPlacementBrowserTests"` — all pass (AppHost starts via the fixture; Chromium must be installed once per machine).

### Phase Summary

_(write when phase completes)_

## Phase 6: Final verification

Status: Not started <!-- Not started | In progress | Complete -->

- [ ] `dotnet build Nova.slnx` — clean build.
- [ ] `dotnet format Nova.slnx --verify-no-changes` — scope to touched files with `--include` if the
      pre-existing sibling-session `CHARSET` failures persist.
- [ ] Full unit suite: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — all pass.
- [ ] Full integration suite: `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — all pass (Aspire AppHost + PostgreSQL).
- [ ] Full browser suite: `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` — all pass.

### Verification Plan

- All five commands above succeed; no pre-existing tests regress. CI runs build + unit only, so the
  integration and browser suites must pass locally before merge.

### Phase Summary

_(write when phase completes)_

## Final Recap

_(write when all phases complete: summary of the entire piece of work)_

## Deployment Plan

_(write when all phases complete: step-by-step deployment instructions)_
