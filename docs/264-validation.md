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

## GitHub Copilot code review, fifth pass (PR #285, on `f7a3c593`)

Copilot reviewed `f7a3c593` and raised **one inline finding** plus **three suppressed findings**. Two
are dispositioned with code and a test, one with the contract narrowing the finding itself offered,
and one with a guard whose race the unit harness cannot drive — kept for the invariant and disclosed
rather than pinned by a case that does not discriminate.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 (inline) | Player detail's club rebind cleared the detail and started a replacement load without invalidating the previous `LoadDetailAsync` or lifecycle mutation, so a late response from the old club could repopulate `_detail` or publish an archive/restore result | **Fixed.** A club-scope generation (`_clubScopeVersion`) is incremented when the claimed club changes, and `LoadDetailAsync`, `ConfirmArchiveAsync` and `RestorePlayerAsync` re-check it before applying any state; the rebind also clears the in-flight mutation flag so the new scope's controls are not left disabled. New case `PlayerDetailIgnoresADetailReadThatFinishedAfterTheClaimedClubChangedAsync` holds the first club's read open, rebinds to the next club, answers the stale read last and asserts the new scope's player survives. |
| 2 (suppressed) | A same-owner authentication refresh preserved only the pending/recovery fields while `ResetIdentityState` cleared `_receipt`, `_unreleasedOperationId` and `_storageUnavailable`, so a role refresh after a successful create replaced the receipt (and its release retry) with a blank form, losing **Add another**/**View player** and re-presenting the record as unresolved | **Fixed.** The preserved intake state now carries the settled receipt and its release state for the same owner (`CaptureIntakeState`/`RestoreIntakeState`), and `ApplyRouteState` resets only at a real boundary — a path change or an owner change — because the identity reset also clears the route flags that keep the board on the form. New case `PlayersPreservesTheSettledReceiptAndRetryAcrossARoleRefreshAsync`. |
| 3 (suppressed) | Starting another addition released the recovery scope claim before its read awaited, so the render that precedes the await could start a second read whose landed command overwrote input typed meanwhile | **Fixed, with no discriminating test.** The re-read claims `CurrentScope` before awaiting, exactly as the initial recovery path does, and `RestoreRecoveryAsync` still releases the claim on a version mismatch, so no board can be left stuck shut. The case written for it **passed with the guard removed** — the harness cannot produce the render-between-await the race needs — so it was deleted rather than kept as false evidence, as the intake-context guard in round 3 was. |
| 4 (suppressed) | The departure guard intercepts same-origin link clicks and document unload but not browser Back/Forward (`popstate`/Navigation API), so history traversal can discard typed input without the confirmation | **Contract narrowed, which the finding's own alternative allows.** Protecting traversal needs the Navigation API dance `evaluationNavigationGuard.js` performs (restore the origin entry, marshal the prompt, replay the permit) — a feature of its own rather than a review-round fix. The module now names the gap beside its click listener, and the limitations below record it with the rationale; document unload and same-origin link departure remain the guard's stated contract. |

### Confirming evidence (Copilot fifth pass)

Tested revision: the uncommitted working tree on branch `eruvalca-player-form-crud` on top of
`f7a3c593`.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. Two earlier builds of this pass failed: `MA0051` (the identity handler exceeded 40 statements, corrected by extracting the intake-state record and its two helpers) and a leftover unused local during the negative check. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3832 total, 3832 passed, 0 failed, 0 skipped**. The previous full pass was 3830, so the two new cases are the entire delta. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests*' --filter-class '*PlayersDirectoryBrowserTests*' --filter-class '*PlayerDetailBrowserTests*'` — **21 total, 20 passed, 0 failed, 1 skipped** (the pre-existing env-gated capture). |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Negative check, findings 1-3 | With the three guards reverted together (no club-scope generation, the receipt/release state not preserved and `ApplyRouteState` resetting unconditionally, and the scope claim released before the re-read), the run reported **2 failed, 1 passed**. The two failures are findings 1 and 2; the third case is the one described above that cannot discriminate, and it was deleted. Restored from a byte-identical snapshot with timestamps touched before the rebuild. |
| Full browser suite | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` — **228 total, 218 passed, 0 failed, 10 skipped** on the first attempt: the clean pass the before-merge row requires, with the ten skips being the pre-existing env-gated captures. The paragraphs of this section were written after that pass and change no application or browser-suite input, so the pass still covers the tested revision. |

## GitHub Copilot code review, sixth pass (PR #285, on `51436be9`)

Copilot reviewed `51436be9` and raised **two inline findings**, both on the collocated storage module's
cross-tab behaviour, and both dispositioned below.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | `writePending`'s owner check and `localStorage.setItem` were a cross-tab check-then-set: two same-owner tabs could both observe an empty record, persist different operation ids and dispatch, and the last write would overwrite the first — so a lost acknowledgement for the first command had no recoverable record | **Fixed with the platform's own primitive.** `writePending` now runs its read/validate/write inside a Web Locks critical section keyed by the owner (`navigator.locks.request(ownerKey, …)`), so the reservation is atomic across tabs of the same origin. Where the API is absent the work runs unguarded, exactly as before, so the boundary stays usable and single-tab behaviour is unchanged. |
| 2 | `clearPending` had the same TOCTOU — read and validate one operation, then remove the key later — so a removal that raced another tab's new write could delete the newer command's record; `discardInvalidPending` shared it | **Fixed.** Both removals now perform their read, validate and `removeItem` inside the same owner-scoped lock, so a removal can only delete the operation it validated. |

The module's storage entry points therefore answer with a promise when the lock is taken. That is a
contract change only at the boundary the C# side already awaits (`IPlayerIntakeInterop` is
`Task`-returning and `PlayerCreationRecoveryStore` awaits every call), and the suite's own probe was
updated to await the reservation and removal it drives directly.

### Confirming evidence (Copilot sixth pass)

Tested revision: the uncommitted working tree on branch `eruvalca-player-form-crud` on top of
`51436be9`.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Module contract in a real browser | `--filter-method '*IntakeBoardModuleRetainsAndReadsOwnerScopedBytesAsync*'` — **1 total, 1 passed**, driving the module's own `writePending`/`readRecovery`/`clearPending`/`discardInvalidPending` contract directly. Its first run after the change failed on that probe reading a promise as a value (`[object Promise]`), which is the correction recorded above, not a module defect. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests*' --filter-class '*PlayersDirectoryBrowserTests*'` — **21 total, 20 passed, 0 failed, 1 skipped** on the re-run. The first run of the same selection reported three failures: the module probe above, plus the two long directory journeys this record tracks as load-sensitive; both of those passed in this re-run. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3832 total, 3832 passed** on `51436be9`'s inputs; this pass changes no unit-test input (the module is exercised by the browser suite), so that result still covers it. |
| Full integration | **678 total, 678 passed** on `51436be9`'s inputs, unchanged by this pass for the same reason. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Cross-tab atomicity coverage | **None automated, and stated as such.** The lock is verified by reading the module and by the single-tab contract the browser suite exercises; a genuine two-tab test would need a second browser context racing the same origin, which this suite does not do. The behaviour the lock changes (last-write-wins on a raced reservation, a stale removal deleting a newer record) is therefore reasoned, not pinned. |
| Full browser suite | Two runs on this revision. The first reported **228 total, 217 passed, 1 failed, 10 skipped** — `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync` again (7.7s), the tracked load-sensitive journey, which has passed in every isolated run this session. The second was clean: **228 total, 218 passed, 0 failed, 10 skipped**, which satisfies the before-merge row for the final inputs; the ten skips are the pre-existing env-gated captures. This row was written after that pass and changes no application or browser-suite input, so the pass still covers the tested revision. |

## GitHub Copilot code review, seventh and eighth passes (PR #285, on `f42e1f08`)

Two further Copilot reviews — on `1ba6713e` and `f42e1f08` — raised **ten suppressed findings**
between them, all on this change's own code, with no inline threads. Six are fixed here with tests
where the behaviour can be discriminated; two are deferred with their reason; one fix carries no
discriminating test; and one finding was resolved by fixing an ownership mistake the diagnosis
turned up.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | `SettleCommittedAsync` and `ReleaseRetainedAsync` skipped the release when `_board` was null, so a settlement that arrived after a route change left the exact operation in storage and a later visit showed it as unresolved | **Fixed.** The page now injects `IPlayerIntakeInterop` and releases through a `ClearRetainedAsync` helper that uses the board when it is rendered and the browser boundary when it is not. The behaviour cannot be driven in the harness (bUnit keeps the board reference across a route change, so the case written for it passed with the fix reverted and was deleted), so the record states the fix is reasoned rather than pinned. |
| 2 | The confirmed departure only navigated, so `_createForm` survived and returning to the board resurrected the values the confirmation said would be lost | **Fixed.** `LeaveBoard` resets the create state — re-deriving the frozen copy from a durable retained command, whose bytes the confirmation never claimed to discard — and clears the edit form. New case `PlayersDiscardsTypedValuesWhenTheDepartureIsConfirmedAsync`, which fails when reverted. |
| 3 | `ResetIdentityState` cleared the scope claim and the intake context but left `_recoveryChecked`/`_intakeContextLoading` untouched, so a same-owner refresh's replacement reads could land over typed input or state a stale consequence | **Fixed.** A same-owner refresh re-arms both gates in `ApplyRouteState`'s preserved-state branch before its reads run. New case `PlayersWithholdsTheBoardWhileASameOwnerRefreshReReadsAsync`, which fails when reverted. |
| 4 | Both `alertdialog` panels opened without moving focus, leaving keyboard and screen-reader members on the control whose navigation was cancelled | **Fixed.** Each panel's heading takes `tabindex="-1"` and the board queues a focus request for it when the panel opens. New case `PlayersMovesFocusIntoEachConfirmationPanelAsync`, which fails when reverted. |
| 5 | `PlayerCreationRecoveryStore.DisposeAsync` swallowed every `JSException`, hiding real module-disposal failures | **Fixed** to catch only `JSDisconnectedException` (plus the existing cancellation case), leaving unexpected failures visible. |
| 6 | The board's detach-on-dispose swallowed every `JSException`, so a real detach failure would leave listeners and the module's active guard installed with a stale receiver | **Fixed** the same way, with the consequence recorded in the comment. Findings 5 and 6 are reasoned fixes: no test can distinguish them without a JS double that throws on disposal. |
| 7 | Focus/dispose diagnosis turned up an ownership mistake: the identity reset cleared `_showCreateForm`/`_isEditRoute` through `ClearMutationForm`, and the refresh's reconcile short-circuits on an unchanged location key, so nothing re-derived them and a same-owner refresh rendered the directory instead of the form | **Fixed.** Route flags are owned solely by `ApplyRouteState`, which derives them on every application, including the preserved-state branch. This is what makes finding 3's arming correct; the full unit suite is the evidence. |
| 8 | The consequence block renders for the edit board too, where it is not a consequence of editing and can be false | **Deferred, with the reason.** The honest fix is a create-only parameter, and it landed cleanly in the code — but it changes the board's contract for six board-level tests and the edit host, which needs its own pass over those tests rather than a rushed update at the end of a long round. Recorded as an open item below. |
| 9 | After a failed storage read the board stays editable, so a later retry can land a retained command over values typed meanwhile | **Deferred, with the reason.** Withholding the board until storage answers is the right contract, but it supersedes the round-2 decision that deliberately reopened the board after a failed read, so it needs that test rewritten and re-verified rather than flipped at the end of a round. Recorded as an open item below. |
| 10 | The recovery re-read released its scope claim before awaiting, so the render preceding the await could start a second read | **Already fixed in the sixth pass as part of the cross-tab work's related review** — the re-read claims `CurrentScope` before awaiting; this review's line refers to the same statement set and needs no further change. |

### Confirming evidence (Copilot seventh and eighth passes)

Tested revision: the uncommitted working tree on branch `eruvalca-player-form-crud` on top of
`f42e1f08`.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. Two intermediate builds failed and both corrections are recorded above (`MA0051` on the recovery method, resolved by extracting `ApplyRecoveryRead`; a missing `Microsoft.JSInterop` import). |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3835 total, 3835 passed, 0 failed, 0 skipped** (3832 before, the three new cases being the delta). |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests*' --filter-class '*PlayersDirectoryBrowserTests*'` — **21 total, 18 passed, 2 failed, 1 skipped**. Both failures are the two long directory journeys this record tracks as load-sensitive, and both **passed in isolation on the same build** (**2 total, 2 passed**). |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Negative check, findings 2-4 | With those three fixes reverted (the minimal departure handler, no gate re-arming, and no focus requests), the run reported **3 failed, 0 passed** on the three new cases. The fourth case (finding 1) passed with its fix reverted, which is why it was deleted. |
| Full browser suite | Not run in this pass: the previous clean pass covers `1ba6713e`, this revision adds these fixes, so the before-merge row is outstanding for it and is listed as an open item. |

## GitHub Copilot code review, ninth pass (PR #285, on `23bde443`)

Copilot raised **one inline finding** on the previous round's own Player detail work, and it is fixed
here.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | The initial authentication read was applied without an ownership generation, so a notification arriving while `GetAuthenticationStateAsync()` was pending could be overwritten by the stale startup principal — leaving the page showing lifecycle controls, or reloading the wrong club, for a member whose authority had just changed | **Fixed.** The page now carries the monotonic `_authenticationVersion` the directory uses: the startup read captures it and applies the principal only when it is still current, and every notification bumps it and re-checks after its own await, so a stale state can never win. New case `PlayerDetailIgnoresAStartupAuthenticationReadThatResolvedAfterANotificationAsync` — a notification for a club member arrives while the startup read is pending, the read then resolves as a principal with no club membership, and the lifecycle controls must survive. It fails when the guard is reverted. |

### Confirming evidence (Copilot ninth pass)

Tested revision: the uncommitted working tree on branch `eruvalca-player-form-crud` on top of
`23bde443`.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3836 total, 3836 passed, 0 failed, 0 skipped** (3835 before; the new case is the delta). |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests*' --filter-class '*PlayersDirectoryBrowserTests*'` — **21 total, 20 passed, 0 failed, 1 skipped** (the pre-existing env-gated capture), with no load-sensitive journey failing in this pass. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Negative check | With the version guard reverted, `PlayerDetailIgnoresAStartupAuthenticationReadThatResolvedAfterANotificationAsync` reports **1 failed, 0 passed** on `cut.Markup` — the stale read drops the lifecycle controls, which is the defect. Restored from a byte-identical snapshot with its timestamp touched before the rebuild. |
| Full browser suite | Two runs on this revision. The first reported **228 total, 217 passed, 1 failed, 10 skipped** — `OrdinaryMemberCreatesEditsArchivesAndRestoresThroughRoutedFormAsync` (6.4s), one of the two long directory journeys this record tracks as load-sensitive, which had **passed in the affected selection on this same build**. The second was clean: **228 total, 218 passed, 0 failed, 10 skipped**, satisfying the before-merge row for the final inputs; the ten skips are the pre-existing env-gated captures. This row was written after that pass and changes no application or browser-suite input, so the pass still covers the tested revision. |

## GitHub Copilot code review, tenth pass (PR #285, on `c0a50b33`)

Copilot raised **one inline follow-up** on the ninth pass's own fix, and it is fixed here.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | Only `ApplyAuthority` was guarded by `_authenticationVersion`: after a stale startup authentication read, `OnInitializedAsync` still continued into `LoadDetailAsync()`, and because both the notification's load and that one capture the same `_clubScopeVersion`, the stale continuation could apply its own detail over the current scope's | **Fixed.** A stale startup read now returns before its load, so it applies neither its principal nor its detail; the return-URL normalization stays ahead of the check because it is not authentication-derived. New case `PlayerDetailDoesNotLoadDetailFromAStaleStartupAuthenticationReadAsync` holds the notification's detail read open, resolves the startup read as the old club, and asserts the page loaded exactly once — with the guard reverted it reports `calls should be 1 but was 2`, which is the race. |

### Confirming evidence (Copilot tenth pass)

Tested revision: the uncommitted working tree on branch `eruvalca-player-form-crud` on top of
`c0a50b33`.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3837 total, 3837 passed, 0 failed, 0 skipped** (3836 before; the new case is the delta). |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests*' --filter-class '*PlayersDirectoryBrowserTests*'` — **21 total, 20 passed, 0 failed, 1 skipped** (the pre-existing env-gated capture). |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Negative check | With the early return reverted, `PlayerDetailDoesNotLoadDetailFromAStaleStartupAuthenticationReadAsync` reports **1 failed, 0 passed** on `calls` (2 instead of 1). **One earlier attempt at this check was invalid and is recorded rather than dropped:** a scripted file rewrite silently failed to apply, so the run tested the fixed build and reported a pass. Redone with the edit tool, the result above is the valid one. Restored from a byte-identical snapshot with its timestamp touched before the rebuild. |
| Full browser suite | Two runs on this revision. The first reported **228 total, 217 passed, 1 failed, 10 skipped** — `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync` (7.2s), one of the two long directory journeys this record tracks as load-sensitive. The second was clean: **228 total, 218 passed, 0 failed, 10 skipped**, satisfying the before-merge row for the final inputs; the ten skips are the pre-existing env-gated captures. This row was written after that pass and changes no application or browser-suite input, so the pass still covers the tested revision. |

## GitHub Copilot code review, eleventh pass (PR #285, on `ec9d86b2`)

Copilot reported **two suppressed findings** on the tenth pass's head; neither carried an inline
thread, so the dispositions are here and in the triage comment on the PR.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | A create response that leaves the command `Unresolved` or `Expired` freezes the fields but left `_dirty` true and never cleared the module's dirty flag, so the board intercepted the top-level Return/Review links and opened "Leave with uncommitted player details?" even though the exact request is already retained and cannot be edited | **Fixed.** One predicate — `HasNothingUnsaved` — now states what the guard speaks for, and all three gates use it: the freeze clears the stale `_dirty` and syncs the module, `OnFieldChanged` no longer marks a frozen board dirty, and a departure the module asks about proceeds instead of prompting. A frozen board's fields *are* the retained addition, so it holds unsaved work only while its own set-aside decision is open (`_setAsidePending`), the one control it has left; that exception is round 2's finding, so its case `PlayersPromptsOnDepartureAfterTheSetAsideAcknowledgementAloneAsync` and the round-3 focus case still pass unchanged. New case `PlayersPerformsTheDepartureWhenTheFrozenRetainedAdditionCannotBeLostAsync` types, holds the module dirty, submits into an unresolved outcome, and asserts both the cleared flag and a departure with no panel. |
| 2 | The guard leaves Back/Forward unprotected, and the finding offers either the evaluation surface's rollback/approval implementation or "narrow the board contract and add a tested limitation for this departure path" | **Narrowed, and now tested, with the implementation ruled out by measurement rather than by preference.** Two implementations were built and driven in the real browser before being reverted. (a) A module-level `popstate` guard that restores the board's own entry through the Navigation API and prompts from the restoration's own `popstate`: instrumented, the guard was attached with `dirty: true`, `connected: true` and `origin` equal to the current entry, `traverseTo` and `currentEntry.key` present, and its handler ran **0 times** for the traversal that landed on `/players` — while a synthetic `popstate` dispatched at the same moment *did* invoke it, proving the listener was live. The router handles the traversal and disposes the board (the module's `detachActiveGuard` runs before `popstate` is delivered), so no listener the board installs can act on it. (b) A page-level `NavigationLock` whose `OnBeforeInternalNavigation` calls `PreventNavigation()`: the traversal was not prevented either, reproducing this record's earlier finding that traversal paths do not run `NavigationLock` callbacks. Both were reverted; `PlayerFormHistoryTraversalIsTheDocumentedUnguardedDepartureAsync` pins the limitation for the *same* typed value that the link path prompts for, so the gap is pinned by a test instead of asserted only in prose. The module's comment now names the verified mechanism. |

### Confirming evidence (Copilot eleventh pass)

Tested revision: the uncommitted working tree on branch `eruvalca-player-form-crud` on top of
`ec9d86b2`.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3838 total, 3838 passed, 0 failed, 0 skipped** (3837 before; the new case is the delta). |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — **22 total, 21 passed, 0 failed, 1 skipped** (the pre-existing env-gated capture). |
| Format | `dotnet format Nova.slnx --verify-no-changes --no-restore` — **exit 0**. |
| Negative check, finding 1 (clearing) | With `HasNothingUnsaved` reduced to the pre-fix `ShowsReceipt \|\| IsEntryBlocked \|\| !CanManage`, `PlayersPerformsTheDepartureWhenTheFrozenRetainedAdditionCannotBeLostAsync` fails on `Interop.Dirty` — the stale flag the finding describes. **1 failed, 0 passed.** |
| Negative check, finding 1 (guard) | With only the callback guard's condition reverted to the pre-fix form (property restored), the same case fails on `#intake-departure` (count 1 instead of 0). **1 failed, 0 passed.** Both halves are therefore discriminated separately. Fix restored and rebuilt before the runs above. |
| Finding 2 evidence | Instrumented probes on the real build (temporary, deleted): with the module's popstate guard installed and the board dirty at the moment of the traversal, the handler's run counter stayed at **0** while a synthetic `popstate` incremented it, and the page ended at `/players` with the board disposed (`activeGuard` null). With the page-level `NavigationLock` preventing navigation while `_createForm.FirstName` was set, the same traversal still landed on `/players`. Both probes are recorded rather than kept, and no diagnostic code remains in the module or the page. |
| Full browser suite | Two runs on this revision. The first reported **229 total, 218 passed, 1 failed, 10 skipped** — `CampaignCloseoutBrowserTests.CloseoutFailureShowsRetryAndRetryRecoversAsync`, whose failure is a transport error, not an assertion: `Microsoft.Playwright.PlaywrightException : net::ERR_NETWORK_CHANGED` while `BrowserSuiteFixture.SignInAsync` navigated to `/Account/Login`, so the scenario never reached the code under test and nothing in this diff is in its path. The retry was clean: **229 total, 219 passed, 0 failed, 10 skipped**, satisfying the before-merge row for the final inputs; the ten skips are the pre-existing env-gated captures, and the new history-limitation case is the delta in the total. |

## GitHub Copilot code review, twelfth pass (PR #285, on `89e700b8`)

Copilot reported **four suppressed findings**; none carried an inline thread, so the dispositions are
here and in the triage comment on the PR.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | A committed operation whose cleanup failed leaves the retained record and `StorageUnavailable`, yet **Add another** stayed enabled: `StartAnotherAdditionAsync` clears the receipt and then re-reads that same committed record as unresolved, hiding the authoritative receipt and offering recovery for an operation already known to have committed | **Fixed** by the finding's first option. The receipt panel's **Add another** is disabled while `StorageUnavailable`, because the browser's own reservation refuses a second command for this owner until that record is released — the flow it starts cannot succeed, and running it drops the receipt and re-presents a committed addition as unresolved. The storage panel beside it already states that the request was not released and offers **Release retained request**. `PlayersPreservesTheSettledReceiptAndRetryAcrossARoleRefreshAsync` gained the disabled assertion; reverting the attribute fails it. |
| 2 | A duplicate refusal whose `ClearAsync` failed left `_recoveryState` at `Unresolved`, so `CanReplay` still offered **Replay the retained addition** for a command the server had receipt-backed as not committed | **Fixed** as prescribed: `CanReplay` is gated on the terminal duplicate being absent, so a settled refusal is never resent. Because a withheld replay must not still be advertised, the commit label names a replay only where one is offered (`CommitLabel` requires `CanReplay`), and the frozen-state copy and the set-aside confirmation no longer claim an unknown outcome once the refusal settles it. New case `PlayersWithholdsReplayWhenTheRefusedRecordIsUnreleasedAsync` drives the two-attempt path (unknown outcome → replay → refusal) with the release failing, and asserts the disabled control, its non-replay label, the settled copy and that exactly two commands were sent. |
| 3 | The **edit** board supplies no intake context, so it fell through to the creation consequence's default copy — "No campaign is Active… joins the roster when the next campaign opens" — which is unrelated to editing and can be false | **Fixed.** The consequence is now the create host's opt-in (`ShowsEnrollmentConsequence`, left unset by the edit host), so a board that read no consequence states none instead of guessing one; the create host passes the flag explicitly. The five board-level cases that model the create host now pass it too, and new case `PlayersStatesNoEnrollmentConsequenceOnTheEditBoardAsync` renders the page on the edit route and asserts the paragraph is absent. This closes the open item recorded since the seventh/eighth passes. |
| 4 | The comp provenance marked A `approved` and `locked` while also recording that the agent could not inspect the rasters and that the user should still eyeball the comp, which the new `AGENTS.md` rule says needs user confirmation before locking | **Resolved in the provenance, without claiming a passed gate.** The lock rests on the user's explicit delegation ("Please choose the comp candidate based on your best judgement"), which is the confirmation the rule asks for and matches the skill's convention (`.agents/skills/impeccable/scripts/concept-seed.mjs`: "one approved by the user through the decision page or structured question, sidecar `approved": true`"). What the sidecar lacked was the basis being legible beside the flag, so it now carries `approvalBasis` (delegated approval on the recorded measurement, not a perceptual review; the user's own eyeball is still open) and `designGate` ("not run — read `approved` as delegated approval, never as a passed visual gate"), with `approval` cross-referencing both. No new user confirmation is obtainable in this unattended pass, and the record claims no more than the delegation supports. |

### Confirming evidence (Copilot twelfth pass)

Tested revision: the uncommitted working tree on branch `eruvalca-player-form-crud` on top of
`89e700b8`.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3840 total, 3840 passed, 0 failed, 0 skipped** (3838 before; the two new cases are the delta). |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — **22 total, 21 passed, 0 failed, 1 skipped** (the pre-existing env-gated capture). |
| Format | `dotnet format Nova.slnx --verify-no-changes --no-restore` — **exit 0**. |
| Negative check, all three code findings | With the three fixes reverted together (the `disabled` attribute, the `CanReplay` gate plus the label guard, and the consequence gate), exactly the three cases tied to them fail: `PlayersPreservesTheSettledReceiptAndRetryAcrossARoleRefreshAsync`, `PlayersWithholdsReplayWhenTheRefusedRecordIsUnreleasedAsync` and `PlayersStatesNoEnrollmentConsequenceOnTheEditBoardAsync` — **3 failed, 105 passed**, one per finding, so each assertion discriminates its own revert. Fixes restored and rebuilt before the runs above. |
| Comp provenance | `.impeccable/mocks/issue-264-a.png.json` is valid JSON (the file is the sidecar `DESIGN.md` links) and now names `approvalBasis` and `designGate`; no raster changed, so the recorded measurement still applies. |
| Full browser suite | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` — **229 total, 219 passed, 0 failed, 10 skipped** on the first run, satisfying the before-merge row for the final inputs; the ten skips are the pre-existing env-gated captures. This row was written after the pass and changes no application or browser-suite input, so the pass still covers the tested revision. |

## GitHub Copilot code review, thirteenth pass (PR #285, on `135826d1`)

Copilot reported **one suppressed finding**; it carried no inline thread, so the disposition is here
and in the triage comment on the PR.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | A disposal that lands while `AttachDepartureGuardAsync` is still awaiting reads `_guardAttached == false` and skips the detach, so the continuation can leave a guard installed in the document over a receiver that disposal has already released | **Fixed as prescribed, by tracking the attach and settling it in disposal.** `PlayerIntakeBoard` records the attach it started (`_guardAttach`, assigned before the await) and `DisposeAsyncCore` waits for an attach that is still in flight and then releases that lease. Waiting is what makes the release correct: disposal cancels the component token — and `NovaComponentBase` cancels it *before* `DisposeAsyncCore` — which cuts off the answer without undoing what the browser already did, so the release is keyed on "an attach was in flight" as well as on the settled flag. Releasing a lease that is not the active guard is a no-op in the module, so this can never take another mounting's guard away, and the existing narrow `JSDisconnectedException` catch is unchanged. `PlayerIntakeInteropDouble` now models the real boundary: `GuardAttachGate` holds an attach open, a cancelled await still reports `GuardAttached = true` and then throws (as an interop call does), and `GuardAttachSettled` gives the case a deterministic point to assert from. New case `PlayersReleasesTheGuardLeaseWhenDisposedDuringTheAttachAsync`. |

### Confirming evidence (Copilot thirteenth pass)

Tested revision: the uncommitted working tree on branch `eruvalca-player-form-crud` on top of
`135826d1`.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3841 total, 3841 passed, 0 failed, 0 skipped** (3840 before; the new case is the delta). |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — first run **22 total, 19 passed, 2 failed, 1 skipped**, both failures the tracked load-sensitive journeys (`DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync` and `OrdinaryMemberCreatesEditsArchivesAndRestoresThroughRoutedFormAsync`). Re-run on the same build: both in one run → the second passed; the first then passed **twice in isolation** (`2/2`). Recorded as the tracked flake, with the A/B below. |
| Negative check | With the release half removed (the wait kept, so the reverted build still compiles under `S4487`), `PlayersReleasesTheGuardLeaseWhenDisposedDuringTheAttachAsync` fails on `Interop.GuardDetachCount` (**0 instead of 1**) — **1 failed, 0 passed**. Fix restored and rebuilt before the runs above. |
| **Analysis trap, recorded because it produced two invalid results** | The first two attempts at that negative check reported a false pass: the reverted source left `_guardAttach` unread, so the build **failed** on `S4487` while `dotnet test --no-build` silently ran the previously built (fixed) assembly. This is the same trap this record names from the tenth pass, and the fix is the same discipline: read the build line before trusting a `--no-build` result, and revert in a form the analyzers accept. Both invalid runs are recorded rather than dropped. |
| Flake A/B, recorded because it initially implicated the change | The failing journey's assertion is about the detail page's `← Back to roster` href, a step with no board on screen, so the change was checked against the previous revision: `135826d1` passed in one isolated run and this revision passed in two, which is why the failures are recorded as the tracked flake rather than as a regression. |
| Format | `dotnet format Nova.slnx --verify-no-changes --no-restore` — **exit 0**. |
| Full browser suite | Two runs on this revision. The first reported **229 total, 218 passed, 1 failed, 10 skipped** — `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync` again, this time on the directory paging text (`.players-paging` read `Page 1 of 4` where the journey expects the retained draft context to put it on page 2), the same load-sensitive journey and the same class of failure the record already tracks. The retry was clean: **229 total, 219 passed, 0 failed, 10 skipped**, satisfying the before-merge row for the final inputs. Every failure of this journey seen in this pass is recorded rather than summarized away, and none reproduced in isolation on the same build. |

## GitHub Copilot code review, fourteenth pass (PR #285, on `21e5b6ba`)

Copilot raised the **failed-read withholding** finding again, this time with the specific line and a
prescription. It carried no inline thread, so the disposition is here and in the triage comment on the
PR. **This closes the open item the record has carried since the seventh/eighth passes.**

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | `RestoreRecoveryAsync` marks the check settled even when `ReadRecoveryAsync` returned null, so the board opens while the owner's retained state is unknown; values typed after that failure are silently replaced when a later retry lands a retained command (`Players.razor.Intake.cs:121`) | **Fixed as prescribed.** A read that refuses now leaves `RecoveryChecked` false, so the board stays withheld until a read actually answers; the settle moved to after the null check, which is the same rule for every reader (entry, retry, same-owner refresh, **Add another**): input is offered only once the owner's retained command has been examined for this scope by a read that answered. Nothing typed can therefore be replaced, because nothing can be typed while the state is unknown. The withheld state is named for the failure it actually is — `RetainedCheckNote` now distinguishes a check in progress from one storage refused ("could not be checked. Retry storage to continue."), which matters because the refused state persists instead of passing in a frame as the in-flight one does — and the storage panel beside it keeps the retry. **The round-2 decision is superseded, not silently dropped:** that case asserted the board reopens so a broken browser is not a dead end, and it is rewritten as `PlayersKeepsTheBoardWithheldWhenTheRetainedCommandReadFailsAsync`, which now also proves the withholding is escapable (a read that answers opens the board and clears the notice). New case `PlayersKeepsTheBoardWithheldWhenTheRetryReadFailsAgainAsync` pins that a second failure does not reopen either. The replay is withheld with it while storage is unreadable, which is stricter than before by the same rule. |

### Confirming evidence (Copilot fourteenth pass)

Tested revision: the uncommitted working tree on branch `eruvalca-player-form-crud` on top of
`21e5b6ba`.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3842 total, 3842 passed, 0 failed, 0 skipped** (3841 before; the rewritten case plus the new retry case net one). |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — **22 total, 21 passed, 0 failed, 1 skipped** (the pre-existing env-gated capture), with the load-sensitive journey passing in this run. No players browser scenario breaks the storage boundary — only the campaign and evaluation suites override `Storage.prototype.getItem`, and neither touches the intake board — so the withholding cannot reach them. |
| Negative check | With `_recoveryChecked = true` restored inside the refused-read branch (the build re-verified as successful first), both cases fail on `fieldset` `disabled` — **2 failed, 0 passed**. Fix restored and rebuilt before the runs above. |
| Format | `dotnet format Nova.slnx --verify-no-changes --no-restore` — **exit 0**. |
| Full browser suite | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` — **229 total, 219 passed, 0 failed, 10 skipped** on the first run, satisfying the before-merge row for the final inputs; the ten skips are the pre-existing env-gated captures. This row was written after the pass and changes no application or browser-suite input, so the pass still covers the tested revision. |

## GitHub Copilot code review, fifteenth pass (PR #285, on `ee59fc76`, fixed in `4f72ccf9`)

Copilot raised one inline finding and reported two more in the review body. All three are addressed in
`4f72ccf9`.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | Scope invalidation keys only on `_clubId`, so a different member of the same club inherits the previous caller's reviewed confirmation and any in-flight detail/lifecycle completion (`PlayerDetail.razor.cs:196`, **inline thread**) | **Fixed as prescribed.** The page tracks the caller beside the club and treats either change as a scope change: the reviewed panel closes, the previous caller's outcome messages are cleared, `_clubScopeVersion` increments, and the detail reloads against the new scope — so a completion that belongs to the previous caller can no longer publish into the view now on screen. New case `PlayerDetailRebindsScopeWhenAnotherMemberOfTheSameClubTakesOverAsync` drives a same-club takeover *while an archive is in flight* and proves the panel closes, the detail is re-read, and the completed archive leaves no status on the new caller's page. |
| 2 | Field-level server errors render as free-standing `<p>` elements, and no control points at its own message with `aria-describedby` (`PlayerIntakeBoard.razor:274`) | **Fixed, with a measured correction to the premise.** Every profiled control now names its field's error region by a stable id and renders both the form's own validation message and the server messages inside it — `PlayersDescribesAServerFieldMessageFromItsControlAsync` and `PlayersDescribesItsOwnValidationMessageFromItsControlAsync` prove both sources, and the gender case asserts the same for the select. **The premise that the controls already `aria-invalid` does not hold in the built app:** these form components own `aria-invalid`, render it from their own edit context, and ignore one supplied to them ([`InputBase`](https://source.dot.net/Microsoft.AspNetCore.Components.Web/Forms/InputBase.cs.html)) — the new cases measured exactly that, `null` for a server-keyed error while `aria-describedby` rendered, and `"true"` for the form's own message. The dead `aria-invalid` expressions are therefore replaced by the class the surface already uses for its invalid state (`is-invalid`), so a server-keyed error marks its field as well as naming its message, and the board re-renders on validation-state change so those attributes describe the field validation actually left it in. |
| 3 | `CreatePlayerAsync` guards only `_identityVersion` after the await, so a member who leaves the form mid-flight gets the receipt stored into a view that clears it at the next boundary while the retained record is released — the committed operation loses both pieces of evidence (`Players.razor.Intake.cs:204`) | **Fixed as prescribed.** The outcome is now applied only where the create form is showing: a request that answers after the member left the form publishes nothing, and the exact command stays retained, so the same operation identity recovers the same receipt server-side on their return. `PlayersKeepsACommittedCreationRecoverableWhenTheRouteChangesMidFlightAsync` leaves the form while the request is held, releases it with a committed completion, and proves the command is still retained, no receipt is published into the directory, and returning to the form replays the *same* command into the receipt and its release. A committed creation still refreshes the directory, where its player is the one effect this view can honestly report. |

### Confirming evidence (Copilot fifteenth pass)

Tested revision: `4f72ccf9` — the three fixes, pushed as the next commit on the branch. The
documentation-only edit that recorded this evidence changed no application or browser-suite input.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3846 total, 3846 passed, 0 failed, 0 skipped** (3842 before; the four new cases are the delta). |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Negative check | With the four product files restored to their `ee59fc76` content — the revert **build re-verified as successful (0 warnings, 0 errors) before the run**, the trap this record names twice — **all five new cases fail**: the three field-feedback cases, the same-club takeover case, and the route-changed creation case (**5 failed, 3841 passed**). The fixes were restored, rebuilt and re-run green before the suites below. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Affected browser selection | `--filter-method "*DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync"` — **1 total, 1 passed** in isolation on this revision, which is the journey the change can reach and the one that failed inside the first two full runs. |
| Full browser suite | Three runs on this revision. The first reported **229 total, 217 passed, 2 failed, 10 skipped** and the second **229 total, 218 passed, 1 failed, 10 skipped**; in both, `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync` — the journey this record has tracked as load-sensitive since the eleventh pass — failed on absent paging text (`element(s) not found 'Page 2 of 4'` in `.players-paging`), the same class of failure the thirteenth and fourteenth passes recorded (there, reading `Page 1 of 4`). The third run was clean: **229 total, 219 passed, 0 failed, 10 skipped**, satisfying the before-merge row for the final inputs. The failing journey exercises the directory's draft/paging return path, not the field markup, the caller scope or the route-changed settlement this pass changed, it passed in isolation on this revision, and it has failed and passed on unchanged revisions before — so the evidence does not attribute it to this change. |

## GitHub Copilot code review, sixteenth pass (PR #285, on `268fbdcf`, fixed in `ba07ea6b`)

Copilot raised one inline finding against the validation-state handler this record added in the previous
pass; it is addressed in `ba07ea6b`.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | The handler only re-renders after validation, so a refusal leaves focus on the submit control instead of moving to the first field that needs correction; the surface contract requires focus to the first error after validation, for the validation store and for the server's `FieldErrors` alike (`PlayerIntakeBoard.razor.cs:586`, **inline thread**) | **Fixed as prescribed.** `MoveFocusToFeedback` requests focus on the first control marked invalid, and it runs from both places feedback can arrive: the validation-state handler for the edit context's own messages, and `OnParametersSet` for the messages the server keyed to a field, which arrive as parameters. It moves focus **once per refusal** rather than on every state change, because a correction that clears one message while another field still needs attention is the same refusal — following it would pull focus away from the field the member is working in. The module's `focusRegion` now adds `tabindex="-1"` only when its target cannot already take focus, so a control keeps its place in the tab order while a heading behaves exactly as before. The contract this follows is the surface brief's own (`player-intake.md`: "focus moves to the first error, the review heading, or the finish heading after transitions"). |

### Confirming evidence (Copilot sixteenth pass)

Tested revision: `ba07ea6b` — the focus contract, pushed as the next commit on the branch. The
documentation-only edit that recorded this evidence changed no application or browser-suite input.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**, after one fix the new code needed: the static field required the repository's `_` prefix for private fields (`IDE1006`). |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3847 total, 3847 passed, 0 failed, 0 skipped** (3846 before; the anti-chase case is the delta, and two existing cases gained their focus assertion). |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Negative check (unit) | With `PlayerIntakeBoard.razor.cs` restored to its `268fbdcf` content — the revert **build re-verified as successful (0 warnings, 0 errors) before the run**, the trap this record names — **three cases fail**: both focus assertions and the anti-chase case (**3 failed, 3844 passed**). |
| Negative check (browser) | With `PlayerIntakeBoard.razor.js` reverted on that same verified build, `IntakeBoardFocusMovesToTheFieldNeedingCorrectionAsync` fails on the tab-order half (**1 failed, 1 passed, 1 skipped**), which pins the module change itself rather than the board's request. Both files were restored, rebuilt and re-run green before the suites below. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — **23 total, 21 passed, 1 failed, 1 skipped**; the selection grew by the new focus case, and the failure is `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync`. That journey passed again in isolation on this revision (**1 total, 1 passed**). |
| Full browser suite | Two runs on this revision. The first reported **230 total, 219 passed, 1 failed, 10 skipped** — again `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync`. Attribution is checked rather than assumed: that journey **exists on `origin/main`** and this branch's diff does not touch the directory's paging or draft-return path (`git diff origin/main...HEAD -- Nova.UI/Features/Players/Pages/Players.razor` has no paging lines), it passes in isolation on this revision, and this record already carries the same journey failing on `135826d1` and `21e5b6ba`, before any of this pass's changes. The retry was clean — **230 total, 220 passed, 0 failed, 10 skipped** — satisfying the before-merge row for the final inputs (the 230 includes this pass's new browser case; the ten skips are the pre-existing env-gated captures). |

## GitHub Copilot code review, seventeenth pass (PR #285, on `68333bc6`, fixed in `0578c671`)

Copilot raised one inline finding naming three sites in the same file: the ownership generation the page
checks represents only club and caller scope, so it never changes when the reused routed page receives
another `PlayerId`. Addressed in `0578c671`.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | The scope generation does not include the routed player, so a read for player 7 that answers after the route moved to player 21 passes its check and binds player 7's detail to `/players/21` (same shape at lines 341 and 382) (`PlayerDetail.razor.cs:262`, **inline thread**) | **Fixed as prescribed.** The page now owns the routed player: `OnParametersSetAsync` rebinds when the route names another one — clearing the previous player's detail, messages and submission state, then loading the new player — `LoadDetailAsync` requests the player it captured and rejects its result unless that player is still the routed one, and both lifecycle mutations capture the player they started under and require it, with the club scope, to be unchanged before they report. A mutation that loses that ownership also releases `_isMutating`, so the page now on screen is not left with disabled controls. New cases `PlayerDetailBindsTheRoutedPlayerWhenTheRouteNamesAnotherAsync` (a held read for player 7 that answers after the route moved to 21 binds nothing from player 7) and `PlayerDetailDropsAnArchiveOutcomeWhenTheRouteMovesOnMidFlightAsync` (a held archive that answers under a later route reports nothing). **Deliberately unchanged:** the reviewed-subject panel keeps the cross-page semantics an earlier round pinned (`PlayerDetailArchivesTheReviewedSubjectWhenTheRouteChangesWhileThePanelIsOpenAsync`). The ownership rule here is about the *request's* route identity, which is the reviewer's scenario — a confirmation issued *after* the route moved starts under the new route and still targets the subject it reviewed, which is that pin's contract. |

### Confirming evidence (Copilot seventeenth pass)

Tested revision: `0578c671`.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3849 total, 3849 passed, 0 failed, 0 skipped** (3847 before; the two new cases are the delta). |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Negative check | With `PlayerDetail.razor.cs` restored to its `68333bc6` content — the revert **build re-verified as successful (0 warnings, 0 errors) before the run** — **both new cases fail** (**2 failed, 3847 passed**). Restored, rebuilt and re-run green before the suites below. |
| First attempt at the hook, recorded rather than dropped | The first version detected "first application" with a nullable bound-player sentinel set inside `OnParametersSetAsync`. It failed `PlayerDetailBindsTheRoutedPlayerWhenTheRouteNamesAnotherAsync` on a **successful build**: while the startup read is still pending, the framework runs `OnParametersSetAsync` for the parameter change *before* `OnInitializedAsync` completes, so the sentinel was still unset and the hook read the new route as the first application. The field is now set when a read starts — the player the page's state actually belongs to — which is correct in both orders. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — **23 total, 22 passed, 0 failed, 1 skipped** (the pre-existing env-gated capture), with the load-sensitive directory journey passing in this run. |
| Full browser suite | **Five attempts on this revision, none clean**: run 1 **230 total, 218 passed, 2 failed, 10 skipped** (`DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync`, `CampaignEvaluationCaptureBrowserTests.ModifiedPlayerClickOpensNewTabWithoutChangingOriginalDraftAsync`); run 2 **230, 217, 3** (`PlayerFormKeyboardTabAndEnterSubmitsAsync`, `CampaignPlaceBrowserTests.SavingTheLastParticipantOnPageTwoAdoptsPageOneBeforeEnablingEditingAsync`, the directory journey); run 3 **230, 218, 2** (`CampaignClosedRecordBrowserTests.DirectParticipantLinkFocusesHistoryOnInitialAttachmentAndReloadAsync`, `OrdinaryMemberCreatesEditsArchivesAndRestoresThroughRoutedFormAsync`); run 4 **230, 217, 3** (the closed-record journey, the directory journey, the ordinary-member journey); run 5 **230, 219, 1** (the closed-record journey). Every failure occurred only inside full runs. **Attribution, checked per journey rather than assumed:** each **passes in isolation** on this revision — the directory, evaluation, keyboard, ordinary-member and closed-record journeys were each run directly, and the ordinary-member journey also passes inside the affected selection below — and each **exists on `origin/main`**: the closed-record journey arrived with #282, whose commit is in this branch's history, and `git diff --stat origin/main...HEAD` is empty for the campaign surfaces and for those test files. The two failures that are in this surface are the journeys this record has tracked as load-sensitive since the eleventh pass, failing here with the same readiness signature (the board's gated input not yet enabled: the keyboard journey on `#player-first-name` not existing yet, the ordinary-member journey on the routed form's region). The repository's own rule names the mechanism — Aspire-backed suites must stay serial across worktrees because shared Docker capacity can exhaust bounded hydration/storage retries — so the instability tracks the machine's concurrent load, not this change. **The before-merge full-pass row is therefore outstanding on this revision and is recorded as a limitation**, not claimed: the affected selection below is clean and the suite is re-attempted on later ticks. |

## GitHub Copilot code review, eighteenth pass (PR #285, on `d80b50e0`, fixed in `d5e370c5`)

Copilot raised one inline finding on the retained-storage boundary, which landed after the seventeenth
pass was recorded. Addressed in `d5e370c5`.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | `writePending` treats a matching operation id as sufficient to overwrite the retained record without checking that the incoming payload is the exact retained one, so a stale/same-owner tab, or altered bytes in storage, could replace the command under its identity — and with no receipt yet the server could process the replacement as a new creation, breaking the exact-replay contract (`PlayerIntakeBoard.razor.js:91`, **inline thread**) | **Fixed as prescribed.** A same-identity write may now only carry the retained bytes back: the module refuses when the existing record's JSON differs, and the in-memory stand-in mirrors that rule so the boundary's semantics cannot drift in unit tests. The comparison is byte-exact and it is safe for the app's own replay — the retained record *is* the re-serialization of the command the board holds, and the server's own `RequestSha256` fingerprint check depends on that same stability — and it makes the write side symmetric with the read side, which already treats differing bytes as evidence to preserve for an explicit discard rather than to overwrite (`readRecovery`'s `invalidValue`, `discardInvalidPending`'s exact-byte comparison). New case `WriteAsyncRefusesDifferentBytesUnderTheSameOperationAsync` pins the stand-in's refusal and that the original bytes survive; `IntakeBoardModuleRetainsAndReadsOwnerScopedBytesAsync` now drives a same-id altered write in a real browser and asserts both that it is refused and that the retained bytes are the originals. **No board flow changes:** every write the board performs is either a fresh operation identity or the retained command replayed unchanged, and the whole existing suite passing unchanged is the evidence that legitimate replays still write. |

### Confirming evidence (Copilot eighteenth pass)

Tested revision: `d5e370c5`.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**, after one fix the new code needed: the browser assertions had to double their quotes inside the verbatim script literal. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3850 total, 3850 passed, 0 failed, 0 skipped** (3849 before; the new boundary case is the delta). No existing case needed a change, which is the evidence that legitimate replays still write under the stricter rule. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Negative check (unit) | With the stand-in restored to its `d80b50e0` content — the revert **build re-verified as successful (0 warnings, 0 errors) before the run** — `WriteAsyncRefusesDifferentBytesUnderTheSameOperationAsync` fails (**1 failed, 3849 passed**). |
| Negative check (browser) | With the module reverted on that same verified build, `IntakeBoardModuleRetainsAndReadsOwnerScopedBytesAsync` fails on the new refusal and preservation expectations (**1 failed, 1 passed, 1 skipped**), which pins the module's own rule rather than the stand-in's. Both files were restored, rebuilt and re-run green before the suites below. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — **23 total, 22 passed, 0 failed, 1 skipped** (the pre-existing env-gated capture). |
| Full browser suite | Two runs on this revision. The first reported **230 total, 219 passed, 1 failed, 10 skipped** — `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync`, the journey this record has tracked as load-sensitive since the eleventh pass, on the same absent-paging signature; attribution is unchanged from the seventeenth pass (it exists on `origin/main`, this diff does not touch the directory's paging or draft-return path, and it passes in isolation on these revisions), and the run before it had already passed the same journey inside the affected selection. The retry was clean — **230 total, 220 passed, 0 failed, 10 skipped** — which satisfies the before-merge row for the final inputs on `d5e370c5` (the ten skips are the pre-existing env-gated captures). |

## GitHub Copilot code review, nineteenth pass (PR #285, on `27e54b61`, fixed in `4d72bcbd`)

Copilot raised one inline finding on `RestoreRecoveryAsync`; auditing the sibling paths in the same file for
the same invariant found one more. Both are fixed in `4d72bcbd`.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | `RestoreRecoveryAsync` is owned only by `_identityVersion`, so a recovery read from an earlier `/players/new` visit can still apply after the member left and re-entered the route — and if the newer read has already enabled the form and the member has typed, the late `ApplyRecoveryRead` replaces that input with the old storage snapshot (`Players.razor.Intake.cs:95`, **inline thread**) | **Fixed as prescribed.** A recovery read now takes the next generation of a per-visit attempt counter and, before it may claim the board's readiness or publish its snapshot, must still own that generation **and** the create route (`_routeVersion` unchanged, `_showCreateForm`). A rejected answer leaves the claim and the readiness to the read that owns the visit now on screen, closing the "a landed recovery replaces typed values" invariant for the read that had been missed. |
| 2 | (found by the sibling audit, not reported) The same shape in `SetAsideRetainedAsync`: a removal that answered after the member left rewrote the visit that was on screen — `_createForm = CreateDefault()` cleared input typed since, plus the status message, the focus request and the guard call | **Fixed.** The removal is the member's decision and still stands (the bytes are gone, so the retained state is cleared in memory too), but only the visit that asked for it may rewrite the form, its messages, the departure guard or the focus. The outcome handling moved into `ApplySetAsideOutcomeAsync`, which takes the visit's ownership as an explicit input. Sibling paths audited and left alone, with reasons: `LoadIntakeContextAsync` already carries a per-call version; the creation outcome is route-guarded in `ApplyCreationOutcomeAsync`; and `ReleaseRetainedAsync`, `RetryStorageAsync`'s release and the retained-state clears publish *storage truth* — route-independent facts about what the browser holds — rather than view or input state. |

### Confirming evidence (Copilot nineteenth pass)

Tested revision: `4d72bcbd`.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**, after one intermediate failure the new code caused (`MA0051` method length, fixed by extracting the set-aside outcome handling). |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3852 total, 3852 passed, 0 failed, 0 skipped** (3850 before; the two new cases are the delta). |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Negative check | With `Players.razor.Intake.cs` restored to its `27e54b61` content — the revert **build re-verified as successful (0 warnings, 0 errors) before the run** — **both new cases fail** (**2 failed, 3850 passed**). Restored, rebuilt and re-run green before the suites below. |
| Vacuous first attempt, recorded rather than dropped | The first version of the stale-read case passed against the reverted product file, so it proved nothing: the read's answer crosses three async hops (the boundary, the board's read, the page's continuation) and the assertion ran before they drained. The case now waits for the boundary's own answer (`Interop.ReadCount`) and then awaits one dispatch per remaining hop, which is what makes it fail on the reverted code. A case that cannot fail without its fix is not evidence, so it is recorded and replaced rather than kept green. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — **23 total, 21 passed, 1 failed, 1 skipped**; the failure is the tracked load-sensitive directory journey, which **passes in isolation** on this revision (**1 total, 1 passed**). |
| Full browser suite | **230 total, 220 passed, 0 failed, 10 skipped on the first run** on this revision, satisfying the before-merge row for the final inputs (the ten skips are the pre-existing env-gated captures). The tracked load-sensitive journeys — including the directory journey that failed once inside the selection above — passed in this run. |

## GitHub Copilot code review, twentieth pass (PR #285, on `16027ba2`, fixed in `84c9cf71`)

Copilot raised two inline findings. One is fixed; the other is answered with evidence that its premise does
not hold in the built code. Both threads are resolved.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | `readRecovery` "returns a Promise whenever `navigator.locks` is available", so the browser probe dereferences a promise at three places and "the expected assertion cannot pass and the locked read contract is not exercised" (`PlayerFormBrowserTests.cs:72`, **inline thread**) | **Verified factually wrong, and the contract is now pinned rather than assumed.** The module's `readRecovery` is synchronous by design: one `localStorage.getItem` plus validation, with the cross-tab lock scoped to the read-then-write reservation and the removals (`writePending`, `clearPending`, `discardInvalidPending` — all three of which the probe already awaits); a single `getItem` cannot interleave, so a read needs no lock. The premise would also have surfaced as a *failing* assertion rather than a passing one: given a promise, `m.readRecovery(101, 43).json` would be `undefined`, so the probe would report `otherowner-leaked` instead of `otherowner-empty`. The probe now asserts the synchronous contract directly (`typeof read?.then === 'undefined'`), so a future change to a locked read fails loudly instead of silently inspecting a promise — the reviewer's concern, made testable — and it passes in the real browser (affected selection below). Awaiting the call instead would have hidden the very contract the probe exists to exercise. |
| 2 | When the route changes while the confirmation is open, `ConfirmArchiveAsync` captures the new `PlayerId` but archives `_archiveSubjectId`, so a successful archive passes the guard and publishes the generic `Player archived.` above the new player's detail, implying the routed player was archived (`PlayerDetail.razor.cs:381`, **inline thread**) | **Fixed by re-scoping the result — the option the finding allows.** `PlayerLifecycleCopy.ArchivedSubjectResult` names the player the archive actually settled ("Avery Johnson archived.") whenever the reviewed subject is not the routed player, while the landed generic copy is kept for the case it describes truthfully (subject *is* the routed player), so no existing copy or test changed. The result still closes the panel and reloads the routed detail, which is the feedback an explicitly confirmed mutation deserves. The pinned cross-page semantics are kept: the panel still reviews its captured subject (`PlayerDetailArchivesTheReviewedSubjectWhenTheRouteChangesWhileThePanelIsOpenAsync`, now extended so another player's detail is on screen when the result lands) — the archive still targets the reviewed player, and the *message* changed so it cannot misattribute. `RestorePlayerAsync` restores the routed player under an unchanged route, so it has no subject divergence to name. |

### Systematic sweep of the earlier findings' classes (requested)

Rather than fixing only the reported lines, this pass audited every instance of the classes the PR's review
rounds have raised across the whole diff, classifying each continuation and publication by what it owns:

| Class | Instances checked | Result |
| --- | --- | --- |
| A completion publishing onto a view that has left it | `CreatePlayerAsync`/`ApplyCreationOutcomeAsync`, `ConfirmArchiveAsync` and `RestorePlayerAsync` (both hosts), `UpdatePlayerAsync` (edit save), `SetAsideRetainedAsync`, `RestoreRecoveryAsync`, `LoadIntakeContextAsync`, `LoadRosterAsync`/`LoadSummaryAsync`/`LoadTagsAsync`, `LoadEditAsync`, `OnSearchInputChangedAsync` (debounce), `ApplyAuthenticationStateAsync`, and the detail page's reads and mutations | **Owned.** Each captures its identity — and where relevant the route, visit or reviewed subject — before its await and re-checks it before publishing. The already-versioned ones (intake context, directory reads, edit read, debounce token) needed no change; this pass's two fixes close the intake ones that did, and the sweep found no third. |
| A landed recovery replacing typed input | every writer of `_createForm`/`_editForm`: `ApplyRecoveryRead` (all four callers), the duplicate-refusal path in `SettleProblemAsync`, `ApplySetAsideOutcomeAsync`, `LeaveBoard`, `ResetIdentityState`/`ApplyIdentityAsync`, `LoadEditAsync`, `ApplyRouteState` | **Owned.** `ApplyRecoveryRead` is the only writer fed by storage and is now visit-owned; the rest are route-guarded or deliberate, user-initiated resets. |
| A message attributing an outcome to the wrong subject | the enrollment consequence (create-only), the replay/duplicate/commit labels, the withheld-check note, the archive result (this pass), the directory's roster-level results, the shared confirmation's heading, and `PlayerLifecycleConfirmation`'s subject-keyed acknowledgement | **Owned.** Only the archive result could describe a subject the view no longer showed; the others either name their subject or describe the view they appear on. The confirmation already resets its acknowledgement when the subject changes. |
| Field feedback associated with its control | all six profiled fields, both hosts (one shared component) | **Owned** since the fifteenth and sixteenth passes; re-checked here, nothing else sets `aria-invalid`/`aria-describedby` on this surface. |
| Storage-boundary invariants | `readRecovery`, `writePending`, `clearPending`, `discardInvalidPending`, and their stand-in mirrors | **Owned.** Writes are byte-guarded per identity, the discard compares exact inspected bytes, the clear compares the settled operation, and the read is lock-free (finding 1). |
| Disposal and lease ownership | the board's attach-in-flight disposal, and focus/guard calls that could outlive the mounting | **Owned** since the thirteenth pass. |
| Server-side request ownership | `PlayerIntakeContextService` (new), the intake-context endpoint, `PlayerManagementService.Creation`, the DI registration | **Owned.** The new query validates the caller's club against the requested one and logs the refusal, the endpoint carries `RequireClubMember` with full problem metadata, and creation recovery stays receipt-, actor- and fingerprint-bound. |

### Confirming evidence (Copilot twentieth pass)

Tested revision: `84c9cf71`.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3852 total, 3852 passed, 0 failed, 0 skipped** (the same count as the previous pass: the re-scope extends an existing case rather than adding one). |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Negative check | With `PlayerDetail.razor.cs` and `PlayerLifecycleCopy.cs` restored to their `16027ba2` content — the revert **build re-verified as successful (0 warnings, 0 errors) before the run** — `PlayerDetailArchivesTheReviewedSubjectWhenTheRouteChangesWhileThePanelIsOpenAsync` fails (**1 failed, 3851 passed**), because the landed generic copy then appears above another player's detail. The files were restored, rebuilt and re-run green before the suites below. |
| Contract evidence for finding 1 | The affected selection below runs `IntakeBoardModuleRetainsAndReadsOwnerScopedBytesAsync`, whose probe now includes `direct` — `typeof read?.then === 'undefined'` — and passes **in Chromium**: the read answers with the record, not a promise. That assertion fails if the module ever locks the read, which is the case the finding asked to be covered. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — **23 total, 22 passed, 0 failed, 1 skipped** (the pre-existing env-gated capture), with the tracked load-sensitive journeys passing in this run. |
| Full browser suite | Two runs on this revision. The first reported **230 total, 219 passed, 1 failed, 10 skipped** — `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync`, the journey this record has tracked as load-sensitive since the eleventh pass, on the same signature; it had passed the affected selection minutes earlier and passes in isolation on these revisions, and the issue it is now tracked under is filed (#286). The retry was clean — **230 total, 220 passed, 0 failed, 10 skipped** — satisfying the before-merge row for the final inputs on `84c9cf71` (the ten skips are the pre-existing env-gated captures). |

## GitHub Copilot code review, twenty-first pass (PR #285, on `8b799923`, fixed in `8d708f6b`)

Copilot raised one inline finding: the Web Lock protects only `writePending`, so it is released before the
page dispatches, and another same-owner tab can read the record and clear it before the first tab reaches
`CreateAsync`.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | The write lock is released before dispatch, so another same-owner tab can clear the retained record while the first tab is dispatching, "leaving a lost acknowledgement with no recoverable operation"; the finding asks to hold an owner/operation reservation through dispatch, or to make cleanup reject an in-flight reservation (`PlayerIntakeBoard.razor.js:99`, **inline thread**) | **The reachable misreport is fixed; the reservation is answered with the decision and its reasons rather than implemented.** `ReleasedOrAlreadyGoneAsync` resolves a release that found nothing by reading storage, so a record another tab already removed is reported as gone instead of as bytes the browser kept. Both halves of that judgement are set out below, and the interleaving is recorded in *Limitations*. |

**What the interleaving produces, and what was fixed.** A tab can only clear the record deliberately:
`clearPending` is reached by a settlement in that tab (a receipt or a receipt-backed refusal) or by the
member's own **set aside**, an acknowledged action whose copy already says the earlier result stays unknown.
The dispatching tab keeps its own evidence in every outcome — a receipt, a receipt-backed refusal, per-field
validation, or an unresolved result whose replay re-retains before it dispatches — so the acknowledgement is
not lost while that tab lives. What *was* wrong is what the settling tab then reported: a clear that found
nothing to remove (because the other tab had already taken it) was treated as "the browser is holding the
record", so the member got a storage-failure claim and a **retry that can never succeed**. That is reachable
with no race at all — two tabs replaying the same retained command each call the release, and the second
finds nothing — so it is fixed as a reporting defect in this PR's own class ("never claim what did not
happen"): a read that answers "nothing is retained" is treated as gone, because the operation is settled and
the refusal is proven, while a read that answers the record — or that refuses — keeps today's honest report
and its retry. `PlayersShowsTheReceiptWhenTheRetainedRecordWasAlreadyGoneAsync` and
`PlayersReportsARefusalWhoseRecordWasAlreadyGoneAsync` pin both consumers.

**Why the reservation is not implemented.** Holding a Web Lock across the HTTP round-trip is unavailable
here: the dispatch happens in C#, and a lock held across interop calls would block the *same* tab's own later
writes (a second `locks.request` for the same name waits), so the app could not settle the operation it is
protecting. A stored reservation is implementable, but it needs a richer boundary outcome (removed / absent /
in-flight) so that a refused discard is not reported as "the browser kept it" — the same misreport fixed
above — and it would refuse the member's deliberate discard for the length of the window, including when the
reserving tab has already died, which is exactly the case that discard exists for. Against that, the
untoward outcome it prevents is bounded: the member's explicit discard happens; the server keeps its
receipt; a later dispatch of the same bytes is refused as a duplicate naming the existing player; and the
directory refresh shows it. With the byte-exact write identity the previous round hardened, this record's
judgement is that the honest report is the right in-scope fix, and the reservation belongs in a tracked
follow-up rather than in a merge-ready boundary.

### Confirming evidence (Copilot twenty-first pass)

Tested revision: `8d708f6b`.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3854 total, 3854 passed, 0 failed, 0 skipped** (3852 before; the two new cases are the delta). |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Negative check | With the new resolution removed but the ownership guard kept (a scoped revert, because an unrestricted one hangs the pre-existing identity-refresh cases — see below) — the revert **build re-verified as successful (0 warnings, 0 errors) before the run** — **both new cases fail** (**2 failed, 0 passed**). Restored, rebuilt and re-run green before the suites below. |
| Hang incident, recorded because it cost real time and would again | The first version ran the follow-up read even when the continuation had already lost its page. The identity-refresh cases deliberately hold the replacement page's read gate, so `PlayersIgnoresAReleaseThatFinishedAfterTheIdentityRefreshedAsync` and `PlayersDoesNotCloseTheGuardWhenTheCommitSettlesAfterAnIdentityRefreshAsync` awaited a gate nothing releases, and this runner has no per-test timeout: two full-suite runs and a class-level run **hung** instead of failing. The fix is the ownership rule this PR already applies everywhere else — a stale continuation does not spend another boundary call — and the identity-refresh case now also asserts that its read count is unchanged, so the rule is pinned by an assertion rather than by a hang. A leftover test host from a stopped run then locked `Nova.UI.dll` (`MSB3027`, "being used by another process") and had to be stopped by PID before the rebuild could succeed. |
| Stale-assembly trap, recorded | The first attempt at the negative check removed the resolution outright, leaving `RetainedRecordIsGoneAsync` unused: the build failed on `S1144` while the `--no-build` run reported **2 passed** against the previous, fixed assembly. This is the trap the record names from the tenth, thirteenth, fourteenth and sixteenth passes; the build line was read before the result was used, the revert was rewritten in an analyzer-clean form, and only that run is cited as evidence. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — **23 total, 22 passed, 0 failed, 1 skipped** (the pre-existing env-gated capture), with the tracked load-sensitive journeys passing in this run. |
| Full browser suite | Two runs on this revision. The first reported **230 total, 219 passed, 1 failed, 10 skipped** — again `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync`, the tracked load-sensitive journey (now filed as #286), which passed the affected selection minutes earlier and passes in isolation on these revisions. The retry was clean — **230 total, 220 passed, 0 failed, 10 skipped** — satisfying the before-merge row for the final inputs on `8d708f6b` (the ten skips are the pre-existing env-gated captures). |

## GitHub Copilot code review, twenty-second pass (PR #285, on `139b140a`, fixed in `087a0141`)

Copilot's review body (no inline thread — the overview reports "Findings: None" while listing this one as
*previously missed*, medium severity) raised one finding: **"False `ClearAsync` result leaves set-aside
request unresolved"**, at `Nova.UI/Features/Players/Pages/Players.razor.Intake.cs:518`, with the note that it
"also appears on line 599 of the same file" — the set-aside's removal and the release retry's clear (line
`627` in this revision; the reviewed snapshot's numbering differs by the lines the twenty-first pass added).

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | Two owner-scoped cleanup paths treat an already-removed recovery record as a storage failure: a removal that returns `false` leaves the set-aside unresolved with `_pendingCreate`/`Unresolved` (or the unreadable value) on screen and reports that the browser kept the request, and it leaves the release retry's `_unreleasedOperationId` set with its notice and **Add another** disabled — "a retry that can never succeed" in both cases. The suggested remedy: re-read owner storage on a false clear, where empty means the removal's effect has been reached and a still-present record stays blocked (`Players.razor.Intake.cs:518` and `:599`, **review body**) | **Fixed at both sites, and the shared rule generalized rather than copied.** Both paths now go through `RemovalSucceededOrTheRecordIsGoneAsync`, which the release paths already used: a removal that happened proves its own effect, and one that found nothing is resolved by reading the owner's storage, guarded by the page's identity version so a re-scoped continuation spends no boundary call. `RetainedRecordIsGoneAsync` now also takes *which* record the removal settles, because one record per owner is the module's own invariant (`writePending` refuses a second until the first is set aside). |

**The class, swept across the diff.** A removal result is consumed in exactly four places, and all four now
resolve the same ambiguity instead of inferring storage state from a boolean:

| Removal site | Result consumer | Resolution |
| --- | --- | --- |
| `ClearRetainedAsync` (board or interop) | `SettleCommittedAsync` (receipt), `ReleaseRetainedAsync` (receipt-backed refusal) | `ReleasedOrAlreadyGoneAsync` → the shared rule with the settled operation's id. |
| `SetAsideRetainedAsync` — unreadable value | `ApplySetAsideOutcomeAsync` | the shared rule with `null`: any record still retained keeps the deliberate set-aside blocked, so the board cannot report a set-aside over bytes that are still there. |
| `SetAsideRetainedAsync` — pending command | `ApplySetAsideOutcomeAsync` | the shared rule with `null`, for the same reason: the removal settles the record the member was shown. |
| `RetryStorageAsync` — release retry | `_unreleasedOperationId` and `_storageUnavailable` | `ReleasedOrAlreadyGoneAsync` with that record's id, so the retry cannot be stranded. |

No other consumer exists: the board's `ClearAsync`/`DiscardUnreadableAsync` wrappers pass the boundary result
through untouched, `PlayerCreationRecoveryStore` and `PlayerIntakeBoard.razor.js` *are* the boundary
(`clearPending`/`discardInvalidPending` return `false` both for a refusal and for nothing-to-remove — the
ambiguity being resolved here, not a defect to change there), and no server-side path removes a recovery
record.

**Why the retry needs the operation id and the set-aside does not.** Both reads answer `Empty` identically, so
that case is what the finding named. They differ when the read answers `Pending`: the retry asks whether *the
record this receipt settled* is still there, and a record naming another operation proves it is gone (storage
holds one record per owner), whereas the set-aside asks whether the member's decision about the record they
were shown has been carried out, which a *different* record does not establish. Passing the id in the retry
removes a permanent strand — without it, `clearPending` can never match a replaced record, so the notice and
the disabled **Add another** would be unrecoverable without a reload. Passing `null` in the set-aside keeps
the honest answer: the retry read adopts whatever is retained and puts it in front of the member to resolve.
Unreadable bytes stay blocking in both, because they cannot be attributed to an operation.

### Confirming evidence (Copilot twenty-second pass)

Tested revision: `087a0141`.

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3857 total, 3857 passed, 0 failed, 0 skipped** (3854 before; the three new cases are the delta). |
| Negative check | Both call sites reverted to the raw boundary calls (keeping the shared rule, which the release paths still use, so the revert stayed analyzer-clean): the **revert build re-verified as successful (0 warnings, 0 errors) before the run**, and the suite then reported **3 failed, 3854 passed, 0 skipped** — exactly `PlayersCompletesTheSetAsideWhenTheRetainedRecordWasAlreadyGoneAsync`, `PlayersReleasesTheReceiptRetryWhenTheRetainedRecordWasAlreadyGoneAsync` and `PlayersReportsTheReleaseWhenAnotherOperationReplacedTheRetainedRecordAsync`, each on its awaited assertion. Restored, rebuilt and re-run green before the suites below. |
| Stale-assembly trap, a **new flavor**, recorded | Restoring the file with `Copy-Item` preserved the *backup's* timestamp, so the restored source was older than the `Nova.UI.dll` the revert had produced: `dotnet build Nova.slnx` finished in **4.9 s** without compiling, and the suite reported the three new cases failing "again" — against the reverted assembly. Comparing timestamps (dll `23:31:28`, restored source `23:28:11`) identified it, touching the file forced a real 1 m 35 s build (dll `23:36:46`), and the run then passed **3857/3857**. The tell is the one the tenth, thirteenth, fourteenth, sixteenth and twenty-first passes recorded: read what the build actually did, not what it printed. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Full browser suite | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` — **230 total, 220 passed, 0 failed, 10 skipped**, clean on the **first** run of this revision: the first first-try-clean full pass of this merge loop, with the suite composition unchanged from the twenty-first pass (the ten skips are the pre-existing env-gated captures). |
| Affected browser coverage | Covered by that full pass — `PlayerFormBrowserTests` and `PlayersDirectoryBrowserTests` are inside it. The selection command remains `--filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'`; a mid-pattern wildcard selects nothing while still reporting success, which is now stated in the browser-suite reference rather than left for the next run to rediscover. |

## GitHub Copilot code review, twenty-third pass (PR #285, on `dcf809d6`, fixed in `a1b681ac`)

Copilot's review body carried three findings, all in code that had not changed since the last review (no
inline threads, so none to resolve). One is fixed, one narrowed to the part that holds, and one is answered
from measurements that predate it.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | While the set-aside confirmation is open, `RecoveryState == Unresolved` and `CanReplay` still enable the form's replay submit button behind the dialog, so "the member can start a replay instead of completing the set-aside decision, creating competing operations against the same retained record" (`PlayerIntakeBoard.razor.cs:259`) | **Fixed.** One predicate — `OffersReplay` (`CanReplay && !_setAsidePending`) — now states when a replay is offered, and both the commit control and its label use it, so the replay is unavailable exactly while its own set-aside decision owns the retained command and the label stops naming an action that is not offered. The premise about competing operations is not exact — a replay re-sends the *same* operation identity, which the server settles from that operation's receipt rather than creating a second player — but the interaction is real: the decision in front of the member is about the command the enabled button would settle. The board already treats an open set-aside decision as outstanding work in `HasNothingUnsaved`, so this makes the two predicates agree. |
| 2 | The departure guard leaves Back/Forward unguarded, so typed details can be discarded without a confirmation panel; the finding asks to handle history traversal at the page-owned navigation boundary "as the existing evaluation guard does" (`PlayerIntakeBoard.razor.js:161`, third raise) | **Deferred with its measurements, not re-litigated.** Review round 11 built and drove both mechanisms in the real browser before reverting them: a module-level `popstate` guard that restores the board's entry recorded **0 handler runs** for the traversal while a synthetic `popstate` proved the listener live (the router disposes the board before `popstate` is delivered), and a page-level `NavigationLock` with `PreventNavigation()` did not prevent it either — which is why the module states the guard's contract (document upload and same-origin link departure) instead of implying wider coverage. The evaluation surface's pattern needs an owner that outlives the traversal; this board's owner does not, so closing the gap is its own feature, and the current narrowing is pinned by `PlayerFormHistoryTraversalIsTheDocumentedUnguardedDepartureAsync` rather than only described. Recorded as *Limitations* below and carried in the PR comment; it should have its own tracked issue. |
| 3 | The creation completion is guarded only by `_identityVersion`, not by the route visit that dispatched it, so leaving `/players/new` and re-entering before the response resolves "can replace the new visit with the old receipt/frozen command"; the finding asks for a captured visit generation and a regression (`Players.razor.Intake.cs:223`) | **The reachable defect is fixed with the operation as the ownership signal; the suggested visit generation would break a pinned behavior.** Guarding on the visit alone fails three cases of `ReopenedPendingCreationReceivesItsOwnCompletionAsync` (the suite caught exactly that during this round, see below), because a member who left and came back reads the same retained record: that board *is* about the same operation, and settling it in place ("the board becomes the receipt in place; it does not navigate away") is the behavior those rounds pinned and the better experience. What does not hold is publishing the outcome into a board that *resolved* the addition instead — set aside, so the exact command is no longer retained, or replaced by another command. The guard is therefore `_pendingCreate?.OperationId != command.OperationId \|\| !_showCreateForm`: the outcome reaches a board that still holds the operation it answers, and a board that moved on is left exactly as its own state left it (with the directory still refreshed, which is where the member was told to look). |

**First attempt, recorded because the suite caught it and it is the round's real lesson.** The first version
of finding 3's fix did exactly what the finding asked — it captured `_routeVersion` before the dispatch and
demanded it after — and the full unit suite failed **three cases of
`ReopenedPendingCreationReceivesItsOwnCompletionAsync`** (3856 of 3859 passing). Those cases exist precisely
to pin the opposite behavior for a member who leaves and comes back: the reopened board reads the same
retained command, so the completion settles the operation on display instead of being discarded. The fix was
rewritten to test ownership of the *operation* rather than of the *visit*, which keeps that behavior and
closes the case the finding describes. Nothing in the old behavior loses work: while a dispatch is in flight
the board locks both the fields and the commit control (`IsEditable` and `CanCommit` require `!IsSubmitting`),
so no input can be retyped into a board that a stale outcome could then overwrite.

### Confirming evidence (Copilot twenty-third pass)

Tested revision: `a1b681ac` (the record's own commit follows it).

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3859 total, 3859 passed, 0 failed, 0 skipped** (3857 before; the two new cases are the delta). |
| Negative check | Both fixes reverted in place (`CanCommit` back to `CanReplay`, the ownership clause removed from the outcome guard), the **revert build re-verified as successful (0 warnings, 0 errors) before the run**: **2 failed, 3857 passed** — exactly `PlayersKeepsTheReplayUnavailableWhileTheSetAsideDecisionIsOpenAsync` (`#intake-submit` disabled should be true, was false) and `PlayersIgnoresACreationResultThatLandsAfterTheAdditionWasSetAsideAsync` (`#intake-receipt-heading` count should be 0, was 1). Fixes restored with `edit` — not a file copy, because a `Copy-Item` restore preserves the source timestamp and MSBuild then skips the recompile (the twenty-second pass's trap) — rebuilt and re-run green before the suites below. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | `--filter-class '*PlayersDirectoryBrowserTests' --filter-class '*CampaignEvaluationCaptureBrowserTests'` — **26 total, 26 passed, 0 failed, 0 skipped**, including both journeys that failed in the full runs below. |
| Full browser suite | **Not clean on this revision after three attempts, and recorded as pending rather than reported as a pass.** Attempt 1: 230 total, 219 passed, **1 failed** (`DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync` — the "Return to draft" link absent while the page showed its read-failure states). Attempt 2: 230 total, 219 passed, **1 failed** (`CampaignEvaluationCaptureBrowserTests.ModifiedPlayerClickOpensNewTabWithoutChangingOriginalDraftAsync` — `#1 ` text not present; a campaign surface this diff never touches). Attempt 3: 230 total, 218 passed, **2 failed** (`OrdinaryMemberCreatesEditsArchivesAndRestoresThroughRoutedFormAsync` — `#player-first-name` not attached within the locator's 5 s; and `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync` again — a stale return-context href). Every one of those journeys is in #286's tracked load-sensitive set or on a surface outside this diff, each passed in the selection run above on this same revision, and the player journey that failed in isolation (`DirectoryRecordAndForm…`, stale href at `PlayersDirectoryBrowserTests.cs:205`) **passed on its isolation retry**. No competing Aspire suite was running during these runs (checked: no `Nova` process, no Postgres/Azurite container, only 12 unrelated MCP-server `dotnet` processes), so today's 3/3 failure rate against the twenty-second pass's first-try-clean run is an observation for #286, not evidence about this diff: the failures are non-deterministic, span three different journeys, and leave this change's code paths untouched. |

## GitHub Copilot code review, twenty-fourth pass (PR #285, on `cbf3a348`, fixed in `236d65e6`)

Copilot raised **two inline findings** — the first inline threads since the twenty-second pass. Both were
fixed, replied to and resolved.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | The detail page's ownership check captures only `_clubScopeVersion`, which does not change across a 7 → 21 → 7 route excursion, so the first player-7 read can pass both `version` and `playerId == PlayerId` after the return and publish stale work into the new visit; "track a monotonically increasing route/player generation and require it for every route-bound read and mutation completion" (`PlayerDetail.razor.cs:295`, **inline**) | **Fixed as prescribed.** A new `_playerVisitVersion` is taken at every rebind to another player (the only place the routed player changes — a *return* is a new visit, which an id comparison cannot express), and all three route-bound continuations — `LoadDetailAsync`, `ConfirmArchiveAsync`, `RestorePlayerAsync` — capture it and require it alongside the existing club-scope and routed-id checks. The id check is kept: it answers "is this still the routed player" even before the rebind runs, while the generation answers "is this still the visit I started in". The bump is per visit rather than per load (as `TeamDetail`'s `_loadDetailVersion` is) because a same-visit reload must not invalidate an in-flight mutation's outcome: dropping an archive's success would leave its confirmation panel open over an already-archived player, since the outcome is what closes it. |
| 2 | The set-aside entry point stays available while `CreatePlayerAsync` is awaiting the server — `IsSubmitting` is not checked on it — so a member can remove the operation's only recovery copy mid-flight, leaving a committed response with no receipt to show and an unknown one with nothing to replay after a reload; "disable the set-aside entry point while a submission is active (and cover the in-flight interleaving)" (`PlayerIntakeBoard.razor:132`, **inline**) | **Fixed, and the twenty-third pass's related clause is withdrawn with it.** One predicate — `CanResolveRetained => !IsSubmitting` — now gates all three actions that resolve the retained record (the unreadable discard at `:88`, and the unresolved and expired set-aside buttons at `:132`/`:150`, the siblings the finding's class covers), so a submission in flight owns its recovery copy until the answer arrives. Reloading remains the way out of a submission that never answers, because the record outlives the page. Because that interleaving is now impossible, `ApplyCreationOutcomeAsync`'s `_pendingCreate?.OperationId != command.OperationId` clause is removed: it only existed to withhold an outcome from a board that had resolved the operation, and publishing whenever the form shows is what keeps a committed receipt *visible* instead of dropping it — the goal this finding states. `PlayersKeepsTheRetainedAdditionUnresolvableWhileTheSubmissionIsInFlightAsync` replaces the withdrawn case and pins the disabled state plus the settlement that follows with the copy intact. |

**Incidents, recorded because each cost real time and two of them are the record's own traps.** (a) The
first attempt at finding 1's negative check reverted the guard but left the captured local in place, so the
build failed on `S1481` while `--no-build` reported a meaningless **3860 passed** against the previously
built assembly — the stale-assembly trap in its seventh appearance, and reading the build line is what
caught it; the revert was rewritten analyzer-clean (local removed) and only the run whose build is quoted as
successful is cited. (b) The affected-selection command used a mid-pattern wildcard
(`--filter-class '*Player*BrowserTests'`), which selected **zero tests while reporting success** — the trap
the twenty-third pass added to the browser-suite reference precisely so it would not be rediscovered; the
suffix form from that reference ran 23 tests. (c) Two edits to the board markup dropped neighbouring lines
without intending to (a four-line reset block in `CreatePlayerAsync`, and the unresolved card's closing tags
plus the `else if` branch); both were caught by reading the diff before building and repaired, and the final
diff is reviewed hunk by hunk.

### Confirming evidence (Copilot twenty-fourth pass)

Tested revision: `236d65e6` (the record's own commit follows it).

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3860 total, 3860 passed, 0 failed, 0 skipped** (3859 before: the two new cases replace the twenty-third pass's withdrawn case). |
| Negative check | Both fixes reverted in place, the **revert build re-verified as successful (0 warnings, 0 errors) before the run**: **2 failed, 3858 passed** — exactly `PlayerDetailIgnoresAReadForAPlayerWhoseRouteWasRevisitedAsync` (`cut.Markup` still contained the stale payload) and `PlayersKeepsTheRetainedAdditionUnresolvableWhileTheSubmissionIsInFlightAsync` (`#intake-unresolved button` was not disabled). Fixes restored with `edit`, rebuilt, and re-run green. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — **23 total, 22 passed, 0 failed, 1 skipped** (the pre-existing env-gated capture), covering the detail-page journeys this pass changed as well as the form journeys. |
| Full browser suite | **Still pending: two attempts, 1 failed (219/230) and 2 failed (218/230).** Attempt 1 failed `CampaignClosedRecordBrowserTests.DirectParticipantLinkFocusesHistoryOnInitialAttachmentAndReloadAsync`; attempt 2 failed that journey again plus `PlayersDirectoryBrowserTests.DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync`. The campaign journey is entirely outside this diff — it seeds a closed campaign, drives `/campaigns/{id}?tab=close&closeParticipant=…`, asserts document **focus** on `#closed-history-heading` and reloads as WebAssembly, so no changed file is in its path — and its signature ("locator expected to be focused") is the environment-sensitive kind; the directory journey is the same tracked one from the twenty-second and twenty-third passes. Across the two ticks that makes **five full runs with 1–2 failures each**, every failing journey passing in another run on the same revision and the affected selection clean both times. The gate's before-merge row therefore stays unsatisfied and the observation stays for #286. |

## GitHub Copilot code review, twenty-fifth pass (PR #285, on `4056c687`, fixed in `01b398df`)

Copilot's review body lists the twenty-fourth pass's two inline threads as resolved and carries one new
finding, raised as *previously missed* (no inline thread, so none to resolve). Its summary line names both
sites: "fresh operations and successful set-aside can retain stale duplicate state, mislabeling recovery and
suppressing replay for the current operation".

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | When a duplicate rejection has released its retained command, `_creationDuplicate` stays populated while the corrected form starts a new operation; if that new request is unresolved, the board still renders the old duplicate panel and `CanReplay` stays suppressed by the stale value, so the recovery UI describes the previous input and offers no replay for the new operation. "Clear the duplicate state when allocating a fresh command, before retention/dispatch"; the finding notes it "also appears on line 599", which is the set-aside completion (`Players.razor.Intake.cs:198` and `:599` in the reviewed snapshot, **review body**) | **Both sites clear it now, and the reachable one is pinned.** `CreatePlayerAsync` clears `_creationDuplicate` with the rest of a submission's published state, so a new operation's outcome — receipt, refusal or unresolved — is its own; `ApplySetAsideOutcomeAsync` clears it with the other retained state, so a completed set-aside leaves no panel for an operation the board no longer keeps. The first site is the reachable one: a submission is the only path that can put the board back into `Unresolved` while the panel stands, and it is proven non-vacuous below. The second is parity rather than a reproduced defect — behind the first fix, starting a submission already clears the panel, and only a receipt-backed refusal can set it again, which releases the record and leaves no set-aside card to reach — so it is recorded as such instead of claimed as an observed bug. |

### Confirming evidence (Copilot twenty-fifth pass)

Tested revision: `01b398df` (the record's own commit follows it).

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3861 total, 3861 passed, 0 failed, 0 skipped** (3860 before; the new case is the delta). |
| Negative check | The fresh-command clear reverted in place, the **revert build re-verified as successful (0 warnings, 0 errors) before the run**: **1 failed, 3860 passed** — exactly `PlayersStartsANewOperationWithoutThePreviousDuplicatePanelAsync`, on `cut.FindAll("#intake-duplicate").Count` still being 1 after the corrected form's new operation went unresolved. Fix restored with `edit`, rebuilt, and re-run green. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | **Two attempts, neither clean: 21/23 then 20/23.** Attempt 1 failed `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync` (its `"Page 2 of 4"` paging assertion); attempt 2 failed that journey again plus `OrdinaryMemberCreatesEditsArchivesAndRestoresThroughRoutedFormAsync`. Both **pass alone on this revision** (`--filter-method`, 1/1 each), and the first is unchanged from `origin/main` apart from five lines this PR spends on a create-flow helper's assertions — the failing paging text is a **count-derived** value, and each run's database is empty at the start (the fixture's `RemoveDataVolumes` strips the AppHost's persistent mounts), so what it counts is what the tests that ran before it seeded into the shared AppHost. This pass therefore sharpens the twenty-third/twenty-fourth passes' diagnosis: the failure is the suite's shared-run data and ordering, not load alone, and not behaviour this diff changed. |
| Full browser suite | Not attempted this pass: with the selection itself failing on pre-existing count-derived assertions, a full run could not produce the row the gate wants, and the honest record is the diagnosis above plus the retry history already recorded. The before-merge row stays pending (see *Limitations*). |
| Capacity check | `docker system df` and `docker ps -a`: no test container is running, the two exited ones date from three weeks ago, there are 20 local volumes (607 MB reclaimable) and 635 GB free on `C:` — so the degradation is not exhausted capacity, and the fixture does not reuse Postgres data between runs by design. |

## GitHub Copilot code review, twenty-sixth pass (PR #285, on `8d4caa6d`, fixed in `3926096e`)

Copilot's review body carries one finding, raised as *previously missed* (no inline thread, so none to
resolve).

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | When `Duplicate` is non-null, `PlayerCreationProblems.IsNotCommitted` gives receipt-backed proof that the operation was refused, so the acknowledgement that asks the member to affirm "the earlier result stays unknown" contradicts the settled outcome; render duplicate-specific acknowledgement text (`PlayerIntakeBoard.razor:176`, **review body**) | **Fixed at the named site and its two siblings in the same claim.** The dialog's heading, its acknowledgement label and the status line a completed set-aside leaves all now name the refusal when a receipt-backed one is what settled the operation, and keep today's unknown-result wording otherwise. The status wording is a pure `internal static` helper (`Players.RetainedSetAsideStatus`) so it is pinned directly rather than through an arrangement no longer reachable. |

**Reachability, measured rather than assumed.** The branch describes `RecoveryState == Unresolved` (or
`Expired`) while `Duplicate != null`, and the twenty-fifth pass's fix removed the state that used to produce
it: a receipt-backed refusal releases the retained record and leaves `_recoveryState = None`, the only path
that could return the board to `Unresolved` in the same visit is a submission (which now clears the panel),
a route departure clears it too, and a same-owner refresh preserves the intake state instead of re-reading
it. The first attempt at this pass's tests tried to reach it exactly that way — refusal, another same-owner
tab's retained command, then a refresh — and the awaited card never appeared (two 44-second waits,
recorded). The copy is therefore pinned where the state can be reached: two component tests render the board
with and without `Duplicate` and assert the wording each state gets, plus a pure test of the status wording.
The branch stays: it describes a state the board still models, and if any future path returns to it, its copy
is now right.

### Confirming evidence (Copilot twenty-sixth pass)

Tested revision: `3926096e` (the record's own commit follows it).

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3864 total, 3864 passed, 0 failed, 0 skipped** (3861 before; three cases added, the two unarrangeable app-level ones removed). |
| Negative check | The two board strings reverted to the unconditional unknown wording and the status helper's branches swapped (an analyzer-clean revert, since the first attempt's shape would have left an unused local), the **revert build re-verified as successful (0 warnings, 0 errors) before the run**: **2 failed, 3862 passed** — exactly `PlayerIntakeBoardNamesTheRefusalInTheSetAsideDialog` and `PlayersNamesTheSetAsideResultForTheOutcomeItSettled`; the control case that an unsettled addition keeps the unknown wording still passed, which is what it asserts. Fixes restored with `edit`, rebuilt, and re-run green. |
| Format | `dotnet format Nova.slnx --verify-no-changes` first reported `IMPORTS: Fix imports ordering` for the alias this pass added; `dotnet format Nova.slnx` applied it and the verify then reported **exit 0**, with only the three edited files in the diff. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — **23 total, 22 passed, 0 failed, 1 skipped** (the pre-existing env-gated capture), including `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync`, which had failed in both of the previous pass's selections. |
| Full browser suite | One attempt: **230 total, 219 passed, 1 failed, 10 skipped** — the same count-derived journey as the previous two passes (it failed minutes earlier in the same revision's selection). The before-merge row stays pending; see *Limitations*. |
| Build/test incidents, recorded | The first two tests written for this pass never reached their state (above) and were replaced by the boundary tests; the component test then failed to compile on the board's `PlayerCreationRecoveryState` (declared in the component namespace, which the test file aliases only for the board), fixed with a file alias; and that alias tripped the import-ordering rule. All three were caught by reading the build and format output before trusting any result. |

## GitHub Copilot code review, twenty-seventh pass (PR #285, on `a24027ab`, fixed in `3b209ef0`)

Copilot raised **two inline findings**, both about the uncommitted-departure guard, and both were fixed,
replied to and resolved.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | The board's Cancel button bypasses the departure guard: the module intercepts only anchor clicks while `OnCancel` navigates immediately, so a member who typed create/edit values loses them to one click instead of meeting the board's confirmation, contrary to the uncommitted-departure contract; route Cancel through the confirmation and cover typed input plus Cancel in the browser suite (`PlayerIntakeBoard.razor:376`, **inline**) | **Fixed as prescribed.** Cancel now asks when the board holds input that would be lost (`_dirty`) and opens the same panel the guard uses, with the same focus and the same two answers; with nothing typed it leaves immediately, exactly as before, so the empty-form case does not acquire a pointless prompt. The browser journey that pins the guard now also types, clicks Cancel, and asserts the panel appears and `Keep editing` preserves the value. |
| 2 | The module skips the guard for every anchor inside the board whenever it is dirty, so in the duplicate state `View existing player` discards corrected values without a prompt and `Review players` does the same while the set-aside decision is open; only clean receipt/frozen links are safe, and those are already `dirty === false` — do not bypass the guard for all in-board anchors (`PlayerIntakeBoard.razor.js:174`, **inline**) | **Fixed as prescribed.** The exemption is gone: every same-origin link a dirty board holds is asked about, the board's own included, and the safe cases stay silent because the board syncs `dirty` to "nothing unsaved" for its receipt, frozen and unreadable states. The module probe in the browser suite now attaches a guard over a probe root, marks it dirty, dispatches a click on an *in-board* anchor and asserts the click was prevented and the board was asked for `/players?view=archived`. |

**The behavior change it implies, and the journeys that encoded the old one.** Making in-board links and
Cancel honest with the panel means the confirmed departure *does* discard the draft, and three existing
cases had encoded the old silent paths: the browser journey that cancels out of a duplicate state (its helper
now confirms the panel), the journey that inspects the existing player from the duplicate board (it now
confirms, because that link departs from a board holding the refused input) and the journey that corrects
after cancelling (it re-enters the details, since the confirmed discard took them, which is what the panel's
copy promises). The unit case that closes a rejected form with Cancel needed the same adaptation, plus a wait
for the submit control's readiness: it had been relying on the *old* cancel path leaving `_createForm`
populated, and on a withheld board's disabled submit not mattering — a latent race the flow change exposed
rather than caused. The board's own behavior needed no change there: a disabled submit ignores clicks in a
real browser, and the board withholds input until its recovery read answers by design.

### Confirming evidence (Copilot twenty-seventh pass)

Tested revision: `3b209ef0` (the record's own commit follows it).

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3866 total, 3866 passed, 0 failed, 0 skipped** (3864 before; two cases added for Cancel, one adapted). |
| Negative check | `CancelFormAsync` reverted to invoking the callback directly (an analyzer-clean revert), the **revert build re-verified as successful (0 warnings, 0 errors) before the run**: **2 failed, 3864 passed** — `PlayersAsksBeforeCancelDiscardsTypedInputAsync` and the adapted `PlayersClearsDuplicateWhenCancelledFormReopensAsync`, both waiting for a panel that never opens; the control case (Cancel with nothing typed leaves at once) still passed, which is what it asserts. Fix restored with `edit`, rebuilt, and re-run green. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | **Two attempts, each 21/23 with one failure, and the failures differ**: the first failed `OrdinaryMemberCreatesEditsArchivesAndRestoresThroughRoutedFormAsync`, the retry failed `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync` — both from #286's tracked set, and neither in this pass's changed paths (that journey's Cancel click happens on a board with nothing typed, and its other departures are receipt links, which stay silent because `dirty` is false). Everything this pass changed passed in **both** runs: the module probe with its in-board-anchor assertion, the extended departure journey, and both duplicate journeys that the new in-board prompting could have broken. |
| Full browser suite | Not attempted this pass: with the selection itself alternating between two tracked journeys, and the before-merge row already recorded as pending for four passes, another full run would not change the record. |

## GitHub Copilot code review, twenty-eighth pass (PR #285, on `7755f6fd`, fixed in `3a2a7a11`)

Copilot's review body lists the twenty-seventh pass's two inline threads as resolved and carries one finding,
raised as *previously missed* (no inline thread, so none to resolve).

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | The in-memory boundary does not preserve the lease ownership the production module implements: a late `MarkDirtyAsync` from an old board overwrites `Dirty` even when a newer guard owns the module state, so the async-ownership tests cannot catch stale dirty updates; ignore the update unless the supplied lease matches the active guard, and add a stale-lease assertion (`PlayerIntakeInteropDouble.cs:239`, the assignment at `:243`, **review body**) | **Fixed as prescribed.** The double's `MarkDirtyAsync` now writes only for the lease that owns the attached guard, which is the module's own rule (`activeGuard?.lease !== lease` ⇔ not attached, or a different lease), so a superseded mounting's completion and a detached guard's late update are both ignored. `MarkDirtyAsyncIgnoresALeaseThatDoesNotOwnTheGuardAsync` pins the three cases — the active lease writes, a stale lease does not, and neither does the detached one — in the double's contract test file, which is where its mirroring of the module is already asserted for bytes and identities. |

### Confirming evidence (Copilot twenty-eighth pass)

Tested revision: `3a2a7a11` (the record's own commit follows it).

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3867 total, 3867 passed, 0 failed, 0 skipped** (3866 before; the contract case is the delta). No existing case relied on the looser double: every page-side sync passes the lease it attached with, so the stricter contract changes no arrangement. |
| Negative check | The lease check reverted to the unconditional assignment (an analyzer-clean revert), the **revert build re-verified as successful (0 warnings, 0 errors) before the run**: **1 failed, 3866 passed** — exactly `MarkDirtyAsyncIgnoresALeaseThatDoesNotOwnTheGuardAsync`, on `interop.Dirty` still being false after the stale update. Fix restored with `edit`, rebuilt, and re-run green. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Integration and browser | **N/A for this push, on the applicability rule**: the change is the unit suite's in-memory boundary and its contract test, so no application input, browser-suite input, dependency, build/runtime configuration, discovery, or generated asset changed. The before-merge rows stand on the code revision `3b209ef0` — integration **678/678** on that revision, and the browser row as *Limitations* records it — and this push changes neither of their inputs. |

## GitHub Copilot code review, twenty-ninth pass (PR #285, on `43ea3d06`, fixed in `2773c955`)

Copilot's review body carries three findings, raised as *previously missed* (no inline threads, so none to
resolve). Two are behavior, one is a test name.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | When a duplicate is definitively refused but the release fails, the parent leaves `RecoveryState=Unresolved`, `Duplicate` non-null and `StorageUnavailable=true`, and the storage panel then says the addition "could not be checked" and that an earlier addition cannot be found — even though the server already proved the refusal and only browser cleanup is outstanding (`PlayerIntakeBoard.razor:210`) | **Fixed.** The panel has a third branch for that state: the refused addition's request was not released, the refusal above stands and the player it matched is untouched, the retained request can come back as unresolved, and setting the refused addition aside releases it. The controlled test now asserts that copy and that the "could not be checked" text is absent. |
| 2 | Server field errors are only copied into `FieldErrors` and rendered; they are never cleared when a field changes, so a correction leaves the old message and `is-invalid` until another valid submit, and a locally invalid correction cannot reach that callback (`PlayerIntakeBoard.razor.cs:125`) | **Fixed with the pruning the finding asks for, kept in the board's own channel rather than moved into a `ValidationMessageStore`.** The board adopts each `FieldErrors` answer into a pruned mirror and drops the edited field's messages on `OnFieldChanged` (the same event the framework uses), so the message, the class and the description go with the correction, and the mirror feeds the summary, the per-field regions and `FieldHasError` alike. A store was considered and declined: `EditForm`'s submit runs `Validate()`, which clears the store and recomputes the model's own messages, so server messages placed there would be wiped on every resubmit and re-added from the parameter anyway — the mirror has one owner and no second lifecycle. The create-path case pins it (`PlayersDropsAServerFieldMessageWhenItsFieldIsCorrectedAsync`); the edit path shares the same board and event. |
| 3 | `DoesNotReturnAnotherClubsActiveCampaignAsync` arranges a club whose campaigns are all non-Active and never requests another club, so its name claims isolation that the theory below covers; rename it for the no-active-campaign case or change the arrangement (`PlayerIntakeContextServiceTests.cs:73`) | **Renamed to what it asserts** — `DoesNotSubstituteAnotherClubsCampaignForAClubWithoutOneAsync` — because the arrangement is exactly that: the club has no active campaign while another club does, and the context reports none of the other's. The isolation case stays where it is covered. |

### Confirming evidence (Copilot twenty-ninth pass)

Tested revision: `2773c955` (the record's own commit follows it).

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. Two analyzer errors were hit and fixed on the way: `MA0016` (the mirror was first exposed as a concrete `Dictionary`, now a private field behind an `IReadOnlyDictionary` property) and `MA0002` (the new test asserted through `ClassList.ShouldContain`, which wants a comparer; it now reads the class attribute as text, as the neighbouring cases do). |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3868 total, 3868 passed, 0 failed, 0 skipped** (3867 before; the pruning case is the delta). |
| Negative check | The pruning removed from `OnFieldChanged` and the panel's refusal branch removed, the **revert build re-verified as successful (0 warnings, 0 errors) before the run**: **2 failed, 3866 passed** — exactly `PlayersDropsAServerFieldMessageWhenItsFieldIsCorrectedAsync` (the message persisted) and `PlayersKeepsARefusedOperationBlockedWhenItsBytesCannotBeReleasedAsync` (the panel still claimed the check failed). Fixes restored with `edit`, rebuilt, and re-run green. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — **23 total, 21 passed, 1 failed, 1 skipped**: the failure is `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync` again, the tracked count-derived journey from #286, and everything this pass touched passed, including the journeys that submit, correct fields after a refusal and read the storage panel. |
| Full browser suite | Not attempted: the before-merge row is already recorded as pending, and this pass's own selection shows the same tracked journey failing. |

## GitHub Copilot code review, thirtieth pass (PR #285, on `33a8ecd5`, fixed in `906813e5`)

Copilot's review body carries two findings, raised as *previously missed* (no inline threads, so none to
resolve).

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | When receipt settlement cannot release browser storage, the completion exists only in memory; if the member leaves and re-enters while the recovery read is unavailable, the route reset drops `_receipt` but keeps `_unreleasedOperationId`/`_storageUnavailable`, and a successful retry then removes the retained command without any completion left to show — preserve the receipt or suppress that retry (`Players.razor:76`) | **Fixed by preserving it.** The route boundary now keeps a settled receipt while its exact request is unreleased, and drops it once the release succeeds; a member who returns sees the receipt with the enrollment text and the release retry, and releasing it keeps the receipt on screen. Fixing it exposed a second defect worth recording: with the receipt preserved, the board's *heading* chain preferred the frozen branch, so the panel rendered under a "fields below are frozen" heading and note whose fields are not on screen — the receipt now takes precedence there too, matching the body panel's own order. Three cases pin this: the page-level round trip with the read unavailable, the heading precedence with a receipt, and the control that a frozen board without one still names why its fields are frozen. |
| 2 | The in-memory boundary detaches the guard for any lease, while the production module detaches only the guard that owns the lease, so a stale mounting's teardown takes a newer mount's guard in tests but not in the browser (`PlayerIntakeInteropDouble.cs:253`) | **Fixed as prescribed.** The double clears the attachment only when the lease matches the guard it holds, and clears the lease with it, which is the module's own rule. `DetachDepartureGuardAsyncIgnoresALeaseThatDoesNotOwnTheGuardAsync` pins the sequence: attach two leases, detach the stale one, and assert the newer mount still owns the guard and can still write its dirty state. |

### Confirming evidence (Copilot thirtieth pass)

Tested revision: `906813e5` (the record's own commit follows it).

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3872 total, 3872 passed, 0 failed, 0 skipped** (3868 before; four cases added). |
| Negative check | The receipt preservation removed, the heading order restored and the double's detach made unconditional, the **revert build re-verified as successful (0 warnings, 0 errors) before the run**: **3 failed, 3869 passed** — exactly `PlayersKeepsTheUnreleasedReceiptAcrossAVisitAsync`, `PlayerIntakeBoardHeadsASettledReceiptOverItsFrozenState` and `DetachDepartureGuardAsyncIgnoresALeaseThatDoesNotOwnTheGuardAsync`; the control case (a frozen board without a receipt still names why its fields are frozen) kept passing, which is what it asserts. Fixes restored with `edit`, rebuilt, and re-run green. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — **23 total, 22 passed, 0 failed, 1 skipped**, clean, including the tracked count-derived journey that had failed the previous pass's selection. |
| Full browser suite | One attempt: **230 total, 219 passed, 1 failed, 10 skipped** — `CampaignClosedRecordBrowserTests.DirectParticipantLinkFocusesHistoryOnInitialAttachmentAndReloadAsync`, the closed-record **focus** journey whose `/campaigns/{id}?tab=close` assertions and WebAssembly reload share no code with this pass (it failed the same way in the twenty-fourth pass). The before-merge row stays pending; see *Limitations*. |
| Test incidents, recorded | The first arrangement of the receipt case re-entered the form while the recovery read *succeeded*, which landed the retained command and produced the heading-precedence defect above rather than a receipt-panel assertion failure; aligning the case to the finding's own scenario (the read unavailable) and pinning the precedence separately is what made both deterministic. A first attempt at the double's case also swallowed the previous case's closing assertion and brace — caught by the compiler, not by a passing run — and the receipt panel's "View player" link needed `DetailUrlFactory` to render, so the case asserts the panel's own control instead. |

## GitHub Copilot code review, thirty-first pass (PR #285, on `4700c2de`, fixed in `b3ac0d4e`)

Copilot's review body carries four findings, raised as *previously missed* (no inline threads, so none to
resolve).

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | Replacing the edit context when the host supplies a different `Model` leaves `OnFieldChanged`/`OnValidationStateChanged` subscribed on the old context, rooting its model and delegates until disposal — and disposal only unsubscribes the new one (`PlayerIntakeBoard.razor.cs:448`) | **Fixed as prescribed.** The board detaches its handlers from the outgoing context before replacing it, so the replaced model is released and the new context's subscription is the only one. This one is verified by inspection rather than by a case: nothing validates or edits the replaced model in a test, so the defect has no observable behavior to assert — what the fix removes is the rooting itself. |
| 2 | Cancel consults only `_dirty`, but the set-aside acknowledgement lives outside the edit context, so acknowledging it leaves `_dirty` false and Cancel navigates immediately instead of asking, discarding an acknowledged decision (`PlayerIntakeBoard.razor.cs:587`) | **Fixed.** Cancel now treats the acknowledgement as the board's own signal of an uncommitted decision, so it opens the same panel the link path uses; with neither typing nor an acknowledgement it still leaves at once. `PlayersAsksBeforeCancelDiscardsAnAcknowledgedSetAsideDecisionAsync` seeds a retained addition, acknowledges the decision without typing (asserting the module's dirty flag is still clear), and requires the panel — the case the finding asks for. |
| 3 | The shared confirmation dropped the previous hosts' live region and relies on `autofocus` on an `h3`, which is not honoured for content Blazor inserts after the click, so the panel and its blockers may not be announced (`PlayerLifecycleConfirmation.razor:14`) | **Fixed.** The panel is a labelled `alertdialog` — the same semantics the board's own dialogs use — and its heading is focused from the component's first render instead of relying on `autofocus`, which is gone. A live region alone cannot announce an *inserted* panel reliably, so focus is the mechanism, and the markup contract is pinned by asserting the dialog role, the focusable heading and the absence of `autofocus`; what a screen reader then says is not automatable here and is not claimed. |
| 4 | After a receipt, `StartAnotherAdditionAsync` resets the form and rechecks storage but does not re-read the intake context, so another tab's campaign change leaves the next addition stating the old campaign and the wrong pre-commit consequence (`Players.razor.Intake.cs:701`) | **Fixed.** Another addition now re-reads the intake context and arms its checking state, exactly as a route entry does, so the consequence is named as unread until the fresh answer lands. `PlayersReloadsTheEnrollmentConsequenceForAnotherAdditionAsync` changes the campaign between the receipt and the next addition and requires the board to state the new one. |

### Confirming evidence (Copilot thirty-first pass)

Tested revision: `b3ac0d4e` (the record's own commit follows it).

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3875 total, 3875 passed, 0 failed, 0 skipped** (3872 before; three cases added). |
| Negative check | The acknowledgement removed from Cancel's condition, the dialog role and the lifecycle focus reverted (with the heading reference kept, because dropping it left an unassigned field and failed the build — the revert was rewritten analyzer-clean) and the context re-read removed, the **revert build re-verified as successful (0 warnings, 0 errors) before the run**: **3 failed, 3872 passed** — exactly `PlayersAsksBeforeCancelDiscardsAnAcknowledgedSetAsideDecisionAsync`, `PlayerDetailAnnouncesTheArchiveConfirmationAsync` (on `role` being null rather than `alertdialog`) and `PlayersReloadsTheEnrollmentConsequenceForAnotherAdditionAsync`. Fixes restored with `edit`, rebuilt, and re-run green. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | **Two attempts.** The first failed `PlayerFormDuplicateCanBeCorrectedWithoutOverrideAsync` inside the cancel helper: its probe for the departure panel raced the click's own handler, so a prompt that had not arrived yet was read as "no prompt" and the confirmed close never happened — a race the helper has carried since the twenty-seventh pass and which passed in the runs between. The helper now retries the click until either the panel or the directory is on screen, and the second attempt is **23 total, 21 passed, 1 failed, 1 skipped** with only `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync` — the tracked count-derived journey from #286 — failing, and everything this pass touched passing. |
| Full browser suite | Not attempted: the before-merge row is already recorded as pending, and the selection shows the same tracked journey failing. |

## GitHub Copilot code review, thirty-second pass (PR #285, on `7eb9f751`, fixed in `fa499955`)

Copilot raised **one inline finding**, which was fixed, replied to and resolved.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | When the directory reuses the confirmation for a different archive subject, `OnParametersSet` resets the acknowledgement and the heading changes, but `OnAfterRenderAsync` focuses only on `firstRender`, so the member can open another archive action while the panel stays open and focus remains on the underlying row instead of the new confirmation (`PlayerLifecycleConfirmation.razor.cs:29`, **inline**) | **Fixed as prescribed.** Focus follows the subject: the panel focuses its heading whenever the heading describes a subject this mounting has not focused yet (`firstRender || _focusedSubject != PlayerId`), so the second subject's confirmation is announced like the first and a re-render of the same subject does not steal focus back. `PlayerLifecycleConfirmationFocusesEachSubjectsHeadingAsync` renders the panel, asserts one focus invocation, re-renders it for another player, and asserts a second invocation with the new heading — the interop call is the observable the browser would act on. |

### Confirming evidence (Copilot thirty-second pass)

Tested revision: `fa499955` (the record's own commit follows it).

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. The new case first failed to compile because that test file imports the detail page through an alias and nothing from the components namespace; the component now has its own alias beside it. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3876 total, 3876 passed, 0 failed, 0 skipped** (3875 before). |
| Negative check | The focus condition reverted to `firstRender` alone, with the tracking field removed so the revert stayed analyzer-clean, the **revert build re-verified as successful (0 warnings, 0 errors) before the run**: **1 failed, 3875 passed** — exactly `PlayerLifecycleConfirmationFocusesEachSubjectsHeadingAsync`, on the second invocation never being made. Fix restored with `edit`, rebuilt, and re-run green. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — **23 total, 22 passed, 0 failed, 1 skipped**, clean, including the archive flows that render the confirmation. |
| CI incident on the previous head, with its outcome | The required `Build` check failed once on `7eb9f751` with `error S125: Remove this commented out code` at `Nova.Browser.Tests/ClubCrestBrowserTests.cs:67` — a two-line **prose** comment (no code) in a file whose diff against `origin/main` is empty, whose last change was #253, and which had passed CI on the seven preceding runs of this branch, with the same revision green locally (full build, 3875 unit cases). Diagnosed as a flaky analyzer verdict rather than this diff, it was re-run without changing any file, and the re-run **completed successfully** — so the check is green on `7eb9f751` and the flake is recorded rather than papered over. |

## GitHub Copilot code review, thirty-third pass (PR #285, on `ae80aed8`, fixed in `2654a731`)

Copilot raised **two inline findings**, both fixed, replied to and resolved.

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | An attach can be canceled after the JS module installed its guard but before the continuation set `_guardAttached`, and by the time disposal runs `_guardAttach` can already be completed, making both `_guardAttached` and "attach in flight" false — so the module keeps the disposed receiver; track the attempted lease and detach it, since the lease check makes a stale detach safe (`PlayerIntakeBoard.razor.cs:667`, **inline**) | **Fixed as prescribed.** Disposal now releases the lease the mounting **attempted** (`_guardLease is not null`) rather than only the one it saw installed, and the wait for an in-flight attach is unchanged, so an attach that installs and then loses its answer is released as well. Releasing a lease the module never installed is a no-op there, so this cannot take another mounting's guard. The interop double gained the boundary state the real module already had — `FailAttachAfterInstalling`, an attach that installs the guard and then throws — and `PlayersReleasesAGuardInstalledByAnAttachThatLostItsAnswerAsync` disposes in that state and requires the guard to be released. |
| 2 | `_isMutating` is cleared before `ApplyCreationOutcomeAsync`, but a receipt and a definitive refusal still await storage cleanup, so during that await **Add another** (and an unresolved refusal's **Set aside**) are live and a click can start a second recovery operation over the record the first still owns, whose later settlement can then clear the receipt or overwrite the new state (`Players.razor.Intake.cs:230`, **inline**) | **Fixed.** The submission stays busy until the outcome has been applied, so the settlement's cleanup happens inside the operation that owns the record; `Add another` is disabled while submitting, and the page's resolution handlers (`StartAnotherAdditionAsync`, `RetryStorageAsync`, `SetAsideRetainedAsync`) refuse while a settlement is running — the set-aside and discard controls were already gated by `CanResolveRetained`. The gate is pinned at the board boundary (`PlayerIntakeBoardClosesTheReceiptActionsWhileTheSettlementRuns`: disabled while `IsSubmitting` with a receipt, enabled again once it is false), which is the mechanism the page now drives. |

### Confirming evidence (Copilot thirty-third pass)

Tested revision: `2654a731` (the record's own commit follows it).

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3878 total, 3878 passed, 0 failed, 0 skipped** (3876 before). |
| Negative check | The disposal condition reverted to `(_guardAttached \|\| attachWasInFlight)` and `Add another`'s gate dropped, the **revert build re-verified as successful (0 warnings, 0 errors) before the run**: **2 failed, 3876 passed** — exactly `PlayersReleasesAGuardInstalledByAnAttachThatLostItsAnswerAsync` and `PlayerIntakeBoardClosesTheReceiptActionsWhileTheSettlementRuns`. Fixes restored with `edit`, rebuilt, and re-run green. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — **23 total, 21 passed, 1 failed, 1 skipped**, the failure being the tracked count-derived `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync` journey from #286 and everything this pass touched passing. |
| Harness incident, recorded | The first arrangement of the second finding held the clear gate and asserted the mid-settlement screen, which **hung the runner twice** (over ten minutes each) rather than failing: a bUnit click whose handler is still awaiting the settlement does not deliver a render, so `WaitForAssertionAsync` — which waits for renders — never observes the state the assertion describes. The case was replaced by the two board-boundary cases above, and the page-side busy window is recorded as verified by inspection plus the board gate that now drives it. |
| Full browser suite | Not attempted: the before-merge row is already recorded as pending, and the selection shows the same tracked journey failing. |

## GitHub Copilot code review, thirty-fourth pass (PR #285, on `6733af95`, fixed in `b8d3c8d9`)

Copilot's review body lists the thirty-third pass's two inline threads as resolved and carries one finding,
raised as *previously missed* (no inline thread, so none to resolve).

| # | Finding | Disposition |
| --- | --- | --- |
| 1 | `OnFieldChanged` starts `SyncDirtyAsync()` detached, and its catch does not handle `OperationCanceledException` when the component token is cancelled — the filter requires the token *not* to be cancelled — so a slow `MarkDirtyAsync` still running during navigation/disposal faults a task nobody observes instead of being treated as teardown (`PlayerIntakeBoard.razor.cs:724`, **review body**) | **Fixed.** Teardown cancellation is caught and treated as the normal end of a sync whose guard is going away, and the boundary-failure filter no longer depends on the token: a JS/disconnected/disposed failure that raced teardown is the same news as one that happened while the board was live, and failing to record the flag only weakens the warning. The sync is `internal` so the case can call it directly, and the interop double gained the boundary state the real store has — `FailDirtyWithCancellation` (a cancelled write) and a `DirtyAttempts` counter. |

### Confirming evidence (Copilot thirty-fourth pass)

Tested revision: `b8d3c8d9` (the record's own commit follows it).

| Check | Command / result |
| --- | --- |
| Build | `dotnet build Nova.slnx` — **passed, 0 warnings, 0 errors**. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3879 total, 3879 passed, 0 failed, 0 skipped** (3878 before). |
| Negative check, with two incidents recorded | On the reverted catch (an analyzer-clean revert, the **revert build re-verified as successful**), `PlayerIntakeBoardTreatsACancelledDirtySyncAsTeardownAsync` fails with the raw **`System.OperationCanceledException : The dirty-state write was cancelled by teardown`** unwinding out of `SyncDirtyAsync` — the fault the finding describes. The first two attempts at this case were **vacuous and were caught by re-running them against the revert**: the sync returned early because the board had not yet recorded its attach (only the double's flag was set), so the test passed pre-fix; the case now drains one dispatcher hop after the attach's answer and asserts, relatively, that the call reached the boundary (`DirtyAttempts` rises by one) before requiring that it does not throw. Fix restored with `edit`, rebuilt, and re-run green. |
| Format | `dotnet format Nova.slnx --verify-no-changes` — **exit 0**, after one `MA0196` error from an `<inheritdoc />` that my insertion had left sitting on the new counter property rather than on the interop method it documents; the tag is back on the method. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **678 total, 678 passed, 0 failed, 0 skipped**. |
| Affected browser selection | `--filter-class '*PlayerFormBrowserTests' --filter-class '*PlayersDirectoryBrowserTests'` — **23 total, 22 passed, 0 failed, 1 skipped**, clean, including the tracked count-derived journey that has failed most selections. |
| Full browser suite | Not attempted: the before-merge row is already recorded as pending, and the selection is clean on this revision. |

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
| A browser selection expression with a mid-pattern wildcard (`*Player*BrowserTests`) ran **zero tests and reported success** | **Add**, one sentence to the `browser-suite.md` run commands: `--filter-class` matches the class-name suffix and repeats per class, and `total` is read before a selection is treated as evidence. A green run proving nothing is the same class as the stale-assembly trap, and nothing in the guidance warned about it. |

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
- **The last review finding carried from the seventh/eighth passes is closed, and the two open items
  from that pair are now both resolved.** One was the enrollment consequence rendering for the edit
  board, closed by making the consequence the create host's opt-in (twelfth pass,
  `PlayersStatesNoEnrollmentConsequenceOnTheEditBoardAsync`). The other was that a **failed storage
  read** left the board editable, so a later retry could land a retained command over values typed
  meanwhile; the fourteenth pass closed it by keeping `RecoveryChecked` false whenever a read refuses,
  so input is offered only once the owner's retained command has been examined by a read that
  answered (`PlayersKeepsTheBoardWithheldWhenTheRetainedCommandReadFailsAsync` and
  `PlayersKeepsTheBoardWithheldWhenTheRetryReadFailsAgainAsync`). That **supersedes round 2's reopen
  decision deliberately**, and the trade-off is now the record's: with storage refusing both reads and
  writes, the form is unusable until storage answers again — which is honest rather than lenient,
  because a dispatch needs that same storage, and the member keeps the retry that resolves it. The
  rule is uniform across readers (entry, retry, same-owner refresh, **Add another**), so a failed
  same-owner refresh also keeps the board withheld until a read answers.
- The departure guard does not intercept browser Back/Forward, and that narrowing is now **tested
  rather than only documented**: `PlayerFormHistoryTraversalIsTheDocumentedUnguardedDepartureAsync`
  proves the guard is attached and dirty for the same typed value by having the link path prompt for
  it, then pins that the traversal leaves without one. The guard covers document unload and
  same-origin link departure, which is the contract the module's own comment states. Review round 11
  asked again for the `evaluationNavigationGuard.js` rollback/approval pattern, so two
  implementations were built and measured in the real browser before being reverted, and **neither
  can protect this board**: a module-level `popstate` guard is never invoked for the traversal (the
  router handles it and disposes the board first — instrumented: attached, `dirty: true`, `origin`
  current, listener live for a synthetic `popstate`, **0** handler runs for the traversal), and a
  page-level `NavigationLock` with `PreventNavigation()` does not stop it either. The evaluation
  surface's pattern works there because its panel outlives the traversal; this board's owner does
  not, so closing the gap needs the traversal handled where the owner survives it — a feature of its
  own, not a review-round fix. The module names the verified mechanism beside its click listener so a
  reader does not infer wider coverage.
- **The form components own `aria-invalid`, so a server-keyed field error cannot set it.** Blazor's
  `InputBase` renders `aria-invalid` from its own `EditContext` and ignores one supplied to it, which the
  fifteenth pass measured directly: the attribute was absent for a server-keyed error while
  `aria-describedby` rendered, and the framework set it on its own for the form's message. A *server's*
  field error therefore marks its control with the class Bootstrap styles (`is-invalid`) and names the
  message with `aria-describedby`, so the semantic invalid state for that case rides on the described
  message rather than on an attribute the component will not render; the `EditContext`'s own refusals are
  announced by the framework itself. Re-adding `aria-invalid` markup to these controls would be dead code
  that reads as coverage. That class is also the single source of truth the focus contract uses: the board
  moves focus to the first field it marks invalid after a refusal, so the field that is announced, marked
  and focused is decided in one place (`FieldHasError`).
- **The full browser-suite row: outstanding on `0578c671`, satisfied on the final revision `d5e370c5`.** Five
  full runs on `0578c671` were never clean: every failure occurred only inside a full run, each failing
  journey passes in isolation on that revision, each exists on `origin/main`, and the two in this surface are
  the journeys this record has tracked as load-sensitive since the eleventh pass
  (`PlayerFormKeyboardTabAndEnterSubmitsAsync`,
  `OrdinaryMemberCreatesEditsArchivesAndRestoresThroughRoutedFormAsync`) failing with the same
  board-not-yet-ready signature. The repository's own rule names the mechanism (shared Docker capacity across
  worktrees exhausting bounded hydration/storage retries), so those attempts track the machine's concurrent
  load rather than the change. The row is satisfied on the revisions that carry those changes forward: the
  first full run on `d5e370c5` failed only the tracked directory journey and its retry was clean, and
  `4d72bcbd`'s first full run was clean — **230 total, 220 passed, 0 failed, 10 skipped** — with the affected
  selection also clean on both revisions.
- **A same-owner tab's deliberate discard can still race a dispatch in another tab, and that interleaving is
  recorded as a follow-up rather than papered over.** The write boundary cannot hold its Web Lock across the
  C# dispatch (a lock held across interop calls would block the same tab's own later writes), and a stored
  reservation would refuse the member's deliberate set-aside for the length of its window — including when
  the reserving tab has already died, which is exactly the case the discard exists for. What the discard can
  no longer produce is a misreport: a release that finds nothing is now reported as gone (`8d708f6b`,
  twenty-first pass), so no tab claims the browser is holding bytes that are gone, and both consumers are
  pinned by cases. If the reservation is wanted, it needs an explicit in-flight outcome in the boundary plus
  an expiry; the trade-off is stated in that pass's section for a human to weigh.
- **The before-merge full-browser-suite row is still pending after four review rounds, and the refined
  diagnosis is that the suite's *shared-run data* is the cause, not load alone.** Six full runs on the
  twenty-third to twenty-sixth passes' revisions reported 219/230, 219/230, 218/230, 219/230, 218/230 and
  219/230, failing one or two journeys each, and the twenty-fifth pass's affected selections failed 1 and 2
  of 23 — while the twenty-sixth pass's selection was **clean (22/23)** on the same revision whose full run
  then failed the recurring journey, and the twenty-seventh pass's two selections each failed one tracked
  journey (a different one each time) with everything that pass changed passing in both. Every failing journey passes **alone** on the revision that failed it
  (`--filter-method`, verified for `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync`
  and `OrdinaryMemberCreatesEditsArchivesAndRestoresThroughRoutedFormAsync`), and the recurring one fails on a
  **count-derived** assertion (`"Page 2 of 4"`) that pre-exists on `origin/main` — this PR changes that file by
  five lines, in a create-flow helper, none of them the assertion — while each run's database starts empty
  (`RemoveDataVolumes` strips the AppHost's persistent mounts), so the count is whatever the tests that ran
  before it seeded into the shared AppHost. That points at two fixes that are **not** this change's to make:
  count-derived assertions should be derived from what the test seeded rather than from a literal page count,
  and the journeys that share one club's roster need to be serialized or isolated. Both belong in **#286**,
  which is where the suite's readiness and retry budget are tracked; this run's authorized actions did not
  include filing or commenting on issues, so the observation is recorded here for the human or a later run.
  Capacity was ruled out: no test container runs, the exited ones are three weeks old, and `C:` has 635 GB
  free.

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
