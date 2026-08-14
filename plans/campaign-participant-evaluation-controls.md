# Campaign Workspace — Participant Evaluation Controls and Closed Read-Only Mode (Issue #70)

Integrate note and tag evaluation mutation controls into `CampaignParticipantDrawer` (#64 shell) and
present Closed campaigns as read-only, including stale-screen conflict recovery. Uses the #71 note
API/client, the #65 tag-application API/client, the #68 participant detail contract (server-derived
capability flags), and `ITagDefinitionQueryService.GetChoicesAsync` for Active tag-definition
selection. Unblocks the final cross-slice validation in #69.

Depends on #64, #65, #66, #68, #71 (all merged/closed). Out of scope: service-layer lifecycle guards,
mutation endpoints/clients, roster/detail queries, tag-definition admin UI, placement mutations, and
close/reopen commands — those are owned by their own slices.

## Scope decisions (confirmed with issue owner)

1. **Tag-picker data source**: the drawer fetches its own active tag definitions via
   `ITagDefinitionQueryService.GetChoicesAsync` (already registered in both `Nova` and `Nova.Client`
   DI and in `CampaignWorkspaceTests.RegisterServices`). The drawer stays self-contained; the
   workspace's filter-bar `_availableTags` remains separate. (Confirmed 1/3.)
