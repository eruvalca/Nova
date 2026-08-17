# Placement Epic (#11) Gap Remediation

Close the four evidence-backed gaps found in the epic #11 review of the placement slice
(merged via PRs #89/#90/#92/#93 on `main`): an in-flight-save/filter race, silently swallowed
summary failures, unbounded team choices, and lax WASM success-payload deserialization. Fixes
land in this branch (`eruvalca-issue-11-epic-team-placement-and-closeout-outcom-2efff3`) as one
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

Filters and pager disable during saves, navigation is deferred until save completion, and targeted
panel tests pass.

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

Summary failures clear stale counts and render a retryable warning; recovery clears the warning.

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

Team roster supports a validated SQL-side cap, placement choices request 200 and show truncation, and
unit plus HTTP integration coverage passes.

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

Strict JSON success deserialization is enabled globally; placement ordering keys and validation are
implemented. Full unit suite passes (1477), with missing-summary and out-of-order contract coverage.

## Phase 5: Full-suite validation gate

Status: In progress

Suggested executor: task sub-agent (run commands, report)

- [x] `dotnet build Nova.slnx` — 0 warnings/errors.
- [x] `dotnet format Nova.slnx --verify-no-changes` — record result; if blocked only by the
      pre-existing unrelated CHARSET findings, re-verify with `--include <all touched files>` and record both.
- [x] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — 1477 pass.
- [x] `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*TeamRosterHttpTests"` — 4 pass
      (Aspire AppHost + PostgreSQL; run locally, CI does not cover this).
- [ ] `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` — all pass
      (Playwright Chromium; install via `Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1 install chromium`
      if needed; 1 env-gated a11y-screenshot skip is acceptable).

### Verification Plan

- All commands above recorded with pass counts in the phase summary.

### Phase Summary

Build, touched-file format, full unit, and targeted integration validation pass. Repository-wide format
is blocked only by pre-existing unrelated tag CHARSET and migration IDE0161 findings. Browser suite
remains pending.

## Phase 6: Ship — PR, merge, close epic

Status: Not started

Suggested executor: orchestrator

- [ ] Commit the changes (conventional, kebab-case summary referencing #11) and push the branch.
- [ ] Create the PR to `main` via `create_pull_request` (title like "Remediate placement epic
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

_(write when phase completes)_

## Final Recap

_(write when all phases complete: summary of the entire piece of work)_

## Deployment Plan

_(write when all phases complete: step-by-step deployment instructions)_
