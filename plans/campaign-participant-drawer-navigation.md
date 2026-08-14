# Campaign Workspace — Responsive Participant Drawer with Navigation (Issue #64)

Build out the participant-detail drawer from the #67 shell: read-only detail from the #68 read APIs
(`ICampaignParticipantQueryService.GetParticipantDetailAsync`), the wide side-drawer / narrow
full-screen responsive layout, previous/next navigation across the exact filtered/sorted/paged
roster sequence with position display and true first/last disable states, stale-response
protection, focus trap + focus return + Escape, URL/state preservation, and component + focused
browser validation.

Depends on #68 (merged — detail contract, endpoint, WASM client all exist) and #67 (merged —
drawer shell, `?participant={assignmentId}` URL contract, roster state/scroll anchoring).
Unblocks #70 (note/tag mutation controls).

## Scope decisions (confirmed with issue owner)

1. **Arrow-key shortcuts**: deferred. The issue lists ←/→ prev/next as optional with an
   editable-control guard requirement; #70 adds editable controls (note editor, tag picker).
   Documented as not implemented in the drawer's XML docs. (Confirmed 1/4.)
2. **Focus trap + focus return**: small JS helpers in `Nova/wwwroot/js/site.js` (mirroring the
   existing focus/scroll helpers), installed on open, removed on close. Robust for the dynamic
   focusable set (prev/next, retry, later #70 controls). (Confirmed 2/4.)
3. **Detail-load failure UX**: inline error message + Retry button inside the drawer; close
   stays available. (Confirmed 3/4.)
4. **Participant not on the loaded roster page** (URL drift after refresh/Back/Forward): detail
   still loads and renders; position hidden and prev/next disabled — position cannot be computed
   without extra API calls. (Confirmed 4/4.)
5. **Prev/next mechanics** (design, follows #67 conventions): the page owns the filtered/sorted
   sequence. Position = `(roster.Page - 1) * roster.PageSize + indexWithinPage + 1` of
   `roster.TotalCount`. Crossing a page boundary is a two-phase navigation (see below). Every
   state change pushes a history entry (per #67 scope decision 4), so Back/Forward walks
   participant and page history.
6. **Rapid navigation**: prev/next stay enabled while detail loads; a request-sequence guard
   discards stale detail responses (same pattern as the roster's `_requestSequence`).
7. **Controls placement**: drawer header row — prev button, "N of M" position text, next button,
   then the close button. Disabled at true first/last and in the off-page state.
8. **Read-only body**: player context (grad year, tryout #, outcome, team), campaign status,
   notes (author, timestamps, edited state), tag chips with archived-definition indicators and
   actor/applied-at metadata, created/modified timestamps. `CanEdit`/`CanRemove`/capability flags
   are not rendered in this phase (#70).

## Sequence contract (how prev/next works)

- **Within-page move**: `participant` param changes only; roster does not reload; scroll anchor
  untouched; history entry pushed.
- **Boundary move** (last item on page → next, or first item → previous, when the position is
  not the true first/last of `TotalCount`): the next page's first/last item id is unknown until
  the roster loads, so the page navigates with only `page` changed (participant param kept),
  records a pending boundary intent (`First`/`Last`), then after `LoadRosterAsync` completes:
  select the first (or last) item of the loaded page, clear the intent, and correct the URL with
  `replaceHistoryItem: true` so the transient `page=N&participant=<off-page id>` URL never
  becomes a permanent history entry. Net result: one history entry per boundary crossing.
  If the newly loaded page is empty (concurrent drift), clear the intent and leave the drawer in
  the off-page state (decision 4).
- **True first/last**: prev disabled when position = 1; next disabled when position =
  `TotalCount`.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its
status to `Complete` and write its **Phase Summary**; run the phase's **Verification Plan** and
record the result before moving on. When all phases are done, fill in **Final Recap** and
**Deployment Plan**.

Use the repo skills: `add-blazor-ui` (Phases 1–3), `nova-testing` (all test work),
`aspire-playwright-validation` (Phase 6). Follow the targeted instruction files for the affected
areas (`blazor-architecture`, `testing`, `csharp-conventions`, `service-layer`).

## Phase 1: Drawer detail loading + read-only presentation

Status: Complete

Suggested executor: orchestrator (establishes the drawer's internal state machine that later
phases and #70 build on).

- [x] Extend `CampaignParticipantDrawer.razor.cs` (primary-constructor DI): add
      `ICampaignParticipantQueryService` alongside `IJSRuntime`; load
      `CampaignParticipantDetailDto` via `GetParticipantDetailAsync(new
      GetCampaignParticipantDetailInput(CampaignId, ParticipantId), ComponentCancellationToken)`
      in `OnInitializedAsync` (prerender-safe: `[PersistentState]` props persist the detail/error
      so the WASM attach restores instead of double-fetching — same pattern as the page's roster).
- [x] Internal state machine: `Loading` / `Loaded` / `Failed` with `_detail`, `_detailState`,
      and a `_detailRequestSequence` guard — a response is applied only when its request id
      matches the current sequence. `RetryAsync()` re-invokes the load.
- [x] `OnParametersSetAsync`: when `ParticipantId` changes, reset to `Loading` (clear the old
      detail so it never lingers under the new heading) and reload; bump the sequence.
- [x] Heading: `_detail?.DisplayName ?? RosterItem?.DisplayName ?? "Participant"` (keeps the
      existing `participant-drawer-heading` id / `aria-labelledby` contract).
- [x] Razor body states: loading placeholder (spinner, `role="status"`); failure state with
      message + Retry button (`id="participant-drawer-retry"`); loaded state with:
      - Player context: graduation year, tryout number, placement-outcome badge, team summary.
      - Campaign context: campaign status badge.
      - Notes list: content, `AuthorDisplayName`, `CreatedAt`, edited indicator when
        `ModifiedAt` differs. `CanEdit`/`CanDelete` flags ignored (read-only phase).
      - Applied tags: chip per `CampaignParticipantTagApplicationDto` using `TagColor`, with a
        muted "(archived)" indicator when `IsArchived`, and `ActorDisplayName` + `AppliedAt`
        metadata.
      - Footer metadata: `CreatedAt` / `ModifiedAt` timestamps.
- [x] `.razor.css`: scrollable body (exists — kept), section/heading spacing, archived-tag chip
      style, loading/error spacing; kept the existing 100% → `26rem` responsive width rules and
      confirmed they hold with the new content. Bootstrap-first, rem units.
- [x] XML docs on every new member; updated the class summary (no longer "shell only").
- [x] bUnit tests in `Nova.Unit.Tests/Campaigns/CampaignParticipantDrawerTests.cs` (new file,
      fake `ICampaignParticipantQueryService`): loading state renders; loaded state renders
      every section including the archived-tag indicator and note edited indicator; failure
      renders Retry and clicking it re-invokes the service; heading fallback chain; participant
      change resets state and reloads; stale guard (slow first response discarded when the
      second load completes first); persisted-state restore skips refetching; close-button and
      Escape callbacks; optional-field fallbacks (— / empty states). Existing
      `CampaignWorkspaceTests` drawer tests updated for the new behavior.

### Verification Plan

- [x] `dotnet build Nova.slnx` — clean build (0 warnings, 0 errors).
- [x] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full suite pass:
      1308/1308 (includes the new `CampaignParticipantDrawerTests` and the updated
      `CampaignWorkspaceTests` drawer tests).

### Phase Summary

Drawer shell extended into a read-only participant-detail drawer. Code-behind gained
`ICampaignParticipantQueryService` (primary-constructor DI), a `CampaignId` parameter, the
`[PersistentState]` + `Initialized` prerender plumbing used by the page's roster, a three-state
`DetailLoadState` machine, a request-sequence stale guard, `RetryAsync`, the three-tier heading
fallback, and shared badge/tag helpers. Markup renders loading (spinner with `role="status"`),
failed (inline alert + `#participant-drawer-retry`), and loaded states; the loaded body shows the
Player `dl` (grad year, tryout #, outcome badge, team), campaign-status badge, notes with
author/timestamps/edited, applied-tag chips with archived styling and actor/timestamp metadata,
and a created/modified footer. `dl` uses valid `dt`/`dd` direct children with a two-column CSS
grid. `CampaignWorkspace.razor` now passes `CampaignId` to the drawer.

Verification: clean build; 1308/1308 unit tests pass including the new drawer test file. Note:
`dotnet format --verify-no-changes` still reports pre-existing `CHARSET` failures in unrelated
Tag-feature and migration files; none of this phase's files are flagged.

## Phase 2: Page-owned sequence state, prev/next, and position plumbing

Status: Complete

Suggested executor: orchestrator (URL/history mechanics must stay consistent with #67's
contract).

- [x] `CampaignWorkspace.razor.cs`: compute from `_roster` + `_selectedParticipantId`:
      `SelectedParticipantPosition` (int?, 1-based) using `_roster.Page`,
      `_roster.PageSize`, index in `_roster.Items`; `ParticipantSequenceCount`
      (`_roster.TotalCount`); `HasPreviousParticipant` / `HasNextParticipant`. All null/false
      when the roster is null or the participant is not in `Items` (off-page state).
- [x] `OnPreviousParticipantAsync` / `OnNextParticipantAsync`:
      - Within-page: capture scroll (existing helper), update only `participant` in the URL,
        push history.
      - Boundary: set pending boundary intent, navigate with only `page` changed (push); after
        the roster reload completes, select first/last item of the loaded page, clear the
        intent, correct the URL with `replaceHistoryItem: true` (see Sequence contract).
      - Preserve every other param (filters, sort, tab) exactly.
- [x] Reuse the existing pager page-change scroll behavior for boundary crossings so a
      boundary move behaves like a pager page change; within-page moves leave roster scroll
      untouched.
- [x] Pass `Position`, `TotalCount`, `HasPrevious`, `HasNext`, `OnPrevious`, `OnNext` to the
      drawer; drawer header renders prev (`id="participant-drawer-previous"`), "N of M" text,
      next (`id="participant-drawer-next"`) with `disabled` at true first/last and in the
      off-page state (position hidden there).
- [x] Confirm no code remounts the drawer on media-query change (wide↔narrow is CSS-only), so
      in-progress detail content persists across transitions.
- [x] Tests (extend `CampaignWorkspaceTests` / new file): position text "3 of 142"; prev/next
      disabled at position 1 and at `TotalCount`; within-page next updates only `participant`
      and pushes history; boundary next from the last item of a page loads the next page,
      selects its first item, and corrects the URL without an extra history entry (assert via
      fake `NavigationManager` and paged fake query service); boundary prev symmetrical;
      all filter/sort params preserved across every move; off-page participant → position
      hidden, nav disabled, detail still rendered; scroll-capture JS invoked on each nav.

### Verification Plan

- `dotnet build Nova.slnx` — clean build.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter CampaignWorkspace` —
  all workspace tests (existing + new) pass.

### Phase Summary

Page-owned sequence state is in place. `CampaignWorkspace` computes position ("N of M"),
total count, and prev/next availability from the loaded roster plus the selected participant,
and hands them to the drawer along with `OnPrevious`/`OnNext` callbacks. Within-page moves
capture roster scroll, change only the `participant` query parameter, and push one history
entry; the roster is not reloaded. Boundary moves push only a `page` change, record a
pending boundary intent (`First` after next, `Last` after previous), and once the target
page finishes loading select the first/last item and correct the URL with
`ReplaceHistoryEntry = true` so each crossing adds exactly one history entry; a failed or
empty target page clears the intent and leaves the drawer off-page (position hidden, both
buttons disabled) without touching the URL again. Filter/sort/tab parameters are preserved
across every move, and boundary crossings reuse the pager's scroll-to-top behavior.

The drawer header gained a prev button, "N of M" position line, and a next button with
`disabled` at the true sequence ends, plus CSS-isolated header-row styles. The media-query
wide↔narrow switch remains CSS-only, so drawer state persists across layout transitions.

Tests: 4 new drawer tests (position text, callback invocation, disabled-at-ends,
off-page rendering) and 11 new workspace tests (position + disabled matrix, within-page
next/prev without roster reload + push-only history, boundary next/prev selecting the
correct edge item with in-place URL correction and a single net history entry, filter/sort
preservation across moves, off-page detail rendering, and boundary move onto an empty page).
Full unit suite: 1323 tests pass. Build clean; `dotnet format --verify-no-changes` passes
on the touched files (pre-existing CHARSET normalization in unrelated Tag-feature files was
reverted rather than fixed silently).

## Phase 3: Focus trap, focus return, and Escape (JS helpers)

Status: Complete

Suggested executor: orchestrator (site.js is shared and load-bearing; subtle keydown behavior).

- [x] `Nova/wwwroot/js/site.js`: add
      `novaCampaignParticipantDrawerOpen(dialogSelector, closeButtonId)` — captures
      `document.activeElement` (the activating row/card), installs a document-level capture
      `keydown` trap that cycles Tab/Shift+Tab through the focusable elements inside the dialog
      (buttons, links, and non-disabled form controls), and focuses the close button. Add
      `novaCampaignParticipantDrawerClose(restoreFallbackId)` — removes the trap and restores
      focus to the selected participant's visible row/card (fallback id, card id derived from
      the row id), else the captured element if still connected, else nothing.
- [x] Give roster rows/cards stable DOM ids for the fallback:
      `id="roster-row-{assignmentId}"` (`tr`) and `id="roster-card-{assignmentId}"` (`li`) in
      `CampaignRosterTable.razor` / `CampaignRosterCards.razor`.
- [x] Drawer code-behind: on first render invoke `novaCampaignParticipantDrawerOpen` (replacing
      the `novaCampaignWorkspaceFocus` call); `CloseAsync` invokes
      `novaCampaignParticipantDrawerClose` (fallback = the selected row id) before delivering
      `OnClose`; Escape keeps the C# handler and takes the same close path; `DisposeAsyncCore`
      removes the trap without restoring focus (navigation away while open must not leave a
      stale trap).
- [x] Document in the drawer XML docs: arrow-key prev/next intentionally not implemented
      (scope decision 1); focus behavior summary.
- [x] Update the existing bUnit drawer tests that assert the old `novaCampaignWorkspaceFocus`
      call; add assertions: open helper invoked on first render with the expected ids; close
      helper invoked with the fallback row id on close-button click, backdrop click, and
      Escape.

### Verification Plan

- `dotnet build Nova.slnx` — clean build.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter CampaignParticipantDrawer`
  and `--filter CampaignWorkspace` — pass. (Trap/return behavior itself is browser-verified in
  Phase 6.)

### Phase Summary

`site.js` replaced `novaCampaignWorkspaceFocus` (now unused) with three drawer-specific helpers:
`novaCampaignParticipantDrawerOpen` captures the activating row/card, installs a capture-phase
Tab/Shift+Tab trap that cycles the dialog's visible focusable elements and wraps at both ends,
and focuses the close button; `novaCampaignParticipantDrawerClose(restoreFallbackId)` removes the
trap and restores focus; `novaCampaignParticipantDrawerDispose` removes the trap only. Focus
return refines the plan slightly: the visible selected row/card (fallback id, with the card id
derived from the row id so the correct layout wins via a visibility check) is preferred over the
captured activating element, so closing after a prev/next move lands on the newly selected
participant rather than the originally clicked one; the captured element remains the fallback,
then nothing.

Roster rows and cards now emit stable `roster-row-{assignmentId}` / `roster-card-{assignmentId}`
ids. The drawer code-behind installs the trap in `OnAfterRenderAsync(firstRender)` only (so the
prerendered instance never issues JS interop), closes through a shared `CloseAsync` that removes
the trap and restores focus before delivering `OnClose` (Escape and backdrop clicks take the same
path), and `DisposeAsyncCore` removes the trap without restoring focus, swallowing
`JSDisconnectedException` like `NovaCropperComponent` (full-document navigation while open). A
`_focusTrapInstalled` guard keeps the prerender/dispose paths no-ops. Arrow-key exclusion and the
focus contract are documented in the drawer XML docs.

Tests: 6 new/updated drawer tests (open invocation with the expected selector + close-button id,
single trap install across participant parameter changes, close-helper invocation with the
fallback row id on close-button click, backdrop click, and Escape, and dispose removing the trap
without restoring focus). Targeted suites pass (20 drawer, 39 workspace); full unit suite passes
(1327). Build clean; format gate clean on the touched files (same pre-existing Tag-feature
CHARSET violations as Phase 2).

## Phase 4: Component-test hardening for the cross-cutting acceptance criteria

Status: Complete

Suggested executor: sub-agent with a smaller model (mechanical expansion of a fixed,
well-specified matrix — see list below), with the orchestrator reviewing the resulting tests.
(The delegated sub-agent returned no output, so the orchestrator implemented the tests directly.)

- [x] State preservation: open → prev/next → close keeps search/filter/sort/page params and
      restores roster scroll (extend the existing scroll tests to the nav flows).
- [x] Refresh with `participant=` in the URL reopens the drawer, loads detail, and shows the
      position when the participant is on the loaded page.
- [x] Rapid navigation matrix: 3+ quick prev/next clicks end on the correct participant with
      no stale detail (fake service with controllable task-completion delays).
- [x] First/last bounds across a multi-page roster, including a boundary move landing exactly
      on the true first/last item (buttons then disabled).
- [x] Filtered-sequence correctness: with fake paged data, prev/next order matches the sorted
      filtered order across a boundary.
- [x] Narrow-layout transitions: cards and table both render in the page markup (classes
      `d-md-none` / `d-none d-md-block`), the drawer renders a single `aside.participant-drawer`
      for both, and drawer markup is identical regardless of viewport (structure-level check;
      the CSS rules are static).
- [x] Archived tag chip markup (indicator + actor + timestamp) and note metadata readable in
      the rendered output.
- [x] Empty-notes / empty-tags detail renders without crashing (empty-state text).
- [x] Off-page participant: detail loads, position hidden, nav disabled (regression pin for
      decision 4).

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full suite green.
- `dotnet format Nova.slnx --verify-no-changes` — formatting clean.

### Phase Summary

Five new tests added to `CampaignWorkspaceTests` in a "Sequence hardening (Phase 4)" section
(44/44 class green; full suite 1332/1332):

- `CampaignWorkspace_OpenNavigateClose_RestoresScroll_AndPreservesState` — opens on
  `participant=301` with search/sort params present, moves next (participant 302), closes via
  `#participant-drawer-close`; asserts the URI drops `participant=` but retains
  `search=lee&sortBy=displayName&sortDirection=asc`, and scroll restore was invoked twice
  (once per capture: the within-page move and the close) with the last args
  `["roster-scroll-region", 60.0]`.
- `CampaignWorkspace_RapidNavigation_EndsOnFinalParticipant_WithoutStaleDetail` — per-assignment
  `TaskCompletionSource`s gate detail responses; three rapid next-clicks on 301 while details
  are pending end on participant 304 ("4 of 6"); completing responses out of order (304 first,
  then 301/303/302 with distinct names) proves stale detail is dropped and the heading stays
  "Detail 304".
- `CampaignWorkspace_BoundaryMoves_DisableButtons_AtTrueSequenceEnds` — 4 items over 2 pages
  (page 2 = `[304]`); next from 303 crosses to 304 ("4 of 4", next disabled), then three prev
  moves walk back to "1 of 4" with prev disabled and next re-enabled.
- `CampaignWorkspace_BoundaryMove_LandsOnFirstItem_OfFilteredNextPage` — a custom fake asserts
  the reloaded page-2 request preserves `Search == "jones"` and `SortBy == "displayName"`;
  lands on filtered item 801, position "4 of 5", filters/sort intact in the URL.
- `CampaignWorkspace_RendersResponsiveRosterLayout_WithDrawerOutsideResponsiveContainers` —
  asserts exactly one `div.table-responsive.d-none.d-md-block` (1 row), one `div.d-md-none`
  (1 card), one `aside.participant-drawer`, and that neither responsive container nests the
  drawer.

Checklist items 2, 7, 8, and 9 were already covered by existing tests and are checked on that
basis: open-on-refresh by `CampaignWorkspace_ShowsParticipantPositionAndEnabledNavigation_WhenDrawerOpen`
and the drawer restore/refresh tests; archived tag chips by
`Drawer_RendersTagMetadata_AndArchivedTagIndicators`; empty notes/tags fallbacks by
`Drawer_RendersFallbacks_WhenOptionalFieldsAreMissing`; off-page participant by
`CampaignWorkspace_OffPageParticipant_HidesPositionAndDisablesNavigation_ButRendersDetail`.

The format gate remains blocked solely by pre-existing Tag-feature files (CHARSET) and three
migration warnings; none of the Phase 4 files are flagged.

## Phase 5: Format, build, and full-suite regression

Status: Not started

Suggested executor: sub-agent with a smaller model (mechanical run + report).

- [ ] `dotnet format Nova.slnx` (apply fixes if needed), then
      `dotnet format Nova.slnx --verify-no-changes`.
- [ ] `dotnet build Nova.slnx` — clean, no new warnings.
- [ ] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — all tests pass
      (record the count in the phase summary).
- [ ] Fix anything surfaced; note any pre-existing failures unrelated to this work rather than
      fixing them silently.

### Verification Plan

- The three commands above, with clean results recorded in the Phase Summary.

### Phase Summary

_(write when phase completes)_

## Phase 6: Focused browser validation (Aspire + Playwright)

Status: Not started

Suggested executor: orchestrator, following the `aspire-playwright-validation` skill (requires
judgment to fix blockers found). Reuse #67's approach: isolated AppHost against the seeded
Postgres (campaign 1, "Summer Tryouts 2026", 200 assignments).

- [ ] Write the concrete scenario list before starting the app (skill precondition) — the list
      below.
- [ ] `aspire start --isolated --non-interactive` → `aspire wait nova --non-interactive` →
      discover URLs via `aspire describe --format Json`.
- [ ] Scenarios: open `/campaigns/{id}`, click a roster row → drawer opens with detail (name,
      grad year, tryout #, outcome, team, notes, tags) and focus lands on the close button;
      repeated Tab/Shift+Tab never leaves the dialog (focus trap); Escape closes and focus
      returns to the activating row; close button and backdrop click also restore focus;
      prev/next within a page updates "N of 200" and the `participant` URL param and pushes
      history entries; from the last item of page 1, Next crosses the boundary (`page=2`,
      participant = first item of page 2, "51 of 200") and Prev returns to item 50; Prev
      disabled at position 1 and Next at position 200; with a filter applied (e.g. search),
      prev/next follows the filtered sequence and "N of M" uses the filtered total; narrow
      viewport (~480 px) shows the card list and the full-screen drawer, open/close preserves
      roster scroll, prev/next still works; refresh with `participant=` reopens the drawer with
      detail and position; browser Back/Forward walks participant and page history and restores
      each state; an archived tag chip shows its indicator and actor/timestamp metadata and
      note author/timestamps are visible; simulated detail failure (devtools offline or
      blocked route) shows the error + Retry and Retry recovers.
- [ ] Run the integration tests against the running AppHost
      (`dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj`).
- [ ] Fix any blocker found, rerun the affected browser segment before concluding, then
      `aspire stop --non-interactive`; remove temporary browser-automation artifacts.

### Verification Plan

- Scenario coverage report: every scenario above reached with its expected outcome (or a fixed
  blocker + rerun evidence). Record the report in this phase's summary.

### Phase Summary

_(write when phase completes)_

## Final Recap

_(write when all phases complete: summary of the entire piece of work)_

## Deployment Plan

_(write when all phases complete: step-by-step deployment instructions)_
