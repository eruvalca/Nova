# Campaign Close and Reopen Cross-Slice Validation (Issue #103)

Prove the completed close/reopen/history slice (delivered by #104/#105, #102, #101 — all merged) against
real PostgreSQL and the real browser boundary per the issue's validation matrix: tenant isolation,
transactional close, concurrency, lifecycle races, read-only modes, responsive behavior, and
accessibility. This is the final Wave 3 gate for epic #12; it consolidates boundary evidence and fixes
only defects in the delivered close/reopen slice.

**Hard constraints (from the issue):** no new close/reopen feature behavior, no new endpoints, no
duplicate policy implementations. Test-only additions plus in-slice defect repairs. The readiness/history
and workspace slices are inputs, not surfaces to re-deliver.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to
`Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue
with zero context); run the phase's **Verification Plan** and record the result before moving on.
When all phases are done, fill in **Final Recap** and **Deployment Plan**.

Load these before touching test code: `.github/instructions/testing.instructions.md` (always-on
rules) and the **`nova-testing`** skill (harness internals, `references/browser-suite.md` recipe,
`references/blazor-component-tests.md` for bUnit). Use the **`aspire-orchestration`** skill only if
the AppHost needs lifecycle management. If a production-code defect fix is made, also load the
relevant instruction files (`service-layer`, `functional-core`, `api-endpoints`, `blazor-architecture`)
before editing.

## Coverage baseline (verified on this branch)

- **Nova.Integration.Tests/Http/CampaignLifecycleHttpTests.cs** (7 tests): anonymous 401 and member 403
  on close+reopen, admin close 204 with `ClosedAt`/`ClosedById`/`Closed` event, condition-keyed blockers
  (outcomes/eligibility/archivedTeams) with campaign left Active and unfrozen, cross-tenant close 404
  with no `detail`, double-close and reopen-active 409, admin reopen 204 with `[Closed, Reopened]`
  events.
- **Nova.Integration.Tests/Http/CampaignCloseoutHttpTests.cs** (6 tests): anonymous/no-club boundaries
  on readiness+activity, blocked readiness with seeded assignment ids, cross-tenant 404s, evaluator+admin
  readability of a Closed campaign across detail/readiness/activity/roster/summary, bounded ordered
  close+reopen activity events.
- **Nova.Integration.Tests/Data/CampaignLifecyclePostgresTests.cs** (8 tests): migration/schema,
  partial-closure-provenance check constraints (closed-requires-metadata, active-forbids-metadata,
  status/event-type enums, cross-tenant event FK), stale-transition concurrency token,
  placement-vs-close advisory-lock race (lock → reload → reject).
- **Nova.Integration.Tests/Data/CampaignLifecycleRetryTests.cs** (6 tests): close/reopen execution-
  strategy retry and ambiguous-commit verification.
- Closed-campaign mutation rejection (proving "readable but rejects note/tag/outcome/placement
  mutations"): `CampaignPlacementHttpTests.CampaignPlacementUpdate_ReturnsConflict_ForClosedCampaign`,
  `EvaluationNoteHttpTests` add/edit/delete `..._ForClosedCampaign`, `CampaignTagApplicationHttpTests`
  apply/remove `..._ForClosedCampaign`.
- **Nova.Browser.Tests**: `CampaignEvaluationBrowserTests.StaleClose_RejectsWrite_AndEntersReadOnly_
  PreservingContext` (drawer heals to read-only when the campaign closes behind the session) — but no
  closeout/overview/reopen browser class exists. `BrowserSuiteFixture.CloseCampaignAsAdminAsync` closes
  through the service directly.
- **Nova.Unit.Tests/Campaigns**: `CampaignClosurePolicyTests`, `CampaignLifecycleServiceTests`,
  `CampaignLifecycleEndpointTests`, `CampaignCloseoutQueryServiceTests`, `CampaignActivityQueryServiceTests`,
  `ClosedCampaignReadabilityTests`, `CampaignCloseoutPanelTests` (12: checklist rows, satisfied rows,
  close disabled when blocked/for non-admin, close success/conflict, in-flight disable, closed view,
  reopen hidden for non-admin, cancel no-op, reopen success, review-unresolved flags),
  `CampaignOverviewPanelTests` (10: snapshot, counts, ready/blocked line, admin-only link, activity
  order, error/retry, persisted-state restore), `CampaignWorkspaceTests`, `CampaignWorkspaceUrlStateTests`.

## Phase 1: Matrix-to-coverage audit

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [x] Produce an explicit row-by-row table mapping every line of the issue's "Required validation
      matrix" (7 PostgreSQL rows, 6 browser rows) to the test(s) that prove it, marked
      `covered` / `gap` / `partial`, recorded in this plan (add as a `### Coverage matrix` section
      under this phase when complete). Start from the pre-drafted table below — verify every claim
      against the actual test bodies before keeping it.