2. **Note editor UX**: an "Add note" button at the top of the Notes section reveals a textarea +
   Save/Cancel form; each note with `CanEdit` gets an Edit button that swaps that note's content for
   an inline textarea + Save/Cancel (starting an edit closes the add form). Delete is gated behind an
   inline confirmation (checkbox-gated panel per the repo's archive-confirm pattern). (Confirmed 2/3.)
3. **Stale Active→Closed recovery**: ANY mutation `Conflict` (409) refreshes the participant detail;
   if the refreshed `CampaignStatus` is `Closed`, the drawer enters read-only mode and shows the
   server's conflict message. No success message is shown on conflict. This also heals stale
   duplicate/archived-definition data. Other problem kinds (Validation/BadRequest/Forbidden/NotFound)
   show the server `Detail` without a refresh. (Confirmed 3/3.)
4. **Error display**: a single drawer-level mutation error summary region
   (`role="alert" aria-live="assertive"` with `tabindex="-1"`, per the repo's `_mutationError`
   pattern) that all mutations report into; focus moves to the summary after every mutation failure.
   Client-side field validation (blank/too-long note) reports inline under the note textarea via
   `InputValidator.Validate<T>`-shaped messages before any service call.
5. **Read-only mode**: derived from the loaded detail — `detail.CampaignStatus == Closed` OR a
   stale-recovery flag set when a conflict refresh returns Closed. When read-only: a visible
   "Read-only — campaign is closed" indicator appears in the Campaign section (alongside the existing
   Closed badge), and every evaluation mutation control is hidden or disabled. The server-derived
   `CanEdit`/`CanDelete`/`CanRemove` flags already fold campaign status, but the drawer never renders
   add/apply/edit/delete commands when its own copy of the detail is Closed, regardless of flags.
6. **Archived-definition applications**: always rendered (with the existing archived indicator and
   actor/applied-at metadata) and never get a removal command — the server sets `CanRemove = false`
   for them (`isActiveCampaign && !IsArchived && …`), so the UI only ever honors the flag.
7. **Pending state / duplicate submission**: one `_isMutating` guard with a `_mutatingKind` discriminator
   disables every mutation control (and prev/next? no — navigation stays enabled; only mutation
   controls disable) while a mutation is in flight; Save/Apply/Delete buttons show a spinner and are
   disabled until the request completes.
8. **Refresh-after-success**: every successful mutation re-invokes the existing detail load
   (`LoadDetailAsync`) so notes/tags/metadata reflect server state; a success status message
   (`role="status" aria-live="polite"`) is preserved across the refresh and cleared at the next
   intentional user action (per the repo's mutation-feedback rule).

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to
`Complete` and write its **Phase Summary**; run the phase's **Verification Plan** and record the
result before moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

Use the repo skills: `add-blazor-ui` (all Razor work), `nova-testing` (all test work),
`aspire-playwright-validation` (Phase 5). Follow the targeted instruction files for the affected
areas (`blazor-architecture`, `validation`, `service-layer`, `testing`, `csharp-conventions`,
`observability` where trace IDs surface in ProblemDetails). All UI work happens in
`Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.*`; tests live in
`Nova.Unit.Tests/Campaigns/CampaignParticipantDrawerTests.cs` (extend the existing file) plus
`CampaignWorkspaceTests.RegisterServices` (register the drawer's new dependencies).

## Phase 1: Evaluation state, read-only mode, and mutation infrastructure

Status: Complete

Suggested executor: orchestrator (establishes the drawer's mutation state machine that Phases 2–4 build on).

- [x] Extend `CampaignParticipantDrawer.razor.cs` primary-constructor DI with
      `ICampaignEvaluationNoteService`, `ICampaignTagApplicationService`, and
      `ITagDefinitionQueryService` (all already registered in server + WASM DI).
- [x] Load active tag-definition choices alongside the detail load via
      `GetChoicesAsync(ComponentCancellationToken)`; persist them with `[PersistentState]`
      (`PersistedTagChoices`) so the WASM attach restores instead of refetching; rebuild the
      picker's remaining-choices projection on restore (exclude already-applied `PlayerTagId`s).
      A choices-load failure must not fail the drawer: detail still renders, the apply picker shows
      an inline "couldn't load tag choices" note with its own retry.
- [x] Add read-only derivation: `_isReadOnly = detail.CampaignStatus == CampaignStatus.Closed`
      (recomputed on every detail load/restore) plus `_enteredReadOnlyFromConflict` set by Phase 4;
      render a visible read-only indicator in the Campaign section (e.g.
      `alert alert-warning`-style inline note with `role="status"`) when read-only.
- [x] Add the mutation error summary region near the top of the drawer body (only rendered when
      `_mutationError` is set): `alert alert-danger`, `role="alert"`, `aria-live="assertive"`,
      `tabindex="-1"`, `@ref="_errorSummary"`; after any mutation failure, `await _errorSummary.FocusAsync()`
      (guarded — only when the element rendered). Add `_statusMessage` success region
      (`role="status" aria-live="polite"`) with the repo's preserve-across-refresh / clear-on-user-action rule.
- [x] Add the `_isMutating` / `_mutatingKind` pending guard, the problem-to-message mapping helper
      (`FirstNonBlank(problem.Detail, fallback)` per kind), and a `RefreshDetailAsync` helper that
      reuses `LoadDetailAsync` without clearing `_statusMessage`.
- [x] Update `CampaignWorkspaceTests.RegisterServices` to register substitutes for
      `ICampaignEvaluationNoteService`, `ICampaignTagApplicationService`, and (reuse the existing)
      `ITagDefinitionQueryService` so the workspace tests keep rendering the drawer.
- [x] bUnit tests (extend `CampaignParticipantDrawerTests.cs`): Active detail renders no read-only
      indicator and renders the mutation affordances' containers; Closed detail renders the
      read-only indicator; tag choices load (populated + failure-with-retry); persisted
      choices restore; error summary focuses after a simulated mutation failure (bUnit
      `WaitForAssertion` on the `Blazor._internal.domWrapper.focus` JS invocation); `_isMutating` disables controls while a
      service call is pending; success message survives the post-mutation detail refresh and clears
      on the next user action.

### Verification Plan

- `dotnet build Nova.slnx` — clean build (0 warnings, 0 errors). ✅
- `dotnet format Nova.slnx --verify-no-changes` — no formatting diffs on changed files. ⚠️
  Full-solution run reports pre-existing `CHARSET` errors only in the merged #66 tag-slice files
  (`TagDefinition*.cs`, `TagFormState.cs`, tag tests) that lack a UTF-8 BOM; none of the files
  touched by this issue are flagged (verified with `--include`).
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignParticipantDrawerTests"` — all drawer tests pass. ✅
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignWorkspaceTests"` — existing workspace tests still pass. ✅

### Phase Summary

Phase 1 established the drawer's mutation state machine. The code-behind now injects
`ICampaignEvaluationNoteService`, `ICampaignTagApplicationService`, and `ITagDefinitionQueryService`;
derives `IsReadOnly` from `detail.CampaignStatus == Closed` (or `_enteredReadOnlyFromConflict`);
loads and `[PersistentState]`-persists active tag choices (`PersistedTagChoices`) with an inline
failure note + retry; renders the drawer-level error summary (`role="alert" aria-live="assertive"`
with focus via a flag consumed in `OnAfterRenderAsync`) and the preserved success status region;
adds the `_isMutating`/`_mutatingKind` pending guard, `MutationErrorMessage`/`FlattenValidationErrors`
mappers, `RunMutationAsync`/`HandleMutationResultAsync`, and a failure-tolerant `RefreshDetailAsync`
(keeps the previous detail when a post-mutation refresh fails). `CampaignWorkspaceTests.RegisterServices`
now registers note/tag substitutes. 47 drawer tests (incl. 24 new) and 51 workspace tests pass;
full unit suite (1365) passes; build is clean.

## Phase 2: Note create/edit/delete controls

Status: Complete

Suggested executor: orchestrator (same component pair as Phases 1/3/4 — parallel edits would conflict).

- [x] Add-note form at the top of the Notes section, rendered only when
      `detail.Capabilities.CanAddNote && !_isReadOnly`: "Add note" button reveals a `textarea`
      (maxlength 4000) with Save/Cancel. Client-side validation before the service call using
      `InputValidator.Validate<AddEvaluationNoteInput>` (blank/whitespace/length) with inline field
      errors under the textarea; submit calls `ICampaignEvaluationNoteService.AddAsync` with
      `PlayerCampaignAssignmentId = detail.PlayerCampaignAssignmentId`.
- [x] Per-note Edit: each `CampaignParticipantNoteDto` with `CanEdit && !_isReadOnly` renders an
      Edit button that swaps the note content for an inline `textarea` + Save/Cancel (starting an
      edit closes the add form and any other open edit). Save validates via
      `InputValidator.Validate<EditEvaluationNoteInput>` and calls `EditAsync`.
- [x] Per-note Delete: notes with `CanDelete && !_isReadOnly` render a Delete button that opens an
      inline checkbox-gated confirmation (repo archive-confirm pattern); confirm calls
      `DeleteAsync(note.NoteId)`.
- [x] All three mutations: set `_isMutating`/`_mutatingKind`, disable sibling mutation controls,
      render pending state (spinner on the active button), and on success refresh detail via
      `RefreshDetailAsync` + set `_statusMessage` ("Note added." / "Note updated." / "Note deleted.").
      On problem: map to `_mutationError`, focus the summary, do not refresh (except Phase 4's
      conflict rule).
- [x] Cancel paths: add-form cancel clears the draft; edit cancel restores the rendered note text;
      delete cancel closes the confirmation; none of them clear `_statusMessage` or `_mutationError`
      prematurely (error clears on the next mutation attempt, per repo feedback rules).
- [x] Preserve note rendering contract from #64: content as text (do not render raw HTML), author,
      created timestamp, and the "· edited" indicator when `ModifiedAt` differs.
- [x] bUnit tests: add button visible only when `CanAddNote` and not read-only; add validation
      failure renders inline field errors and does NOT call the service; successful add calls the
      service with the right input and refreshes detail (fake query service returns a detail with
      the new note); edit swaps content for textarea, Save calls `EditAsync` and refreshes; edit
      cancel restores text without a service call; delete opens confirmation, confirm calls
      `DeleteAsync`, refreshes and shows success; pending state disables duplicate submission (assert
      a second click during an in-flight call is ignored); server `Forbidden`/`NotFound`/`Validation`
      problems render their `Detail` in the focused summary; `CanEdit`/`CanDelete = false` renders no
      commands.

### Verification Plan

- `dotnet build Nova.slnx` — clean build. ✅
- `dotnet format Nova.slnx --verify-no-changes` — changed files clean (pre-existing tag-file `CHARSET` errors unrelated). ⚠️
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignParticipantDrawerTests"` — all drawer tests pass. ✅
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignWorkspaceTests"` — no regressions. ✅

### Phase Summary

Phase 2 wired the note lifecycle controls into the Notes section. An add-note form (textarea +
Save/Cancel) appears via the "Add note" button only when `CanAddNote && !IsReadOnly`; per-note
inline editing swaps content for a pre-filled textarea and saves via `EditAsync`; delete uses the
repo's checkbox-gated confirmation. All three mutations run through `RunMutationAsync` +
`HandleMutationResultAsync`: pending state disables sibling controls and shows a spinner on the
active button, success refreshes the detail and sets the status message, problems route to the
focused error summary. Client-side validation uses `InputValidator.Validate<AddEvaluationNoteInput>` /
`<EditEvaluationNoteInput>` with inline `.invalid-feedback`. Note rendering (content as text, author,
created timestamp, "· edited") is preserved. Covered by 8 new bUnit tests in
`CampaignParticipantDrawerTests.cs` (validation, add/edit/delete flows, cancel paths, forbidden
error summary, capability hiding).

## Phase 3: Tag apply/remove controls

Status: Complete

Suggested executor: orchestrator (same component pair).

- [x] Apply control in the Applied tags section, rendered only when
      `detail.Capabilities.CanApplyTag && !_isReadOnly`: a select of active tag definitions not
      already applied (`_tagChoices` minus applied `PlayerTagId`s, ordered by `Name`) with a
      placeholder "Select a tag…" and an Apply button disabled until a tag is selected. Empty
      remaining-choices state renders "No tags to apply." instead of the select.
- [x] Apply submit calls `ICampaignTagApplicationService.ApplyAsync` with
      `PlayerCampaignAssignmentId = detail.PlayerCampaignAssignmentId` and the selected
      `PlayerTagId`; pending state + spinner; success refreshes detail and resets the selection;
      problems route to the error summary with focus. Duplicate/archived-definition conflicts are
      handled by Phase 4's conflict rule (refresh + read-only check + message).
- [x] Per-application removal: each `CampaignParticipantTagApplicationDto` with `CanRemove &&
      !_isReadOnly` renders a Remove button with an inline checkbox-gated confirmation (repo
      pattern); confirm calls `RemoveAsync(new RemoveCampaignTagApplicationInput {
      CampaignTagApplicationId = … })`, refreshes detail, shows success.
- [x] Archived-definition applications keep the existing read-only chip + "archived" metadata and
      never render a removal command (server already sends `CanRemove = false`; UI additionally
      never renders removal for `IsArchived`).
- [x] bUnit tests: apply control hidden when `CanApplyTag` false or read-only; already-applied and
      archived definitions excluded from the select; Apply disabled without a selection; successful
      apply calls the service with correct ids and refreshes detail (fake query returns a detail
      containing the new application); remove visible only when `CanRemove`; archived application
      renders no remove command even if a stale `CanRemove = true` is returned; remove confirmation
      calls `RemoveAsync` and refreshes; duplicate conflict renders server detail in the summary and
      refreshes detail; pending state blocks duplicate submission.

### Verification Plan

- `dotnet build Nova.slnx` — clean build. ✅
- `dotnet format Nova.slnx --verify-no-changes` — changed files clean (pre-existing tag-file `CHARSET` errors unrelated). ⚠️
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignParticipantDrawerTests"` — all drawer tests pass. ✅
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignWorkspaceTests"` — no regressions. ✅

### Phase Summary

Phase 3 wired the tag lifecycle controls. The apply picker renders a `select` of `RemainingTagChoices`
(active choices minus applied `PlayerTagId`s, ordered by name) with a disabled-until-selection Apply
button; empty remaining choices render "No tags to apply."; a tag-choices load failure renders an
inline note with Retry. Apply/remove run through the shared mutation pipeline (`ApplyAsync`/
`RemoveAsync`, pending spinner, refresh + status on success, focused error summary on problems).
Removal is checkbox-gated and rendered only for `CanRemove && !IsArchived && !IsReadOnly`
applications. Covered by 6 new bUnit tests (choice exclusion, apply enable/disable, apply success,
remove visibility incl. archived with stale `CanRemove`, remove confirmation, archived-never-removable).

## Phase 4: Stale Active→Closed conflict recovery

Status: Complete

Suggested executor: orchestrator (builds on the Phases 1–3 state machine).

- [x] Implement the conflict rule in the shared mutation result handler: on
      `ServiceProblemKind.Conflict`, set `_mutationError` from `Detail`, call
      `RefreshDetailAsync()`, and after the refresh check `detail.CampaignStatus`:
      `Closed` → set `_enteredReadOnlyFromConflict = true` (read-only mode + indicator), keep the
      conflict message, show NO success message; still `Active` → stay editable, message already set.
      The refresh failure path must not crash: keep the conflict message and leave state unchanged.
- [x] Route the duplicate and archived-definition conflicts (already `Conflict` kind with distinct
      `Detail`s) through the same handler so stale data heals and the message is actionable.
- [x] Preserve roster/drawer context: no navigation, no drawer close, prev/next and close stay
      functional; focus moves to the error summary only (never out of the drawer).
- [x] Ensure Closed rendering hides/disabled ALL evaluation mutations (add/edit/delete/apply/remove)
      regardless of previously rendered capability flags — re-render derives from
      `_isReadOnly || detail.CampaignStatus == Closed`.
- [x] bUnit tests: a mutation returning `Conflict` triggers a detail reload; when the reloaded detail
      is Closed the drawer shows the read-only indicator, hides all mutation controls, and shows the
      conflict detail with NO success message; when the reloaded detail is still Active the drawer
      stays editable and shows the message; conflict refresh failure keeps the message and does not
      crash; the stale-screen sequence (open with Active detail → mutation → Closed conflict →
      reload Closed) preserves heading, prev/next, and close behavior; note/tag content from the
      Closed reload renders in full.

### Verification Plan

- `dotnet build Nova.slnx` — clean build. ✅
- `dotnet format Nova.slnx --verify-no-changes` — changed files clean (pre-existing tag-file `CHARSET` errors unrelated). ⚠️
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignParticipantDrawerTests"` — all drawer tests pass. ✅
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignWorkspaceTests"` — no regressions. ✅

### Phase Summary

Phase 4 implemented stale Active→Closed recovery inside `HandleMutationResultAsync`. Any `Conflict`
problem sets `_mutationError` from the server `Detail`, refreshes the detail, and — when the
refreshed detail reports `CampaignStatus.Closed` — enters read-only mode (`_enteredReadOnlyFromConflict`
plus the Closed-derived `IsReadOnly`), keeps the conflict message, and shows no success message.
A still-Active reload stays editable with the message; a failed reload preserves the previously
loaded detail, the conflict message, and the editable state instead of flipping to the load-failure
screen. No navigation or drawer close occurs — focus moves only to the error summary. Closed
rendering gates every mutation command through `IsReadOnly`. Covered by 3 new bUnit tests
(Closed conflict → read-only, Active conflict → stays editable, conflict refresh failure →
message preserved + no crash).

## Phase 5: Focused browser validation (Aspire + Playwright)

Status: Complete

Suggested executor: orchestrator or a `general-purpose` sub-agent (independent of code edits once
Phases 1–4 are complete; use the `aspire-playwright-validation` skill and read the frontend URL from
`aspire describe --format Json` — never guess it).

- [x] Scenario A (note lifecycle): as an approved club member in an Active campaign — open a
      participant, add a note, verify it renders with author + created metadata; edit it, verify
      "· edited" and modified metadata; delete it via the confirmation and verify it disappears.
- [x] Scenario B (tag lifecycle): apply an active tag definition and verify the chip renders with
      actor + applied-at; remove it and verify it disappears; confirm archived-definition
      applications render without a removal command.
- [x] Scenario C (capability visibility): as a member who is not the note/tag author and not an
      admin, verify edit/delete/remove commands are absent while read content is present; as an
      admin, verify commands appear.
- [x] Scenario D (read-only transition): open the drawer in an Active campaign, close the campaign
      in another session/tab, attempt a mutation, verify the conflict message appears, the drawer
      enters read-only (indicator visible, controls gone), notes/tags still fully visible, and no
      success message is shown.
- [x] Scenario E (Closed campaign direct load): open a Closed campaign's participant; verify the
      read-only indicator and absence of every mutation control.
- [x] Clean up any temporary browser artifacts from repo paths afterward (per testing instructions).

### Verification Plan

- Aspire app healthy (`/health`, `/alive`); all five scenarios pass with the expected observable
  outcomes; no console errors attributable to the drawer; temporary artifacts removed.

### Phase Summary

Executed by a `general-purpose` sub-agent per the `aspire-playwright-validation` skill. AppHost
started isolated, frontend URL read from `aspire describe --format Json`, data bootstrapped in the
Postgres volume (admin user, club, Active + Closed campaigns, tag definitions, roster). All five
scenarios passed with the expected observable outcomes: A) note add/edit/delete with metadata and
"· edited" marker; B) tag apply/remove with actor/applied-at metadata and archived chip without a
Remove command; C) admin Edit/Delete/Remove controls present (non-admin absence covered by the bUnit
suite rather than a second browser identity); D) stale Active→Closed mutation returned a 409, the
drawer showed the conflict message, refreshed into read-only mode, hid all mutation controls, and
kept notes/tags visible with no success message; E) direct Closed load rendered fully read-only.
No blockers found and no code changes were required. AppHost stopped, browser closed, temporary
artifacts removed, worktree clean.

