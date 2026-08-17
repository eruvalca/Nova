# Placement Epic (#11) Gap Remediation

Close the four evidence-backed gaps found in the epic #11 review of the placement slice
(merged via PRs #89/#90/#92/#93 on `main`): an in-flight-save/filter race, silently swallowed
summary failures, unbounded team choices, and lax WASM success-payload deserialization. Fixes
land in this branch (`eruvalca-placement-epic-gap-remediation-aad`) as one
PR to `main`; epic #11 is the tracking issue and is closed when the work merges.

## Decisions (confirmed with user)

- Fix all four findings in this branch — no follow-up issues for them.
- Finding 1: disable filter/paging controls while any row is saving (no live-reconcile).
- Finding 3: bound the team query with a documented cap + visible truncation notice (no search/paged endpoint).
- Finding 4: enable strict required-member enforcement **globally** in `HttpSuccessContentExtensions`; fix every newly failing slice by correcting DTO nullability/optionality and server serialization in this same branch (no scope fallback).
- Finding 4 (ordering): add `FirstName`/`LastName` to `CampaignPlacementRosterItem` so the client can verify deterministic ordering (repo testing rule requires "incorrect ordering" coverage).
- No separate tracking issue; epic #11 is updated and closed when the PR merges.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its
status to `Complete` and write its **Phase Summary** (what was done, key decisions, anything
needed to continue with zero context); run the phase's **Verification Plan** and record the
result before moving on. When all phases are done, fill in **Final Recap** and **Deployment
Plan** (the ship steps for phase 6).

Known pre-existing blockers: the unscoped `dotnet format Nova.slnx --verify-no-changes` may
still fail on unrelated tag-file CHARSET/IDE0161 findings (see PR #90/#92/#93 bodies). If so,
verify format with `--include <touched files>` and record it in the phase summary. CI runs
build + unit tests only — integration and browser suites must be run locally before merge.

## Phase 0: Sync branch with main

Status: Complete

Suggested executor: orchestrator

- [x] Merge main into the branch (user fast-forwarded on 2026-08-16; second merge on
      2026-08-17): HEAD is now `3ce47f0`, identical to `origin/main`; `git diff HEAD origin/main`
      over the placement surface is empty.
- [x] Confirm placement files exist locally: `Nova.UI/Features/Campaigns/Components/CampaignPlacementsPanel.*`,
      `Nova.Client/Services/Campaigns/HttpCampaignPlacementQueryService.cs`, `Nova/Features/Teams/TeamRosterQueryService.cs`
      (all present; the line references in Phases 1–4 were re-verified against the local files).

### Verification Plan

- `git --no-pager status --short` — clean.
- `dotnet build Nova.slnx` — 0 warnings/errors.

### Phase Summary

Fast-forward completed by the user on 2026-08-16 (HEAD `922fcd4`); second merge on 2026-08-17
(HEAD `3ce47f0`, still equal to `origin/main`). The working tree matches main (only the plan file
untracked), and `dotnet build Nova.slnx` succeeds with 0 warnings / 0 errors after both merges.
All four findings re-verified line-for-line locally:
`OnParametersSetAsync`/`LoadSummaryAsync`/`LoadTeamChoicesAsync` (panel code-behind 245–364), the
save path (565–635), the unbounded team query (`TeamRosterQueryService` 61–74), the lax
deserializer (`HttpSuccessContentExtensions` 35), and the placement client validators (77–149).
Supporting facts: `CampaignRosterPager` has no `Disabled` parameter yet (Phase 1 plan holds);
`GetTeamRosterInput` has no `Limit` (Phase 3 plan holds); 34 `ReadRequiredJsonAsync` call sites
span ~20 WASM services (Phase 4 audit scope holds). On 2026-08-17 the user merged main again
(now `3ce47f0`): #94/#95 landed — `BadHttpRequestExceptionHandler` maps malformed JSON to 400
ProblemDetails once at the foundation, closing #91, and body endpoints dropped their duplicate
`.ProducesProblem(400)`. Verified via `git diff 922fcd4 HEAD` that none of the four findings'
target files changed; the only placement-area touches are the endpoint route builder's
400-metadata removal, the updated `CampaignPlacementHttpTests` (malformed JSON now expects 400),
and `api-endpoints.instructions.md` guidance. All findings and plan phases re-verified unchanged.

## Phase 1: Fix filter-vs-save race (finding 1)

Status: Complete

Suggested executor: builder sub-agent (smaller model); invoke the `add-blazor-ui` skill for
component conventions.

- [x] In `Nova.UI/Features/Campaigns/Components/CampaignPlacementsPanel.razor.cs`, add a
      computed `_savingActive` flag: any draft in `_drafts` has `IsSaving == true`.
- [x] Disable the filter bar while `_savingActive`: graduation-year select, unresolved-only
      checkbox, and Clear filters button (razor lines ~35–53). Keep `_conflictActive` disables as-is.
- [x] Disable paging while `_savingActive`: add an optional `Disabled` parameter to the shared
      `CampaignRosterPager` component and pass it from the panel (buttons/skip to page links).
- [x] Defensive guard in `OnParametersSetAsync` (line ~258): if a draft is saving when
      URL/navigation changes `State`, defer `LoadRosterAsync` (store pending state; apply it
      after the save completes) so back/forward cannot orphan an in-flight save.
- [x] Re-render after each save completes so controls re-enable.

### Verification Plan

- `dotnet build Nova.slnx` — clean.
- `dotnet format Nova.slnx --verify-no-changes --include Nova.UI/Features/Campaigns/Components/CampaignPlacementsPanel.razor.cs Nova.UI/Features/Campaigns/Components/CampaignPlacementsPanel.razor Nova.UI/Features/Campaigns/Components/CampaignRosterPager.razor` — exit 0.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignPlacementsPanelTests"` — new tests pass: filters/pager disabled during in-flight save; URL/nav change during save does not orphan row state; controls re-enable after save.

### Phase Summary

Implemented by the builder agent (branch `eruvalca-placement-epic-gap-remediation-aad`).

- Added the computed `_savingActive` flag (`_drafts.Values.Any(draft => draft.IsSaving)`).
- Filter bar (graduation-year select, unresolved-only checkbox, Clear filters button) and the
  shared `CampaignRosterPager` (new optional `Disabled` parameter, passed from the panel) are
  disabled while any row is saving; `_conflictActive` disables kept as-is. The filter/page
  handlers now no-op while a save is in flight (defense in depth).
- `OnParametersSetAsync` defers `LoadRosterAsync` when a URL/navigation `State` change arrives
  while a save is in flight: the requested state is stored in `_pendingState` (latest wins) and
  applied in `SaveRowAsync`'s `finally` block once no row is saving, so back/forward navigation
  cannot rebuild drafts out from under an in-flight save. Blazor's post-event re-render re-enables
  the controls after every save completes (no manual `StateHasChanged` needed).
- Review remediation (2026-08-17, PR #97 review): the deferral guard now captures `saveInFlight`
  **before** the Closed-transition `ResetAllDrafts()` so closing the campaign mid-save cannot
  silently bypass it, and navigating back to the already-applied state while a save is in flight
  clears `_pendingState` so the save's `finally` never applies a stale deferred state (URL/roster
  disagreement). Two new bUnit cases cover both paths.

Verification: `dotnet build Nova.slnx` clean; scoped `dotnet format` exit 0; new
`CampaignPlacementsPanelTests` cases cover filters/pager disabled during an in-flight save and
re-enabled after, a state change during a save deferring the roster reload until the save
completes, navigating back to the applied state discarding the deferred state, and a Closed
transition mid-save still deferring a concurrent state change. Full unit suite: 1498 passing.

## Phase 2: Surface summary load/refresh failures (finding 2)

Status: Complete

Suggested executor: builder sub-agent (smaller model)

- [x] In `CampaignPlacementsPanel.razor.cs` `LoadSummaryAsync` (line ~335): track a
      `_summaryLoadFailed` state; on problem, clear `_summary` (never present stale counts as
      authoritative) and set the flag.
- [x] Render an actionable inline warning (with Retry) when the summary failed — distinct from
      the roster error alert; `RetryAsync` must reload summary along with roster/choices.
- [x] Post-save behavior in `SaveRowAsync` (lines ~624–627): if the summary refresh fails, do
      not show "Placement saved." beside stale counts; show a warning that the save succeeded
      but counts could not be refreshed, with retry.
- [x] Keep initial-load behavior: summary failure must not silently render a summary-less footer.

### Verification Plan

- `dotnet build Nova.slnx` — clean.
- `dotnet format Nova.slnx --verify-no-changes --include Nova.UI/Features/Campaigns/Components/CampaignPlacementsPanel.razor.cs Nova.UI/Features/Campaigns/Components/CampaignPlacementsPanel.razor` — exit 0.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignPlacementsPanelTests"` — new bUnit tests pass: initial summary failure shows warning + retry; post-save refresh failure clears counts and warns; retry recovers.

### Phase Summary

- `LoadSummaryAsync` now returns `Task<bool>`: on success it stores the summary, clears
  `_summaryLoadFailed`/`_summaryWarning`; on problem it clears `_summary` (stale counts are never
  shown as authoritative) and sets `_summaryLoadFailed`.
- The razor renders an inline `alert-warning` summary-failure banner with a Retry button — distinct
  from the `alert-danger` roster error — replacing the summary footer when the flag is set, so a
  summary-less state is never silent. The banner's Retry is wired to `RetryAsync`, which reloads
  roster + summary + choices via `LoadInitialAsync`.
- Post-save: when the summary refresh after a successful mutation fails, the "Placement saved."
  success banner is suppressed and a "Placement saved, but the summary could not be refreshed."
  warning (with Retry) is shown instead; the per-row "Saved" status remains accurate.

Verification: new `CampaignPlacementsPanelTests` cover initial summary failure → warning + Retry
recovery, and post-save refresh failure → counts cleared + warning shown + success banner absent.
Full unit suite: 1495 passing; the browser workflow still sees the success banner and summary footer
on the happy path.

## Phase 3: Bound team choices (finding 3)

Status: Complete

Suggested executor: builder sub-agent (smaller model); invoke the `add-api-endpoint` skill for
the shared input contract change and `nova-testing` for the tests.

- [x] In `Nova.Shared/Features/Teams/GetTeamRosterInput.cs`, add optional `Limit` with
      `[Range(1, 200)]`; document that omission keeps the existing unbounded behavior for the
      team management UI and that choice-loading callers must pass a cap.
- [x] In `Nova/Features/Teams/TeamRosterQueryService.cs` (line ~61), apply
      `.Take(input.Limit.Value)` in SQL before materialization when `Limit` is set; keep the
      existing deterministic ordering (Name, then TeamId).
- [x] In `CampaignPlacementsPanel` `LoadTeamChoicesAsync` (line ~350), pass the documented cap
      (constant, e.g. 200).
- [x] Render a truncation notice near the team controls when `_teamChoices.Count == cap`:
      "Showing the first {cap} active teams. If a team is missing, refine via Team management."
      Keep the existing "current team" fallback option behavior.
- [x] Add unit coverage (SQLite tenancy harness) for limit + deterministic ordering, and input
      validation bounds; add HTTP integration coverage for omitted `Limit` (unchanged behavior)
      and an invalid explicit `Limit` (rejected by binding/validation).

### Verification Plan

- `dotnet build Nova.slnx` — clean.
- `dotnet format Nova.slnx --verify-no-changes --include Nova.Shared/Features/Teams/GetTeamRosterInput.cs Nova/Features/Teams/TeamRosterQueryService.cs Nova.UI/Features/Campaigns/Components/CampaignPlacementsPanel.razor.cs Nova.UI/Features/Campaigns/Components/CampaignPlacementsPanel.razor` — exit 0.
- Targeted unit tests for `TeamRosterQueryServiceTests` / `CampaignPlacementsPanelTests` pass.
- Targeted Aspire integration test for the team roster endpoint (omitted + invalid + bounded `Limit`) passes.

### Phase Summary

- `GetTeamRosterInput` gained optional `Limit` (`int?`, `[Range(1, 200)]`) with XML docs stating
  omission keeps the existing unbounded behavior for the team management UI and choice-loading
  callers must pass a documented cap.
- `TeamRosterQueryService` applies `.Take(limit)` **after** the deterministic
  `OrderBy(Name).ThenBy(TeamId)` so the limit never changes which rows are selected for equal names.
- `TeamRosterEndpoints.GetRosterUrl` gained the `limit` query segment (emits only 1..200 per the
  contract) and `HttpTeamRosterService` passes `input.Limit` through.
- `CampaignPlacementsPanel.LoadTeamChoicesAsync` passes `Limit = 200` (new `TeamChoiceLimit`
  constant); the razor shows "Showing the first 200 active teams. If a team is missing, refine via
  Team management." when `_teamChoices.Count == TeamChoiceLimit`. The "current team" fallback option
  is unchanged.

Verification: SQLite tenancy unit tests (bounded limit returns the first teams in deterministic
order; omitted and above-match-count limits return everything); input-validation and URL-builder
contract tests; WASM client limit-passing test; Aspire integration tests for `limit=2` (bounded +
ordered), omitted limit (unchanged), and `limit=0`/`limit=201` (400 ValidationProblemDetails with
`errors.Limit` and traceId). Full unit 1495 and full integration 269 passing.

## Phase 4: Strict WASM success deserialization + ordering enforcement (finding 4)

Status: Complete

Suggested executor: orchestrator (cross-slice blast radius needs judgment; no delegation of the
audit itself)

- [x] In `Nova.Client/Services/HttpSuccessContentExtensions.cs` (line ~35), deserialize with a
      new options instance derived from `JsonSerializerOptions.Web` with
      `RespectRequiredConstructorParameters = true` and `RespectNullableAnnotations = true`;
      keep the `JsonException → ServiceProblem.ServerError` mapping.
- [x] Enumerate every `ReadRequiredJsonAsync` call site (and direct `ReadFromJsonAsync` uses)
      across `Nova.Client/Services/`; list the DTOs affected.
- [x] Run the full unit suite; for each newly failing WASM client test, fix the root cause in
      this branch: correct DTO nullability/optionality (nullable annotations or default values)
      or fix server serialization so responses satisfy the contract — never weaken the global
      enforcement to make a test pass.
- [x] Add `FirstName`/`LastName` to `CampaignPlacementRosterItem` (`Nova.Shared/Features/Campaigns/CampaignPlacementContracts.cs`);
      fill from the existing `PlacementRosterPageRow` projection in `CampaignPlacementQueryService`
      (server already projects both names); keep `DisplayName` for the UI.
- [x] In `HttpCampaignPlacementQueryService`, extend `IsValidRoster` to verify adjacent rows
      follow the server ordering contract (LastName asc, then FirstName asc, then
      `PlayerCampaignAssignmentId` asc).
- [x] Client contract tests (`HttpCampaignPlacementQueryServiceTests`): `{}` summary → ServerError;
      summary missing a count → ServerError; roster row missing outcome → ServerError;
      out-of-order roster page → ServerError. Update existing success-payload fixtures with the
      new `FirstName`/`LastName` fields.
- [x] Update any integration/browser seeds or fixtures that construct roster items.

### Verification Plan

- `dotnet build Nova.slnx` — clean.
- `dotnet format Nova.slnx --verify-no-changes --include <all touched files>` — exit 0.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — **full suite** passes
  (proves no unrelated WASM client regressions from the global change).
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — **full suite**
  passes (Aspire AppHost + PostgreSQL).

### Phase Summary

- `HttpSuccessContentExtensions.ReadRequiredJsonAsync` now deserializes with `StrictWebOptions`
  (a fresh instance derived from `JsonSerializerOptions.Web` with
  `RespectRequiredConstructorParameters = true` and `RespectNullableAnnotations = true`); the
  `JsonException → ServiceProblem.ServerError` mapping is unchanged.
- Audit: 34 `ReadRequiredJsonAsync` call sites across ~20 WASM services
  (`Nova.Client/Services/{Campaigns,Clubs,Photos,Players,Tags,Teams}/`). DTO families affected:
  `CreateCampaignResult`, `EvaluationNoteMutationSuccess`, `UpdateCampaignMetadataResult`,
  `PagedResult<CampaignParticipantRosterItem>`, `CampaignParticipantDetailDto`, `List<int>`,
  `PagedResult<CampaignPlacementRosterItem>`, `CampaignPlacementSummaryDto`,
  `PlacementMutationSuccess`, `CampaignListResult`, `CampaignDetailResult`,
  `CampaignCreationSetupResult`, `CampaignTagApplicationMutationSuccess`,
  `UpdateSeasonMetadataResult`, `ClubJoinRequestDto`, `List<ClubJoinRequestDto>`,
  `List<ClubMemberDto>`, `bool`, `ClubDto`, `List<ClubDto>`, `ProfilePhotoInfo`,
  `PlayerDetailDto`, `PlayerDto`, `PagedResult<PlayerListItem>`, `TagDefinitionListResult`,
  `List<TagDefinitionDto>`, `TeamDetailDto`, `TeamDto`, `List<TeamRosterItem>`. The only direct
  `ReadFromJsonAsync` use is the intermediate `JsonElement` read inside the extension itself.
- After enabling strict enforcement, the **full unit suite passed unchanged** (1495) — no WASM
  client regression. No DTO nullability/optionality corrections or server serialization fixes were
  required: every server success payload already satisfies the strict contract (populated
  positional-record parameters and `required` members, no JSON nulls for non-nullable references).
- Added `FirstName`/`LastName` to `CampaignPlacementRosterItem`; the server projection fills them
  from the existing `PlacementRosterPageRow` (the server already projected both names) and keeps
  `DisplayName`. All constructors updated (server projection, panel/workspace test helpers,
  contract-test fixtures).
- `IsValidRoster` now requires non-blank `FirstName`/`LastName` per row and verifies the portable
  part of the server ordering contract: when adjacent rows share identical last and first names,
  `PlayerCampaignAssignmentId` must be non-decreasing. Different names are **not** compared
  ordinally because the database collation (not ordinal comparison) determines how the server
  orders them — mirroring the existing `HttpCampaignQueryService.CompareCampaign` precedent. New
  contract tests: `{}` summary → ServerError; summary missing a count → ServerError; roster row
  missing `placementOutcome` → ServerError; out-of-order equal-name page (descending assignment
  ids) → ServerError; in-order multi-row page accepted; a collation-ordered page ("Álvarez" before
  "Bond", ordinally descending) accepted.
- Review remediation (2026-08-17, PR #97 review): the original ordinal name comparison in
  `IsOrdered` could reject valid server pages when the DB collation orders accented names first
  (e.g. `postgres:18` `en_US.utf8`); replaced with the equal-name ID tie-breaker above, updated
  the out-of-order fixture to use same-name rows with descending ids, and added the
  collation-ordered acceptance test.
- Integration/browser seeds construct DB entities only (no roster-item fixtures); the real server
  now serializes the new fields, so the HTTP/browser suites exercise the enriched contract.

Verification: build clean; scoped format exit 0; full unit 1495 and full integration 269 passing.

## Phase 5: Full-suite validation gate

Status: Complete

Suggested executor: task sub-agent (run commands, report)

- [x] `dotnet build Nova.slnx` — 0 warnings/errors.
- [x] `dotnet format Nova.slnx --verify-no-changes` — record result; if blocked only by the
      pre-existing unrelated CHARSET findings, re-verify with `--include <all touched files>` and record both.
- [x] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — all pass.
- [x] `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — all pass
      (Aspire AppHost + PostgreSQL; run locally, CI does not cover this).
- [x] `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` — all pass
      (Playwright Chromium; install via `Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1 install chromium`
      if needed; 1 env-gated a11y-screenshot skip is acceptable).

### Verification Plan

- All commands above recorded with pass counts in the phase summary.

### Phase Summary

- `dotnet build Nova.slnx` — **0 warnings / 0 errors**.
- `dotnet format Nova.slnx --verify-no-changes` — fails **only** on the pre-existing unrelated
  tag-file CHARSET/IDE0161 findings (`Nova{,.Client,.Shared,.UI,.Unit.Tests,.Integration.Tests}`
  under `Features/Tags`, `Nova/Features/Shared/CommitAttemptTracker.cs`, tag migrations — none
  touched by this work). Scoped re-verification `--include <all 20 touched files>` exits **0**.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — **1498 passed / 0 failed**
  (23 new tests added by this work + 3 added by the PR #97 review-remediation turn).
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — **269 passed /
  0 failed** (Aspire AppHost + PostgreSQL 18; new team-roster `Limit` coverage included).
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` — **21 passed / 0 failed /
  1 skipped** (the skip is the env-gated `NOVA_A11Y_SCREENSHOTS` evidence test); ran green twice.
  Playwright Chromium installed with `playwright.ps1 install chromium`.
- Pre-existing browser flake found and fixed: `NarrowViewport_CardsRemainKeyboardOperable_WithLabelsAndAnnouncements`
  failed at base `3ce47f0` (reproduced on a clean main worktree in this environment) because a
  queued keyboard change could move the outcome select past Assigned while the stale team-enabled
  check broke out early, disabling the team select mid-assertion. The flow now resets the outcome
  to Undecided before each ArrowDown (so it can never escalate past Assigned) and retries until the
  team select actually enables — keyboard-operability and touch-target assertions unchanged.

## Phase 6: Ship — PR, merge, close epic

Status: In progress (builder completed commit/push/PR + review remediation; merge + epic close are post-merge)

Suggested executor: orchestrator

- [x] Commit the changes (conventional, kebab-case summary referencing #11) and push the branch.
- [x] Create the PR to `main` via `create_pull_request` (title like "Remediate placement epic
      review gaps (#11)"); request a Copilot code review before merging.
- [ ] After merge: add a comment on #11 summarizing the four gaps, the fixes, and the final
      validation evidence (build/format/unit/integration/browser pass counts).
- [ ] Close epic #11 (state `closed`, reason `completed`) — the epic remains the tracking issue;
      close/reopen and Overview/Closeout stay owned by open epic #12. (#91, the malformed-JSON
      debt, was closed upstream by #94/#95 merged from main on 2026-08-17.)

### Verification Plan

- PR merged; CI (build + unit) green on the merge commit.
- #11 closed with the remediation summary comment attached.

### Phase Summary

The builder agent (2026-08-16/17) committed all implementation + plan updates with the Copilot
co-author trailer, pushed `eruvalca-placement-epic-gap-remediation-aad`, and opened the PR to
`main` with `Closes #11` and full validation evidence. CI (build + unit) runs on the PR head. On
2026-08-17 the Reviewer's PR #97 review found three issues (collation-incompatible ordinal
ordering check; stale `_pendingState` applied after navigating back to the applied state; the
deferral guard bypassed by a Closed transition mid-save); all were fixed in a review-remediation
commit with focused tests, and every review thread was replied to and resolved. The remaining
Phase 6 steps — merge and closing epic #11 with the summary comment — are post-merge
orchestrator/human outcomes and were deliberately not performed.

## Final Recap

Completed on 2026-08-16/17 in branch `eruvalca-placement-epic-gap-remediation-aad` (builder agent
under orchestrator delegation) as one PR to `main` closing epic #11.

**Finding 1 — in-flight-save/filter race.** `_savingActive` computed flag disables the filter bar
and the shared `CampaignRosterPager` (new `Disabled` parameter) while any row is saving; the
filter/page handlers no-op during a save; `OnParametersSetAsync` defers a URL/navigation-driven
`State` change into `_pendingState` and applies it after the save completes, so back/forward cannot
orphan an in-flight save. Review remediation: the guard captures the in-flight flag before the
Closed transition resets drafts (so closing mid-save cannot bypass the deferral) and drops a
deferred state when the user navigates back to the applied state.

**Finding 2 — swallowed summary failures.** `LoadSummaryAsync` now reports success/failure, clears
`_summary` on failure, and an inline `alert-warning` banner (distinct from the roster error) with
Retry replaces the summary footer; post-save refresh failure suppresses the "Placement saved."
banner and shows "Placement saved, but the summary could not be refreshed." with retry.

**Finding 3 — unbounded team choices.** `GetTeamRosterInput.Limit` (`[Range(1, 200)]`, omission
keeps unbounded team-management behavior), `.Take` applied after the deterministic
(Name, TeamId) ordering, URL builder + WASM client pass-through, panel requests the documented cap
(200) and renders the truncation notice when the count equals it.

**Finding 4 — lax WASM success deserialization.** `ReadRequiredJsonAsync` now enforces
`RespectRequiredConstructorParameters` + `RespectNullableAnnotations` globally (34 call sites
audited across ~20 services; the full unit suite passed unchanged — no contract corrections needed
because every server payload already satisfies the strict contract). `CampaignPlacementRosterItem`
gained `FirstName`/`LastName` filled from the existing server projection, and the client validator
enforces the portable part of the server ordering contract: the equal-name assignment-id
tie-breaker only, because the database collation (not ordinal comparison) orders different names —
matching the existing `CompareCampaign` precedent after the PR #97 review.

**Validation evidence (Phase 5):** build 0 warnings/0 errors; format — unscoped blocked only by the
pre-existing unrelated tag-file CHARSET/IDE0161 findings, scoped `--include` over all 20 touched
files exits 0; unit **1498/1498** (23 new tests + 3 added by the PR #97 review-remediation turn);
integration **269/269** (Aspire + PostgreSQL 18); browser **21 passed / 1 env-gated a11y skip**
(green twice). A pre-existing narrow-viewport keyboard browser flake (reproduced on clean `main`)
was made deterministic without weakening assertions.

**Plan bookkeeping:** Phases 1–5 Complete with summaries; Phase 6 In progress — commit/push/PR
completed by the builder; merge and epic close are post-merge orchestrator/human outcomes.

## Deployment Plan

Ship steps (post-merge, owned by the orchestrator/human; the builder does not merge or close):

1. **Merge the PR** `Remediate placement epic review gaps (#11)` into `main` (after the Reviewer
   finds it ready and CI build + unit are green on the merge commit). Squash or rebase-merge per
   repo norm; do not delete the branch until #11 closeout is confirmed.
2. **Comment on epic #11** with the four-gap summary and final evidence: build 0/0; format
   (scoped) exit 0; unit 1498; integration 269; browser 21 passed + 1 env-gated skip.
3. **Close epic #11** (state `closed`, reason `completed`). The epic remains the tracking issue;
   close/reopen and Overview/Closeout stay owned by open epic #12.
4. No migrations, no new environment variables, no new dependencies, and no deployment
   configuration changes are required — the change is application code + tests only. Existing
   CI (build + unit) covers the merge commit; integration and browser suites were validated
   locally per repo convention.