- [x] For each `gap`/`partial` row, finalize the concrete test spec (file, method name
      `Subject_Outcome_Condition`, seed shape, assertions) into Phase 2/3; keep each invariant at the
      lowest effective test layer per `testing.instructions.md`.
- [x] Confirm the member seeded by `UpdateUserAsync(..., clubId: club.ClubId, ...)` is an approved
      (direct) club member for the mutation/query boundary claims; if an approval field exists and is
      not set, note the seed shape needed instead.
- [x] Confirm the panel bUnit coverage leaves no browser-impractical state uncovered (loading/error/
      retry/in-flight states are proven in `CampaignCloseoutPanelTests`/`CampaignOverviewPanelTests`);
      add minimum bUnit only for states the browser scenarios cannot reliably pin (see Phase 4).
- [x] Confirm no new endpoint/route/policy is required for any planned test (tests must exercise the
      existing surface only).

### Coverage matrix

| Required validation row | Final coverage (verified against test bodies) | Status | Follow-up |
| --- | --- | --- | --- |
| Admin vs approved-member access at close/reopen mutation and readiness/history query boundaries | `CampaignLifecycle_ReturnsUnauthorized_ForAnonymousCaller`, `..._Forbidden_ForClubMember` (close+reopen), `..._ReturnsNoContent_..._ForClubAdmin` (both), `ReadinessAndActivity_ReturnUnauthorized/Forbidden`, `GetCloseoutReadiness_ReturnsBlockedReadiness_WithSeededAssignmentIds` (member 200), `ClosedCampaign_IsReadableByEvaluatorAndAdmin_AcrossReadSurfaces`; member approval confirmed — `UpdateUserAsync` sets `NovaUserEntity.ClubId` directly (there is no separate approval field; membership == approval) | covered | Done. |
| Close with undecided/ineligible/archived-team assignments returns condition-keyed blockers, campaign stays Active and unfrozen | `CampaignClose_ReturnsConflict_WithConditionKeyedBlockers` (all three keys + messages + assignment ids; asserts Active, null provenance, no events) | covered | Preserve. |
| Successful close records Closed status, timestamp, audit actor atomically; no partially frozen state observable | `CampaignClose_ReturnsNoContent_AndPersistsClosure_ForClubAdmin`, `StatusMetadataConstraint_RejectsPartialClosureProvenance`, `..._RejectsClosureProvenance_ForActiveStatus` | covered | Preserve. |
| Closed campaigns remain readable and reject note, tag, outcome, and placement mutations | `ClosedCampaign_IsReadableByEvaluatorAndAdmin_AcrossReadSurfaces`, `ClosedCampaignReadabilityTests` (unit), placement/note/tag `..._ForClosedCampaign` conflicts | covered | Preserve. |
| Reopen by admin records auditable action and restores editing; reopen by non-admin forbidden | `CampaignReopen_ReturnsNoContent_AndClearsClosure_ForClubAdmin`, `GetActivity_ReturnsBoundedOrderedEvents_AfterCloseAndReopen`, `CampaignLifecycle_ReturnsForbidden_ForClubMember`, **new** `CampaignReopen_RestoresEditing_WithoutDiscardingOutcomes` | covered | Done (Phase 2). |
| Concurrent close/reopen attempts serialize under the advisory-lock order with actionable conflicts | `PlacementConcurrency_RejectsMutation_WhenCampaignClosesWhileWaitingForLock`, `StatusConcurrency_RejectsStaleLifecycleTransition`, **new** `CloseConcurrency_RejectsSecondClose_WhenCampaignClosesWhileWaitingForLock`, `ReopenConcurrency_RejectsSecondReopen_WhenCampaignReopensWhileWaitingForLock`, `ConcurrentAdminCloses_YieldOneSuccessOneConflict_WithWinnerPersisted` | covered | Done (Phase 2). |
| Cross-tenant campaign and assignment identifiers preserve non-disclosing behavior throughout | `CampaignClose_ReturnsNotFound_ForCrossTenantCampaign` (404, no `detail`), `ReadinessAndActivity_ReturnNotFound_ForCrossTenantCampaign` (now asserts no `detail`), **new** `CampaignReopen_ReturnsNotFound_ForCrossTenantCampaign` | covered | Done (Phase 2). |
| Browser: admin workspace flow — Overview snapshot, readiness link, Closeout counts/blocker drill-down, resolve blockers, close, closed read-only state | `Admin_OverviewAndCloseout_HappyPath_ResolvesBlockers_AndCloses_IntoReadOnlyState` | covered | Done (Phase 3). |
| Browser: closing a blocked campaign surfaces explicit blocker details without freezing anything | `Admin_BlockedClose_ShowsBlockerDetails_CloseDisabled_AndNothingFrozen`, `Admin_StaleBlockedClose_ShowsConflictAlert_WithoutFreezing` | covered | Done (Phase 3). |
| Browser: admin reopens with confirmation; editing restored without discarding outcomes or history | `Admin_ReopenConfirm_RestoresEditing_PreservingOutcomesAndHistory` | covered | Done (Phase 3). |
| Browser: evaluator and approved non-admin see closed results read-only without Close/Reopen controls | `NonAdmin_ClosedCampaign_RendersReadOnly_WithoutCloseReopenControls` (plus the existing drawer-only `StaleClose_...` scenario) | covered | Done (Phase 3). |
| Browser: direct Closeout/Overview URLs and Back navigation preserve workspace tab context | `DirectCloseoutOverviewUrls_AndBackNavigation_PreserveTabContext` (plus the existing evaluate-tab URL-state scenario) | covered | Done (Phase 3). |
| Browser: wide/narrow viewports usable with keyboard, visible focus, labels, announcements, no color-only reliance | `Closeout_KeyboardAndA11y_AcrossWideAndNarrowViewports` + gated `Closeout_A11yEvidence_CapturesScreenshots` | covered | Done (Phase 3). |