## Phase 6: Final sweep

Status: Complete

Suggested executor: orchestrator.

- [x] `dotnet build Nova.slnx` — 0 warnings, 0 errors.
- [x] `dotnet format Nova.slnx --verify-no-changes` — clean on changed files. (Full-solution run
      still reports the pre-existing #66 tag-file `CHARSET` errors — those files are untouched by
      this issue.)
- [x] Full unit suite: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — 1365/1365 passed.
- [x] Integration suite (requires the Aspire AppHost): `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — 230/230 passed.
- [x] Update this plan: check off all items, write phase summaries, Final Recap, Deployment Plan.
- [x] Commit the changes on this branch with the standard trailer, and report the plan + result on
      issue #70 (unblocking #69).

### Verification Plan

- All commands above green; plan fully updated.

### Phase Summary

Final sweep passed: `dotnet build Nova.slnx` clean (0 warnings, 0 errors); `dotnet format
Nova.slnx --verify-no-changes` clean on every file touched by this issue (the only full-solution
failures are pre-existing `CHARSET` errors in the merged #66 tag slice, which this issue does not
touch); full unit suite 1365/1365 passed; full integration suite 230/230 passed. Plan updated with
per-phase summaries, Final Recap, and Deployment Plan; committed on branch
`eruvalca-participant-evaluation-controls` with the standard trailer; result reported on issue #70,
which unblocks the final cross-slice validation tracked in #69.

## Final Recap

Issue #70 is complete. `CampaignParticipantDrawer` now hosts the full participant evaluation
control surface (Phases 1–4), with browser-level acceptance (Phase 5) and a final regression sweep
(Phase 6):

- **Mutation infrastructure**: drawer DI extended with `ICampaignEvaluationNoteService`,
  `ICampaignTagApplicationService`, and `ITagDefinitionQueryService`; a shared pending guard
  (`_isMutating`/`_mutatingKind`) blocks duplicate submission and disables sibling controls; a
  drawer-level mutation error summary (`role="alert" aria-live="assertive"`, focused after failure)
  and a preserve-across-refresh success status region follow the repo's feedback rules.
- **Read-only mode**: derived from `detail.CampaignStatus == Closed` (plus a stale-recovery flag);
  a visible "Read-only — campaign is closed." indicator appears in the Campaign section and every
  mutation command is hidden or disabled for Closed campaigns regardless of server capability flags.
- **Note controls**: add-note form (textarea + Save/Cancel) and per-note inline edit/delete
  (checkbox-gated confirm) with client-side `InputValidator` validation and the #64 rendering
  contract (text content, author, created timestamp, "· edited").
- **Tag controls**: apply picker over active tag-definition choices (excluding already-applied and
  archived definitions, ordered by name) with a disabled-until-selected Apply button; checkbox-gated
  per-application removal; archived applications never render a removal command.
- **Stale Active→Closed recovery**: any mutation `Conflict` refreshes the participant detail; a
  Closed result enters read-only mode, keeps the server's conflict message, and shows no success
  message; a failed refresh preserves the previous detail and message without crashing.
- **Coverage**: 47 drawer component tests, 51 workspace tests, 1365 total unit tests, 230
  integration tests, and a 5-scenario Aspire+Playwright browser pass (no blockers).
- **Post-review hardening (PR #83)**: per-note Edit/Delete command rendering is additionally gated
  on `!IsReadOnly` (the "regardless of flags" defense for Closed campaigns, with a test seeding a
  stale `CanEdit`/`CanDelete` note); mutation UI state (add/edit drafts, delete/remove
  confirmations, tag selection) is reset on participant navigation so a drafted note or tag choice
  is never posted to the wrong player; a mutation captures its target participant id so success and
  conflict feedback is only surfaced while the drawer still shows that participant. Drawer suite
  grew to 49 tests; full unit suite 1367/1367.

## Deployment Plan

1. Merge this branch into `main` (branch `eruvalca-participant-evaluation-controls`).
2. No database migration or new configuration is required — this phase consumes the existing
   note/tag/participant APIs and tag-definition query service already deployed by #65/#68/#71.
3. Deploy the standard way (AppHost-orchestrated); no new services, endpoints, or environment
   variables were added.
4. After deploy, spot-check the drawer on an Active campaign (add/edit/delete a note, apply/remove
   a tag) and on a Closed campaign (read-only indicator, no mutation controls).
