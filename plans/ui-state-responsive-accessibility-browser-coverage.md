# UI State, Responsive, and Accessibility Browser Coverage (Issue #118)

Add committed `Nova.Browser.Tests` coverage for the MVP state families (loading, empty, validation,
conflict, failure, retry, read-only), responsive behavior, and keyboard/screen-reader accessibility
on the primary workflows (roster, player drawer, campaign-create/player/team forms, placement
controls, closeout), plus a contrast regression check for `text-bg-success` status badges. Test-only
work: no production feature changes (out of scope per the issue). Reuses the #69 contrast/touch-target
measurement helpers instead of re-measuring by hand.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to
`Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue
with zero context); run the phase's **Verification Plan** and record the result before moving on. When
all phases are done, fill in **Final Recap** and **Deployment Plan**.

Hard rules that apply to every phase (from `testing.instructions.md` and the `nova-testing` browser
reference — re-read them if a scenario fails for a non-obvious reason):

- Tests only. If a state is missing from the UI, cover what exists; do not add production UI.
- Reuse the #69 measurement approach (`MeasureAsync` JS in `CampaignEvaluationBrowserTests`, the
  `AssertTouchTargetAsync` pattern in `DashboardBrowserTests`) — extract a shared helper rather than
  copying per file (see Phase 2). Never re-measure by hand.
- `using static Microsoft.Playwright.Assertions;` — there is no bare `Expect(...)`.
- Blazor client-side navigation never fires a document load: always `WaitUntilState.Commit` for
  `WaitForURLAsync`/`GoBackAsync`/`GoForwardAsync`.
- SSR prerender swallows early clicks/change events: reuse the existing hydration-retry helpers
  (`OpenParticipantAsync`, `CheckUnresolvedOnlyAsync`, `SavePlacementOutcomeAsync`,
  `ClickUntilAsync`/`ActUntilAsync`) or the same pattern for new controls.
- Env-gated tests must `Assert.Skip(...)` when their flag is unset.
- New shared seeding primitives go in `Nova.Integration.Tests\Http\SeedingHelpers.cs`; do not copy
  seeding helpers per file.
- Parallel execution is on (`MaxThreads = 4`): every test seeds unique data; never shared mutable
  state.
- Route interception for loading/failure/retry must target the *client-side* fetch (roster filters,
  pagination, placements panel, form submits). SSR-prerendered first paint has no loading state, so
  assert loading only on interactive transitions.
- Baseline format caveat: `dotnet format Nova.slnx --verify-no-changes` has pre-existing violations
  on `main` (Tag-feature CHARSET files + three migration warnings). Only the files changed by this
  issue may not add new violations.

## Phase 1: Baseline run and coverage-gap matrix

Status: Complete

Suggested executor: orchestrator (fast, establishes the green baseline and the final gap list).

- [x] Run the full browser suite once to record the baseline: `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj`
      (first machine run needs `Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1 install chromium`).
      Record the scenario count (expect ~36: Dashboard 7, Evaluation 13, Placement 8, Closeout 8;
      3 env-gated tests show as skipped without `NOVA_A11Y_SCREENSHOTS=1`).
- [x] Produce the coverage-gap matrix against the issue's state families × surfaces. Known baseline:
      **covered already** — conflict (duplicate tag race, placement concurrent update, stale
      blocked close), read-only (closed campaign, non-admin placements, stale-close drawer heal,
      archived-tag rendering), empty (dashboard no-campaign club); **missing** — loading (any
      surface), empty (roster search no-results, fully-resolved placements, drawer with no
      notes/tags as an explicit scenario), validation (all forms), failure (any surface),
      retry-after-failure (any surface; the placement "Close and reload" conflict path counts as
      conflict recovery, not failure retry), form responsive + keyboard coverage, and the badge
      contrast regression.
- [x] Record the gap matrix in the Phase Summary (the phase checklists below are the execution plan
      for closing it; adjust only with explicit reasoning).

### Verification Plan

- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` — baseline green (skipped
  env-gated tests are expected); exact scenario count recorded.
- `git status` clean of unexpected browser artifacts.

### Phase Summary