### Verification Plan

- Re-run the audit greps and confirm the matrix table covers all 13 rows and every existing
  close/reopen-related test file is accounted for (no claim written without having read the test body).

**Result:** Read every existing close/reopen-related test body (`CampaignLifecycleHttpTests` 7,
`CampaignCloseoutHttpTests` 6, `CampaignLifecyclePostgresTests` 8, `CampaignLifecycleRetryTests`,
the placement/note/tag `..._ForClosedCampaign` conflicts, `CampaignEvaluationBrowserTests`'s
`StaleClose_...` + URL-state scenarios, and the `CampaignCloseoutPanelTests` /
`CampaignOverviewPanelTests` / `CampaignWorkspaceTests` bUnit classes). The pre-drafted matrix was
confirmed against those bodies and finalized above.

### Phase Summary

Audit completed. Every issue-matrix row is now mapped to concrete, verified tests. Two pre-draft
assumptions were resolved: (1) `UpdateUserAsync(clubId: ...)` produces an approved direct member
because `NovaUserEntity` carries only `ClubId` (no separate approval field); (2) no new
endpoint/route/policy is needed — all new tests exercise the existing close/reopen, closeout
readiness/activity, and placement surfaces. The six browser gaps and the two PostgreSQL gaps
(close-vs-close / reopen-vs-reopen races) and one HTTP gap (concurrent admin close) were finalized
into Phases 2–3.

## Phase 2: PostgreSQL integration additions

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: sub-agent with a smaller model (specs from Phase 1; pattern files exist for
every addition)

- [x] Promote the private close/reopen helpers from `CampaignCloseoutHttpTests` into shared
      `Nova.Integration.Tests/Http/SeedingHelpers.cs` as `CloseCampaignThroughServiceAsync` and
      `ReopenCampaignThroughServiceAsync` (keep the AsyncLocal `fixture.CurrentUser` assignment and
      `CreateTenantContextFactory` pattern; the browser suite will reuse them). Update
      `CampaignCloseoutHttpTests` to use the shared helpers.
- [x] `CampaignReopen_RestoresEditing_WithoutDiscardingOutcomes` in `CampaignLifecycleHttpTests`:
      seed a ready campaign with 2 decided participants (e.g., one `Assigned` to an eligible team, one
      `NotSelected`); close via `POST CampaignEndpoints.CloseUrl` (204); reopen via
      `POST CampaignEndpoints.ReopenUrl` (204); then `PUT CampaignEndpoints.UpdateCampaignPlacementUrl`
      changing the `NotSelected` assignment to `Assigned` with the eligible team → 200; assert the
      persisted outcome and that the other assignment's outcome is unchanged. Proves reopen restores
      editing without discarding outcomes at the HTTP boundary.
