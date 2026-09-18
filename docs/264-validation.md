# Issue #264 — Player form: create, edit, archive and restore

## Scope and status

**Status: implemented and committed to PR #285; review rounds 1, 2 and 3 are addressed.** On the
round-3 revision the build, format check, full unit suite, the affected browser selection and the
env-gated capture scenario are green (see [PR review round 3](#pr-review-round-3-pr-285)); each
intermittent browser failure seen on the way is named where it occurred with its isolated re-run.
Full integration and full browser runs on that revision, and the merge-stage reruns required before
merge, are still outstanding.

Delivered:

1. A bounded club-scoped read for the pre-commit enrollment consequence.
2. Durable, owner-scoped creation recovery with reconciliation and an explicit set-aside flow.
3. The redesigned manual intake board, replacing the pre-redesign inline CRUD form.
4. One shared archive confirmation and one shared lifecycle wording for both hosts.
5. An injectable browser boundary that makes the recovery semantics directly testable.

## Tested revision

Original implementation: uncommitted working tree on branch `eruvalca-player-form-crud`, based on
`5df81800` (merged #284); the work was then committed as `b51558b2` and opened as PR #285. The
review-round-1 fixes below were an uncommitted working tree on top of `b51558b2`, and round 1 was
then committed as `cb88a1b9`. The review-round-2 fixes below are an uncommitted working tree on top
of `cb88a1b9`, so that pass's revision is the working tree itself; the tree is the only revision a
reader can reproduce from this record alone. The review-round-3 fixes below are an uncommitted
working tree on top of `aa22450d`, so that pass's revision is likewise the working tree itself, and
that tree is also what regenerated the three curated rasters.

## Guidance actually read

- `.github/instructions/`: `api-endpoints`, `service-layer`, `validation`, `ef-core-tenancy`,
  `blazor-architecture`, `ui-design`, `testing`, `csharp-conventions`, `functional-core`.
- `.agents/skills/add-blazor-ui/references/lifecycle-and-state.md` (pending-command recovery),
  `.agents/skills/impeccable/SKILL.md` and `reference/new-work.md` (comp-led vs code-led path).
- `.impeccable/surfaces/player-intake.md`, `DESIGN.md`, `PRODUCT.md`,
  `docs/279-validation.md` (consumer handoff), `docs/263-validation.md`, `AGENTS.md`.
- Review round 3 re-read `blazor-architecture`, `ui-design`, `csharp-conventions` and `testing` from
  the same set (the round-3 edits touch a component, a page, component tests and the browser capture
  scenario), plus the live PR #285 review bodies for rounds 1-3 to reconcile each finding's wording
  with this record.

## What was implemented

### 1. Pre-commit enrollment consequence (`IPlayerIntakeContextService`)

- Contracts `GetPlayerIntakeContextInput`, `PlayerIntakeContext`, `IPlayerIntakeContextService`;
  server service authorizing from the authenticated club and reading the club's single Active
  campaign. The tenant query filter scopes the read from the authenticated context rather than the
  caller-supplied route value, as `ef-core-tenancy` requires.
- `GET /api/clubs/{clubId}/players/intake-context` (member policy, ProblemDetails). **This deviates
  from the plan's literal `/api/players/intake-context`** because every sibling club-scoped player
  read lives in `/api/clubs/{clubId}/players/*`; consistency was preferred and the deviation is
  recorded here deliberately.
- The client validates a *paired* payload: both fields null, or a positive identity with a
  non-blank name. A partial pair is a contract violation, not a "no campaign" result.

### 2. Durable creation recovery

- `PlayerIntakeBoard.razor.js` owns `localStorage` under the owner-scoped key
  `nova:player-creation:{actorUserId}:{clubId}`, building the key from the owner so the C# and JS
  sides cannot drift on the prefix. Retained bytes are validated before they can become a command;
  invalid, corrupt or owner-mismatched bytes are preserved for an explicit discard and never
  dispatched.
- `PlayerCreationRecoveryStore` implements `IPlayerIntakeInterop` and re-validates the typed payload
  (shared `InputValidator` rules plus deadline consistency), so structurally valid JSON that
  contradicts its own contract is also unusable evidence.
- The exact command is persisted **before** dispatch on every submission path; a failed write blocks
  dispatch and nothing is sent. **The C# policy for this is unit-verified through the injected
  boundary; no browser assertion pins the retained bytes between dispatch and receipt — see the
  limitation below.**
- The collocated module is imported once per circuit but a **failed import is never cached**: the
  next attempt re-imports, so one transient failure cannot make every later read and write fail and
  leave *Retry storage* a permanent no-op for the life of the circuit.
- Outcomes classify as committed (immutable `PlayerCreationCompletion` receipt), definitively
  rejected (receipt-backed duplicate with the operation marker), or unresolved. Expiry never clears
  or re-identifies a retained command, and the set-aside action requires an explicit acknowledgement
  that the earlier result stays unknown.

### 3. The manual intake board

Replaces `PlayerForm.razor`. `PlayerFormState` moved to its own file with added projections, and
`PlayerGraduationYearBlockers` was extracted from the page into a shared helper.

- Required/optional language on every permanent field; no photo field (owned by #278).
- **Unsettled evidence is named, never guessed.** The enrollment consequence states a *check in
  progress* ("Checking the enrollment consequence…") until the club's Active campaign has actually
  been read — it never falls through to the no-campaign sentence in that window — and the fields and
  the commit control are withheld until the owner's retained command has been read, so input typed
  during that window cannot be silently replaced by a landed recovery payload. The withheld state is
  itself named — **Checking this browser for a retained addition…**, in the same note position as the
  frozen and blocked notes — and the withheld field set points at that note through its
  `aria-describedby`, so an unexplained disabled board is not a state the member meets (review round
  3, finding 2).
- Per-field server errors; graduation-year and archive blockers beside the fields they concern.
- A retained unresolved addition keeps its fields **visible but frozen** (the #279 handoff's
  contract) and replays through the same commit control, whose label names the action. An operation
  whose own 24-hour window has closed cannot be replayed, so that control is unavailable and the
  member reviews the directory and sets the addition aside deliberately.
- The receipt replaces the fields and offers **Add another** (resetting only player-specific input),
  **View player** and **Return to players**; the directory preserves a success message on return.
- Possible duplicates block with no override and link to the existing active or archived record.
- The uncommitted-departure guard warns on document unload and same-origin link departure while the
  board holds typed input, released on commit, cancel and set-aside. The module observes DOM input
  (including controls outside the `EditForm`, such as the set-aside acknowledgement) and decides
  whether a prompt is due. A board that holds nothing that could be lost **completes the departure
  the module already cancelled** rather than refusing the attempt, so a cancelled click is inert in
  no state (review round 3, finding 1).

### 4. Shared lifecycle control

`PlayerLifecycleConfirmation.razor` and `PlayerLifecycleCopy` now own the archive confirmation and
the lifecycle sentences both hosts state on success, consumed by **both** the Players directory and
Player detail: the confirmation heading, consequence, acknowledgement, commit label and blocker
text, plus `ArchivedResult`, `RestoredResult` and `RestoreNote`. The two duplicated implementations
are gone, so the hosts cannot state different consequences or different results for the same action.
(Each host's fallback text for a lifecycle *failure* whose problem carries no detail — "Could not
archive player." / "Could not restore player." — stays inline; the result sentences do not.)
Confirmation state (including the acknowledgement) lives in the shared control, the acknowledgement
is scoped to the subject it was given for, and the blockers are captured when the confirmation opens.
Both hosts also derive the *authority* that control requires from authenticated club membership —
the server's own mutation gate — so Player detail no longer narrows archive and restore to the
club-admin role while the directory offers them to every member (review round 3, finding 6).

### 5. Correctness decisions worth recording

- **Validation retains unresolved work.** An earlier draft treated a `Validation` response as proof
  the operation never executed and released the retained command. `docs/279-validation.md` states the
  rule instead — only a valid matching receipt-backed duplicate rejection proves the operation did
  not create — so validation now retains, and the field feedback is shown beside the unresolved
  outcome. The pre-existing tests caught this; they were right and the draft was wrong.
- **The client's own window check, not the server's refusal text, decides replayability.** A refusal
  whose evidence says "expired" while the retained operation id is still inside its own window stays
  replayable, and the server keeps refusing. Nothing is inferred from the refusal text, and no
  fabricated "expired" UI state is shown.

### 6. Defects found and fixed during test migration

| Defect | Evidence and fix |
| --- | --- |
| Unprefixed string parameters passed to the board rendered the **field name** as literal text (`ErrorMessage="_mutationError"` produced an alert reading `_mutationError`) | Caught by the component tests' markup assertions. `@` added for the two `string` parameters. `blazor-architecture` already documents this trap; the draft violated an existing rule. |
| The graduation-year blocker sentence rendered with a line break instead of a space, so `... requires graduation year 2034.` never matched | Caught by `PlayersShowsGraduationYearConflictBlockersWhenUpdateReturnsConflict`. The sentence is now emitted on one line. |
| **Intermittent, two different long journey tests**: `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync` failed in full runs at 11s and 36s and once in a 7-test class run; `OrdinaryMemberCreatesEditsArchivesAndRestoresThroughRoutedFormAsync` failed once at 33s in a later full run | **Unresolved cause, not attributable to a specific change.** Both are long multi-navigation directory journeys (30–46s) and both pass in isolation; the earlier failure of the first had a known cause (the stale departure guard blocking navigation) which was fixed before it next passed. Failing runs are load-sensitive under the suite's four test threads, and the failing step was not re-captured. A green full-suite pass was achieved on the app inputs immediately preceding the final diagnostic-only removal, so the app behaviour is not implicated, but a repeat failure must be investigated rather than re-run away. **Review round 1 reproduced both in its first selection run (13s 982ms and 33s 845ms); both passed in isolation on the same build and in the two selection runs after it.** Round 2 met the same class on three other journeys plus a repeat of the first; that pass's own table names each one with its isolated result. |
| The frozen-state note claimed "Profile entry is closed" while the fields were visible and frozen | Caught while reconciling the tests. The note now says the fields are frozen and why, and the closed-entry wording is limited to the genuinely unreadable state. |
| The retained command was not restored when the board mounted after a route change (only on the page's first render) | Found by reasoning about the mount order; restore now runs once per owner scope after the board mounts, and re-reads on every entry to the board route. |

## Evidence

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx --no-restore` — **passed, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3800 total, 3800 passed, 0 failed, 0 skipped**. The #263 baseline was 3774 passed, so 26 tests were added and nothing regressed. |
| Player component selection | `--filter-class '*PlayerComponentsTests'` — **78 passed, 0 failed** (16 failing before the migration was completed). |
| New service/client unit tests | `--filter-class '*PlayerIntakeContextServiceTests' --filter-class '*HttpPlayerIntakeContextServiceTests'` — **23 passed**. |
| Integration boundary | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build --filter-method '*IntakeContext*'` — **3 passed**. |
| Browser selection | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — **18 passed, 0 failed, 1 skipped** (the skip is the pre-existing env-gated capture). |
| Format | `dotnet format Nova.slnx --no-restore` then `--verify-no-changes --no-restore` — **both passed**, exit 0, on the final edits. |
| Full browser | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` — **226 total, 216 passed, 0 failed, 10 skipped** on the final app inputs before the last two test-only additions. Two later runs with only diagnostic instrumentation removed and one extra test added each showed **a single intermittent failure in a different long directory journey test** (`DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync` at 11s and 36s; `OrdinaryMemberCreatesEditsArchivesAndRestoresThroughRoutedFormAsync` at 33s), each of which passes in isolation. See the failure table. All ten skips are the pre-existing env-gated capture tests (`NOVA_A11Y_SCREENSHOTS`, `NOVA_PLACE_EVIDENCE`, `NOVA_PLAYERS_EVIDENCE`), including the two player-surface capture tests added here; this change added and removed no behavioural scenario, so none is skipped or weakened. |
| Module contract (browser) | `--filter-method '*IntakeBoardModuleRetainsAndReadsOwnerScopedBytesAsync*'` — **passed**, exercising the collocated module's write/read/clear/owner-isolation contract in a real browser. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **677 total, 677 passed, 0 failed** (the #263 baseline was 674, so the three new intake-context boundary tests are the entire delta). |

Named integration coverage: `IntakeContextNamesTheActiveCampaignForOrdinaryMemberAsync`,
`IntakeContextReportsExplicitNullPairWhenNoCampaignIsActiveAsync`,
`IntakeContextDeniesAnonymousRequestsAsync`.

Named unit coverage added beyond the intake read: the board's required/optional labelling and
validation, the pre-commit consequence for both campaign cases, and
`PlayersTreatsAnExpiredRetainedOperationAsUnrecoverableAsync` for the closed-window path.

## PR review round 1 (PR #285)

The review of PR #285 raised seven findings — one High, one Medium, four Low and one Nit. All are
addressed on top of `b51558b2`; nothing was deferred and nothing was resolved by weakening a test.
Each fix carries its own regression coverage where the finding had none.

| # | Severity | Finding | Disposition |
| --- | --- | --- | --- |
| 1 | High | The departure guard *performed* the departure on an attempted same-origin link click instead of opening its confirmation, so `#intake-departure` was unreachable and typed input was discarded silently | **Fixed.** `PlayerIntakeBoard.OnBoardDepartureAttemptAsync` now records the attempt and renders (`await InvokeAsync(StateHasChanged)`); only the panel's **Leave and discard** raises `OnConfirmedDeparture`, which both hosts bind to `LeaveBoard`. Nothing in either suite asserted the panel, so `PlayerFormDepartureGuardAsksBeforeDiscardingTypedInputAsync` was added (see below). |
| 2 | Medium | The archive acknowledgement was per-*instance*, so ticking it for player A and then opening the confirmation for player B left it ticked with the commit enabled | **Fixed inside the component**, so every host is protected: `PlayerLifecycleConfirmation` gained `[Parameter] PlayerId`, tracks the subject the acknowledgement was given for, and clears it in `OnParametersSet` when the subject changes. `Players.razor` passes `_archiveCandidate.PlayerId` and `PlayerDetail.razor` passes its route `PlayerId`. New bUnit case `PlayerLifecycleConfirmationRequiresAFreshAcknowledgementForAnotherPlayer`. |
| 3 | Low | A failed recovery-storage read released an in-memory retained command, dropping the only evidence of an already-dispatched addition and then claiming "Nothing has been sent" | **Fixed.** `Players.razor.Intake.cs` keeps `_pendingCreate`/`_invalidRetainedValue` and resets `_recoveryState` only when there is genuinely nothing retained; the storage panel renders its "Nothing has been sent." sentence only when no retained evidence is shown. New bUnit case `PlayersKeepsInMemoryRetainedCommandWhenStorageReadFailsAsync`. |
| 4 | Low | `cut.Markup.ShouldNotContain("Player created successfully.")` no longer matched any copy, so the `oldSucceeded: true` leg of `PlayersIgnoresLateCreationResultWhileNewClubCreationIsPendingAsync` proved nothing | **Fixed.** Replaced with `cut.FindAll("#intake-receipt-heading").Count.ShouldBe(0)`. The assertion is not vacuous: without the identity guard the late club-42 success would reach `SettleCommittedAsync`, set `_receipt`, and the board would render `#intake-receipt-heading`. |
| 5 | Low | `EvaluationNoteService` was registered twice in `Nova/Program.cs` and `IPlayerLifecycleService` twice in `Nova.Client/Program.cs`, both from a concatenated merge line | **Fixed** — one registration per line, matching their neighbours. Each of the four registrations now appears exactly once (verified by counting matches in both files). |
| 6 | Low | `PlayerDetail.razor.cs` hardcoded two result sentences while §4 above claimed `PlayerLifecycleCopy` owns every lifecycle sentence | **Fixed.** Both literals are now `PlayerLifecycleCopy.ArchivedResult` and `PlayerLifecycleCopy.RestoredResult`, and §4 is restated precisely (the shared type owns the confirmation copy and both hosts' *success* sentences; each host's failure fallback when a problem carries no detail stays inline). |
| 7 | Nit | `PlayerFormState.ResetForNextAddition()` and `ToCorrectedCreateInput(...)` had no callers, so the documented "reset only player-specific input" intent was not the code that ran | **Fixed.** `StartAnotherAdditionAsync` now calls `_createForm.ResetForNextAddition()`, and `ToCorrectedCreateInput` is deleted, because a retained payload is deliberately never corrected in place (its fields are frozen). |

### Confirming evidence (round 1)

Tested revision: the uncommitted working tree on branch `eruvalca-player-form-crud` on top of
`b51558b2`, with no commit created in this pass.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx --no-restore` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3802 total, 3802 passed, 0 failed, 0 skipped**. The #264 baseline was 3800, so the two new cases are the entire delta and nothing regressed. |
| Affected browser selection | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — **21 total, 20 passed, 0 failed, 1 skipped**; the skip is the pre-existing env-gated `NOVA_A11Y_SCREENSHOTS` capture. |
| New departure-guard scenario, named | `--filter-method '*PlayerFormDepartureGuardAsksBeforeDiscardingTypedInputAsync*'` — **1 total, 1 passed, 0 failed**. It proves, in WebAssembly: a typed first name plus a click on the nav rail's **Players** opens `#intake-departure` while the URL stays `/players/new`; **Keep editing** closes the panel and keeps the typed value; the next attempt plus **Leave and discard** navigates to `/players`; a board with no uncommitted input leaves without a prompt. |
| Format | `dotnet format Nova.slnx --verify-no-changes --no-restore` — **exit 0** on the final edits. One `dotnet format Nova.slnx --no-restore` pass was required for a `CHARSET` fix on the new test file; `git status` confirms the formatter touched only the files this change edits. |
| Negative check, finding 3 | With `Players.razor.Intake.cs` temporarily restored to the reviewed body, `PlayersKeepsInMemoryRetainedCommandWhenStorageReadFailsAsync` **fails** (`#intake-unresolved` count `0`). The fix was restored and rebuilt before the runs above. |
| Negative check, finding 1 | With `OnBoardDepartureAttemptAsync` temporarily restored to the reviewed body, `PlayerFormDepartureGuardAsksBeforeDiscardingTypedInputAsync` **fails** after 43s — `Interaction did not settle within the retry window`, URL `/players/new` with no `#intake-departure` — which is the discarded-input behaviour the finding describes. The fix was restored and rebuilt before the runs above. |

First selection run of the round recorded **2 failed**, both of them the long directory journeys the
failure table above already tracks: `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync`
(13s 982ms, a player-record link `href` built from the directory's URL state) and
`OrdinaryMemberCreatesEditsArchivesAndRestoresThroughRoutedFormAsync` (33s 845ms, a `Cancel` **button**
click whose enhanced-navigation probe never started). Neither step is one the departure guard can
mediate — it intercepts anchors only, and the failing assertions are URL-state and enhanced-navigation
probe outcomes — and both tests passed in isolation on that same build (2 total, 2 passed), then in
the re-run and final runs of the selection. They remain the documented load-sensitive flakes, not a
consequence of these fixes.

## PR review round 2 (PR #285)

The second review of PR #285 raised seven findings — two Medium, four Low and one Nit. All are
dispositioned on top of `cb88a1b9`; nothing was deferred and nothing was resolved by weakening a
test. Each code fix carries its own regression coverage, and each piece that could be reverted
without a compile error was reverted temporarily to watch its new test fail (see below). Row 6 was a
PR-body correction rather than code, so it carries the body/record reconciliation check below
instead, and the residual revision claim it left behind is round 3's row 5.

| # | Severity | Finding | Disposition |
| --- | --- | --- | --- |
| 1 | Medium | The board had no *checking* state: it rendered "No campaign is Active…" and a pristine, submittable form before `LoadIntakeContextAsync`/`RestoreRecoveryAsync` landed, so input typed in that window was silently replaced | **Fixed.** `PlayerIntakeBoard` gained `IntakeContextLoading` and `RecoveryChecked` (`true` by default) and both gates `IsEditable` and `CanCommit`. The loading state renders one bounded line in the same `p.intake-consequence` region with its `aria-live` intact — `Checking the enrollment consequence…` — and never a campaign fact. `Players` arms `_intakeContextLoading` at the route boundary in `ApplyRouteState` (a synchronous render happens there, so arming it only "immediately before the read" was too late — the first test run of this fix failed exactly that way) and clears it in both settled branches of the read; `_recoveryChecked` is armed `false` on every entry to the board route and set `true` only once `RestoreRecoveryAsync` has examined the owner's retained command. Five new cases: `IntakeBoardWithholdsEntryUntilTheRetainedCommandIsChecked`, `IntakeBoardNamesTheEnrollmentCheckWhileTheConsequenceIsUnread`, `PlayersWithholdsTheBoardUntilTheRetainedCommandIsCheckedAsync` (first entry *and* re-entry, through a new `ReadGate` on the interop double), `PlayersReopensTheBoardWhenTheRetainedCommandReadFailsAsync` and `PlayersNamesTheEnrollmentCheckWhileTheIntakeConsequenceReadIsOpenAsync` (a held-open NSubstitute read, as the existing recovery tests hold the create call). |
| 2 | Medium | The departure guard's two dirty gates disagreed: the module marks dirty on any DOM input inside the board, but `OnBoardDepartureAttemptAsync` re-gated on the .NET flag, which only `EditContext` field changes set — so the set-aside acknowledgement (a plain `@bind` input outside the `EditForm`) made the module prompt and the board swallow the click | **Fixed** exactly as prescribed: the module owns whether a prompt is due, and the board refuses only when it holds nothing that could be lost (`ShowsReceipt || IsEntryBlocked || !CanManage`) before recording the attempt as dirty. New case `PlayersPromptsOnDepartureAfterTheSetAsideAcknowledgementAloneAsync`: with an unresolved retained command it opens the set-aside confirmation, toggles `#set-aside-acknowledge` (asserting the .NET flag stayed clear), then calls the public `OnBoardDepartureAttemptAsync` with the lease the module actually holds — captured by `PlayerIntakeInteropDouble` from `AttachDepartureGuardAsync`, so no production surface was widened — and asserts `#intake-departure` opens. |
| 3 | Low | A migrated assertion asserted that `#intake-expired` does not contain "Replay the retained addition", but that label lives on the submit button in a sibling `EditForm`, so it could never match | **Fixed** to `await Expect(page.Locator("#intake-submit")).ToHaveTextAsync("Create player");` in `PlayerFormExpiryRetainsCommandWithoutRetryGuidanceAsync`; the existing `#player-first-name` disabled assertion is kept. Non-vacuity proved by temporarily labelling the expired commit control with the replay label: the new assertion fails on `#intake-submit` ("Replay the retained addition" vs "Create player") — see the negative checks. |
| 4 | Low | "Nothing has been sent." is unknowable from `RecoveryState == None`, which proves only that *this instance* knows of nothing retained | **Fixed** to the scoped claim **"Nothing has been sent from this board."**, which is what the board can actually know. `PlayersKeepsInMemoryRetainedCommandWhenStorageReadFailsAsync` was updated to the new wording (still asserting the sentence is absent while retained evidence is shown), and `PlayersReopensTheBoardWhenTheRetainedCommandReadFailsAsync` now pins the sentence positively in the state that legitimately renders it. |
| 5 | Low | A faulted module import was cached by `Lazy<Task<IJSObjectReference>>` for the life of the circuit, so one transient `import` failure made every later read and write fail, kept storage reported unavailable, made **Retry storage** a permanent no-op, and blocked creation until a full page reload | **Fixed.** `PlayerCreationRecoveryStore` now holds a nullable cached `Task<IJSObjectReference>` that drops a faulted load, so the next attempt re-imports; `DisposeAsync` disposes only a reference that actually loaded (`IsCompletedSuccessfully`). New case `PlayerCreationRecoveryStoreTests.RecoveryStoreReimportsTheModuleAfterATransientImportFailureAsync`, driving a hand-written `IJSRuntime`/`IJSObjectReference` double (the pattern this repo already uses for the campaign panel's storage boundary) whose first import throws and whose second succeeds, and asserting the second read succeeds with two imports. |
| 6 | Low | The PR body contradicted the revision it described: it still called the departure guard's browser effect unverified and "not delivered", listed "No separate local review yet", and quoted a merge gate the record already recorded as met, while the diff added `PlayerFormDepartureGuardAsksBeforeDiscardingTypedInputAsync` and this record already carried the round-1 review | **Fixed in the body, with a residual that round 3 caught.** Item 1 moved the guard to delivered-with-evidence (naming the browser scenario), item 4 was replaced by the round-1 dispositions, and the review checklist line was retargeted. The residual — the body still named `cb88a1b9` and the round-1 unit count — is round 3's row 5, and the body now names `aa22450d` with the round-2 counts. Recorded because `AGENTS.md` requires each finding once with source, disposition and evidence, and the body and record to agree. |
| 7 | Nit | `DuplicateDetailUrl` and `InvalidRetainedValue` were never read by `PlayerIntakeBoard` | **Fixed.** Both parameters and their two attribute values in `Players.razor` are deleted (`DuplicateUrl` is recomputed from `DetailUrlFactory`, and the unreadable panel uses `OnDiscardUnreadable` with the page's own field). The duplicate-detail return-context browser scenario, `PlayerFormDuplicateDetailPreservesRosterReturnContextAsync`, passed in the selection run below, so the `DetailUrlFactory` path is unaffected. |

### Confirming evidence (round 2)

Tested revision: the uncommitted working tree on branch `eruvalca-player-form-crud` on top of
`cb88a1b9`, with no commit created in this pass.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx --no-restore` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3809 total, 3809 passed, 0 failed, 0 skipped**. The round-1 baseline was 3802, so the seven new cases are the entire delta and nothing regressed. |
| Affected browser selection | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — final run **21 total, 20 passed, 0 failed, 1 skipped** (the skip is the pre-existing env-gated `NOVA_A11Y_SCREENSHOTS` capture). Earlier runs of the same selection on earlier revisions of this pass reported 1 and 3 failures; every failure is named under *intermittent journeys* below with its isolated re-run, and each passed in isolation. |
| Format | `dotnet format Nova.slnx --verify-no-changes --no-restore` — **exit 0** on the final edits. |
| Negative check, finding 1 (board gates) | With `&& RecoveryChecked` removed from `IsEditable`/`CanCommit` and the loading branch removed from the consequence paragraph: `IntakeBoardWithholdsEntryUntilTheRetainedCommandIsChecked` fails (`fieldset` `disabled` **should be but was not**), `IntakeBoardNamesTheEnrollmentCheckWhileTheConsequenceIsUnread` fails (`p.intake-consequence` should contain "Checking the enrollment consequence"), and the two page-level cases fail the same way — **4 failed, 0 passed**. Fix restored and rebuilt before the runs above. |
| Negative check, finding 1 (route-boundary arming) | With `_recoveryChecked = false` removed from `ApplyRouteState`, only the re-entry half of `PlayersWithholdsTheBoardUntilTheRetainedCommandIsCheckedAsync` fails (`fieldset` `disabled` should be but was not after the second entry) — **1 failed, 1 passed**, which is why that case enters the board twice. |
| Negative check, findings 1, 2, 4 and 5 | With `_recoveryChecked = true` removed from `RestoreRecoveryAsync`, the wording reverted to "Nothing has been sent.", `!_dirty` restored in the departure gate, and the faulted-import drop removed: `PlayersReopensTheBoardWhenTheRetainedCommandReadFailsAsync` fails (sentence absent *and* the board never reopens), `PlayersWithholdsTheBoardUntilTheRetainedCommandIsCheckedAsync` fails (never enabled), `PlayersPromptsOnDepartureAfterTheSetAsideAcknowledgementAloneAsync` fails (`#intake-departure` count 0) and `RecoveryStoreReimportsTheModuleAfterATransientImportFailureAsync` fails the second read — **4 failed, 0 passed**. Fixes restored, files touched and the full solution rebuilt before the runs above. |
| Negative check, finding 3 | With `CommitLabel` temporarily returning the replay label for the expired state too, `PlayerFormExpiryRetainsCommandWithoutRetryGuidanceAsync` fails on `Locator("#intake-submit")` expected "Create player" but received "Replay the retained addition" — the new assertion targets the control that can carry the label. Restored and rebuilt before the runs above. |
| Non-fix observation, finding 1 | Paging the page's `RecoveryChecked` wiring to a literal instead of `_recoveryChecked` does not compile: `CS0414`/`S4487` report the field as assigned but never read. The wiring is therefore enforced by the build, not only by a test. |
| Body/record agreement, finding 6 | The PR body's open items, review-history paragraph and checklist were read against this record and the pushed revision: the round-1 status claims were replaced, the guard's browser scenario was named as delivered, and the checklist line that claimed the local reviews were outstanding was retargeted. The residual stale-revision claims the body kept are round 3's row 5, which resolves them in the body. |

**Intermittent journeys seen during this pass.** All three are the load-sensitive class the
limitations below already track; each was re-run alone on the same build and passed. They are
recorded rather than re-run away, and the round-2 change does add one new reason for load
sensitivity: a creation form is now unusable until the interactive circuit has attached *and*
checked browser storage, where the pre-change form was submittable from its server-rendered markup.

| Journey | In the selection run | Alone |
| --- | --- | --- |
| `PlayerFormKeyboardTabAndEnterSubmitsAsync` | failed (21s 215ms) on `#intake-receipt-heading` never containing "Player added" — the first keystrokes were typed into the board's still-withheld field and never landed | **passed** (1 total, 1 passed). Its interaction now waits for `#player-first-name` to be **enabled**, which is the board's actual contract, instead of only for it to be visible; it passed in every later selection run. |
| `PlayerFormResponsivePreservesInputsAcrossViewportsAsync` | failed twice (41s 469ms, 42s 204ms) with `Timeout 30000ms exceeded` from `FillAsync` — Playwright waited for "visible, enabled and editable" while the input was detached and re-resolved, i.e. the board was not editable for 30s under a loaded four-thread suite | **passed** (1 total, 1 passed). **Unresolved cause:** the captured log shows the wait, not the circuit state, so it is not established whether the circuit attach, the storage check or hydration churn consumed the window; the run that succeeded was on the same build. |
| `PlayerFormDuplicateCanBeCorrectedWithoutOverrideAsync` | failed once (41s 486ms) | **passed** (1 total, 1 passed). |
| `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync` | failed once (38s 120ms) | not re-run in this pass: it is one of the two journeys the round-1 record already names as load-sensitive, and the round-1 pass had already recorded its isolated pass on that build. |

## PR review round 3 (PR #285)

The third review of PR #285 raised six findings — four Low inline, one Nit in the review body, and
one further Low marked **pre-existing** (Player detail's authority for archive and restore) — with
nothing above Low remaining, and verified round-2 findings 1-5 and 7 as genuinely fixed, each with a
test that fails when the fix is reverted. All six are dispositioned on top of `aa22450d`; nothing was
deferred and nothing was resolved by weakening a test. The review also judged the round-2
withholding trade-off acceptable; this record keeps that as a limitation rather than re-arguing it.

| # | Severity | Finding | Disposition |
| --- | --- | --- | --- |
| 1 | Low | A refused departure attempt still swallowed the click: the collocated module cancels the click *before* asking the board, so the board's lossless refusal left the link inert — the symptom round 2 set out to remove — and the module's own dirty flag is cleared through a separate interop round trip that can lag or fail | **Fixed.** `PlayerIntakeBoard.OnBoardDepartureAttemptAsync` now performs the departure when the board holds nothing that could be lost (`ShowsReceipt || IsEntryBlocked || !CanManage`): it clears `_dirty` and raises the same `OnConfirmedDeparture` the panel's confirm raises. No `_dirty` gate was re-added, so the round-2 finding stays fixed. New case `PlayersPerformsTheDepartureWhenTheAttemptCannotLoseAnythingAsync` asks a receipt-state board through the real invokable with the lease `PlayerIntakeInteropDouble` captured from `AttachDepartureGuardAsync`, and asserts the navigation manager lands on the attempted destination with no `#intake-departure` panel. |
| 2 | Low | The withheld board was unexplained once the consequence read settled: the prerendered HTML shows the settled consequence sentence above a disabled field set and a disabled **Create player**, with no line saying why and nothing naming the state for assistive technology | **Fixed.** A named checking note — `#intake-checking-note`, `Checking this browser for a retained addition…` — renders in the same place as the blocked and frozen notes, and the field set's `aria-describedby` resolves to `intake-recovery-note` when blocked or frozen, `intake-checking-note` while the retention check is unsettled, and nothing once it settles. **Deviation from the prescribed diff:** the nested conditional operator the finding prescribes is rejected by SonarAnalyzer `S3358` in this build, so the same three-case mapping lives in the `FieldsDescription` property as an if-chain and the markup reads `aria-describedby="@FieldsDescription"` — identical rendering, analyzer-clean. `DESIGN.md`'s **Manual Player Intake Board** rule and the brief's state enumeration now name the checking/withheld state. New case `IntakeBoardNamesTheRetainedCommandCheckWhileItWithholdsEntry` asserts the note and the attribute while `RecoveryChecked` is false, and their absence once it is true. |
| 3 | Low | The capture scenario — the sole producer of the curated rasters that lock the approved comp — waited only for the consequence read, so it could screenshot the *withheld* board, a composition the committed rasters do not depict and the comp does not cover | **Fixed and re-executed.** The scenario now waits for `#player-first-name` to be **enabled** before its first click, so the gate itself fails a permanently withheld board instead of photographing it. The three tracked rasters were re-captured on this revision (sizes and timestamps below), and the ignored full-page capture was regenerated with them. |
| 4 | Low | The record omitted a round-2 finding and miscounted: the prose said six findings (two Medium, three Low, one Nit) and the round-2 table ran 1, 2, 3, 4, 5, 7 with no row 6, while the PR body said round 2 raised seven (two Medium, four Low, one Nit) | **Fixed.** Row 6 — the PR-body/revision contradiction — is recorded in the round-2 table with its disposition and its body/record reconciliation evidence, and the round-2 prose now reads seven findings (two Medium, four Low and one Nit), matching the body. |
| 5 | Nit | The PR body's revision and round-2 status were stale: it named `cb88a1b9` and the round-1 unit count while the head was `aa22450d`, and open item 1 still listed the two round-2 fixes as outstanding work | **Addressed in the body, not the diff.** This is the residual of round-2 row 6; the body now names `aa22450d` with the round-2 counts and lists the round-3 items as the open work. Recorded here because round 3 verified it in the body rather than on a changed line. |
| 6 | Low (pre-existing) | Player detail withheld archive/restore from ordinary members: `PlayerDetail.razor.cs` gated on `IsInRole(Roles.ClubAdmin)`, while the server's mutation gate is membership-only and the directory uses the member flag — so an ordinary member saw **Archive/Restore** on directory rows but not on the record, contradicting #264's authority contract and this change's own claim that both hosts reach one shared lifecycle control | **Fixed in the direction the brief requires.** The flag is now derived exactly as `Players.razor.cs` derives it — the `NovaClaimTypes.ClubId` claim through the same `ReadClubIdClaim` helper shape, the `NameIdentifier` claim, and an authenticated identity — so both hosts grant the authority the server actually enforces. `PlayerDetailHidesAdminActionsForEvaluator` becomes `PlayerDetailHidesLifecycleActionsWithoutClubMembership` and asserts only the genuinely unauthorised case (no club claim), and two new cases prove an ordinary member reaches both controls: `PlayerDetailShowsArchiveForOrdinaryClubMember` and `PlayerDetailShowsRestoreForOrdinaryClubMemberOnAnArchivedRecord`. |

### Confirming evidence (round 3)

Tested revision: the uncommitted working tree on branch `eruvalca-player-form-crud` on top of
`aa22450d`, with no commit created in this pass.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx --no-restore` — **passed, 0 warnings, 0 errors**. The first build of this pass failed with a single `S3358` on the prescribed nested conditional operator (finding 2); the `FieldsDescription` property is that correction, and the rebuild is the clean one recorded here. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3813 total, 3813 passed, 0 failed, 0 skipped**. The round-2 baseline was 3809, so the four new cases (one departure, one withheld-note, two authority) are the entire delta; the replaced authority test was renamed rather than dropped, so no case was lost. |
| Affected browser selection | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — **21 total, 20 passed, 0 failed, 1 skipped**; the skip is the pre-existing env-gated `NOVA_A11Y_SCREENSHOTS` capture. **No intermittent journey failed in this pass**, so none needed an isolated re-run. |
| Capture scenario (finding 3) | `$env:NOVA_PLAYERS_EVIDENCE = '<worktree>\.impeccable\review\issue-264\captures'` then `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-class '*PlayerIntakeBoardEvidenceTests'` — **1 total, 1 passed, 0 failed, 0 skipped**; the class holds only `CaptureIntakeBoardStatesAsync`, and with the flag set it is not skipped. The three tracked rasters were rewritten — `intake-board-desktop.png` **44,534 bytes** (was 44,418), `intake-board-mobile.png` **27,692** (was 27,610), `intake-board-receipt.png` **43,853** (was 43,787), all at 2026-09-18 02:18 local — and the ignored `intake-board-desktop-full.png` was regenerated too (44,534 bytes, untracked, `git status` lists only the three tracked rasters as modified). |
| Format | `dotnet format Nova.slnx --verify-no-changes --no-restore` — **exit 0** on the final edits, after one `dotnet format Nova.slnx --no-restore` pass whose only change was restoring the `CHARSET` BOM on `PlayerDetail.razor.cs`; the formatter created no artifact of its own. |
| Negative check, findings 1, 2 and 6 | With the departure gate restored to the reviewed `if (ShowsReceipt \|\| IsEntryBlocked \|\| !CanManage) { return; }`, the checking note and its `aria-describedby` case removed, and `_canManagePlayers` re-gated on `IsInRole(Roles.ClubAdmin)`, `dotnet test … --no-build` over the four new cases reported **4 failed, 0 passed**: `PlayersPerformsTheDepartureWhenTheAttemptCannotLoseAnythingAsync` failed on `NavigationManager().Uri` "should end with `/players?view=archived` but was `http://localhost/players/new`"; `IntakeBoardNamesTheRetainedCommandCheckWhileItWithholdsEntry` failed with `Bunit.ElementNotFoundException: No elements were found that matches the selector '#intake-checking-note'`; `PlayerDetailShowsArchiveForOrdinaryClubMember` and `PlayerDetailShowsRestoreForOrdinaryClubMemberOnAnArchivedRecord` both failed their control counts (0). Fixes restored, the full solution rebuilt (0 warnings, 0 errors) and the full unit suite re-run before the runs above. |
| Composition check re-measured (consequence of the re-capture) | `node .impeccable/review/issue-264/comp-measure.mjs` against the re-captured settled board: A still leads on both measures — shell 0.0224 (B 0.0237, C 0.0261), field 0.0273 (B 0.0343, C 0.0336), two-board rows 33.8% (C 76.4%) — so the locked reference is unchanged; the approval-time figures against the earlier capture were 0.0223 / 0.0272 with the same ordering. The script's module import resolved one directory short and could not run as committed, so that path was corrected in this pass and the record's claim that it reproduces every figure is now true. |

After those runs, the only further edits were documentation, the one-line import correction in
`comp-measure.mjs`, the re-captured rasters themselves, and a restore of one doc comment and blank
line in `Nova.Unit.Tests/Players/PlayerComponentsTests.cs`; the application and browser-suite inputs
are unchanged from the revision the browser runs covered, so that selection and that capture pass are
reused rather than repeated (a documentation-only difference, as `AGENTS.md` allows with the
comparison recorded).

## Independent finish review

An independent `impeccable-finish-reviewer` reviewed the finished surface against the direction
contract, the approved comp's own composition inventory and `DESIGN.md`'s named rules. **Verdict:
`fix` — ship with fixes.** Like the implementer, the reviewer **could not perceptually inspect any
raster** (the image tool reported success without yielding viewable content), so it reviewed the
direction contract, rule compliance, copy, states and composition logic at source level, and said so
rather than inventing visual findings. It also recorded that the surface has **no hero measurement of
its own** — `.impeccable/build/state.json` is scoped to issue #197 — which matches the recorded fact
that the formal comp-spec/comp-diff gate was not run.

All seven material fixes are addressed:

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | Three labels pointed at ids that do not exist (`player-date-of-birth`, `player-graduation-year`, `player-jersey-number`), leaving those fields without an accessible name and orphaning their required/optional words | **Fixed.** The three `for` values now match their ids (`player-dob`, `player-grad-year`, `player-jersey`). |
| 2 | Both acknowledgement checkboxes missed the 2.75rem target | **Fixed.** Both stylesheets now carry the established `.form-check` flex/min-height rule from `CampaignCreateForm.razor.css`. |
| 3 | The archive entry control on Player detail was a `btn-sm` (~30px) while the directory host raises its controls to 2.75rem | **Fixed.** `PlayerDetail.razor.css` now raises every `a.btn`/`button.btn` inside the page container to 2.75rem. |
| 4 | The frozen fieldset was not associated with its own explanation | **Fixed.** `aria-describedby` now applies for `IsEntryBlocked \|\| IsFrozen`; review round 3 extended the same association to the withheld/unsettled field set (`intake-checking-note`) — see the round-3 table. |
| 5 | The expired state labelled a disabled control with a status sentence, not an action | **Fixed.** `CommitLabel` now returns the action label for every state; the `#intake-expired` region carries the window-closed explanation. |
| 6 | Graduation-year blockers rendered above the whole field set instead of beside the field to correct | **Fixed.** The region now sits immediately after the date-of-birth/graduation-year row. |
| 7 | The shared confirmation was a live region that also wrapped its own controls, so it could announce nothing or re-announce on churn | **Fixed.** `aria-live` removed; the panel is `aria-labelledby` its heading, and the heading takes focus on open via `tabindex="-1" autofocus`. |

The reviewer's non-material observations were left deliberately, consistent with its own
classification: the graduation-year blocker sentence still names numeric campaign and team ids
because the payload carries no names (a server-side gap); `.intake-blocker` is defined but unused, so
blocking panels render in the amber attention field rather than copper (words carry the state, so
this is a refinement to raise separately); `PlayerDetail.razor`'s pre-existing `card shadow-sm`
stack is outside #264's boundary and belongs to #216. Reported drift: `.impeccable/design.json` is
older than `DESIGN.md`, which the skill says to report rather than repair as a side effect of a
design task.

## Instruction and skill retrospective

Requested against Microsoft's
[instructions-hygiene article](https://devblogs.microsoft.com/dotnet/instructions-hygiene-what-frontier-models-still-need-you-to-say/),
applying its keep/remove/move/verify lens and its test — *what does the model need that it cannot
reliably discover, infer, or retrieve for itself?*

The honest headline: **five of this change's eight lessons were already documented**, four of them in
files the routing table already points to. That is a consultation failure, not a missing-rule failure,
and the article's warning against turning every implementation mistake into a new rule applies
directly.

| Lesson from this change | Guidance decision |
| --- | --- |
| Unprefixed `string` parameters reached the child as literal text, so an alert rendered `_mutationError` | **Keep — already canonical.** `.agents/skills/nova-testing/references/blazor-component-tests.md` documents this exact trap *with the negative assertion that catches it* (`cut.Markup.ShouldNotContain("_formError")`) and a named example. Adding anything would duplicate it. The gap was not reading that reference before writing component tests. |
| Asserting on a private component field (`Instance.Model.FirstName`) | **Keep — already documented.** The same reference says to assert rendered markup, not private component fields. |
| Unit tests passed 3800/3800 with a substituted `IPlayerIntakeInterop`, which said nothing about whether the browser boundary worked | **Keep — already documented.** The same reference already states that browser focus and DOM-replacement behaviour belongs in the browser suite, "not a bUnit JS mock". The enforcement added by this change is a real browser test of the module contract, not new prose. |
| Two long directory journey tests failed intermittently under full-suite load and passed in isolation | **Keep — already documented.** `browser-suite.md` already requires retaining the failing URL and rendered state, and already says an isolated pass does not prove contention caused the failure. Both failing runs *were* wrapped in `WithNavigationDiagnosticsAsync`; the retained diagnostics simply were not read. Not a guidance gap. |
| A line break inside a Razor sentence rendered whitespace instead of a space, so the copy did not read as written and an exact-text assertion missed it | **Add**, one scoped rule in `.github/instructions/blazor-architecture.instructions.md`. Genuinely absent, non-obvious, not tool-enforced, and it silently changes user-visible copy. |
| The availability of an environment-gated capability was judged from the **process** scope while the variable lives in the **user** scope | **Add**, one line in `AGENTS.md` build/validation. Absent, and this single wrong check reversed a plan-level decision and produced a whole deviation record that had to be unwound. |
| A comp could not be inspected visually, so the composition check had to be asserted rather than seen | **Add**, one sentence to the existing `AGENTS.md` comp bullet: disclose the limitation, establish the check by measuring each candidate against the surface's own capture, and get the user's confirmation before locking. |
| Durable creation recovery had no precedent in the recovery recipe | **Add — done in the same change** to the existing `.agents/skills/add-blazor-ui/references/lifecycle-and-state.md`: the owner-scoped `localStorage` variant, its injected boundary, and the frozen-payload rule beside the tab-scoped precedent. |

**No new instruction file and no new skill.** The three additions extend files whose existing scope
already covers the subject: `blazor-architecture.instructions.md` already applies to every `*.razor`,
and the two `AGENTS.md` facts are repo-wide operational constraints. The routing table already sends
markup/interop/recovery work to `add-blazor-ui` and evidence work to `nova-testing`, so a new skill
would fragment those owners and duplicate the four lessons above. Existing guidance was also not
trimmed speculatively: this change proved nothing obsolete, and `AGENTS.md` keeps unrelated cleanup
out of the change.

Dual-ecosystem: there is no `.github/skills` copy of `add-blazor-ui` or `nova-testing`, so no mirror
needed synchronizing. `.codex/agents`, both hook sets, and the four canonical Impeccable agent
definitions were untouched, so `scripts/Test-AgentGuidance.ps1` is unaffected and was re-run to prove
it. This is documentation verification, not a new agent-performance experiment; the application
evidence above is unchanged by it.

## Limitations and remaining work

- **The PR-stage gates are not yet complete.** The work is committed as `b51558b2`, opened as
  PR #285 and committed for review round 1 as `cb88a1b9` and round 2 as `aa22450d`; the separate
  local reviews this change's persisted/recoverable and asynchronous-state work requires have now
  been obtained for all three rounds, and each is recorded above with a disposition for every
  finding. Still outstanding before merge: a full integration run and a full browser run on the
  final revision (round 3 changed no provider, EF or domain behaviour), plus the branch's CI. The
  earlier full-suite evidence above is tied to earlier working trees and must be re-established
  after any further edit.
- **The board's input is now gated on the interactive circuit, by design.** A creation form is
  unusable until the circuit has attached *and* the owner's retained command has been read from
  browser storage, where the pre-change form was submittable from its server-rendered markup. That is
  the point of the round-2 fix — input can no longer be replaced by a landed recovery payload — but
  it moves a slice of the board's readiness behind interactivity, which is why the affected browser
  selection is more load-sensitive than before (see the *intermittent journeys* table). Review round
  3 removed the unexplained half of that trade-off: the withheld board now names its check, so no
  state the member meets is unlabelled. The named keyboard journey waits for the field to be enabled
  instead of merely visible; the other journeys rely on Playwright's own enabled-actionability wait.
- **Approved comp locked: reference A.** Image generation was available through the user-scope
  `OPENAI_API_KEY` (my first check read the process scope, which does not inherit a user-level
  variable — corrected). Three structural candidates were generated on the board's own captured
  shell, and **A was locked on the user's explicit delegation** ("Please choose the comp candidate
  based on your best judgement") using a measured composition check rather than a thumbnail: A
  retains the incumbent shell most faithfully (normalised mean absolute luminance difference 0.0224
  vs 0.0237 for B and 0.0261 for C) and its main field is closest to the composition the brief
  commits to (0.0273 vs 0.0343 and 0.0336), with only 33.8% of main-field rows showing two separated
  ink clusters — the single bounded field sheet — against C's 76.4%. Those figures were re-measured
  on review round 3 against the re-captured settled board; the approval-time measurement against the
  earlier capture read 0.0223 and 0.0272 for A with the same ordering, so the lock holds on the
  shipped composition. The measurement script (`.impeccable/review/issue-264/comp-measure.mjs`)
  reproduces every figure; its module import resolved one directory short and was corrected in
  round 3, since it could not run as committed. **The formal comp-spec/comp-diff gate was not run**,
  so these are direct comparisons against the real build, not the workflow's hero measurement.
- **The agent could not perceptually inspect the rasters.** The image tool reports success without
  yielding viewable content in this environment, so the composition check rests on the measurements
  above, on the prompts each candidate committed to, and on A being the composition the shipped build
  already implements. **The user should still eyeball the locked comp.**
- **Finish review complete; formal hero measurement still absent.** The independent review returned
  `fix` (ship with fixes) and all seven material fixes are addressed (see above). The surface has no
  hero measurement of its own, so its reproduction is proven by the recorded direct comparison against
  the real build, not by the workflow's comp-diff gate.    - **The collocated module's storage contract, and now the departure guard's own effect, are
  browser-verified; the board's dispatch-writes-storage path is not.**
  `IntakeBoardModuleRetainsAndReadsOwnerScopedBytesAsync` imports the module in a real browser and
  drives its own contract: `writePending` retains the exact bytes under the owner-scoped key,
  `readRecovery` reads them back as readable, `clearPending` removes them, and a different owner's key
  reads empty. That is the durability boundary the issue actually needs, and it works.
  `PlayerFormDepartureGuardAsksBeforeDiscardingTypedInputAsync` (added in review round 1) proves the
  guard's behaviour end to end in WebAssembly: with a typed first name, clicking a departure the board
  does not mediate (the nav rail's **Players**) opens `#intake-departure` while the URL stays at
  `/players/new`; **Keep editing** closes the panel and keeps the typed value; clicking the departure
  again and choosing **Leave and discard** navigates to `/players`; and a board holding no uncommitted
  input leaves without a prompt. Each attempt re-enters the board and retypes, so the guard's
  attach-after-first-render window can neither pass nor fail the scenario by timing.
  The board's own *dispatch* → storage path still lacks a browser assertion: retained bytes are
  exercised through the module directly and through the injected boundary in unit tests, but no
  browser journey asserts them between dispatch and receipt. The creation journeys that recover from a
  lost acknowledgement show the path works; nothing pins it.
  Earlier probes of the same boundary, kept here because they explain the record's history:
  - `_content/Nova.UI/Features/Players/Components/PlayerIntakeBoard.razor.js` **is** served (HTTP 200),
    and the file parses and exports all nine functions under `node`.
  - A `console.log` inside `attachDepartureGuard` **did** reach the browser console, proving Blazor's
    interop invocation reaches the module — but a marker written on the next statement
    (`document.documentElement.dataset`) was `MISSING` when read from Playwright, and the attach
    reported success twice with **two different leases**. That points at two mountings across two
    documents (server-interactive, then the WebAssembly reload) rather than at a broken call, but the
    marker discrepancy was not run to ground.
  - A probe for the board's *own* dispatch → storage path never got there: the submit produced
    `posts=0` (no POST at all), so it proved nothing about retention and is recorded as inconclusive,
    not as a failure.
  - **Browser Back/Forward inside the SPA is not intercepted**, so history navigation can discard
    typed input without a prompt. The #270 evaluation guard solved this with history-traversal
    protection, which was not replicated here.
  - The guard's one traced defect — an element-proxy-keyed `WeakMap` that silently missed on every
    later interop call, leaving a stale guard on `document` — is fixed by the lease key plus the
    `isConnected` check, and that fix is what the two-lease console evidence above was taken with.
- The unreadable-retained-state copy merges "corrupt bytes" and "belongs to another owner" into one
  message. Both preserve the bytes and refuse to dispatch, but they are not distinguished in the UI.
- `IPlayerIntakeInterop` is public because a Razor component's constructor must be public; the
  interface and its result types are therefore part of `Nova.UI`'s public surface. This is a
  deliberate trade-off for testability and is recorded rather than hidden.

## Design evidence

**Approved reference:** [`.impeccable/mocks/issue-264-a.png`](../.impeccable/mocks/issue-264-a.png)
with its prompt (`.prompt.txt`) and provenance/composition check (`.png.json`), generated with the
board's own viewport capture as the reference image. Curated captures:
`.impeccable/review/issue-264/captures/intake-board-desktop.png` (1440 × 1000, the comp frame,
44,534 bytes after the review-round-3 re-capture), `intake-board-mobile.png` (390 × 844, 27,692
bytes) and `intake-board-receipt.png` (the committed receipt, 43,853 bytes), plus `comp-measure.mjs`,
which reproduces the composition figures. `.gitignore` carries narrow exceptions for exactly these
files; rejected candidates B and C remain local working artifacts. The re-capture was required by
round 3 because the scenario previously waited only for the consequence read and could photograph
the withheld board; the provenance JSON keeps the measurement taken when the comp was approved,
while the re-measured figures are recorded above.

`.impeccable/surfaces/player-intake.md` records the `#264 manual intake board delivery` with the
approved reference, the measured composition check and the delivery boundary (CSV stays with #218,
photos with #278, record/history with #216), plus the `Direction contract — #264`. `DESIGN.md` gained
**Manual Player Intake Board** (with the Retained Evidence Rule) and **Shared Lifecycle
Confirmation** component sections. No new palette, type, spacing, radius or acceptance-threshold
token was introduced. `.agents/skills/add-blazor-ui/references/lifecycle-and-state.md` records the
durable `localStorage` recovery variant beside the tab-scoped precedent; there is no `.github/skills`
copy of that skill, so no dual-ecosystem sync was required.
