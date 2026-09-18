# Issue #264 — Player form: create, edit, archive and restore

## Scope and status

**Status: implemented, committed to PR #285, and reviewed to a clean result; the merge candidate
passes every gate.** Five fresh-context review rounds have run, each verifying the previous round's
fixes; rounds 1-4 each raised findings that are all addressed, and **round 5 returned "No issues
found"**. On the merge candidate `639c97f6` the build, format check, full unit suite, **full
integration suite** and **full browser suite** are all green (see
[Final validation on the merge candidate](#final-validation-on-the-merge-candidate-639c97f6)); each
intermittent browser failure seen on the way is named where it occurred with its isolated re-run, and
none fired in the final browser pass. What remains is the merge itself, plus re-establishing these
results after any further edit.

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
that tree is also what regenerated the three curated rasters. The review-round-4 fixes below are an
uncommitted working tree on top of `9e9c0706`, so that pass's revision is likewise the working tree
itself; they were then committed as `639c97f6`, which is the **merge candidate** the final evidence
below covers.

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
- Review round 4 re-read `blazor-architecture` and `testing` (the round-4 edits touch the page's two
  re-read paths and the component tests) and `csharp-conventions` for the two edits themselves, plus
  the live PR #285 round-4 review body, so each finding's wording is reconciled with what the fix
  actually does.

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
  the commit control are withheld until the owner's retained command has   been read — on every entry to the board *and* on each re-read that can land one (**Add another** and
  **Retry storage**), so input typed during any such window cannot be silently replaced by a landed
  recovery payload (review round 4, finding 1). The withheld state is
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
| Composition check re-measured (consequence of the re-capture) | `node .impeccable/review/issue-264/comp-measure.mjs` against the re-captured settled board: A still leads on both measures — shell 0.0224 (B 0.0237, C 0.0261), field 0.0273 (B 0.0343, C 0.0336), two-board rows 33.8% (C 76.4%) — so the locked reference is unchanged; the approval-time figures against the earlier capture were 0.0223 / 0.0272 with the same ordering. The script's module import resolved one directory short and could not run as committed, so that path was corrected in this pass and the record's claim that it reproduces every figure is now true. **Round 4 narrowed that claim's scope:** A's and the reference's figures reproduce from the repository, while B's and C's need the locally retained rejected rasters (round 4, row 2). |

After those runs, the only further edits were documentation, the one-line import correction in
`comp-measure.mjs`, the re-captured rasters themselves, and a restore of one doc comment and blank
line in `Nova.Unit.Tests/Players/PlayerComponentsTests.cs`; the application and browser-suite inputs
are unchanged from the revision the browser runs covered, so that selection and that capture pass are
reused rather than repeated (a documentation-only difference, as `AGENTS.md` allows with the
comparison recorded).

## PR review round 4 (PR #285)

The fourth review of PR #285 raised three findings — two Low (both inline) and one Nit in the review
body — and **verified all six round-3 findings as fixed**, reproducing the build (**0 warnings, 0
errors**), the full unit suite (**3813 total, 3813 passed, 0 failed, 0 skipped** on `9e9c0706`) and
`node .impeccable/review/issue-264/comp-measure.mjs` itself. It found **nothing at Critical, High or
Medium**; its three findings are dispositioned here on top of `9e9c0706`, nothing was deferred, and
nothing was resolved by weakening a test. The review's *Not reached* section — the Aspire/PostgreSQL
integration suite and the Playwright suites were not executed by the reviewer, and the rasters could
not be inspected perceptually — matches the limitations this record already carries, so it is not
re-argued.

| # | Severity | Finding | Disposition |
| --- | --- | --- | --- |
| 1 | Low | Two re-read paths replaced typed input without re-arming the withhold gate: `StartAnotherAdditionAsync` (**Add another**) and `RetryStorageAsync` (**Retry storage**) called `RestoreRecoveryAsync` with `_recoveryChecked` still `true`, so the fields stayed **enabled** while a read that can land a retained command was in flight and `_createForm = PlayerFormState.FromPendingCommand(pending.Payload)` could replace what the member typed meanwhile | **Fixed** in `Nova.UI/Features/Players/Pages/Players.razor.Intake.cs`: every read that can land a retained command now withholds input for its duration, not only the route boundary. `RetryStorageAsync` sets `_recoveryChecked = false` before its read; `StartAnotherAdditionAsync` sets `_recoveryChecked = false; _recoveryScope = null;` before its read. Releasing the scope is what keeps that safe: `RestoreRecoveryAsync` sets `_recoveryChecked = true` on every settled path (including `read is null`) and releases `_recoveryScope` on its version-mismatch exit, which makes `Players.OnAfterRenderAsync` re-claim and re-read — so no board can be left stuck shut. **Deviation from the prescribed diff, recorded:** `RequestFocusOnFirstField()` moves from before the read to after it. Left in place, its request is consumed by the render of the *withheld* board, where the browser refuses focus inside a disabled field set, so **Add another** would leave focus nowhere; requesting it once the read has settled lands focus in a field the member can actually use, and is the same no-op as before when the read freezes the board on a retained command. Two new cases, one per path — named in the evidence table. **Which of the finding's two acceptable forms was implemented:** the *typed-value-survives* form, because neither case seeds a retained command, so the settled read legitimately has nothing to replace; the assertion that actually pins the reported defect is the withheld window (the field is not editable while the read is open, so nothing a member could have typed can be replaced), and the negative check below shows both cases failing on exactly that assertion when the fix is reverted. |
| 2 | Low | `comp-measure.mjs` could not reproduce its own figures from a clean checkout: only `issue-264-a.png` is tracked, B and C are gitignored on purpose, and `loadRaster`'s unguarded `fs.readFileSync` made the script die with `ENOENT` on `b` | **Fixed with both halves of the finding's either/or.** `comp-measure.mjs` imports `node:fs` and skips an absent candidate with a printed `skipped — candidate raster is not committed` line, so the run completes from a clean checkout; and the claim beside it is narrowed in `.impeccable/surfaces/player-intake.md`, this record's limitations bullet and its design-evidence section to state that the reference's and A's figures reproduce from the repository while B's and C's need the locally retained rejected rasters. Both runs are in the evidence table: with B and C present the published figures reproduce exactly, and with the two local rasters temporarily renamed away the script prints the two skip lines, exits 0 and does not throw (the rasters were restored byte-identically, SHA-256 compared). |
| 3 | Nit | The PR body again named a stale revision, unit count and open-item status — the same class round 3 recorded as its row 5 — so the review prescribed a structural fix rather than another patch | **Addressed in the body, not the diff; the disposition belongs to the session: body restructured to state the method and defer revision and counts to this record.** Verified against the live body: the Validation section now states the method ("on the current branch head, `dotnet build Nova.slnx` is clean, `dotnet format Nova.slnx --verify-no-changes` exits 0, the full unit suite passes …"), names **no** revision and **no** case count (neither `aa22450d` nor `9e9c0706` nor either round's count appears in the body), and points at this record as the owner of both; open item 1 no longer claims round-3 work is outstanding; and the merge-condition line reads "Before merge: full browser-suite evidence covers the final inputs, or documented browser N/A applies. — left unchecked: the final inputs have not had one clean full pass (see open item 1)". **Still for the session:** the second *unchecked* line (guidance, sibling paths and reviews) gives its reason as "left unchecked until the current round's findings are pushed" rather than naming the remaining full integration/browser pass the review asked it to name; the record cannot edit the body, so this is the outstanding half of the finding. Recorded here because the review verified it in the body rather than on a changed line. |

### Confirming evidence (round 4)

Tested revision: the uncommitted working tree on branch `eruvalca-player-form-crud` on top of
`9e9c0706`, with no commit created in this pass.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx --no-restore` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3815 total, 3815 passed, 0 failed, 0 skipped**. The round-3 baseline was 3813, so the two new cases are the entire delta and nothing regressed. |
| Affected browser selection | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — second run, with the full output captured to a file, **21 total, 20 passed, 0 failed, 1 skipped**; the skip is the pre-existing env-gated `NOVA_A11Y_SCREENSHOTS` capture. The first run of the same selection on the same build reported **21 total, 19 passed, 1 failed, 1 skipped** — see the unresolved failure note below. |
| Affected journeys in isolation | `--filter-method '*DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync*' --filter-method '*OrdinaryMemberCreatesEditsArchivesAndRestoresThroughRoutedFormAsync*'` — **2 total, 2 passed, 0 failed** (the two long journeys this record already tracks as the intermittent class), and `--filter-method '*PlayerFormDuplicateCanBeCorrectedWithoutOverrideAsync*'` — **1 total, 1 passed, 0 failed**, the only browser journey that clicks **Add another** and therefore the only scenario this round's change can reach. All three on the same build as the selection runs. |
| Format | `dotnet format Nova.slnx --verify-no-changes --no-restore` — **exit 0** on the final edits; no `dotnet format Nova.slnx --no-restore` pass was needed. |
| New cases, named | `--filter-method '*PlayersWithholdsTheBoardWhileAddAnotherReReadsTheRetainedCommandAsync*' --filter-method '*PlayersWithholdsTheBoardWhileRetryStorageReReadsTheRetainedCommandAsync*'` — **2 total, 2 passed, 0 failed**. Each holds the interop double's `ReadGate` open, triggers its own path (**Add another** through `#intake-add-another`; **Retry storage** through `#intake-storage-unavailable button`, reached by making the retention write fail), asserts the window is withheld — `fieldset` and `#intake-submit` disabled, `#intake-checking-note` present — types into `#player-first-name`, releases the gate and asserts the settled read left the typed value (`Typed`) in place. The retry case also asserts the storage panel is gone, so a successful retry restores the board rather than stranding it. |
| Negative check, finding 1 | With both gate-arming hunks temporarily restored to the reviewed body (`RetryStorageAsync` calling `RestoreRecoveryAsync` directly, `StartAnotherAdditionAsync` requesting focus before an unguarded read), the same two cases reported **2 failed, 0 passed**, both on `cut.Find("fieldset").HasAttribute("disabled")` **should be True but was False** — the field was editable while the read was open, which is the defect. Fix restored, the full solution rebuilt (0 warnings, 0 errors) and `git diff` confirming the restored file matches the fixed body, before the runs above. |
| Composition script (finding 2), with B and C present | `node .impeccable/review/issue-264/comp-measure.mjs` from the repository root — `reference 1440 x 1000`; `a shellDiff 0.0224 fieldDiff 0.0273 inkRowPct 66.0 twoBoardRowPct 33.8`; `b shellDiff 0.0237 fieldDiff 0.0343 inkRowPct 75.4 twoBoardRowPct 32.8`; `c shellDiff 0.0261 fieldDiff 0.0336 inkRowPct 79.9 twoBoardRowPct 76.4` — the published figures, unchanged. |
| Composition script (finding 2), clean-checkout simulation | The same command with `.impeccable/mocks/issue-264-b.png` and `issue-264-c.png` temporarily renamed away — the reference and A's lines print, then `b skipped — candidate raster is not committed` and `c skipped — candidate raster is not committed`, **exit 0**, no exception. Both rasters were renamed back and their SHA-256 hashes compared before and after: identical. |

**Unresolved failure in the first selection run.** The first run of the selection reported one failure
among the 21, and its identity was **not captured** — the console output was read only as its tail
and the suite left no artifact (no `TestResults` directory, no trace file) — so it is recorded as
unresolved rather than attributed. What the same build does show: the second, fully captured
selection run was green at **21 total, 20 passed, 0 failed, 1 skipped**, the two long journeys this
record already tracks as the intermittent class passed in isolation (**2 total, 2 passed**), and the
one journey this round's change can reach — `PlayerFormDuplicateCanBeCorrectedWithoutOverrideAsync`,
the sole browser click on **Add another** — passed in isolation too (**1 total, 1 passed**). A green
re-run does not resolve an unexplained failure, so this stays a limitation: a repeat needs its
identity captured (a file-captured console or a TRX report) rather than another re-run.

## Final validation on the merge candidate (`639c97f6`)

**Round-5 review: "No issues found."** Round 5 verified both round-4 fixes — including a complete
sweep of every `RestoreRecoveryAsync` call site and of every assignment of `_createForm`, confirming
no read path is ungated, and confirming that the load-bearing assertion in the two new cases is the
withheld window rather than the typed-value line, which this record already states — and found
nothing at Low or above across the whole diff. It independently reproduced the build (0 warnings,
0 errors), the full unit suite (**3815/3815**) and the composition figures. The review is submitted
on the PR as a Comment verdict with no inline findings.

**Final gates, all on `639c97f6`:**

| Check | Command | Result |
| --- | --- | --- |
| Build | `dotnet build Nova.slnx --no-restore` | 0 warnings, 0 errors |
| Format | `dotnet format Nova.slnx --verify-no-changes --no-restore` | exit 0 |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | **3815 total, 3815 passed, 0 failed** |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | **677 total, 677 passed, 0 failed** |
| Full browser | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | **228 total, 218 passed, 0 failed, 10 skipped** |

The ten browser skips are the pre-existing env-gated capture tests (`NOVA_A11Y_SCREENSHOTS`,
`NOVA_PLACE_EVIDENCE`, `NOVA_PLAYERS_EVIDENCE`), including the two player-surface captures added
here; no behavioural scenario is skipped. The two tracked intermittent journeys did **not** fire in
this pass, and the full browser suite produced 0 failures on the merge candidate. These runs are the
first full integration and full browser evidence taken after the round-1 server change (`Nova/Program.cs`),
so both merge-stage gates now cover the final inputs.

This section was added after the browser pass and changes no application or browser-suite input, so
that pass still covers the tested revision.

## GitHub Copilot code review (PR #285, on `e84cd1ef`)

Once the PR was un-drafted, GitHub Copilot code review posted **six inline findings** against this
change's own new code on `e84cd1ef`: four on the asynchronous ownership of the browser boundary, one
repository-convention violation, and one missing HTTP-boundary assertion. All six are dispositioned
below on top of `e84cd1ef`; nothing was deferred and nothing was resolved by weakening a test. Each
code fix carries a case that fails when it is reverted (see the negative check), and the four
boundary findings share one invariant — a boundary await must not mutate or dispatch for an identity
and a recovery record that no longer hold.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | The departure guard was attached inside the `firstRender` branch only, so a transient import or attach failure left typed input without a same-origin departure prompt for the rest of the mount | **Fixed.** `PlayerIntakeBoard.OnAfterRenderAsync` now attempts the attachment on every render while `_guardAttached` is false, gated by `_guardAttachInFlight` so concurrent renders cannot stack attempts. `TryAttachDepartureGuardAsync` issues a fresh lease per attempt, re-pushes the dirty flag on success, and relies on the module's own `attachDepartureGuard`, which aborts any active guard first — so a retry supersedes the previous listeners instead of duplicating them. A failed attempt schedules no render of its own, so retries follow real interaction rather than a hot loop. New case `PlayersRetriesTheDepartureGuardAfterATransientAttachFailureAsync`. |
| 2 | The replay path skipped `RetainAsync`, so it could dispatch a retained command whose record another tab had released, leaving a lost reply unrecoverable | **Fixed.** `CreatePlayerAsync` now retains the exact command on every dispatch, fresh or replay. The storage module already accepts a same-operation rewrite and still refuses a *different* operation, so the replay re-establishes the record before the request leaves without ever changing its operation identity. New cases `PlayersRetainsTheReplayedCommandAgainBeforeDispatchingItAsync` (the second write is the evidence: `WriteCount` 1 → 2) and `PlayersDispatchesNoReplayWhenTheRetainedRequestCannotBeWrittenAgainAsync` (nothing is dispatched, and the storage panel and its retry appear instead). |
| 3 | The dispatch path did not re-check the captured `_identityVersion` after the retention await, so a page re-scoped while storage was written could dispatch a club-42 command with the club-43 identity source and mutate the new page state | **Fixed.** The version/cancellation check now runs immediately after `RetainAsync` and before either `_pendingCreate = command` or `CreateAsync`, matching the check the HTTP await already carried. New case `PlayersDispatchesNoCreationWhoseIdentityChangedWhileTheRetainedWriteRanAsync` holds the write open through a new `WriteGate` on the interop double, changes the principal to club 43, releases the gate and asserts no dispatch and no receipt. |
| 4 | The set-aside ignored both the removal result and ownership changes, then cleared the in-memory retained command, so a refused release left bytes the page had forgotten | **Fixed.** `SetAsideRetainedAsync` now treats the removal as the decision: it captures `_identityVersion`, re-checks it after the await, and keeps the retained state blocked — with the storage panel, its retry and an explicit message — when `ClearAsync`/`DiscardInvalidAsync` report that the bytes were not released. Sibling paths fixed for the same invariant: `ReleaseRetainedAsync` attempts the release before clearing state and reports a refusal, and `SettleCommittedAsync` reports an unreleased record instead of claiming it. New case `PlayersKeepsTheRetainedAdditionWhenItsSetAsideCannotBeReleasedAsync`, which also proves the decision completes after **Retry storage**. |
| 5 | `AddPlayerIntakeInterop` was a classic `this`-parameter extension method, which the repository's current C# convention forbids for new extension members | **Fixed.** It is now a C# 14 `extension(IServiceCollection services)` block, matching `.github/instructions/csharp-conventions.instructions.md` and its canonical examples. The `CA1034` suppression the rule requires of a **public** extension class follows `Nova.SharedKernel/Results/HttpResponseMessageExtensions.cs`; both call sites (`Nova/Program.cs`, `Nova.Client/Program.cs`) are in other assemblies, so the class stays public. |
| 6 | The new endpoint declared `ProducesValidationProblem()` while the HTTP suite covered only 200, 401 and 403, so route binding and endpoint-layer validation were unproven | **Fixed.** `IntakeContextRejectsAnInvalidClubRouteValueAsync` requests `/api/clubs/0/players/intake-context` as an authenticated club member and asserts the 400, the `application/problem+json` media type, `status`, a non-empty `ClubId` error array and a non-empty `traceId`. The member passes `RequireClubMember` (which requires only the club claim), so the route value itself is what the request exercises; validation runs first inside the service, before the club-ownership check. |

### Confirming evidence (Copilot round)

Tested revision: the uncommitted working tree on branch `eruvalca-player-form-crud` on top of
`e84cd1ef`.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. One earlier build failed with a single `CA1034` on the new public extension block; that suppression is the correction, and the rebuild is the clean one recorded here. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3820 total, 3820 passed, 0 failed, 0 skipped**. The round-4 baseline was 3815, so the five new cases are the entire delta. |
| Intake-context HTTP class | `--filter-method '*IntakeContext*'` — **4 total, 4 passed, 0 failed, 0 skipped**, the new 400 case included. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. The previous full pass was 677, so the new route-value case is the delta, and both merge-stage integration gates now cover the final inputs. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests*' --filter-class '*PlayersDirectoryBrowserTests*'` — **21 total, 20 passed, 0 failed, 1 skipped**; the skip is the pre-existing env-gated `NOVA_A11Y_SCREENSHOTS` capture. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Negative check, all four boundary fixes | With the four fixes reverted together (attachment gated on `firstRender` again, replay skipping retention, no post-retention identity check, unconditional set-aside release) and the solution rebuilt, the five new cases reported **5 failed, 0 passed** — on `Interop.GuardAttached` false, `Interop.WriteCount` 1, `commands` not empty and `#intake-storage-unavailable` absent respectively. The fixes were then restored from a byte-identical snapshot (SHA-256 compared before and after) and rebuilt; the runs above are on the restored build. |
| Full browser suite | Three runs of `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` on this revision. **See the note below** — no run repeated the same victim, and every failing journey passed alone on the same build. |

**A stale-build trap this round turned up, recorded because it first looked like five real failures.**
The first full-unit run after restoring the reverted files reported **5 failed**; the assembly under
test was the *reverted* one. `Copy-Item` restores a file with its original, older `LastWriteTime`, so
MSBuild judged the restored sources up to date and skipped recompiling. The same trap had already
turned one negative-check attempt into a false pass (the reverted condition did not compile, the build
failed, and the test run silently reused the previous assembly). **Both times the fix was to touch the
restored sources before rebuilding and to require a successful build before reading any test result.**
Recorded with the round because an unrecompiled assembly is indistinguishable from a real regression
in the test output.

**Full browser suite on this revision.** Run 3 — the run that covers the final inputs — reported
**228 total, 218 passed, 0 failed, 10 skipped**, which is the clean pass the before-merge row
requires. Runs 1 and 2 reported `228 total, 217 passed, 1 failed, 10 skipped` with a **different**
victim each time: `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync` — one of
the two load-sensitive journeys this record already tracks — and
`CampaignWorkspaceBrowserTests.InheritedPlacementContextStaysBesideDiscoveryAndBecomesMobileDialogAsync`,
a campaign-workspace focus assertion (`#participant-drawer-close` "inactive" within 5s) on a surface
this change does not touch. Both passed alone on the same build (**1 total, 1 passed** each). Neither
victim repeated, and two unrelated surfaces failing at one test apiece is the shape of the
load-sensitivity class this record already documents rather than a diff-caused regression. The ten
skips remain the pre-existing env-gated captures, so no behavioural scenario is skipped. These
paragraphs were written after run 3 and change no application or browser-suite input, so that pass
still covers the tested revision.

## GitHub Copilot code review, second pass (PR #285, on `bca7f514`)

Copilot reviewed the fixed head and posted **one inline finding** plus **three suppressed findings**
in its review body ("previously missed — in code that hasn't changed since the last review"). All
four are on this change's own code and all four are dispositioned below on top of `bca7f514`. Three
of them are the same invariant the previous round established — a boundary await must not mutate or
dispatch for an identity or a recovery record that no longer holds — and the fourth is a per-field
feedback gap the board's own contract requires.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 (inline) | The release path awaited storage cleanup and then unconditionally cleared `_pendingCreate`/`_recoveryState`: a clear that returned false (or that finished after an identity change) erased the current page's preserved recovery state while the retained bytes still existed | **Fixed.** `ReleaseRetainedAsync` now captures `_identityVersion`, re-checks it after the clear, returns `false` when the page was re-scoped, and otherwise keeps the operation **blocked** — setting the storage status and leaving the retained state in place — when the browser did not release the bytes. `SettleProblemAsync` carries that ownership: a stale continuation stops before it writes the refusal or duplicate state for the new owner. New cases `PlayersKeepsARefusedOperationBlockedWhenItsBytesCannotBeReleasedAsync` (the refusal stands, the retained state stays, **Retry storage** then completes the release) and `PlayersIgnoresAReleaseThatFinishedAfterTheIdentityRefreshedAsync` (a role-only refresh preserves the work; the late release neither erases the preserved state nor reports another identity's refusal or storage trouble, proved with `FailClears` plus a `ClearGate`). |
| 2 (suppressed) | The Gender control rendered only `ValidationMessage`, so a server error keyed to `Gender` — `[EnumDataType(typeof(Gender))]` on `PlayerProfileInput.Gender` can produce one — was silently omitted instead of appearing beside its owning control | **Fixed, with a recorded framework limit.** The server message loop now renders beside the select, exactly as the profiled inputs do. **Deviation:** the neighbouring inputs carry the invalid state as `aria-invalid`, and `InputSelect` cannot: it drops unmatched attributes, so even a literal `aria-invalid="true"` never reaches the DOM (the rendered `<select>` carries `id,name,class,blazor:onchange,blazor:elementreference` — captured during the fix), and Razor rejects the composed `class` on a component (`RZ9986`, complex content). The invalid state is therefore carried by the computed `GenderSelectClass` (`form-select is-invalid`), and the field's messages are additionally announced through the unresolved panel's `role="status"` list. New case `PlayersRendersAGenderFieldErrorBesideItsControlAsync`. |
| 3 (suppressed) | `SettleCommittedAsync` performed further state changes and refreshes after the `ClearAsync` await without re-checking the identity that `CreatePlayerAsync` had captured | **Fixed.** Settlement now captures `_identityVersion` on entry and stops after the release await when that ownership has changed, so a late continuation cannot close the guard, refresh the new owner's data or move focus into a form that no longer holds the receipt. New case `PlayersDoesNotCloseTheGuardWhenTheCommitSettlesAfterAnIdentityRefreshAsync`. |
| 4 (suppressed) | `LoadIntakeContextAsync` was guarded only by `_intakeContextVersion`, which a route read bumps but an identity change does not, so a stale read could assign the previous club's campaign | **Fixed defensively, and honestly: no discriminating test exists.** The read now captures `_identityVersion` and rejects a result whose ownership changed. The guard is nevertheless unobservable today — an identity change routes through `ReconcileLocationAsync` → `ApplyRouteState`, which arms the withhold gate *and* starts a fresh read that bumps `_intakeContextVersion`, so the pre-existing version guard already rejects the stale read in every flow where the board can state a consequence. The test written for it **passed with the guard removed**, so it was **deleted rather than kept as false evidence**; the guard stays because it makes the invariant explicit and does not depend on a later read happening to bump the version. |

### Confirming evidence (Copilot second pass)

Tested revision: the uncommitted working tree on branch `eruvalca-player-form-crud` on top of
`bca7f514`.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. Two earlier builds of this pass failed and both corrections are recorded above: `RZ9986` on the composed component `class`, and a nullable-annotation error in the new test. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3824 total, 3824 passed, 0 failed, 0 skipped**. The previous full pass was 3820, so the four new cases are the entire delta. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests*' --filter-class '*PlayersDirectoryBrowserTests*'` — **21 total, 20 passed, 0 failed, 1 skipped** (the pre-existing env-gated `NOVA_A11Y_SCREENSHOTS` capture). |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Negative check, findings 1-3 | With the three fixes reverted together (unconditional release clear, no settlement ownership, no Gender message loop or invalid class), the four new cases reported **4 failed, 0 passed** — the Gender case on the missing message and `is-invalid` class, the refusal case on the erased retained state, the release case on a storage claim for the new identity, and the settlement case on the same claim after a stale commit. The fifth (intake-context) case **passed with its guard removed**, which is why it was deleted; the fixes were then restored from a byte-identical snapshot (SHA-256 compared) with their timestamps touched so MSBuild could not skip the recompile — the stale-artifact trap the previous round recorded. |
| Full browser suite | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` — **228 total, 216 passed, 2 failed, 10 skipped**. Both failures are the two long directory journeys this record already tracks as load-sensitive: `OrdinaryMemberCreatesEditsArchivesAndRestoresThroughRoutedFormAsync` (32s) and `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync` (34s). Both passed together in isolation on the same build (**2 total, 2 passed**), and both diagnostic dumps show the previous route's content still on screen at the new URL (`forms: 0`, the directory's own links) after a navigation that had reported `completed` — a render lag under a loaded four-thread suite, not a board semantics failure. **The before-merge row is therefore still outstanding for these inputs:** the previous clean full pass covered `bca7f514`, and this revision has not yet produced one. |

## GitHub Copilot code review, third pass (PR #285, on `d594253d`)

Copilot reviewed `d594253d` and raised **one inline finding** plus **two suppressed findings**. All
three are dispositioned below. The inline one is a subject-integrity defect in the shared archive
confirmation's detail host; the two suppressed ones are consequences of the previous round's own
fixes, traced by the reviewer to the paths where they were still reachable.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 (inline) | `PlayerDetail` passed its live `PlayerId`, display name and blockers into the archive confirmation, so a routed detail reused for another player could show the reviewed copy while `ConfirmArchiveAsync` archived the new route target | **Fixed.** `BeginArchive` snapshots the reviewed subject (`_archiveSubjectId`, `_archiveSubjectName`), the panel renders from that snapshot, and the confirm archives the snapshot's id — so the mutation can only target the player the member reviewed. The directory already held its candidate in a field, so both hosts now behave alike; the blockers were never a live projection (they are the panel's own state, replaced only by a failed attempt's response). New case `PlayerDetailArchivesTheReviewedSubjectWhenTheRouteChangesWhileThePanelIsOpenAsync` asserts the confirm archives the reviewed id and never the new route's. |
| 2 (suppressed) | A successful creation set `_storageUnavailable` when the clear failed but left `Receipt` non-null, and the panel's `&& !ShowsReceipt` suppressant hid it, so the unreleased record was neither reported nor cleanable and returned as an unresolved addition on the next mount | **Fixed.** The panel now renders in the receipt state too, with copy for that state and an action labelled **Release retained request**; the page remembers the settled operation id (`_unreleasedOperationId`) so the action clears the exact record instead of re-reading a decision already made, and the field is reset with the rest of the identity state. New case `PlayersKeepsTheStorageRetryVisibleBesideASettledReceiptAsync` asserts the panel and its copy beside the receipt, then that the action removes the record from storage. |
| 3 (suppressed) | The ownership check was bypassed when retention failed: `CreatePlayerAsync` exited at the `!await RetainAsync(...)` branch before its `_identityVersion` check, and `RetainAsync` published `_storageUnavailable`/`_mutationError` into the new identity's state | **Fixed.** `RetainAsync` now takes the owning version and publishes nothing — neither the failure nor the success — when the identity changed while the write was open, and the caller skips its own state change for the same reason. New case `PlayersDoesNotPublishAStaleRetentionFailureIntoTheNewIdentityAsync` fails the write after a refresh and asserts no storage panel, no error and no dispatch on the page now on screen. |

### Confirming evidence (Copilot third pass)

Tested revision: the uncommitted working tree on branch `eruvalca-player-form-crud` on top of
`d594253d`.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. An earlier build of this pass failed on `SetParametersAndRender`, which this bUnit version does not provide; the repository's `cut.Render(...)` form is the correction. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3827 total, 3827 passed, 0 failed, 0 skipped**. The previous full pass was 3824, so the three new cases are the entire delta. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests*' --filter-class '*PlayersDirectoryBrowserTests*' --filter-class '*PlayerDetailBrowserTests*'` — **21 total, 19 passed, 1 failed, 1 skipped** in the first run, then **7 total, 6 passed, 1 failed** for the directory class alone. The single failure is `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync`, one of the two load-sensitive journeys this record already tracks, and it **passed alone on the same build** (**1 total, 1 passed**). Its captured failure is a `ToHaveAttributeAsync` window expiring on an intermediate render (the back link still showing the outer return destination for the 5s the assertion polls) rather than a missing control, which is the shape of the tracked class. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Negative check, findings 1-3 | With the three fixes reverted (the live subject passed to the panel and its confirm, `&& !ShowsReceipt` restored on the storage panel, and `RetainAsync` publishing without ownership) and the solution rebuilt, the three new cases reported **3 failed, 0 passed** — on the archived id, on the hidden panel, and on the published stale failure. Restored from a byte-identical snapshot with timestamps touched before the rebuild. |
| Full browser suite | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` — **228 total, 218 passed, 0 failed, 10 skipped**: the clean full pass the before-merge row requires, on this revision. The ten skips are the pre-existing env-gated captures, so no behavioural scenario is skipped. |

## GitHub Copilot code review, fourth pass (PR #285, on `6fece48b`)

Copilot reviewed `6fece48b` and raised **one inline finding** plus **one suppressed finding**. Both are
consequences of the previous two rounds' own fixes, and both are dispositioned below.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 (inline) | The release retry awaited a browser clear and then unconditionally cleared `_unreleasedOperationId`/`_storageUnavailable`, so a continuation that finished after a re-scope — or after another settled operation — could erase the state the newer page or operation owns | **Fixed.** The retry captures `_identityVersion` before the clear and re-checks it after, returning without touching state when the page was re-scoped; it also only reports the release for the exact operation it cleared, so a newer settled operation keeps its own retry state. New case `PlayersDoesNotLetAStaleReleaseClearTheRefreshedStorageReportAsync` holds the release open, refreshes the page, breaks storage for the identity now on screen (which reports that truthfully), releases the gate and asserts the newer report survives. Both halves of the guard are needed for it to hold; reverting the guard makes it fail. |
| 2 (suppressed) | The membership-based authority on Player detail was computed only in `OnInitializedAsync`, so a claim change while the `InteractiveAuto` page stayed mounted left `_detail` and the Archive/Restore controls showing the previous scope, with an open confirmation still actionable | **Fixed.** The page now mirrors the directory and `TeamDetail`: it subscribes to `AuthenticationStateChanged` (unsubscribing in `DisposeAsyncCore`), recomputes the club scope and management permission from the new principal, closes the archive confirmation and clears its reviewed subject, and — when the claimed club changes — drops the stale detail and reloads so the server re-authorizes the read. New cases `PlayerDetailRebindsClubScopeWhenTheClaimedClubChangesAsync` (a club change closes the panel and re-reads the detail) and `PlayerDetailClosesTheReviewedPanelWhenMembershipIsRevokedAsync` (a revoked membership removes the control and closes the panel). |

### Confirming evidence (Copilot fourth pass)

Tested revision: the uncommitted working tree on branch `eruvalca-player-form-crud` on top of
`6fece48b`.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. An earlier build of this pass failed on three test-side errors (`Change` missing on the detail page's fake authentication provider, and a missing `System.Globalization` import); both corrections are in the tests only. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3830 total, 3830 passed, 0 failed, 0 skipped**. The previous full pass was 3827, so the three new cases are the entire delta. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests*' --filter-class '*PlayersDirectoryBrowserTests*' --filter-class '*PlayerDetailBrowserTests*'` — **21 total, 20 passed, 0 failed, 1 skipped**; the skip is the pre-existing env-gated `NOVA_A11Y_SCREENSHOTS` capture. No load-sensitive journey failed in this pass. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Negative check, findings 1-2 | With both fixes reverted (the retry clearing without ownership, and the page computing its authority only in `OnInitializedAsync` with no subscription or disposal), the three new cases reported **3 failed, 0 passed** — on the erased storage report and on both detail-page authority paths. Restored from a byte-identical snapshot with timestamps touched before the rebuild. |
| Full browser suite | Two runs on this revision. The first reported **228 total, 217 passed, 1 failed, 10 skipped** — `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync` again, with the signature captured last round (the record page's back link still resolving to the innermost return destination when the 5s `ToHaveAttributeAsync` window closed), and it **passed alone on the same build** (**1 total, 1 passed**). The second run was clean: **228 total, 218 passed, 0 failed, 10 skipped**, which satisfies the before-merge row for this revision; the ten skips are the pre-existing env-gated captures. This section was written after that pass and changes no application or browser-suite input, so the pass still covers the tested revision. |

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

- **The PR-stage gates are complete on the merge candidate, except the merge itself.** The work is
  committed as `b51558b2`, opened as PR #285, committed for review rounds 1-4 as `cb88a1b9`,
  `aa22450d`, `9e9c0706` and `639c97f6`, and reviewed in five fresh-context rounds. The separate
  local reviews this change's persisted/recoverable and asynchronous-state work requires have been
  obtained for all five rounds, each recorded above with a disposition for every finding, and round 5
  returned no findings. The full integration and full browser suites have both been run on the merge
  candidate and pass (see "Final validation on the merge candidate"); CI passes on every pushed
  revision. What remains is the merge itself, plus re-establishing these results after any further
  edit.
- **The board's input is now gated on the interactive circuit, by design.** A creation form is
  unusable until the circuit has attached *and* the owner's retained command has been read from
  browser storage, where the pre-change form was submittable from its server-rendered markup. That is
  the point of the round-2 fix — input can no longer be replaced by a landed recovery payload — but
  it moves a slice of the board's readiness behind interactivity, which is why the affected browser
  selection is more load-sensitive than before (see the *intermittent journeys* table). Review round
  3 removed the unexplained half of that trade-off: the withheld board now names its check, so no
  state the member meets is unlabelled. Review round 4 extended the same withholding to the two
  re-read paths (**Add another** and **Retry storage**), so each of those interactions is also only
  actionable once its own storage read has settled — one more window on the same interactivity
  dependency, and the reason the withholding guarantee now holds for every read that can land a
  retained command rather than for entries alone. The named keyboard journey waits for the field to be enabled
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
  reproduces the reference's and A's figures from the repository alone, and B's and C's when the
  locally retained rejected rasters (`issue-264-b.png`, `issue-264-c.png`) are present; a candidate
  whose raster is not committed is reported as skipped instead of stopping the run. Its module import
  resolved one directory short and was corrected in round 3, since it could not run as committed, and
  review round 4 made the absent-candidate case explicit so the published claim holds from a clean
  checkout. **The formal comp-spec/comp-diff gate was not run**,
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
which reproduces the composition figures from the repository for the reference and A and needs the
locally retained B and C rasters for those two candidates. `.gitignore` carries narrow exceptions for exactly these
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