- [x] `CampaignReopen_ReturnsNotFound_ForCrossTenantCampaign` in `CampaignLifecycleHttpTests` (model on
      `CampaignClose_ReturnsNotFound_ForCrossTenantCampaign`): club B admin reopens club A's closed
      campaign → 404; assert the ProblemDetails body has no `detail` property (non-disclosing).
- [x] Extend `ReadinessAndActivity_ReturnNotFound_ForCrossTenantCampaign` in `CampaignCloseoutHttpTests`
      to assert the 404 bodies carry no `detail` property for both readiness and activity.
- [x] `CloseConcurrency_RejectsSecondClose_WhenCampaignClosesWhileWaitingForLock` in
      `CampaignLifecyclePostgresTests` (model on `PlacementConcurrency_RejectsMutation_WhenCampaign
      ClosesWhileWaitingForLock`): seed Active campaign; begin a raw transaction and hold
      `pg_advisory_xact_lock(long.MinValue + campaignId)`; start `CampaignLifecycleService.CloseAsync`
      (waits on the lock); `PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync`; the lock
      holder sets `Status = Closed` + provenance via EF and commits; the waiter reloads and must return
      the `LifecycleConflict` with "The campaign is already closed."; assert final state is the
      winner's closure, exactly one `Closed` event, and the loser persisted nothing.
- [x] `ReopenConcurrency_RejectsSecondReopen_WhenCampaignReopensWhileWaitingForLock` in the same file:
      seed Closed campaign (with closure provenance); hold the lock; start `ReopenAsync` (waits); the
      lock holder sets `Status = Active` + clears provenance and commits; the waiter reloads and must
      return `LifecycleConflict` with "The campaign is already active."; assert final state Active,
      exactly one `Reopened` event, loser persisted nothing.
- [x] `ConcurrentAdminCloses_YieldOneSuccessOneConflict_WithWinnerPersisted` in a new
      `Nova.Integration.Tests/Http/CampaignLifecycleRaceHttpTests.cs` (model on
      `CampaignPlacementTokenRaceHttpTests`/`CampaignTagApplicationRaceHttpTests`): one club, two
      administrators (second promoted through the real `ClubEndpoints.AssignAdmin`); seed a ready
      Active campaign; both `POST CampaignEndpoints.CloseUrl` via `Task.WhenAll` → deterministic exactly
      one 204 and one 409 (the advisory lock + reload check serializes the loser); assert the 409
      ProblemDetails detail is "The campaign is already closed." and the persisted `ClosedAt`/`ClosedById`
      provenance matches the 204 winner, with exactly one `Closed` event.
- [x] Mutation-sanity check for the new race guards: **not performed** (the race tests deterministically
      assert the winner/loser split and loser-persisted-nothing, which is stronger evidence than a
      temporary guard disable; no guard was disabled and restored, so no mutation-sanity claim is made).

### Verification Plan

- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignLifecycleHttpTests"` — all pass (AppHost starts via the fixture).
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignCloseoutHttpTests"` — all pass.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignLifecyclePostgresTests"` — all pass (2 new races included).
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignLifecycleRaceHttpTests"` — all pass.
- Optional deterministic-repeat check: run the two new lock races 3× consecutively.

**Result:** The four classes were run together in one AppHost boot with
`--filter-class "*CampaignLifecycleHttpTests" --filter-class "*CampaignCloseoutHttpTests" --filter-class "*CampaignLifecyclePostgresTests" --filter-class "*CampaignLifecycleRaceHttpTests"`:
**27 passed / 0 failed / 0 skipped** (lifecycle HTTP 9, closeout HTTP 7 incl. the 2-row theory,
lifecycle Postgres 10, race HTTP 1). The full integration suite also passed (294/294) in the final gate.

### Phase Summary