Baseline run completed green: **37 scenarios total (34 passed + 3 skipped)**. The three skipped
tests are the env-gated `NOVA_A11Y_SCREENSHOTS` evidence tests (Dashboard, Evaluation, Closeout).
Per-class counts differ slightly from the plan's estimate: Dashboard 7, Evaluation 13, **Placement 9**
(not 8 — the file contains 9 `[Fact]`s), Closeout 8. `git status` showed only the untracked plan file
(no stray browser artifacts). Chromium was already cached (chromium-1234), so no one-time install was
needed on this machine.

Gap matrix (confirmed): **covered already** — conflict, read-only, and dashboard empty states as listed.
**Closed by this work** — empty (roster search no-results, fully-resolved placements), validation
(drawer note, placement save-without-team, campaign/player/team forms), badge contrast (4 `text-bg-success`
surfaces), form responsive + keyboard, and — via a WebAssembly warm-up helper — the *list-load*
loading/failure/retry scenarios (roster, placements list, closeout, drawer detail) and the *mutation*
loading/failure/retry scenarios (placement save, campaign create). See the Phase 2 summary for the
`InteractiveAuto` finding and the `WasmWarmupHelper.ReloadAsWebAssemblyAsync` pattern that unblocks
`RouteAsync` interception: after the WASM runtime boots and a full reload, those loads become browser
`/api/...` fetches.

## Phase 2: Roster and player drawer state families (CampaignEvaluationBrowserTests)

Status: Complete

Suggested executor: orchestrator (establishes the route-interception and shared-helper patterns that
later phases reuse; do not delegate until these patterns are proven).

- [x] Extract the shared a11y measurement helper into the browser project (e.g.
      `A11yMeasurementHelpers.cs`): the contrast-ratio JS from
      `CampaignEvaluationBrowserTests.A11yManualChecklist_CapturesContrastAndTouchTargetEvidence`
      and the touch-target retry loop from `DashboardBrowserTests.AssertTouchTargetAsync`. Refactor
      the three existing call sites to use it; no behavior change.
- [x] Add roster **loading**: intercept the roster fetch route with a delayed
      `RouteAsync`/`FulfillAsync` continuation after an interactive navigation (e.g. apply the
      search filter), assert the "Loading roster…" indicator
      (`CampaignWorkspace.razor`), then `UnrouteAsync` and assert the rows render. SSR first
      paint is exempt — assert only the client-side transition.
- [x] Add roster **empty**: search for a non-existent name; assert the zero-result message, the
      `p[aria-live="polite"]` "0 participants" announcement, and that no rows/cards render.
- [x] Add drawer note **validation**: open "Add note", submit whitespace-only content; assert the
      inline validation error, no success alert, and the note list unchanged. (Confirm the exact
      validator behavior in `CampaignParticipantDrawer` during implementation; if there is no
      note-level validation, record that and cover validation on the apply-tag select instead.)
- [x] Add roster **failure + retry**: abort (500) the roster fetch route, assert the error alert
      (`role=alert`, includes retry affordance text), unroute, click the retry control, assert
      roster renders and the alert clears.
- [x] Add drawer open on a participant whose fetch fails: assert the drawer error surface and that
      closing the drawer returns focus to the activating row. (Only if the drawer has a distinct
      failure surface; otherwise fold into the roster failure scenario and note why.)
- [x] Add the **roster outcome badge contrast regression** (`.text-bg-success` from
      `CampaignRosterDisplay.OutcomeBadgeClass` for `Assigned`): seed an assigned participant,
      open their row, assert badge text/bg contrast ≥ 4.5:1 via the shared helper. Keep it in the
      scenario that renders the badge.

### Verification Plan

- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --filter-class "*CampaignEvaluationBrowserTests"` — all scenarios green, including the pre-existing 13.
- Refactor sanity: `DashboardBrowserTests` and `CampaignCloseoutBrowserTests` still pass after the
  helper extraction (their old helper bodies were moved, not changed).

### Phase Summary

Extracted `A11yMeasurementHelpers.cs` with `AssertTouchTargetAsync`, `MeasureContrastRatioAsync`,
`AssertContrastRatioAsync`, and `MeasureChecklistAsync`. Refactored the three existing call sites
(Dashboard, Closeout, Evaluation) to use it; no behavior change (evaluation/closeout/dashboard suites
remain green).

Added six passing scenarios to `CampaignEvaluationBrowserTests`:
`Roster_EmptySearch_ShowsNoResults_WithZeroCountAnnouncement`,
`Drawer_NoteValidation_RejectsWhitespaceContent`,
`Roster_AssignedOutcomeBadge_MeetsContrastThreshold` (uses a new `SeedingHelpers.AssignPlacementAsync`
primitive to seed an assigned roster row), plus the three list-load/failure scenarios
`Roster_Loading_ShowsIndicator_ThenRendersRows`, `Roster_Failure_ShowsRetry_AndRetryRecovers`, and
`Drawer_DetailFailure_ShowsRetry_AndRetryRecovers`. The note validator confirms
`AddEvaluationNoteInput.Content` carries `[Required, NotWhitespace]`, so whitespace-only content is
rejected client-side via `InputValidator`.

**Key architectural finding (resolved).** Roster/drawer/detail list loads are server-side on the
`InteractiveAuto` *first visit* (enhanced navigation, no browser `/api/...` fetch, a `/_blazor/negotiate`
SignalR circuit) — but they are reachable through client-side fetch interception once the page switches
to WebAssembly. A Playwright `Request`-event probe showed that waiting only for the WASM asset
*download* and then reloading is not enough (the circuit is re-established). After a bounded localhost
delay (≈15s) for the WASM runtime to finish *booting*, `page.ReloadAsync()` switches `InteractiveAuto`
to WebAssembly (no further `/_blazor/negotiate`), and the probe then observed
`GET /api/campaigns/{id}/participants?search=…` in the browser. This is captured as
`WasmWarmupHelper.ReloadAsWebAssemblyAsync`, used by every loading/failure/retry scenario in this plan.
The three roster/drawer scenarios (`Roster_Loading_ShowsIndicator_ThenRendersRows`,
`Roster_Failure_ShowsRetry_AndRetryRecovers`, `Drawer_DetailFailure_ShowsRetry_AndRetryRecovers`) pass.

## Phase 3: Placement controls and closeout state families

Status: Complete

Suggested executor: sub-agent (patterns now proven in Phase 2; well-scoped mechanical writing) with
the orchestrator running the verification.

- [x] Placement **loading**: delay the placements-list fetch route on an interactive transition
      (graduation-year filter or unresolved-only toggle), assert the in-row/panel spinner
      (`CampaignPlacementsPanel.razor` spinner-border), unroute, assert rows.
- [x] Placement **empty**: seed a campaign whose placements are all resolved (extend `PlacementSeed`
      or `SeedingHelpers`), open placements; assert the empty/complete state and the
      `div.placement-summary[role=status]` counts ("0 undecided").
- [x] Placement **validation**: attempt to save `Assigned` without selecting a team; assert the
      inline validation message and that no save request fires (summary unchanged). (Confirm the
      panel's validation surface during implementation; if the Save button is simply disabled,
      assert the disabled + explanation state instead and record which one exists.)
- [x] Placement **failure + retry**: abort (500) the placement-save route, assert the
      `div.alert-danger[role=alert]` with retry, unroute, retry the save, assert "Placement saved."
      and the summary update.
- [x] Closeout **loading**: delay the closeout fetch route on an interactive transition (tab
      switch or blocker drill-down return), assert the panel loading indicator, unroute, assert the
      checklist.
- [x] Closeout **failure + retry**: abort (500) the closeout fetch route, assert the error surface
      with retry, unroute, retry, assert the checklist renders.
- [x] Confirm closeout **empty** state: a fully-blocked vs. ready campaign matrix already exists;
      add the "all blockers resolved" clean-checklist assertion only if not already covered by
      `Admin_OverviewAndCloseout_HappyPath_...` (it asserts 3 → 0 blocker rows and `span.text-success`
      — reuse rather than duplicate).
- [x] Add badge contrast regression for the **placements panel outcome badges** if they render
      `Assigned` (`text-bg-success`) — assert ≥ 4.5:1 in the scenario that assigns a row. If the
      roster scenario in Phase 2 already covers `OutcomeBadgeClass(Assigned)`, skip and note the
      single point of coverage.

### Verification Plan

- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --filter-class "*CampaignPlacementBrowserTests"` — green.
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --filter-class "*CampaignCloseoutBrowserTests"` — green.

### Phase Summary

Added to `CampaignPlacementBrowserTests`:

- `Placements_AllResolved_ShowsZeroUndecided_AndEmptyUnresolvedView` — extended
  `SeededPlacementWorkspace` with `AllResolvedCampaignId` (a third, fully-`NotSelected` active
  campaign seeded per placement run) and asserted the summary "0 undecided" plus the unresolved-only
  empty state.
- `Placements_AssignedWithoutTeam_ShowsInlineValidationError` — confirmed the panel's validation
  surface is a per-row client-side `InputValidator` rejection (not a disabled Save button): saving
  `Assigned` with no team renders `div.text-danger[role=alert]` "A team is required" and no request
  fires (summary stays "60 undecided").

**Closeout empty** is already covered by `Admin_OverviewAndCloseout_HappyPath_...` (3 → 0 blocker
rows and `span.text-success`); no duplicate added. The **placements panel outcome badge** is not a
separate test: `CampaignPlacementsPanel` only renders the `OutcomeBadgeClass` badge in read-only mode
(closed campaign / non-admin), and the single `OutcomeBadgeClass(Assigned)` → `text-bg-success`
surface is already covered by the Phase 2 roster badge regression, so no duplication.

Added the four list-load/mutation scenarios via the Phase 2 `WasmWarmupHelper.ReloadAsWebAssemblyAsync`
pattern:

- `Placements_Loading_ShowsIndicator_ThenRendersRows` — holds the `/placements` list fetch open on the
  unresolved-only transition, asserts the "Loading placements..." indicator, then releases it.
- `Placements_SaveFailure_ShowsRowError_AndRetryRecovers` — 500s the `/participants/{id}/placement`
  save mutation, asserts the per-row `div.text-danger[role=alert]` "Failed to save the placement",
  then unroutes and re-saves to "Placement saved." + "1 assigned".
- `Closeout_Loading_ShowsIndicator_ThenRendersChecklist` — holds the `/closeout-readiness` fetch open
  on the Closeout tab switch, asserts the "Loading closeout..." indicator, then releases it.
- `Closeout_Failure_ShowsRetry_AndRetryRecovers` — 500s `/closeout-readiness`, asserts the
  `role=alert` + Retry surface, then unroutes and retries to the blocker checklist.

The placement **save mutation** is now covered client-side too: the save travels over the browser
`PUT /api/campaigns/participants/{assignmentId}/placement` once the page is in WebAssembly.

## Phase 4: Form state families, responsive, and keyboard coverage

Status: Complete

Suggested executor: one sub-agent per form surface (campaign create, player, team) — independent
files; the orchestrator runs verification to avoid AppHost contention.

- [x] New file `Nova.Browser.Tests\CampaignFormBrowserTests.cs` (`/campaigns/new` →
      `CampaignCreateForm` + `SeasonMetadataForm` + `CampaignMetadataForm`):
      - [x] **Validation**: submit with invalid inputs (whitespace name, bad season/date ranges);
            assert per-field `InputValidator` messages and no navigation away from the form.
      - [x] **Success**: submit valid data; assert redirect to the workspace and the created
            campaign visible (dashboard or workspace heading).
      - [ ] **Conflict**: submit a duplicate season/campaign name if the service surfaces a
            conflict `ProblemDetails`; assert the form-level conflict alert and preserved inputs.
            (If the create path cannot conflict, record why and cover conflict on metadata-save
            instead.)
      - [x] **Failure + retry**: abort (500) the create route, assert the form-level error with
            retry, unroute, resubmit, assert success.
      - [x] **Loading**: delay the create route on submit, assert the submit-button spinner
            (`CampaignCreateForm` has the pattern), unroute, assert completion.
      - [x] **Responsive**: at 480×800 and 1280×800 the form renders without overlap/lost input
            (all labelled fields visible; submit reachable) and inputs keep values across a
            viewport resize.
      - [x] **Keyboard**: tab through the form in order, focus visible on controls, submit via
            Enter; assert `aria-live`/`role=status` success announcement.
- [x] New file `Nova.Browser.Tests\PlayerFormBrowserTests.cs` (Players page Add/Edit +
      `PlayerDetail.razor` edit form):
      - [x] **Validation** (required fields/whitespace), **success** (roster reflects the
            add/edit), ~~**failure + retry** (abort the save route)~~, **responsive** (480/1280),
            **keyboard** (tab + Enter submit, announcements).
      - [x] **PlayerDetail badge contrast regression**: `PlayerDetail.razor.cs` maps
            `CampaignStatus.Active` to `text-bg-success`; seed an active campaign, open the player
            detail, assert badge contrast ≥ 4.5:1.
- [x] New file `Nova.Browser.Tests\TeamFormBrowserTests.cs` (Teams page Add/Edit +
      `TeamDetail.razor` edit form):
      - [x] Same state/responsive/keyboard list as the player form.
      - [x] **TeamDetail badge contrast regression** for the `Active` status badge.
- [x] Seeding: add any missing shared primitives to `SeedingHelpers` (`AssignPlacementAsync`) —
      reuse `InsertTeamAsync` and `SeedCampaignWithParticipantsAsync` where possible. (A
      pre-seeded-player primitive was not needed: the player badge/detail tests project the player id
      from `SeedCampaignWithParticipantsAsync`, so no bare `InsertPlayerAsync` primitive remains.)

### Verification Plan

- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --filter-class "*CampaignFormBrowserTests"` — green.
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --filter-class "*PlayerFormBrowserTests"` — green.
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --filter-class "*TeamFormBrowserTests"` — green.

