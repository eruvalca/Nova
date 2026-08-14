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

Status: Not started

Suggested executor: orchestrator (establishes the drawer's mutation state machine that Phases 2–4 build on).

- [ ] Extend `CampaignParticipantDrawer.razor.cs` primary-constructor DI with
      `ICampaignEvaluationNoteService`, `ICampaignTagApplicationService`, and
      `ITagDefinitionQueryService` (all already registered in server + WASM DI).
- [ ] Load active tag-definition choices alongside the detail load via
      `GetChoicesAsync(ComponentCancellationToken)`; persist them with `[PersistentState]`
      (`PersistedTagChoices`) so the WASM attach restores instead of refetching; rebuild the
      picker's remaining-choices projection on restore (exclude already-applied `PlayerTagId`s).
      A choices-load failure must not fail the drawer: detail still renders, the apply picker shows
      an inline "couldn't load tag choices" note with its own retry.
- [ ] Add read-only derivation: `_isReadOnly = detail.CampaignStatus == CampaignStatus.Closed`
      (recomputed on every detail load/restore) plus `_enteredReadOnlyFromConflict` set by Phase 4;
      render a visible read-only indicator in the Campaign section (e.g.
      `alert alert-warning`-style inline note with `role="status"`) when read-only.
- [ ] Add the mutation error summary region near the top of the drawer body (only rendered when
      `_mutationError` is set): `alert alert-danger`, `role="alert"`, `aria-live="assertive"`,
      `tabindex="-1"`, `@ref="_errorSummary"`; after any mutation failure, `await _errorSummary.FocusAsync()`
      (guarded — only when the element rendered). Add `_statusMessage` success region
      (`role="status" aria-live="polite"`) with the repo's preserve-across-refresh / clear-on-user-action rule.
- [ ] Add the `_isMutating` / `_mutatingKind` pending guard, the problem-to-message mapping helper
      (`FirstNonBlank(problem.Detail, fallback)` per kind), and a `RefreshDetailAsync` helper that
      reuses `LoadDetailAsync` without clearing `_statusMessage`.
- [ ] Update `CampaignWorkspaceTests.RegisterServices` to register substitutes for
      `ICampaignEvaluationNoteService`, `ICampaignTagApplicationService`, and (reuse the existing)
      `ITagDefinitionQueryService` so the workspace tests keep rendering the drawer.
- [ ] bUnit tests (extend `CampaignParticipantDrawerTests.cs`): Active detail renders no read-only
      indicator and renders the mutation affordances' containers; Closed detail renders the
      read-only indicator; tag choices load (populated + failure-with-retry); persisted
      choices restore; error summary focuses after a simulated mutation failure (bUnit
      `WaitForAssertion` on `document.activeElement`); `_isMutating` disables controls while a
      service call is pending; success message survives the post-mutation detail refresh and clears
      on the next user action.

### Verification Plan

- `dotnet build Nova.slnx` — clean build (0 warnings, 0 errors).
- `dotnet format Nova.slnx --verify-no-changes` — no formatting diffs.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignParticipantDrawerTests"` — all drawer tests pass.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignWorkspaceTests"` — existing workspace tests still pass.

### Phase Summary

_(write when phase completes)_

## Phase 2: Note create/edit/delete controls

Status: Not started

Suggested executor: orchestrator (same component pair as Phases 1/3/4 — parallel edits would conflict).

- [ ] Add-note form at the top of the Notes section, rendered only when
      `detail.Capabilities.CanAddNote && !_isReadOnly`: "Add note" button reveals a `textarea`
      (maxlength 4000) with Save/Cancel. Client-side validation before the service call using
      `InputValidator.Validate<AddEvaluationNoteInput>` (blank/whitespace/length) with inline field
      errors under the textarea; submit calls `ICampaignEvaluationNoteService.AddAsync` with
      `PlayerCampaignAssignmentId = detail.PlayerCampaignAssignmentId`.
- [ ] Per-note Edit: each `CampaignParticipantNoteDto` with `CanEdit && !_isReadOnly` renders an
      Edit button that swaps the note content for an inline `textarea` + Save/Cancel (starting an
      edit closes the add form and any other open edit). Save validates via
      `InputValidator.Validate<EditEvaluationNoteInput>` and calls `EditAsync`.
- [ ] Per-note Delete: notes with `CanDelete && !_isReadOnly` render a Delete button that opens an
      inline checkbox-gated confirmation (repo archive-confirm pattern); confirm calls
      `DeleteAsync(note.NoteId)`.
- [ ] All three mutations: set `_isMutating`/`_mutatingKind`, disable sibling mutation controls,
      render pending state (spinner on the active button), and on success refresh detail via
      `RefreshDetailAsync` + set `_statusMessage` ("Note added." / "Note updated." / "Note deleted.").
      On problem: map to `_mutationError`, focus the summary, do not refresh (except Phase 4's
      conflict rule).
- [ ] Cancel paths: add-form cancel clears the draft; edit cancel restores the rendered note text;
      delete cancel closes the confirmation; none of them clear `_statusMessage` or `_mutationError`
      prematurely (error clears on the next mutation attempt, per repo feedback rules).
- [ ] Preserve note rendering contract from #64: content as text (do not render raw HTML), author,
      created timestamp, and the "· edited" indicator when `ModifiedAt` differs.
- [ ] bUnit tests: add button visible only when `CanAddNote` and not read-only; add validation
      failure renders inline field errors and does NOT call the service; successful add calls the
      service with the right input and refreshes detail (fake query service returns a detail with
      the new note); edit swaps content for textarea, Save calls `EditAsync` and refreshes; edit
      cancel restores text without a service call; delete opens confirmation, confirm calls
      `DeleteAsync`, refreshes and shows success; pending state disables duplicate submission (assert
      a second click during an in-flight call is ignored); server `Forbidden`/`NotFound`/`Validation`
      problems render their `Detail` in the focused summary; `CanEdit`/`CanDelete = false` renders no
      commands.

### Verification Plan

- `dotnet build Nova.slnx` — clean build.
- `dotnet format Nova.slnx --verify-no-changes`.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignParticipantDrawerTests"` — all drawer tests pass.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignWorkspaceTests"` — no regressions.

### Phase Summary

_(write when phase completes)_

## Phase 3: Tag apply/remove controls

Status: Not started

Suggested executor: orchestrator (same component pair).

- [ ] Apply control in the Applied tags section, rendered only when
      `detail.Capabilities.CanApplyTag && !_isReadOnly`: a select of active tag definitions not
      already applied (`_tagChoices` minus applied `PlayerTagId`s, ordered by `Name`) with a
      placeholder "Select a tag…" and an Apply button disabled until a tag is selected. Empty
      remaining-choices state renders "No tags to apply." instead of the select.
- [ ] Apply submit calls `ICampaignTagApplicationService.ApplyAsync` with
      `PlayerCampaignAssignmentId = detail.PlayerCampaignAssignmentId` and the selected
      `PlayerTagId`; pending state + spinner; success refreshes detail and resets the selection;
      problems route to the error summary with focus. Duplicate/archived-definition conflicts are
      handled by Phase 4's conflict rule (refresh + read-only check + message).
- [ ] Per-application removal: each `CampaignParticipantTagApplicationDto` with `CanRemove &&
      !_isReadOnly` renders a Remove button with an inline checkbox-gated confirmation (repo
      pattern); confirm calls `RemoveAsync(new RemoveCampaignTagApplicationInput {
      CampaignTagApplicationId = … })`, refreshes detail, shows success.
- [ ] Archived-definition applications keep the existing read-only chip + "archived" metadata and
      never render a removal command (server already sends `CanRemove = false`; UI additionally
      never renders removal for `IsArchived`).
- [ ] bUnit tests: apply control hidden when `CanApplyTag` false or read-only; already-applied and
      archived definitions excluded from the select; Apply disabled without a selection; successful
      apply calls the service with correct ids and refreshes detail (fake query returns a detail
      containing the new application); remove visible only when `CanRemove`; archived application
      renders no remove command even if a stale `CanRemove = true` is returned; remove confirmation
      calls `RemoveAsync` and refreshes; duplicate conflict renders server detail in the summary and
      refreshes detail; pending state blocks duplicate submission.

### Verification Plan

- `dotnet build Nova.slnx` — clean build.
- `dotnet format Nova.slnx --verify-no-changes`.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignParticipantDrawerTests"` — all drawer tests pass.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignWorkspaceTests"` — no regressions.

### Phase Summary

_(write when phase completes)_

## Phase 4: Stale Active→Closed conflict recovery

Status: Not started

Suggested executor: orchestrator (builds on the Phases 1–3 state machine).

- [ ] Implement the conflict rule in the shared mutation result handler: on
      `ServiceProblemKind.Conflict`, set `_mutationError` from `Detail`, call
      `RefreshDetailAsync()`, and after the refresh check `detail.CampaignStatus`:
      `Closed` → set `_enteredReadOnlyFromConflict = true` (read-only mode + indicator), keep the
      conflict message, show NO success message; still `Active` → stay editable, message already set.
      The refresh failure path must not crash: keep the conflict message and leave state unchanged.
- [ ] Route the duplicate and archived-definition conflicts (already `Conflict` kind with distinct
      `Detail`s) through the same handler so stale data heals and the message is actionable.
- [ ] Preserve roster/drawer context: no navigation, no drawer close, prev/next and close stay
      functional; focus moves to the error summary only (never out of the drawer).
- [ ] Ensure Closed rendering hides/disabled ALL evaluation mutations (add/edit/delete/apply/remove)
      regardless of previously rendered capability flags — re-render derives from
      `_isReadOnly || detail.CampaignStatus == Closed`.
- [ ] bUnit tests: a mutation returning `Conflict` triggers a detail reload; when the reloaded detail
      is Closed the drawer shows the read-only indicator, hides all mutation controls, and shows the
      conflict detail with NO success message; when the reloaded detail is still Active the drawer
      stays editable and shows the message; conflict refresh failure keeps the message and does not
      crash; the stale-screen sequence (open with Active detail → mutation → Closed conflict →
      reload Closed) preserves heading, prev/next, and close behavior; note/tag content from the
      Closed reload renders in full.

### Verification Plan

- `dotnet build Nova.slnx` — clean build.
- `dotnet format Nova.slnx --verify-no-changes`.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignParticipantDrawerTests"` — all drawer tests pass.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignWorkspaceTests"` — no regressions.

### Phase Summary

_(write when phase completes)_

## Phase 5: Focused browser validation (Aspire + Playwright)

Status: Not started

Suggested executor: orchestrator or a `general-purpose` sub-agent (independent of code edits once
Phases 1–4 are complete; use the `aspire-playwright-validation` skill and read the frontend URL from
`aspire describe --format Json` — never guess it).

- [ ] Scenario A (note lifecycle): as an approved club member in an Active campaign — open a
      participant, add a note, verify it renders with author + created metadata; edit it, verify
      "· edited" and modified metadata; delete it via the confirmation and verify it disappears.
- [ ] Scenario B (tag lifecycle): apply an active tag definition and verify the chip renders with
      actor + applied-at; remove it and verify it disappears; confirm archived-definition
      applications render without a removal command.
- [ ] Scenario C (capability visibility): as a member who is not the note/tag author and not an
      admin, verify edit/delete/remove commands are absent while read content is present; as an
      admin, verify commands appear.
- [ ] Scenario D (read-only transition): open the drawer in an Active campaign, close the campaign
      in another session/tab, attempt a mutation, verify the conflict message appears, the drawer
      enters read-only (indicator visible, controls gone), notes/tags still fully visible, and no
      success message is shown.
- [ ] Scenario E (Closed campaign direct load): open a Closed campaign's participant; verify the
      read-only indicator and absence of every mutation control.
- [ ] Clean up any temporary browser artifacts from repo paths afterward (per testing instructions).

### Verification Plan

- Aspire app healthy (`/health`, `/alive`); all five scenarios pass with the expected observable
  outcomes; no console errors attributable to the drawer; temporary artifacts removed.

### Phase Summary

_(write when phase completes)_

## Phase 6: Final sweep

Status: Not started

Suggested executor: orchestrator.

- [ ] `dotnet build Nova.slnx` — 0 warnings, 0 errors.
- [ ] `dotnet format Nova.slnx --verify-no-changes` — clean.
- [ ] Full unit suite: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj`.
- [ ] Integration suite (requires the Aspire AppHost): `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — run locally before merge (CI only runs build + unit tests).
- [ ] Update this plan: check off all items, write phase summaries, Final Recap, Deployment Plan.
- [ ] Commit the changes on this branch with the standard trailer, and report the plan + result on
      issue #70 (unblocking #69).

### Verification Plan

- All commands above green; plan fully updated.

### Phase Summary

_(write when phase completes)_

## Final Recap

_(write when all phases complete: summary of the entire piece of work)_

## Deployment Plan

_(write when all phases complete: step-by-step deployment instructions)_