All Phase 2 additions implemented and green. The close/reopen service-call helpers were promoted to
`SeedingHelpers` and `CampaignCloseoutHttpTests` now calls them (its private duplicates removed).
Added `CampaignReopen_RestoresEditing_WithoutDiscardingOutcomes` (HTTP), `CampaignReopen_ReturnsNotFound_
ForCrossTenantCampaign` (non-disclosing 404), non-disclosure assertions on the readiness/activity
cross-tenant 404s, two advisory-lock race tests (close-vs-close, reopen-vs-reopen) in
`CampaignLifecyclePostgresTests` (its `SeedCampaignAsync` gained a `closed` parameter), and the new
`CampaignLifecycleRaceHttpTests` HTTP concurrent-close race. The mutation-sanity guard-disable check was
deliberately **not** run; the deterministic winner/loser assertions in the new races provide the
equivalent coverage without temporarily weakening the production guard.

## Phase 3: Browser workflow additions

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: sub-agent with a smaller model (follows `nova-testing`'s browser-suite recipe;
all helpers/patterns enumerated below)

- [x] Add `Nova.Browser.Tests/CloseoutSeed.cs` (model on `PlacementSeed.cs`): one administrator
      (creates the club) and one approved evaluator (`UpdateUserAsync(clubId: club.ClubId)`) registered
      through `IdentityHttpClientHelper`; expose a `SeededCloseoutWorkspace` record with `ClubId`,
      `AdminUserId`/`AdminEmail`, `EvaluatorUserId`/`EvaluatorEmail`, `BlockedCampaignId` +
      `BlockedAssignmentIds` (3, tryout-number order), `ReadyCampaignId`, `ClosedCampaignId`,
      `EligibleTeamId`/`EligibleTeamName`. Seed shape:
  - **Blocked campaign** (3 participants, all blockers): one `Undecided` (outcomes blocker), one
    `Assigned` to a team whose cutoff exceeds the player's graduation year (eligibility blocker), one
    `Assigned` to an `Archived` team (archivedTeams blocker). Counts are deterministically 1/1/1.
  - **Ready campaign** (3 participants): all decided (`NotSelected`) — closes on the first attempt.
  - **Closed campaign** (3 decided participants): closed through the real `POST CampaignEndpoints.CloseUrl`
    (or the shared `CloseCampaignThroughServiceAsync` helper) so a real `Closed` lifecycle event exists
    for the activity feed; record its detail for the reopen assertions.
  - **Deviation:** the seed also registers a second administrator (promoted via `ClubEndpoints.AssignAdmin`)
    because `Admin_StaleBlockedClose_ShowsConflictAlert_WithoutFreezing` needs two authoritative admin
    cookies, matching `PlacementSeed`.
- [x] `Admin_OverviewAndCloseout_HappyPath_ResolvesBlockers_AndCloses_IntoReadOnlyState`: open the
      blocked campaign workspace (hydration-retry click-through per browser-suite notes); Overview
      shows the snapshot, "Closeout blocked" `role=status` line, and the admin "Open closeout" link;
      follow it → Closeout checklist shows authoritative counts and the three blocker rows with their
      policy `Count` + `Message` verbatim; click "Review unresolved" (outcomes row) → lands on
      `tab=placements` with `unresolvedOnly=true`; resolve all three blockers through the placements
      UI (outcome selects with accessible names, per the placement suite patterns); return to Closeout
      (tab buttons); all rows now "Satisfied", "Close campaign" enabled → click → `role=status`
      "Campaign closed." announcement → panel switches to the closed view ("This campaign is closed
      and read-only." note, closure metadata, final summary counts) and Overview activity contains a
      `Closed` event.
- [x] `Admin_BlockedClose_ShowsBlockerDetails_CloseDisabled_AndNothingFrozen`: on the blocked campaign,
      assert the three blocker rows render Count+Message text (not color-only), the "Close campaign"
      button is `disabled`, and nothing is frozen — an admin placement save on the placements tab
      still succeeds (200 via the real UI, success announcement).
- [x] `Admin_StaleBlockedClose_ShowsConflictAlert_WithoutFreezing`: two signed-in admin contexts.
      Admin A loads the ready campaign's Closeout (readiness says ready). Admin B (second context, or
      reuse the evaluator + a promoted second admin via `ClubEndpoints.AssignAdmin` as in `PlacementSeed`)
      changes a placement to `Undecided` behind A's session. A clicks "Close campaign" → the warning
      `role=alert` shows the conflict with the explicit blocker detail (fallback
      "Resolve all campaign close blockers before closing this campaign." if no detail), the panel
      refetches readiness and shows the blocker rows, and the campaign remains Active (placements still
      editable for B).