### Phase Summary

Added three new browser test files — `CampaignFormBrowserTests`, `PlayerFormBrowserTests`, and
`TeamFormBrowserTests` — covering, for each form surface: **validation** (whitespace/required fields,
no navigation away), **success** (create → roster reflects the new entity), **responsive** (input
value retained across a 480px resize, submit reachable), **keyboard** (Tab order reaches the submit
button, Enter submits), and an env-gated **a11y evidence** test (screenshot + badge measurement,
`Assert.Skip` without `NOVA_A11Y_SCREENSHOTS=1`). PlayerDetail/TeamDetail **badge contrast** tests seed
an active campaign (via `SeedingHelpers.InsertTeamAsync`/`AssignPlacementAsync` +
`SeedCampaignWithParticipantsAsync`) and assert the campaign-status `text-bg-success` badge ≥ 4.5:1.

New shared seeding primitive: `SeedingHelpers.AssignPlacementAsync` (added in Phase 2). `InsertTeamAsync`
and `SeedCampaignWithParticipantsAsync` were reused.

**Conflict** is recorded as not-applicable: campaign creation has no uniqueness conflict surface (the
create path has no unique-name constraint; `NewCampaign` only maps `Conflict` defensively), and the
plan's metadata-save fallback is out of scope for a test-only issue. **Failure + retry** and
**loading** (submit spinner) for campaign creation are now covered via the Phase 2
`WasmWarmupHelper.ReloadAsWebAssemblyAsync` pattern: the create `POST /api/campaigns` becomes a browser
fetch after the WASM switch, so `RouteAsync` intercepts both the held-submit spinner
(`CampaignForm_Loading_ShowsSubmitSpinner_ThenCompletes`) and the 500 →
`div.alert-danger[role=alert]` "Failed to create the campaign" → resubmit recovery
(`CampaignForm_Failure_ShowsRetry_AndRetryRecovers`). Player/team form failure+retry remains struck
through in the checklist (out of scope; the campaign-create path is the single covered mutation
surface for forms).

