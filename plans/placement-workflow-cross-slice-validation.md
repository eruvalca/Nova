# Placement Workflow Cross-Slice Validation (Issue #88)

Prove the completed placement slice (delivered by #85, #86, #87 — all closed) against real
PostgreSQL and the real browser boundary per the issue's validation matrix: tenant isolation,
concurrency/lifecycle races, optimistic-concurrency behavior, responsive behavior, and
accessibility essentials. This is the final Wave 3 gate for epic #11; it consolidates boundary
evidence and fixes only defects in the delivered slice.

**Hard constraints (from the issue):** no new placement feature behavior, no new endpoints, no
duplicate policy implementations. Test-only additions plus in-slice defect repairs. Overview/
Closeout composition, close/reopen flows, closeout history, and unrelated regression expansion are
out of scope (#12's Closeout UI is not a dependency).

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

- **Nova.Integration.Tests/Http/CampaignPlacementHttpTests.cs** (23 tests): anonymous/member/admin
  mutation boundaries, route/body id mismatch, Assigned-without-team, unparseable JSON,
  cross-tenant assignment (404), stale token (409, preserves winner), closed campaign (409),
  archived player (409), ineligible team (400), ordered roster page, grad-year+unresolved
  composition, deterministic paging, all-four-outcome summary independent of filters, least-
  privileged member reads, anonymous/without-club/cross-tenant query boundaries, invalid query
  values, default paging.
- **Nova.Integration.Tests/Data/CampaignPlacementRetryTests.cs** (2 tests): retry after pre-commit
  transient failure; ambiguous-commit success verification (stable replacement token).
- **Nova.Integration.Tests/Data/CampaignLifecyclePostgresTests.cs**:
  `PlacementConcurrency_RejectsMutation_WhenCampaignClosesWhileWaitingForLock` — the campaign-close
  advisory-lock race (lock → reload → reject) is already proven.
- **Nova.Integration.Tests/Data/TeamPlayerGraduationYearRaceTests.cs** (3 tests): team-update lock
  order (players before team), concurrent team/player graduation-year changes cannot strand an
  ineligible placement, unlocked-player placement window conflict.
- **Nova.Browser.Tests** (`CampaignPlacementBrowserTests.cs` + `PlacementSeed.cs`, 3 scenarios):
  admin assign/save/summary/unresolved-row-removal + reload round-trip + touch targets;
  non-admin read-only; closed-campaign frozen banner. Seed: 1 admin, 1 approved evaluator, Active
  campaign with 60 Undecided participants, 4 teams (cutoffs 2028/2030/2032/2033), Closed campaign.
- **Nova.Unit.Tests/Campaigns** (9 placement files incl. `CampaignPlacementsPanelTests.cs` with 16
  component tests): policy matrix, service shell, endpoint metadata, client serialization,
  validation, panel edit state machine, token adoption, conflict flow, narrow-card rendering,
  return-URL propagation.

## Phase 1: Matrix-to-coverage audit

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [x] Produce an explicit row-by-row table mapping every line of the issue's "Required validation
      matrix" (8 PostgreSQL rows, 6 browser rows) to the test(s) that prove it, marked
      `covered` / `gap` / `partial`, recorded in this plan (add as a `### Coverage matrix` section
      under this phase when complete).
- [x] For each `gap`/`partial` row, write the concrete test spec (file, method name
      `Subject_Outcome_Condition`, seed shape, assertions) into the appropriate phase below; keep
      each invariant at the lowest effective test layer per `testing.instructions.md`.
- [x] Verify the planned seeds are deterministic: grad-year distribution in `PlacementSeed`,
      closed-campaign state, team cutoffs, and the second-admin promotion path (real
      assign-ClubAdmin endpoint in `Nova.Shared/Features/Clubs/ClubEndpoints.cs`).
- [x] Check whether a player-archive-while-waiting-for-lock race test exists anywhere (grep for
      player advisory-lock race patterns); if missing, finalize its spec for Phase 2.
- [x] Confirm no new endpoint/route/policy is required for any planned test (tests must exercise
      the existing surface only).

### Coverage matrix

| Required validation row | Existing evidence read during audit | Status | Follow-up |
| --- | --- | --- | --- |
| Approved member versus administrator access at query and mutation boundaries | `CampaignPlacementHttpTests.GetPlacementRoutes_ReturnPayload_ForLeastPrivilegedClubMember`, `CampaignPlacementUpdate_ReturnsForbidden_ForClubMember`, `CampaignPlacementUpdate_ReturnsOk_WithReplacementToken_AndPersistsPlacement_ForClubAdmin` | covered | Keep boundary split. |
| Cross-tenant campaign, assignment, player, and team identifiers preserve non-disclosing behavior | `CampaignPlacementHttpTests.GetPlacementRoutes_ReturnNotFound_ForCrossTenantCampaign`, `CampaignPlacementUpdate_ReturnsNotFound_ForCrossTenantAssignment` | partial | Add cross-tenant team PUT and verify non-disclosing detail. |
| Summary counts remain correct across all four outcomes and independent of roster filters/paging | `CampaignPlacementHttpTests.GetPlacementSummary_ReturnsWholeCampaignCounts_IndependentOfRosterFilters` and query seed with all four outcomes | covered | Browser summary transitions are still a gap below. |
| Graduation-year plus unresolved-only filtering composes with deterministic paging/order | `CampaignPlacementHttpTests.GetPlacementRoster_ComposesGraduationYearAndUnresolvedOnlyFilters`, `GetPlacementRoster_PagesDeterministicallyAcrossPages` | covered | Browser URL/filter composition remains a gap below. |
| Two administrators submit the same expected token: one wins, stale request conflicts, persisted row/token match winner | `CampaignPlacementUpdate_ReturnsConflict_AndPreservesWinner_WhenTokenIsStale` is sequential only | partial | Add concurrent HTTP race with two real admin cookies and winner persistence assertions. |
| Campaign, player, or team lifecycle/cutoff state changes between read and save serialize and reject stale intent | `CampaignLifecyclePostgresTests.PlacementConcurrency_RejectsMutation_WhenCampaignClosesWhileWaitingForLock`, `TeamPlayerGraduationYearRaceTests` (three provider races), archived-player HTTP guard | partial | Add player-archive-while-waiting-for-lock race; add archived-team HTTP guard. |
| Assigned requires eligible Active same-tenant team; every non-Assigned outcome persists without a team | `CampaignPlacementUpdate_ReturnsValidationProblem_ForIneligibleTeam`, `...WhenAssignedOutcomeLacksTeam`; no provider assertion for clearing an existing team | partial | Add archived-team and NotSelected/Withdrawn clearing tests. |
| Closed campaigns remain readable and reject all placement mutations | `CampaignPlacementUpdate_ReturnsConflict_ForClosedCampaign`; browser closed campaign is read-only | covered | Browser read-only scenario remains listed below for complete matrix proof. |
| Administrator direct Placements URL, filters, each outcome, eligible team, save, summary/row state | Existing browser admin scenario assigns only; unresolved filter and reload round-trip | partial | Add graduation filter, NotSelected/Withdrawn, explicit summary transitions, and deterministic seed years. |
| Successful second edit reuses replacement token without full reload | Existing browser coverage reloads after first save only | gap | Add same-row second edit/save without navigation. |
| Staged concurrent update shows visible conflict recovery and reload winner | No browser multi-admin conflict scenario | gap | Add second-admin promotion and two-context stale-draft scenario. |
| Closed campaign and approved non-administrator render static results without enabled controls | `PlacementsTab_RendersReadOnly_ForApprovedNonAdmin`, `ClosedCampaign_ShowsFrozenBanner_AndStaticRows` | covered | Preserve. |
| Participant navigation and Back preserve placement tab/filter context | No placement browser navigation/history scenario | gap | Add participant link return URL and `GoBackAsync(WaitUntil=Commit)` assertions. |
| Wide/narrow viewports keyboard, focus, labels, announcements, color-independent essentials | Existing browser scenario checks pointer save and touch-target size; no narrow keyboard/focus/labels/status/error coverage | partial | Add responsive keyboard scenario and keep a11y checks beside exercised controls. |

Audit scope accounted for: `Nova.Integration.Tests/Http/CampaignPlacementHttpTests.cs`,
`Data/CampaignPlacementRetryTests.cs`, `Data/CampaignLifecyclePostgresTests.cs`,
`Data/TeamPlayerGraduationYearRaceTests.cs`, `Nova.Browser.Tests/CampaignPlacementBrowserTests.cs`,
`PlacementSeed.cs`, and the placement-focused `Nova.Unit.Tests/Campaigns` component, policy, service,
endpoint, client, validation, token, conflict, narrow-card, and return-URL test files. The audit found
no existing player-archive-while-waiting-for-lock race and no required new endpoint or policy.

### Verification Plan

- Re-run the grep/scan and confirm the matrix table covers all 14 rows and every existing placement
  test file is accounted for (no claim written without having read the test body).

### Phase Summary

Complete. The audit read the placement HTTP, retry, lifecycle, graduation-race, browser, seed, and
placement-focused unit files listed above. The 14-row matrix identifies existing evidence and the
exact additions below. The browser seed now uses deterministic 2028/2031 years, four team cutoffs,
a closed campaign, and a second administrator promoted through `ClubEndpoints.AssignAdmin`; no new
endpoint, route, or policy is needed. The player archive lock race was absent and is specified in
Phase 2.

## Phase 2: PostgreSQL integration additions

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: sub-agent with a smaller model (specs from Phase 1; pattern files exist for
every addition)

- [x] `CampaignPlacementUpdate_ReturnsNotFound_ForCrossTenantTeam` in
      `CampaignPlacementHttpTests`: two clubs; club A admin PUTs club A's assignment with club B's
      team id → 404; assert the response detail does not disclose the foreign team/club (extend the
      private `SeedPlacementDataAsync` or add a focused seed; keep seeding helpers per existing
      conventions — shared ones in `Nova.Integration.Tests/Http/SeedingHelpers.cs`).
- [x] `CampaignPlacementUpdate_ReturnsConflict_ForArchivedTeam` in
      `CampaignPlacementHttpTests`: same-tenant team with `LifecycleStatus.Archived` →
      409 with detail "Archived teams cannot receive new placements."
- [x] `CampaignPlacementUpdate_ClearsTeam_ForNonAssignedOutcomes` (`[Theory]` NotSelected,
      Withdrawn) in `CampaignPlacementHttpTests`: seed an assignment that is currently `Assigned`
      with a team; PUT each non-Assigned outcome with `teamId: null` → 200; verify persisted
      `TeamId` is null and the replacement token is returned (proves "every non-Assigned outcome
      persists with no team" at the provider boundary).
- [x] `ConcurrentAdminUpdates_SameExpectedToken_YieldOneSuccessOneConflict_WithWinnerPersisted`
      (new file `Nova.Integration.Tests/Http/CampaignPlacementTokenRaceHttpTests.cs`, modeled on
      `CampaignTagApplicationRaceHttpTests`): two admin HTTP clients PUT the same assignment with
      the same expected token via `Task.WhenAll` → exactly one 200 and one 409 (deterministic: the
      token original-value check serializes the loser regardless of interleaving); assert the
      persisted outcome and `ConcurrencyToken` equal the winner's returned values, and the loser's
      ProblemDetails detail is the conflict message.
- [x] Player-archive race (if the Phase 1 audit confirms the gap): new
      `Data/CampaignPlacementLifecycleRaceTests.cs` test modeled on
      `PlacementConcurrency_RejectsMutation_WhenCampaignClosesWhileWaitingForLock` — hold the
      player advisory lock on a second connection, start the placement mutation, wait for the
      waiter, archive the player, commit, then assert the mutation returns the
      "Archived players cannot receive new placement decisions." conflict and the row is
      unchanged. Promote `WaitForAdvisoryLockWaiterAsync` (currently private in
      `CampaignLifecyclePostgresTests.cs`) to a shared helper if it isn't already shared.
- [x] Mutation-sanity check for the new guards: temporarily disable the cross-tenant-team and
      stale-token guards locally (one at a time) and confirm the corresponding new test fails,
      then restore. Record the result in the Phase Summary.

### Verification Plan

- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignPlacementHttpTests"` — all pass.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignPlacementTokenRaceHttpTests"` — all pass (AppHost starts via the fixture).
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignPlacementLifecycleRaceTests"` (if created) — all pass.
- Existing race suites stay green: `--filter-class "*CampaignLifecyclePostgresTests"` and `--filter-class "*TeamPlayerGraduationYearRaceTests"`.
- Optional deterministic-repeat check: run the new race tests 3× consecutively.

### Phase Summary

Complete. Added cross-tenant-team non-disclosure, archived-team conflict, and non-assigned
team-clearing cases to `CampaignPlacementHttpTests`; added the two-administrator token race in
`CampaignPlacementTokenRaceHttpTests`; and added the PostgreSQL player-archive lock race in
`CampaignPlacementLifecycleRaceTests`. The campaign lock waiter poller is shared by
`PostgresAdvisoryLockTestHelper`. Targeted results: HTTP 26/26, token race 1/1, lifecycle race 1/1,
retry 2/2, campaign lifecycle 8/8, and graduation race 3/3. Mutation-sanity checks were run with
each guard temporarily disabled and restored: the stale-token test returned 200 instead of 409,
and the cross-tenant-team test returned 200 instead of 404.

## Phase 3: Browser workflow additions

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: sub-agent with a smaller model (follows `nova-testing`'s browser-suite recipe;
all helpers/patterns enumerated below)

- [x] Extend `PlacementSeed` (or `SeedingHelpers` where shared): add a **second administrator**
      — register via `IdentityHttpClientHelper`, join the club, promote through the real
      assign-ClubAdmin HTTP endpoint — and expose `SecondAdminEmail` on `SeededPlacementWorkspace`.
- [x] Make the seed's graduation-year distribution explicit and deterministic (e.g., first N
      participants 2028, remainder 2031, or expose per-assignment grad years on the seed record) so
      the filter scenarios can assert exact row sets; keep the 60-participant/4-team/Closed-campaign
      shape.
- [x] `Workspace_AppliesGraduationYearFilter_AndComposesWithUnresolvedOnly`: apply the
      `#placement-graduation-year` select (with a hydration-retry helper like the unresolved-only
      checkbox one), assert the URL gains `placementGraduationYear=`, every visible row matches the
      year, the summary stays whole-campaign (independent of filters), and composing with
      `#placement-unresolved-only` narrows further.
- [x] `Workspace_ChangesEverySupportedOutcome_AndUpdatesSummary`: from the wide table, change
      distinct rows to Assigned (with eligible team), NotSelected, and Withdrawn; after each save
      assert the success `role=status` announcement and the expected summary transition (e.g.,
      "1 assigned" / "1 not selected" / "1 withdrawn", "60 undecided" → decreasing); assert the
      NotSelected/Withdrawn rows persist with no team.
- [x] `SecondEdit_ReusesReplacementToken_WithoutReload`: save a row (no unresolved filter so it
      stays visible), change the same row's outcome again and save without any navigation/reload;
      assert success and updated summary — proves the client adopts the replacement token.
- [x] `ConcurrentUpdate_ShowsConflictRecovery_AndReloadShowsWinner`: two signed-in browser
      contexts (admin + second admin). Admin A opens the row and saves a change. Admin B opens the
      same row (loaded before A's save), edits, and saves → assert the visible conflict alert
      (`role="alert"` with the conflict message), focus moves to the conflict region, and all row
      saves are blocked. "Close and reload" → assert the reloaded row shows A's winning value and
      B's stale draft is never silently reapplied (assert the select values match the winner).
- [x] `ParticipantNavigation_AndBack_RestoreTabAndFilters`: on the placements tab, apply
      graduation-year + unresolved-only (+ a page change), click a participant link (it carries the
      placements return URL), then browser Back with `WaitUntilState.Commit` → assert
      `tab=placements` plus all filter query parameters and the filtered row set are restored.
- [x] `NarrowViewport_CardsRemainKeyboardOperable_WithLabelsAndAnnouncements`: 480×800 context
      (default 1280×800 for the wide checks in the same scenario or a separate one): assert the
      table is hidden and `placement-card-*` cards render; drive the card outcome/team selects and
      Save **by keyboard only** (Tab to the control, arrows/Enter), assert visible focus
      (`ToBeFocusedAsync`), programmatic labels (locate the controls by accessible name
      `Outcome for …` / `Team for …`), the `role=status` save announcement, and touch targets
      ≥24×24 CSS px on the card controls. Wide viewport: same controls usable with pointer and
      keyboard.
- [x] Keep accessibility regression assertions in the scenario that exercises the control (per
      browser-suite conventions); reuse the contrast/measurement approach from
      `CampaignEvaluationBrowserTests` (extract a shared helper only if the existing one is
      private and reuse is clean).

### Verification Plan

- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --filter-class "*CampaignPlacementBrowserTests"` — all pass (AppHost starts via the fixture; Chromium must be installed once per machine).
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` — full suite stays green (existing scenarios + the new placement ones).
- Optional repeat: run the new placement class 2× consecutively to confirm determinism.

### Phase Summary

Complete. `PlacementSeed` now promotes a second administrator through the existing HTTP endpoint
and sets a deterministic 55/5 graduation-year split while preserving the 60-participant,
four-team, active/closed campaign shape. Added six browser scenarios for filter composition, all
outcomes, replacement-token reuse, conflict recovery, URL/history restoration, and narrow
keyboard/a11y behavior. The placement browser class passed 9/9; the full browser suite passed 21/21
with one explicit `NOVA_A11Y_SCREENSHOTS`-gated skip.

## Phase 4: Defect repair (contingent, placement slice only)

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator (diagnosis and any production edit need cross-cutting judgment)

- [x] Only if Phases 2–3 expose failing behavior: diagnose whether the failure is a test error or a
      genuine defect in the delivered placement slice.
- [x] Fix genuine defects in-place (service, policy, endpoint, client, or component as appropriate)
      without adding feature behavior, endpoints, or duplicate policies; load the relevant
      instruction file(s) (`service-layer`, `functional-core`, `api-endpoints`,
      `blazor-architecture`) before editing production code.
- [x] If a component defect is found, add/update the minimal bUnit coverage in
      `CampaignPlacementsPanelTests` for the repaired behavior (only if the browser scenario cannot
      reliably pin it).
- [x] Record every defect found and its fix in the Phase Summary (so the PR description can cite
      evidence without hiding production changes behind the validation work).

### Verification Plan

- Re-run the failing scenario/class until green, then run the full test project it lives in.
- `dotnet format Nova.slnx --verify-no-changes` (scope with `--include` to touched files if
  pre-existing sibling-session failures persist, as in #87's final gate).

### Phase Summary

Complete. The targeted additions exposed no production defect in the delivered placement slice; all
initial failures were browser hydration timing/assertion issues in the new test helpers and were
repaired in test code only. No service, policy, endpoint, client, or component production behavior
was changed, so no additional bUnit coverage was required.

## Phase 5: Deduplication audit

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [x] With the Phase 1 matrix table as the checklist, walk the four layers (policy/service unit,
      endpoint/client unit, HTTP integration, browser) and remove or repair overlapping lower-level
      cases exposed by the final matrix (e.g., a stale-token assertion duplicated verbatim across
      service-shell and endpoint tests; component conflict-mechanics assertions that the browser
      scenario now proves end-to-end).
- [x] Never remove a case the matrix requires at that layer, and never remove provider-specific
      assertions from the integration suite (per `testing.instructions.md`, SQLite cannot prove
      advisory locks, races, or SQL translation).
- [x] Update the Phase 1 matrix table so it reflects the final, deduplicated mapping.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full unit suite green (no
  coverage silently lost).
- Targeted re-runs of any integration/browser classes whose overlap was trimmed.

### Phase Summary

Complete. The final matrix was walked across policy/service, endpoint/client, PostgreSQL HTTP/data,
and browser layers. Existing lower-level cases remain because they prove distinct authorization,
serialization, provider-lock, retry, or component state invariants; no duplicate policy or feature
behavior was introduced and no required provider-specific assertion was removed.

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
- [x] `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` — all pass (env-gated
      a11y-screenshot skips are acceptable and must skip via `Assert.Skip`, never pass silently).
- [x] Record final test counts and any env-gated skips in the Phase Summary.

### Verification Plan

- All five commands succeed; no pre-existing tests regress. CI runs build + unit only.

### Phase Summary

Complete. `dotnet build Nova.slnx` passed with 0 warnings and 0 errors. The touched-file format
check passed with `dotnet format Nova.slnx --verify-no-changes --include ...`; the unscoped format
check remains blocked by pre-existing charset/IDE0161 findings in unrelated tag files, which are
not part of this change. Full unit tests passed 1469/1469, full PostgreSQL integration tests passed
264/264, and full browser tests passed 21/21 with one explicit
`NOVA_A11Y_SCREENSHOTS`-gated skip.

## Final Recap

The placement slice now has objective coverage for all 14 issue-matrix rows. PostgreSQL evidence
covers tenant boundaries, admin/member authorization, summary/filter invariants, archived and closed
lifecycle guards, eligible-team rules, non-assigned team clearing, concurrent token winners, retry
semantics, campaign/player advisory-lock races, and team/player cutoff races. Browser evidence covers
the administrator workflow, all supported outcomes, deterministic filters and paging, replacement
token reuse, two-admin conflict recovery, read-only modes, participant Back restoration, and
responsive keyboard/focus/label/status/touch-target behavior. Only tests and shared test helpers were
changed; no placement feature behavior, endpoint, or duplicate policy was added.

## Deployment Plan

1. Merge the PR into `main` after CI is green and the reviewer confirms the matrix evidence.
2. No database migration or runtime configuration change is included; deploy the existing Nova
   application artifacts normally.
3. Run the existing campaign-workspace and administrator-placement smoke checks after deployment;
   Closeout UI/history is not a dependency.
4. Keep the local PostgreSQL and Playwright suites available for release verification; CI continues
   to run build and unit tests, while integration/browser validation is recorded above.
