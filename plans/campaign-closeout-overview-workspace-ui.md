# Campaign Closeout and Overview Workspace UI

Activate the workspace **Overview** and **Closeout** tabs and the header **Campaign menu** on
`/campaigns/{id}` (issue #101, sub-issue of epic #12), replacing the disabled "Coming soon"
placeholders. Pure UI slice: consumes the completed read slice (#102) and close/reopen mutation
slice (#104) contracts as-is — no new endpoints, contracts, entities, or migrations, and readiness
is never recalculated in the UI.

Issue: https://github.com/eruvalca/Nova/issues/101. Prerequisites #102 (read slice) and #104
(mutation slice) are merged; their contracts exist in `Nova.Shared/Features/Campaigns`:
`CampaignCloseoutContracts.cs`, `CampaignActivityContracts.cs`, `ICampaignCloseoutQueryService`,
`ICampaignLifecycleService`, plus the extended `CampaignDetailResult` (closure fields).

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to
`Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue
with zero context); run the phase's **Verification Plan** and record the result before moving on.
When all phases are done, fill in **Final Recap** and **Deployment Plan**.

Repo conventions that govern this work (read before editing):

- `.github/instructions/blazor-architecture.instructions.md` — `Nova.UI` feature folders, code-behind
  pairs, `NovaComponentBase`, primary-constructor DI, `EventCallback` (never `Action`), lifecycle
  selection, `[PersistentState]` prerender/attach pattern, collocated JS rules, Bootstrap-first styling.
- `.github/instructions/testing.instructions.md` — bUnit in `Nova.Unit.Tests` (xUnit v4/MTP/Shouldly,
  `Subject_Outcome_Condition`, `[Theory(IncludeTestCaseIndex = true)]`), render-mode assertions.
- `.github/instructions/functional-core.instructions.md` — the UI presents the foundation policy's
  blocker details verbatim (`CloseoutBlockerConditions` keys) and never re-derives readiness rules.
- `.github/instructions/csharp-conventions.instructions.md` — XML docs on public members, style.

Skills (step-by-step recipes): `add-blazor-ui` (component build steps), `nova-testing` (bUnit setup
and run commands).

### Design decisions (confirmed with user 2026-08-19 — do not revisit)

1. **Blocker drill-down** = checklist rows per condition key (`outcomes` / `eligibility` /
   `archivedTeams`) showing the policy `Count` + `Message` verbatim, each with a **Review unresolved**
   action navigating into the Placements tab: `outcomes` → `unresolvedOnly=true`; `eligibility` and
   `archivedTeams` → default placements view. **Raw assignment ids are never rendered.**
2. **Edit metadata** opens the inline `CampaignMetadataForm` in the workspace header (reusing #56's
   component and save flow); season choices load via `ICampaignCreationService.GetSetupAsync()`
   (bounded), with the campaign's current season prepended when omitted (list-page pattern).
3. **Close confirmation** = the Closeout readiness checklist itself (Close disabled until ready);
   no extra modal. **Reopen** keeps a confirm dialog.
4. **Closed-state Closeout tab** = closure metadata (`Closed {date} by {admin}`), final outcome
   summary stat blocks (from `readiness.Summary`), muted read-only banner, and Reopen. The screen
   design's "per-team roster" is **deferred** — no contract exists and new query contracts are out
   of scope.
5. **Campaign menu** is a native, keyboard-operable disclosure (button + `aria-expanded` + `ul`,
   Escape closes) — not Bootstrap's JS dropdown — so bUnit can drive it without JS interop. Items:
   **Edit metadata** (admin, Active only), **Close campaign** (admin, Active → Closeout tab),
   **Reopen** (admin, Closed → Closeout tab). Menu close/reopen items navigate; the action buttons
   with confirmation UX live on the Closeout tab.
6. New panels follow the `CampaignPlacementsPanel` pattern exactly: primary-constructor service
   injection, `[PersistentState]` public props + `Initialized` guard, `EventCallback` for all
   parent interactions, navigation owned by the page.

### Key facts discovered (do not re-derive)

- Tab activation is URL-driven: `CampaignWorkspaceUrlState.ValidTabs` gates `NormalizeTab`; the page
  re-derives `_activeTab` from `TabQuery` on every `OnParametersSet`, and tab clicks perform
  client-side query-only navigation.
- The workspace already loads `CampaignDetailResult` (with `ClosedAt`, `ClosedByUserId`,
  `ClosedByDisplayName`), derives `_isClubAdmin`, and gates placements editing via `_canEditPlacements`.
- `CampaignCloseoutReadinessDto` embeds `CampaignPlacementSummaryDto` and condition-keyed blockers;
  `GetActivityAsync` returns up to 50 newest-first lifecycle events (`Closed`/`Reopened` verb rows).
- Close/reopen return `ServiceResult<Success>`; a blocked close surfaces as
  `ServiceProblem.Conflict(detail, errors)` with the structured 409 `errors` extension.
- DI is already complete in both tiers for every service this slice needs
  (`ICampaignCloseoutQueryService`, `ICampaignLifecycleService`, `ICampaignMetadataService`,
  `ICampaignCreationService`) — **no DI or Program.cs changes**.
- Ripple: adding two constructor services to `CampaignWorkspace` requires every existing
  `CampaignWorkspaceTests` render to register two more `Substitute.For<>()` services, and the test
  `CampaignWorkspace_ShowsEvaluateActive_AndOtherTabsDisabled` must be rewritten (tabs become enabled).

---

## Phase 1: Workspace URL-state extensions

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator (small, convention-sensitive)

- [x] In `Nova.UI/Features/Campaigns/Services/CampaignWorkspaceUrlState.cs`: add `OverviewTab = "overview"`
      and `CloseoutTab = "closeout"` constants; add both to `ValidTabs`; add `BuildOverviewWorkspaceUrl(long campaignId)`
      and `BuildCloseoutWorkspaceUrl(long campaignId)` returning `/campaigns/{id}?tab=overview|closeout`
      (tab-only query, no roster/placement params); add `BuildReviewUnresolvedUrl(long campaignId)`
      reusing `BuildPlacementsWorkspaceUrl` with `new CampaignWorkspacePlacementState { UnresolvedOnly = true }`.
- [x] In `Nova.Unit.Tests/Campaigns/CampaignWorkspaceUrlStateTests.cs`: add round-trip/normalization
      tests for the two new tab tokens (including `NormalizeTab("overview"/"closeout")`, case-insensitive,
      unknown still falls back to evaluate) and tests for the three new URL builders (exact query shape:
      only `tab=` for overview/closeout; `unresolvedOnly=true&tab=placements` for review-unresolved).

### Verification Plan

- `dotnet build Nova.slnx` — zero errors.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignWorkspaceUrlStateTests"` — all green.

### Phase Summary

Added `OverviewTab`/`CloseoutTab` constants, extended `ValidTabs`, and added the three URL builders
(`BuildOverviewWorkspaceUrl`, `BuildCloseoutWorkspaceUrl`, `BuildReviewUnresolvedUrl`). Updated the
normalization theory (now recognizes `overview`/`closeout` case-insensitively; unknown still falls back
to `evaluate`) and added exact-shape builder tests. Verification: build 0 errors; URL-state tests 36/36 green.

---

## Phase 2: CampaignOverviewPanel component

Status: Complete

Suggested executor: orchestrator (new interactive component; markup + state decisions)

- [x] Create `Nova.UI/Features/Campaigns/Components/CampaignOverviewPanel.razor` + `.razor.cs`:
  - Primary constructor: `ICampaignCloseoutQueryService` (readiness embeds the summary; activity via
    the same service). Parameters: `long CampaignId`, `CampaignDetailResult Detail`, `bool IsClubAdmin`,
    `EventCallback OnOpenCloseout` (admin/Active "Open closeout" link → Closeout tab).
  - `[PersistentState]` public props: `PersistedReadiness`, `PersistedActivity`, `PersistedError`,
    `Initialized`; `OnInitializedAsync` restores persisted state when `Initialized`, otherwise loads
    readiness + activity (limit `GetCampaignActivityInput.DefaultLimit`) in parallel with
    `ComponentCancellationToken`, persists, sets `Initialized`.
  - Markup: snapshot card (season name + `FormatCampaignDates`-style date range + enrollment count
    from `Detail`), outcome summary row (four stat blocks from `readiness.Summary`), closeout-readiness
    line (ready vs blocked summary) with **Open closeout** link rendered only when `IsClubAdmin &&
    Detail.Status == Active`, activity feed (newest-first rows: `{date} {actor} {verb}` where verb is
    "closed the campaign" / "reopened the campaign" from `EventType`), empty-feed state, loading
    spinner, error `alert-danger` + Retry. Retry helper preserves the `Initialized`-guard semantics
    (explicit user-triggered reload helper).
- [x] Create `Nova.Unit.Tests/Campaigns/CampaignOverviewPanelTests.cs` (bUnit + NSubstitute):
      snapshot fields render; summary counts render exactly; readiness link shown for admin+Active,
      absent for non-admin and absent when Closed; activity rows newest-first with verb/date/actor;
      empty activity state; load error + Retry recovers; persisted-state restore skips refetch;
      `OnOpenCloseout` fires on link click.

### Verification Plan

- `dotnet build Nova.slnx` — zero errors.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignOverviewPanelTests"` — all green.

### Phase Summary

Created `CampaignOverviewPanel` (`.razor` + `.razor.cs`) following the panel pattern (primary-constructor
DI, `[PersistentState]` public props + `Initialized` guard, `EventCallback` for parent interaction).
Loads readiness + activity in parallel; renders snapshot fields, four outcome stat blocks, the
ready/blocked line with the admin+Active "Open closeout" link, and the newest-first activity feed with
empty/loading/error states. Added `CampaignOverviewPanelTests` (10 tests, all green).

---

## Phase 3: CampaignCloseoutPanel component

Status: Complete

Suggested executor: orchestrator (close/reopen state machine and blocker presentation)

- [x] Create `Nova.UI/Features/Campaigns/Components/CampaignCloseoutPanel.razor` + `.razor.cs`:
  - Primary constructor: `ICampaignCloseoutQueryService`, `ICampaignLifecycleService`. Parameters:
    `long CampaignId`, `CampaignDetailResult Detail`, `bool IsClubAdmin`,
    `EventCallback<bool> OnReviewUnresolved` (carries the `unresolvedOnly` flag for the target
    placements URL), `EventCallback OnCancel` (→ Evaluate tab), `EventCallback OnReloadRequested`
    (page reloads detail after a successful transition).
  - `[PersistentState]` props: `PersistedReadiness`, `PersistedError`, `Initialized` (panel pattern).
    Readiness loads for **both** Active and Closed campaigns (Closed naturally yields zero blockers).
  - **Active view**: readiness checklist built strictly from `readiness.Blockers` keyed by
    `CloseoutBlockerConditions` — enrolled row (`Summary.TotalCount`), final-outcomes row
    (`TotalCount - UndecidedCount`), undecided row, eligibility row, archived-teams row; each blocked
    row shows the policy `Count` + `Message` verbatim and a **Review unresolved** button calling
    `OnReviewUnresolved` with `true` for `outcomes` and `false` otherwise. Explanatory text
    "Closing freezes notes, tags, outcomes, and placements." **Close campaign** `btn-primary` disabled
    when `!readiness.IsReady || _isClosing || !IsClubAdmin || Status != Active`; **Cancel** button.
  - **Close flow**: pending (`_isClosing`, spinner, buttons disabled) → `ICampaignLifecycleService.CloseAsync`;
    success → success message (`role=status`) + `OnReloadRequested`; `Conflict` → `alert-warning` with
    the problem detail (or fallback text) + re-fetch readiness to surface fresh blockers; other
    failures → `alert-danger` with detail. Never re-derive readiness from the conflict payload.
  - **Closed view**: `Closed {date} by {admin}` from `Detail` closure fields, final outcome summary
    stat blocks from `readiness.Summary`, muted read-only banner, **Reopen campaign**
    `btn-outline-warning` (admin only) behind an inline confirm panel (Cancel/Confirm — native
    markup, not a Bootstrap modal) explaining reopening restores editing without discarding outcomes
    and is recorded for audit. Reopen flow mirrors close: pending → `ReopenAsync` → success message +
    `OnReloadRequested`; conflict/failure alerts.
- [x] Create `Nova.Unit.Tests/Campaigns/CampaignCloseoutPanelTests.cs` (bUnit + NSubstitute):
      checklist renders enrolled/final-outcomes/undecided/eligibility/archived-teams rows with exact
      counts and policy messages when blocked; all-clear state shows satisfied rows; Close disabled
      when `IsReady == false` and for non-admin; Close success shows success message and fires
      `OnReloadRequested`; Close conflict shows blocker/conflict alert and re-fetches readiness;
      pending state disables buttons while in flight; closed view shows closure metadata, summary
      blocks, read-only banner; Reopen hidden for non-admin; Reopen confirm requires confirmation
      (Cancel is a no-op); Reopen success fires `OnReloadRequested`; `OnReviewUnresolved` receives
      `true` for the outcomes row and `false` for eligibility/archived-teams rows.

### Verification Plan

- `dotnet build Nova.slnx` — zero errors.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignCloseoutPanelTests"` — all green.

### Phase Summary

Created `CampaignCloseoutPanel` (`.razor` + `.razor.cs`) with the Active readiness checklist (5 rows:
enrolled, final-outcomes, undecided, eligibility, archived-teams — blocked rows render the policy
`Count` + `Message` verbatim plus a Review unresolved button), the close state machine (pending/
success/conflict-refetch/failure), and the Closed view (closure metadata, summary stat blocks, read-only
banner, admin Reopen behind an inline confirm panel). Added `OnParametersSetAsync` status-change
readiness refetch so a close/reopen transition surfaces fresh readiness. Added `CampaignCloseoutPanelTests`
(12 tests, all green).

---

## Phase 4: CampaignMenu component

Status: Complete

Suggested executor: orchestrator (small, a11y-sensitive)

- [x] Create `Nova.UI/Features/Campaigns/Components/CampaignMenu.razor` + `.razor.cs` (+ `.razor.css`
      for dropdown positioning):
  - Parameters: `bool IsClubAdmin`, `bool IsClosed`, `EventCallback OnEditMetadata`,
    `EventCallback OnCloseCampaign`, `EventCallback OnReopen`.
  - Native disclosure: **Campaign menu** button (`aria-haspopup="menu"`, `aria-expanded` toggled);
    `ul role="menu"` with `li role="none"` items — **Edit metadata** (admin + Active),
    **Close campaign** (admin + Active), **Reopen** (admin + Closed). Escape closes; selecting an
    item closes the menu and invokes its callback; re-render keeps `aria-expanded` accurate. No JS
    module (pure Blazor events); scoped CSS only for the absolute-positioned menu.
- [x] Create `Nova.Unit.Tests/Campaigns/CampaignMenuTests.cs`: toggle flips `aria-expanded`; menu
      items absent entirely for non-admin; Edit/Close absent when Closed; Reopen absent when Active;
      Escape closes; item click closes and invokes the right callback.

### Verification Plan

- `dotnet build Nova.slnx` — zero errors.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignMenuTests"` — all green.

### Phase Summary

Created `CampaignMenu` (`.razor` + `.razor.cs` + `.razor.css`) as a native disclosure (button +
`aria-expanded` + `ul role="menu"`, Escape closes, no JS). The menu renders only for club administrators;
items are gated by status (Edit metadata / Close campaign for Active, Reopen for Closed). Scoped CSS
positions the Bootstrap `dropdown-menu` below the trigger. Added `CampaignMenuTests` (7 tests, all green).

---

## Phase 5: Workspace shell integration and edit metadata

Status: Complete

Suggested executor: orchestrator (page-level state and constructor ripple; then delegate mechanical
test-update sweeps if desired)

- [x] `CampaignWorkspace.razor`: replace the two disabled "Coming soon" tab spans with real tab
      buttons (`role="tab"`, `aria-selected`, `SelectOverviewTabAsync` / `SelectCloseoutTabAsync`);
      render `CampaignOverviewPanel` and `CampaignCloseoutPanel` regions when active (passing
      `CampaignId`, `Detail`, `_isClubAdmin`, and the EventCallbacks); add `CampaignMenu` to the
      header card (next to the status badge).
- [x] `CampaignWorkspace.razor.cs`:
  - Add `OverviewTabName` / `CloseoutTabName` constants; `SelectOverviewTabAsync` /
    `SelectCloseoutTabAsync` using the Phase 1 URL builders (query-only navigation, no-op when the
    current `TabQuery` already matches).
  - Navigation callbacks: `OnOpenCloseoutAsync` (→ closeout URL), `OnReviewUnresolvedAsync(bool)`
    (→ placements URL with `UnresolvedOnly`), `OnCancelCloseoutAsync` (→ evaluate URL preserving
    current roster state).
  - Edit metadata: new constructor services `ICampaignMetadataService`, `ICampaignCreationService`;
    state `_editCampaignForm` (`CampaignMetadataFormState`), `_seasonChoices`, `_seasonChoiceTotalCount`,
    `_editFormSeasonChoices`, `_isMutating`, `_mutationError`, `_mutationConflict`, `_editVersion`.
    Menu **Edit metadata** (Active + admin): build form state from `_detail` (add a
    `CampaignMetadataFormState.FromDetail(CampaignDetailResult)` factory — same fields as
    `FromListItem`), load bounded season choices via `ICampaignCreationService.GetSetupAsync()` with
    the current-season prepend fallback (list-page pattern), render `CampaignMetadataForm` in the
    header area. Save: `ICampaignMetadataService.UpdateAsync(model.ToUpdateInput())`; success → close
    form, status message preserved across the `LoadDetailAsync()` refresh (feedback-clear boundary
    rule), `PersistStartupState()`; conflict/lifecycle-conflict → `alert-warning` "Close and reload"
    affordance (list-page pattern); transport failure → form error.
  - Menu **Close campaign** → `SelectCloseoutTabAsync()`; menu **Reopen** → `SelectCloseoutTabAsync()`.
- [x] Update `Nova.Unit.Tests/Campaigns/CampaignWorkspaceTests.cs`:
  - Register the two new substitutes in every render helper; rewrite
    `CampaignWorkspace_ShowsEvaluateActive_AndOtherTabsDisabled` → tabs enabled (`CampaignWorkspace_ShowsAllTabsEnabled_AndEvaluateActive`).
  - Add: Overview/Closeout tab clicks push `tab=overview`/`tab=closeout` and render the matching
    panel region; `tab=overview` / `tab=closeout` query parameters activate the matching tab on load;
    unknown tab still falls back to evaluate; header renders `CampaignMenu`; edit-metadata flow
    (menu → form renders with current values → save success updates header + shows status message;
    conflict shows the warning affordance); non-admin never sees menu mutation items.

### Verification Plan

- `dotnet build Nova.slnx` — zero errors.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignWorkspaceTests"` — all green (updated + new).

### Phase Summary

Integrated the three new components into the workspace shell: real Overview/Closeout tab buttons, the
Overview/Closeout panel regions, the header `CampaignMenu`, and the inline edit-metadata flow (menu →
`CampaignMetadataForm` with current values → save → header refresh + status message; conflict →
"Close and reload"). Added `CampaignMetadataFormState.FromDetail`, an `ICampaignMetadataService`
constructor service, and the URL/navigation callbacks.

Deviation: season choices are loaded via the existing `ICampaignQueryService.GetCreationSetupAsync()`
(the repo's actual setup query, already injected) rather than `ICampaignCreationService.GetSetupAsync()`
(which does not exist); `ICampaignCreationService` is not added. The current-season prepend fallback
uses the campaign start date for display because `CampaignDetailResult` carries no season dates.
This means only one new constructor service (`ICampaignMetadataService`) was added. Updated
`CampaignWorkspaceTests` (62 tests, all green).

---

## Phase 6: Full verification and delivery

Status: Complete

Suggested executor: orchestrator

- [x] `dotnet format Nova.slnx --verify-no-changes` (apply `dotnet format Nova.slnx` if needed).
- [x] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full suite green.
- [x] `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` against the Aspire
      AppHost — no endpoint changes, but confirm no regression (run locally; CI runs build+unit only).
- [ ] Optional manual acceptance pass via the `aspire-playwright-validation` skill (admin close happy
      path, blocked-close checklist, reopen confirm, non-admin menu absence) — one-off only; the
      issue's committed coverage is bUnit.
- [x] Commit and open the PR linked to issue #101.

### Verification Plan

- All commands above pass; PR CI green; PR body maps each acceptance criterion to its tests.

### Phase Summary

Verification results: `dotnet build Nova.slnx` 0 errors / 0 warnings; `dotnet format Nova.slnx
--verify-no-changes` passes (formatter applied first); `dotnet test --project
Nova.Unit.Tests/Nova.Unit.Tests.csproj` 1642/1642 green; `dotnet test --project
Nova.Integration.Tests/Nova.Integration.Tests.csproj` 289/289 green against the Aspire AppHost. The
optional manual browser pass was skipped (the issue's committed coverage is bUnit; no endpoint
changes). PR opened against `main`, linked to issue #101.

---

## Final Recap

Activated the workspace **Overview** and **Closeout** tabs and the header **Campaign menu** on
`/campaigns/{id}` (issue #101), replacing the disabled "Coming soon" placeholders. Pure UI slice — no
new endpoints, contracts, entities, or migrations, and no DI/Program.cs changes.

Delivered:

- **Phase 1** — URL-state extensions: `overview`/`closeout` tab tokens + `ValidTabs`, and
  `BuildOverviewWorkspaceUrl` / `BuildCloseoutWorkspaceUrl` / `BuildReviewUnresolvedUrl` builders.
- **Phase 2** — `CampaignOverviewPanel`: snapshot, four outcome stat blocks, ready/blocked line with
  the admin+Active "Open closeout" link, and a bounded newest-first activity feed.
- **Phase 3** — `CampaignCloseoutPanel`: the five-row readiness checklist (blocked rows render the
  policy `Count` + `Message` verbatim + Review unresolved), the close state machine, and the Closed
  view (metadata + summary + read-only banner + Reopen behind an inline confirm).
- **Phase 4** — `CampaignMenu`: native, keyboard-operable, admin-only disclosure (no JS).
- **Phase 5** — workspace shell integration + inline edit-metadata flow via the reused
  `CampaignMetadataForm` (with a new `CampaignMetadataFormState.FromDetail` factory).

Tests: 36 URL-state, 10 overview-panel, 12 closeout-panel, 7 menu, and 62 workspace (updated + new)
bUnit tests; full unit suite 1642/1642 green; integration suite 289/289 green.

Deviation (documented in Phase 5): season choices load via the existing
`ICampaignQueryService.GetCreationSetupAsync()` rather than the plan's `ICampaignCreationService.GetSetupAsync()`
(which does not exist in the repo); only one new constructor service (`ICampaignMetadataService`) was
added to `CampaignWorkspace`. The current-season prepend fallback uses the campaign start date for
display because `CampaignDetailResult` carries no season dates.

## Deployment Plan

1. Merge the PR (issue #101) into `main`.
2. No database migrations, no environment changes, and no data backfill are required — this is a
   UI-only slice that consumes the already-merged #102 read and #104 mutation contracts.
3. CI runs build + unit tests; the integration suite (289) was run locally and passed. No additional
   deployment steps.