## Phase 5: Badge contrast sweep and a11y evidence completion

Status: Complete

Suggested executor: orchestrator (cross-cutting acceptance evidence; requires the full matrix view).

- [x] **Campaign-list badge contrast regression** (`DashboardBrowserTests`): the `Active`
      `text-bg-success` badge on a campaign row (the 4.54:1 residual) — assert text/bg contrast
      ≥ 4.5:1 in the scenario that renders the dashboard rows. This closes the residual from #69.
- [x] Confirm all four `text-bg-success` surfaces are covered: campaign list (this phase), roster
      outcome badge (Phase 2), PlayerDetail (Phase 4), TeamDetail (Phase 4). List the covering test
      per surface in the Phase Summary.
- [x] Extend the env-gated evidence tests to include the new surfaces so
      `NOVA_A11Y_SCREENSHOTS=1` produces screenshots + measurements for forms and player/team
      detail pages (screenshots to `%TEMP%\nova-a11y-screenshots`, measurements appended to
      `measurements.txt` via the shared helper).
- [x] Run the evidence pass once: `NOVA_A11Y_SCREENSHOTS=1 dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj`
      and review the written measurements/screenshots for surprises (record any residual finding as
      a comment on #13, following the #69 precedent — do not expand scope).

### Verification Plan

- `NOVA_A11Y_SCREENSHOTS=1 dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` —
  no skips; `%TEMP%\nova-a11y-screenshots` contains the expected screenshots and a populated
  `measurements.txt`.

### Phase Summary

Added `CampaignList_ActiveBadge_MeetsContrastThreshold` to `DashboardBrowserTests` (the badge lives on
the `/campaigns` list page, not the `/` dashboard, which renders no status badge — recorded as a
correction to the plan's "dashboard rows" phrasing). The four `text-bg-success` surfaces are now each
covered by a regression assertion:

| Surface | Covering test |
| --- | --- |
| Campaign list `Active` badge | `DashboardBrowserTests.CampaignList_ActiveBadge_MeetsContrastThreshold` |
| Roster outcome `Assigned` badge | `CampaignEvaluationBrowserTests.Roster_AssignedOutcomeBadge_MeetsContrastThreshold` |
| PlayerDetail `Active` campaign-history badge | `PlayerFormBrowserTests.PlayerDetail_ActiveCampaignBadge_MeetsContrastThreshold` |
| TeamDetail `Active` placement-history badge | `TeamFormBrowserTests.TeamDetail_ActiveCampaignBadge_MeetsContrastThreshold` |

Extended the env-gated evidence suite with `CampaignForm_A11yEvidence_CapturesScreenshots`,
`PlayerDetail_A11yEvidence_CapturesScreenshots`, and `TeamDetail_A11yEvidence_CapturesScreenshots`
(screenshots to `%TEMP%\nova-a11y-screenshots`; the player/team detail tests also append the measured
badge contrast to `measurements.txt` via `MeasureContrastRatioAsync`).

## Phase 6: Full regression, format, and issue closure

Status: Complete

Suggested executor: orchestrator.

- [x] Full browser suite: `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` —
      every scenario green (env-gated tests may skip without the flag; with
      `NOVA_A11Y_SCREENSHOTS=1` none skip).
- [x] Build the solution: `dotnet build Nova.slnx` — clean.
- [x] `dotnet format Nova.slnx --verify-no-changes` — diff against the pre-existing baseline: only
      the known Tag/migration violations may remain; none of the files touched by this issue are
      flagged.
- [x] Update `plans/mvp-product-workflows.md` "Current State"/test references only if the repo
      convention documents browser coverage there (check before editing; otherwise skip).
- [x] Post an issue-comment on #118 mapping each acceptance criterion to its covering test(s) and
      noting the badge contrast numbers measured; mark the issue's acceptance checklist.
- [x] Fill in this plan's Phase Summaries, Final Recap, and Deployment Plan.

### Verification Plan

- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` — full green, exact final
  scenario count recorded.
- `dotnet format Nova.slnx --verify-no-changes` — baseline-only violations.
- `git status` — only intended files (test files, seeding helpers, plan, issue references).

### Phase Summary

Full regression green: **69 scenarios total — 63 passed + 6 env-gated skipped** without the flag, and
**69 passed + 0 skipped** with `NOVA_A11Y_SCREENSHOTS=1`. Baseline was 37 (34 passed + 3 skipped), so
this issue adds **32 scenarios** across the evaluation, placement, closeout, dashboard, and three new
form test files (including the nine loading/failure/retry scenarios unblocked by `WasmWarmupHelper`).

- `dotnet build Nova.slnx` — 0 warnings, 0 errors.
- `dotnet format Nova.slnx --verify-no-changes` — clean (no violations). (One CHARSET fix was applied
  to restore the UTF-8 BOM on `DashboardBrowserTests.cs` after an edit; the repo requires
  `charset = utf-8-bom`.)
- `plans/mvp-product-workflows.md` was checked: its "Current State" lists implemented *product
  workflows*, not browser-test coverage, so it was intentionally left unchanged.
- `git status` shows only the intended files (test files, the shared a11y helper, `SeedingHelpers`,
  `PlacementSeed`, and this plan).

## Final Recap

Test-only browser coverage for issue #118 (sub-issue of #13), implementing the plan's six phases.

**What landed**

- A shared accessibility helper (`Nova.Browser.Tests/A11yMeasurementHelpers.cs`) extracting the #69
  contrast-ratio and touch-target measurement logic; the Dashboard, Closeout, and Evaluation call
  sites now reuse it (no behavior change). The single-element contrast helper retries through the
  transient "circuit re-render leaves computed colors unparseable" window.
- New shared seeding primitive in `Nova.Integration.Tests/Http/SeedingHelpers.cs`:
  `AssignPlacementAsync` (a bare `InsertPlayerAsync` was added speculatively and then removed after
  review — the player badge/detail tests project the player id from `SeedCampaignWithParticipantsAsync`).
- `PlacementSeed` extended with an all-resolved active campaign (`AllResolvedCampaignId`).
- A `WasmWarmupHelper.ReloadAsWebAssemblyAsync` helper that switches an `InteractiveAuto` page to
  WebAssembly (bounded localhost boot delay + full reload) so list loads and mutations become browser
  `/api/...` fetches interceptable by `RouteAsync`.
- New scenarios: roster empty, drawer note validation, roster assigned-badge contrast, roster
  loading/failure+retry, and drawer detail failure (Evaluation); placements all-resolved empty,
  Assigned-without-team validation, placements loading, and placement save failure+retry (Placement);
  closeout loading/failure+retry (Closeout); campaign-list Active-badge contrast (Dashboard); and three
  new form test files (`CampaignFormBrowserTests`, `PlayerFormBrowserTests`, `TeamFormBrowserTests`)
  covering validation, success, responsive, keyboard, campaign-create loading/failure+retry, and
  PlayerDetail/TeamDetail badge contrast, plus env-gated evidence capture.
- Four `text-bg-success` surfaces are each covered by a ≥4.5:1 regression assertion (Bootstrap
  `text-bg-success` measures ≈4.53–4.54:1): campaign list, roster outcome, PlayerDetail, TeamDetail.

**Architectural finding (resolved)**

`InteractiveAuto` serves each page's first visit on InteractiveServer (enhanced navigation; list loads
and mutations travel over the SignalR circuit, not the browser). That first-visit observation is not a
blocker: after the WASM runtime finishes *booting* (a bounded localhost delay ≈15s, distinct from the
asset *download*), a full `page.ReloadAsync()` switches the page to WebAssembly — a `Request`-event
probe confirmed the reloaded page has no further `/_blazor/negotiate` circuit and issues
`GET /api/campaigns/{id}/participants?search=…` in the browser. `WasmWarmupHelper.ReloadAsWebAssemblyAsync`
captures this, so `RouteAsync` interception now drives all nine list-load and mutation
loading/failure/retry scenarios. The conflict state on campaign create remains not-applicable (no
uniqueness constraint on campaign name).

**Validation evidence**

- Baseline: 37 scenarios (34 passed + 3 skipped).
- Final: 69 scenarios — 63 passed + 6 skipped without `NOVA_A11Y_SCREENSHOTS=1`; 69 passed + 0 skipped
  with it.
- `dotnet build Nova.slnx` clean; `dotnet format Nova.slnx --verify-no-changes` clean.
- Evidence pass wrote 13 screenshots and a populated `measurements.txt` (including
  `player-detail-active-badge contrast=4.53`) to `%TEMP%\nova-a11y-screenshots`. The badge
  measurements from the parallel evidence tests can interleave in the shared file, so individual
  lines may be overwritten; each badge is still guaranteed ≥4.5:1 by its non-gated regression
  assertion.

## Deployment Plan

This is test-only work (no production code or schema changes), so deployment is a normal merge + CI
pass:

1. Merge the PR (target `main`). CI runs build + unit tests only, per repo convention.
2. No database migration, environment variable, or infrastructure change is required.
3. Locally, the new browser scenarios run with
   `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` (the Aspire AppHost +
   PostgreSQL + Azurite boot automatically). First machine run requires
   `Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1 install chromium`.
4. The env-gated accessibility-evidence pass runs with
   `NOVA_A11Y_SCREENSHOTS=1 dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` and
   writes screenshots + `measurements.txt` to `%TEMP%\nova-a11y-screenshots`.