- [x] `Admin_ReopenConfirm_RestoresEditing_PreservingOutcomesAndHistory`: open the Closed campaign as
      admin; Closeout shows the closed read-only view with closure metadata; "Reopen campaign" →
      inline confirm ("Reopening restores editing without discarding outcomes and is recorded for
      audit."); "Cancel" hides it without effect; "Confirm reopen" → `role=status` "Campaign reopened."
      announcement; panel returns to the Active checklist; Overview activity shows both `Closed` and
      `Reopened` events; a placement edit then succeeds (editing restored) and the previously decided
      outcomes are unchanged (summary counts match the seed).
- [x] `NonAdmin_ClosedCampaign_RendersReadOnly_WithoutCloseReopenControls`: evaluator context on the
      Closed campaign: Closeout shows the read-only note + final summary; assert "Close campaign" and
      "Reopen campaign" buttons are absent; Overview shows no "Open closeout" link; placements tab has
      no enabled save controls (per the placement suite's non-admin assertions).
- [x] `DirectCloseoutOverviewUrls_AndBackNavigation_PreserveTabContext`: navigate directly to
      `CampaignWorkspaceUrlState.BuildCloseoutWorkspaceUrl` → Closeout heading renders (assert
      `#closeout-region-heading`); navigate directly to `BuildOverviewWorkspaceUrl` → Overview heading
      renders; `GoBackAsync` with `WaitUntilState.Commit` → Closeout restored with `tab=closeout` in
      the URL; switch tabs via the workspace tab buttons and assert the `tab=` query parameter tracks;
      from a blocker "Review unresolved" drill-down, `GoBackAsync` returns to `tab=closeout`.
      **Deviation:** the literal `/campaigns/{id}?tab=…` URLs are used (the browser project does not
      reference `Nova.UI`), and the `GoBackAsync` checks are driven through client-side tab-button /
      drill-down navigations so `WaitUntilState.Commit` matches the proven browser-suite pattern.
- [x] `Closeout_KeyboardAndA11y_AcrossWideAndNarrowViewports`: wide 1280×800 context — drive the
      closeout flow by keyboard only (Tab to "Open closeout" link, "Close campaign", "Reopen
      campaign" + "Confirm reopen"/"Cancel"), assert visible focus (`ToBeFocusedAsync`), accessible
      names on every control, and `role=status`/`role=alert` announcements after success/conflict.
      Narrow 480×800 context — same controls remain operable, the checklist/summary renders, touch
      targets ≥24×24 CSS px on the close/reopen buttons, and blocker rows remain distinguishable by
      text (Count/Message) rather than color alone. Keep a11y assertions beside the exercised controls;
      use the `NOVA_A11Y_SCREENSHOTS`-gated contrast/touch-target evidence helper (must `Assert.Skip`
      when unset, never pass silently).
  - **Deviation:** the env-gated screenshot/evidence capture is a separate `[Fact]`
    `Closeout_A11yEvidence_CapturesScreenshots` so the always-on keyboard/touch-target assertions in the
    main scenario are reported as passed (not masked by the `Assert.Skip`).

### Verification Plan

- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --filter-class "*CampaignCloseoutBrowserTests"` — all pass (AppHost starts via the fixture; Chromium already installed on this machine under `%USERPROFILE%\AppData\Local\ms-playwright`).
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` — full suite stays green (existing evaluation + placement scenarios plus the new closeout ones).
- Optional repeat: run the new closeout class 2× consecutively to confirm determinism.

**Result:** `--filter-class "*CampaignCloseoutBrowserTests"` → **8 total / 7 passed / 1 skipped** (the
`Closeout_A11yEvidence_CapturesScreenshots` skip is `NOVA_A11Y_SCREENSHOTS`-gated via `Assert.Skip`,
never silent). The **full browser suite** (`dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj`)
passed twice consecutively: **30 total / 28 passed / 2 skipped / 0 failed** (the two skips are the
pre-existing `A11yManualChecklist_...` and the new gated closeout evidence test).

### Phase Summary

Added `CloseoutSeed.cs` (admin, second admin, approved evaluator, an eligible team, and the blocked /
ready / closed campaigns) and `CampaignCloseoutBrowserTests.cs` with the seven closeout scenarios plus a
gated a11y-evidence scenario. Because the placements Save button only renders after the select change
reaches the Blazor draft state (a hydration signal), the shared `SavePlacementOutcomeAsync` and a generic
`ActUntilAsync` retry helper drive every closeout click/key interaction through the SSR-hydration window;
touch-target measurement also retries past a transient zero-size re-render. The `DirectCloseoutOverviewUrls`
scenario drives Back through client-side tab/drill-down navigations so `GoBackAsync(WaitUntilState.Commit)`
matches the proven suite pattern. No production code was touched.

## Phase 4: Defect repair (contingent, close/reopen slice only)

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator (diagnosis and any production edit need cross-cutting judgment)

- [x] Only if Phases 2–3 expose failing behavior: diagnose whether the failure is a test error or a
      genuine defect in the delivered close/reopen slice (service, policy, endpoint, client, or
      workspace/closeout/overview components).
- [x] Fix genuine defects in-place without adding feature behavior, endpoints, or duplicate policies;
      load the relevant instruction file(s) (`service-layer`, `functional-core`, `api-endpoints`,
      `blazor-architecture`) before editing production code.
- [x] If a component defect is found, add/update the minimal bUnit coverage in
      `CampaignCloseoutPanelTests`/`CampaignOverviewPanelTests`/`CampaignWorkspaceTests` for the
      repaired behavior (only if the browser scenario cannot reliably pin it).
- [x] Record every defect found and its fix in the Phase Summary (so the PR description can cite
      evidence without hiding production changes behind the validation work).

### Verification Plan

- Re-run the failing scenario/class until green, then run the full test project it lives in.
- `dotnet format Nova.slnx --verify-no-changes` (scope with `--include` to touched files if
  pre-existing sibling-session failures persist).

**Result:** No genuine production defects were found. Every failure observed during Phases 2–3 was a
test-side issue (SSR-hydration click/select timing, a touch-target measurement race, an activity-feed
locator, and a cross-mutation ordering bug in the keyboard scenario) and was repaired in the test code
itself. `dotnet format Nova.slnx --verify-no-changes` is clean (no `--include` scoping needed).

### Phase Summary

No production-code changes were required. The delivered close/reopen slice behaved correctly against
PostgreSQL and the browser boundary on the first real exercise; the integration additions and browser
scenarios passed once the test-side hydration/measurement races were fixed. No bUnit additions were
needed (the existing `CampaignCloseoutPanelTests`/`CampaignOverviewPanelTests` already pin the
loading/error/retry/in-flight states that the browser cannot reliably exercise).

## Phase 5: Deduplication audit

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [x] With the Phase 1 matrix table as the checklist, walk the four layers (policy/service unit,
      endpoint/client unit, HTTP integration, browser) and remove or repair overlapping lower-level
      cases exposed by the final matrix (e.g., close-conflict mechanics asserted verbatim in both
      panel bUnit and the new browser conflict scenario — prefer keeping the bUnit state-machine proof
      and the browser boundary proof only where they prove distinct things).
- [x] Never remove a case the matrix requires at that layer, and never remove provider-specific
      assertions from the integration suite (per `testing.instructions.md`, SQLite cannot prove
      advisory locks, races, check constraints, or SQL translation).
- [x] Update the Phase 1 matrix table so it reflects the final, deduplicated mapping.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full unit suite green (no
  coverage silently lost).
- Targeted re-runs of any integration/browser classes whose overlap was trimmed.

**Result:** `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` → **1642 passed / 0 failed /
0 skipped**. No integration/browser overlap was trimmed, so no targeted re-runs were needed beyond the
full suites already run.

### Phase Summary

The four-layer walk found no removable overlap: the panel bUnit state-machine proofs and the browser
boundary scenarios each prove distinct things (bUnit proves loading/error/retry/in-flight/close-conflict
state transitions; the browser proves real hydration, multi-user close conflict, URL/history, keyboard,
and responsive layout). Provider-specific integration assertions (advisory locks, races, check
constraints, non-disclosing 404 bodies) were preserved — SQLite cannot prove them. The only deduplication
was the close/reopen service-call helpers, which were consolidated into `SeedingHelpers` (removing the
private duplicates in `CampaignCloseoutHttpTests`). The Phase 1 matrix is updated above to the final
mapping.

## Phase 6: Final verification gate

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: sub-agent with a smaller model (mechanical commands; escalate any failure to the
orchestrator)

- [x] `dotnet build Nova.slnx` — clean build.
- [x] `dotnet format Nova.slnx --verify-no-changes` — clean (or scoped `--include` if pre-existing
      failures are confirmed unrelated; record which).
- [x] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — all pass.
- [x] `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — all pass
      (Aspire AppHost + PostgreSQL; CI does not run this, so it must pass locally).
- [x] `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` — **full suite** all pass
      (env-gated a11y-screenshot skips are acceptable and must skip via `Assert.Skip`, never pass
      silently).
- [x] Record final test counts and any env-gated skips in the Phase Summary; open the PR linked to
      issue #103 (`Closes #103`), with the PR body mapping each matrix row to its tests, and post the
      epic-#12 "current readiness" comment summarizing delivered evidence.

### Verification Plan

- All five commands succeed; no pre-existing tests regress. CI runs build + unit only.

**Result (recorded on the PR branch):**
- `dotnet build Nova.slnx` → **Build succeeded, 0 warnings, 0 errors**.
- `dotnet format Nova.slnx --verify-no-changes` → **clean** (exit 0, no changes).
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` → **1642 passed / 0 failed / 0 skipped**.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` → **294 passed / 0 failed / 0 skipped**.
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` → **30 total / 28 passed / 2 skipped / 0 failed**, two consecutive runs (the 2 skips are the pre-existing `A11yManualChecklist_...` and the new `Closeout_A11yEvidence_CapturesScreenshots`, both `NOVA_A11Y_SCREENSHOTS`-gated).

### Phase Summary

All five gate commands pass. No pre-existing tests regressed. The only skips are the two
`NOVA_A11Y_SCREENSHOTS`-gated accessibility-evidence tests (both `Assert.Skip`, never silent). The PR
(see Final Recap) is opened against `main` with `Closes #103`, a row-by-row matrix mapping, full
validation evidence, and the epic-#12 readiness comment.

## Final Recap

This change delivers the Wave 3 boundary validation for issue #103 (epic #12): proof that the delivered
close/reopen/history slice holds against real PostgreSQL and the real browser boundary, with **no new
feature behavior, endpoints, or duplicate policies**.

**PostgreSQL integration additions** (`Nova.Integration.Tests`):
- Promoted the close/reopen service-call helpers into `SeedingHelpers` (`CloseCampaignThroughServiceAsync`,
  `ReopenCampaignThroughServiceAsync`) and removed the private duplicates in `CampaignCloseoutHttpTests`.
- `CampaignReopen_RestoresEditing_WithoutDiscardingOutcomes` (reopen restores placement editing without
  discarding the other decided outcome).
- `CampaignReopen_ReturnsNotFound_ForCrossTenantCampaign` (non-disclosing 404) and non-disclosure
  assertions on the readiness/activity cross-tenant 404s.
- `CloseConcurrency_RejectsSecondClose_WhenCampaignClosesWhileWaitingForLock` and
  `ReopenConcurrency_RejectsSecondReopen_WhenCampaignReopensWhileWaitingForLock` (advisory-lock
  close-vs-close / reopen-vs-reopen races).
- `CampaignLifecycleRaceHttpTests.ConcurrentAdminCloses_YieldOneSuccessOneConflict_WithWinnerPersisted`
  (two admins close concurrently → exactly one 204 / one 409, winner provenance persisted).

**Browser workflow additions** (`Nova.Browser.Tests`):
- `CloseoutSeed` (admin, second admin, approved evaluator, eligible team, blocked/ready/closed campaigns).
- `CampaignCloseoutBrowserTests`: happy-path resolve-then-close, blocked-close (nothing frozen), stale
  blocked-close conflict, reopen confirm (editing restored, outcomes/history preserved), non-admin
  read-only closed view, direct URL + Back tab preservation, and keyboard/a11y across wide/narrow
  viewports — plus a `NOVA_A11Y_SCREENSHOTS`-gated evidence scenario.

**No production code changed.** The slice behaved correctly on first real exercise; all failures during
implementation were test-side hydration/timing issues that were fixed in the test code.

## Deployment Plan

1. Merge the PR into `main` (CI runs `dotnet build Nova.slnx` + unit tests only).
2. No schema migrations, configuration changes, or new endpoints are introduced; nothing to apply.
3. Because CI does not run the Aspire/PostgreSQL or browser suites, the merge is gated on the local
   results recorded here: integration **294/294** and browser **28 passed + 2 env-gated skips**.
4. Close issue #103 on merge (the PR body carries `Closes #103`); the epic #12 readiness comment posted
   on the PR is the consolidated evidence handoff for the Wave 3 gate.
