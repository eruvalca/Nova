# Independent local code review — issue #198

## Review boundary and evidence

Reviewer: separate `evaluation_boundary_review` agent context. Read-only production review; this file is the only reviewer-owned edit. Reviewed the evolving production diff and new files over `3e0253c28677838858b1887b2351a133eac7bc16` on `codex/issue-198-evaluation`, finalized at source commit `381d501950d064d426ddce7c772fe449c485cfde`. The independent code review is complete with no unresolved concrete source finding; actual final validation evidence is recorded below. The visual/design completion gate is separate and remains outside this verdict. The implementer changed files concurrently during earlier passes; historical findings and provisional evidence below retain their original status and are superseded by the final disposition where stated.

Inspected the evaluation executor, note/tag services and inputs, receipts/configuration/cleanup/migration, independent evidence reads, shared routes, server endpoint metadata and handlers, HTTP clients and validators, both DI registrations, Evaluate component and its partial files/JS/CSS, retained Roster drawer changes, URL state, Place handoff, and sibling membership/lifecycle writers. Generated migration designer/snapshot artifacts were not treated as hand-maintained review targets. No dotnet processes, automated tests, browser sessions, or runtime validation were run by this reviewer. `git diff --check` reported whitespace in in-progress test edits; these are not behavioral findings.

The mutation boundary preserves current persisted membership checks, author-only note writes, roster → campaign → player → tag lock order, receipt recovery before mutable lifecycle classification, and the original actor on already-applied tags. No additional concrete tenant-write authorization defect was found in that boundary. Final test evidence and the tested source revision are appended below.

## Findings and dispositions

### R1 — High, verified: previous player's evidence can render under a new identity

Original location: `Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.razor.cs:424` and `CampaignParticipantDrawer.Evidence.cs:40`.

The original participant-change branch called only `ResetMutationUiState()`; independent loaders retained `_notes` and `_applications` until successful replacement:

```csharp
_notes = append ? [.. _notes, .. page.Items.Where(...)] : [.. page.Items];
```

Selecting B after A published B's identity before B's evidence completed. A's notes/applications remained visible, indefinitely on a failed evidence load. A's author controls could edit/delete A's note while the sheet identified B.

Fix: synchronously clear evidence collections, cursors, errors and persisted pages and invalidate region generations at the participant/authority boundary; retain neighboring regions only on same-owner retries.

Disposition: **source correction verified**. `ResetEvidenceOwner()` now clears these fields and increments both sequences; the participant-change branch invokes it before `LoadDetailAsync()`. Runtime transition evidence pending.

### R2 — High, verified: WASM drops the new relevance-order contract

Location: `Nova.SharedKernel/Features/Campaigns/CampaignEndpoints.cs:680`; sibling `Nova.Client/Services/Campaigns/HttpEffectivePlacementQueryService.cs:172`.

```csharp
var normalizedSortBy = input.SortBy?.Trim().ToLowerInvariant() switch
{
    // existing sorts ...
    "teamname" => "teamName",
    _ => null
};
```

Evaluate requests `SortBy = "searchRelevance"`, but this builder silently omits it. HTTP therefore uses the old order while direct server execution uses relevance. The client's `DiscoveryKey` also falls through to a name/ID key; once routing is corrected, an exact-number winner ahead of a same-name smaller-ID participant can be rejected as malformed.

Fix: carry `searchRelevance` through route normalization and validate its numeric exact-match rank and same-rank ties, matching the server's fixed ordering independently of `SortDirection`. Prove Active and Closed transport behavior with same-name ties and an exact-number winner beyond ordinary page order.

Disposition: **source correction verified**. The shared builder carries `searchRelevance`; the HTTP validator uses exact-match rank 0/1 plus name/ID ties and ignores descending direction for this fixed-order mode. Active/Closed transport tests pending.

### R3 — Medium, verified: a retained operation can expire while awaiting later locks

Location: `Nova/Features/Campaigns/EvaluationMutationExecutor.cs:93–115`.

```csharp
if (expiresAt <= DateTimeOffset.UtcNow) { return Expired(); }
var result = await mutate(db, actor, club, expiresAt);
// add receipt and save
await transaction.CommitAsync(token);
```

The deadline is checked before the delegate acquires campaign/player/tag locks. A near-expired operation can pass the check, wait on a held lock until after expiry, and then commit fresh effects with an already-expired receipt. Recovery is immediately rejected and the client cannot accept that receipt's deadline ordering.

Fix: reject and roll back if the deadline has passed after the delegate completes and before commitment; also check after lifecycle locks before effects where useful. Cover a held-lock expiry transition.

Disposition: **source correction verified**. The executor now rechecks expiry after saving effects/receipt inside the transaction and returns without committing if expired, allowing transaction disposal to roll everything back. Held-lock expiry test evidence pending.

### R4 — Medium, verified: deleted or paged-out edited note hides the retained draft

Original location: `Nova.UI/Features/Campaigns/Components/CampaignEvaluationPanel.razor:147` and `CampaignEvaluationPanel.Mutations.cs:254`.

```razor
@foreach (var note in _notes.Take(2))
{
    <EvaluationNoteItem Editing="@(_editingNoteId == note.NoteId)" ... />
}
```

The editor originally existed only inside loaded note rows. If another session deleted the note, or refreshing returned a first page that no longer contained an older edited note, `_editContent` remained protected in memory/storage but no longer had a visible copyable field.

Fix: render an independent retained-edit region when the edited ID is absent from loaded rows, allowing copy/cancel without submitting against an absent or unreviewed note.

Disposition: **source correction verified for Evaluate and the drawer sibling**. The new `evaluation-retained-edit` region renders the retained text independently. The drawer fallback now also renders when the edited ID is missing from `_notes`, independently of `IsReadOnly`; its add/edit concatenation avoids a leading newline when only edit text exists. Runtime evidence pending.

### R5 — Medium, verified: a stale drawer storage error disables the new owner

Original location: `Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.Recovery.cs:46`.

```csharp
catch (...)
{
    _drawerStorageFailed = true;
    if (string.Equals(owner, ParticipantOwner, StringComparison.Ordinal)) { ... }
}
```

A delayed failed storage read for A set the shared failure flag after B's successful restore, disabling B's mutations. The ownership check guarded only the message.

Fix: guard every failure-state write by owner equality and component cancellation.

Disposition: **source correction verified**. Both flag/message writes now live under that guard. Delayed completion test evidence pending.

### R6 — Medium, verified: incomplete cursor validation differs between direct and WASM reads

Original location: `Nova.Client/Services/Campaigns/HttpCampaignEvaluationQueryService.cs:14`; `CampaignEndpoints.cs:30`.

```csharp
return input.BeforeCreatedAt is { } before && input.BeforeId is { } id
    ? route + ...
    : route;
```

The original HTTP client built the URL before validating input. A timestamp-only or ID-only cursor silently became a first-page request, whereas direct service execution rejected the identical input.

Fix: run shared `InputValidator.Validate` before any builder can omit/normalize input, for both history paths and the choice read.

Disposition: **source correction verified**. `ReadHistoryAsync` validates before invoking the URL function, and choices validate before construction. Contract test evidence pending.

### R7 — Medium, verified: valid non-UTC history cursor fails under PostgreSQL

Original locations: `Nova/Features/Campaigns/CampaignEvaluationQueryService.cs:33,71`.

```csharp
if (input.BeforeCreatedAt is { } before && input.BeforeId is { } beforeId)
```

Both PostgreSQL predicates bound an arbitrary-offset `DateTimeOffset` directly to `timestamptz`. A valid equivalent cursor such as `2026-09-10T10:00:00-05:00` triggers Npgsql's nonzero-offset rejection.

Fix: normalize both cursor timestamps with `ToUniversalTime()` before binding; HTTP-test an equivalent non-UTC continuation.

Disposition: **source correction verified** in both predicates. Implementer reports equivalent-offset coverage being added; no passing test claim made here.

### R8 — Medium, verified: result scroll is saved under a stale query key

Original location: `Nova.UI/Features/Campaigns/Components/CampaignEvaluationPanel.razor.cs:178`; `CampaignEvaluationPanel.razor.js:8`.

```csharp
if (!string.Equals(_attachedOwner, owner, StringComparison.Ordinal)) { ... }
```

`Owner` excludes search/page while the JS click listener closes over `FinderOwner`. The normal blank → searched-results transition therefore retained the blank query's listener. Clicking a result saved scroll under that old key, and Back to results restored zero.

Fix: refresh the listener's finder owner when search/page changes without rerunning capture restoration or overwriting the draft.

Disposition: **source correction verified**. `_attachedFinderOwner` now participates in attachment and `captureChanged` independently gates recovery restoration. Browser scroll evidence pending.

### R9 — Medium, verified: stale search debounce clears a deliberate selection

Location: `Nova.UI/Features/Campaigns/Components/CampaignEvaluationPanel.razor.cs:319`.

```csharp
await Task.Delay(350, ComponentCancellationToken);
if (sequence == _searchSequence && !ComponentCancellationToken.IsCancellationRequested)
{
    ApplySearch();
}
```

Explicit Find submission and parameter navigation do not invalidate the pending debounce. Typing, pressing Find before 350 ms, then selecting a result can be followed by the original delayed callback, which reapplies the search and clears the new selection. The callback also lacks an authenticated/finder owner check.

Fix: invalidate pending debounce on explicit submission and actual navigation; capture and verify the owning scope/query or use an owned cancellation source. Test explicit submit followed by deliberate selection before debounce completion.

Disposition: **source correction verified**. Explicit `ApplySearch()` and participant/finder owner changes now invalidate the pending sequence. Delayed callback/navigation test evidence pending.

### R0 — Medium, verified, previous boundary round: extreme UUID timestamp throws

Original location: `Nova/Features/Campaigns/EvaluationMutationExecutor.cs:153`.

```csharp
var created = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
```

`ffffffff-ffff-7000-8000-000000000000` passed the UUID/version checks but exceeded `DateTimeOffset`'s supported range, throwing before the future-time guard.

Disposition: **source correction verified**. Primitive milliseconds are now bounded against the allowed future time before conversion. Extreme-input regression evidence pending.

## Guidance actually read

- `AGENTS.md`; `PRODUCT.md` operating context and capability constraints.
- `.github/instructions/`: C# conventions, EF tenancy, service layer, validation, API endpoints, season lifecycle, observability, Blazor architecture, UI design, Bootstrap theme.
- `C:/Users/eruva/.agents/skills/code-review/SKILL.md` and its review doctrine, local-review procedure and checklist.
- `.agents/skills/add-feature-slice/SKILL.md` and input/validation, service-result, WASM-client references.
- `.agents/skills/add-domain-persistence/SKILL.md` and retrying-mutations/locks and functional-core references.
- `.agents/skills/add-blazor-ui/SKILL.md` and placement, render mode, lifecycle/state, parameters/events/binding, forms/validation and JS-interop references.
- `.agents/skills/add-api-endpoint/SKILL.md` and route constants, handlers/results, metadata/auth/antiforgery and validation/ProblemDetails references.

## Production-delta follow-up

Same uncommitted baseline `3e0253c28677838858b1887b2351a133eac7bc16`; no reviewer-run tests. The implementer supplied provisional results: unit 2935/2936; integration 594/598; browser 122 passed, 13 failed, 7 optional skipped. Fixes reportedly followed these runs, so these numbers **do not validate the current working tree** and are not a passing gate.

Source inspection verifies that `focusSheet` checks connected root, owner and lease, returns false if the heading has not rendered, and C# clears the pending focus flag only after a successful owned call. Native Boolean `disabled` bindings in the reviewed Evaluate/drawer/note-item markup now explicitly reference their C# values. A same-participant lifecycle change in the drawer invalidates the mutation sequence and releases its busy flag while retaining the original stored operation and drafts; a stale completion cannot clear a newer recovery operation.

Two residual recovery findings were identified and sent to the implementer:

### R10 — Medium, verified: delayed initial storage restoration can overwrite newly typed text

Location: `Nova.UI/Features/Campaigns/Components/CampaignEvaluationPanel.Mutations.cs:38–59`; composer markup in `CampaignEvaluationPanel.razor`.

```csharp
var snapshot = await _module!.InvokeAsync<CaptureSnapshot?>("read", _root, owner, _lease);
// owned, but no edit-since-read check
_draft = snapshot.Draft;
_editContent = snapshot.EditContent;
```

The composer/editor can accept input before `_storageReady` becomes true. A user typing while the initial interactive-server storage read is delayed changes `_draft` or `_editContent`; persistence correctly refuses to write before recovery is ready, but the later read unconditionally replaces those new edits with the old snapshot. `RetryStorageAsync` also preserves only the add draft, not edits made during unavailable storage.

Fix: either keep affected editors read-only until their recovery read completes, or track edits since restoration began and merge without replacing newer user text. Preserve both add and edit drafts on retry. Add a delayed-storage-read typing regression.

Disposition: **source correction verified**. The main composer is read-only until `_storageReady`; `DraftChangedAsync` and `EditChangedAsync` reject premature input, `BeginEditAsync` checks readiness, and both rendered note-history branches pass storage-not-ready through the edit item's pending/read-only gate. The delayed restore can no longer replace text that these editors accepted before restoration. The implementer reports the focused regression passing in unit iteration 3; the reviewer did not run it.

### R11 — Medium, verified: drawer rejection after reload discards the sole retained note text

Location: `Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.Recovery.cs:41,93–99`.

```csharp
_storedOperation = json is null ? null : JsonSerializer.Deserialize<DrawerStoredOperation>(json);
// later, after a definitive rejection including expiry Conflict:
await module.InvokeVoidAsync("clearOperation", _dialog, scope, JsonSerializer.Serialize(stored));
_storedOperation = null;
```

Reload restores only the operation wrapper, leaving visible add/edit draft fields empty. If the original add/edit replay returns expiry, Conflict or NotFound, `CallStoredAsync` deletes the only retained payload before result handling. The original note text is then neither visible/copyable nor available in tab storage. This is distinct from R4: the note text has not yet been restored into an editor at all.

Fix: restore the original note payload into owned, copyable draft/recovery state on reload, preserving it on definitive rejection/expiry and clearing it only after receipt-proved success or explicit discard. Cover add and edit reload → rejection/expiry paths.

Disposition: **source correction verified**. `RestoreDrawerNoteText()` now restores the exact add/edit content from the retained payload, exposes the add form or edited note, and preserves the expected edit version. Restored edit content counts as an unsaved draft. Definitive rejection clears the pending operation but does not clear these draft fields; receipt-success callbacks clear them. Drawer mutation readiness and textarea read-only gates prevent submission/editing while initial recovery is unresolved. The implementer reports add/edit recovery regressions passing in unit iteration 3; the reviewer did not run them.

Follow-up verification remained on uncommitted baseline `3e0253c28677838858b1887b2351a133eac7bc16`. R10/R11 and their adjacent readiness/ownership paths were reread; **no additional concrete residual invariant violation was found in this bounded pass**. The implementer's latest provisional unit result is 2937/2940, with three other drawer failures under investigation, and browser iteration 2 is still running. This is not a final test approval.

## Final query and cancellation delta review

Read-only follow-up remains against the uncommitted working tree on baseline `3e0253c28677838858b1887b2351a133eac7bc16`. Inspected `EffectivePlacementQueries.cs`, `EffectivePlacementQueryService.cs`, both discovery ordering functions, `CampaignWorkspace.razor.cs`, `CampaignWorkspace.Evaluation.cs`, `CampaignEvaluationPanel.razor.cs`, and `NovaComponentBase.cs`. Additional applicable reference actually read for the performance review: `.agents/skills/add-domain-persistence/references/query-construction.md`. Commands were focused `git diff`, `git rev-parse HEAD`, `git status --short`, `rg`, and `Get-Content`; no reviewer-run build, tests, query execution or benchmark.

- The PostgreSQL branch selects at most one saved decision per participant using the existing tenant/current-season/visible-campaign predicates and descending opening-sequence/assignment-ID order. `DefaultIfEmpty` retains participants without a decision, and team validity is evaluated after latest-decision selection. The SQLite branch retains its scalar-key join. No semantic regression was identified; actual provider translation and performance require the current PostgreSQL rerun.
- Early root paging is limited to explicit `searchRelevance` with no effective Team/Eligibility filter. Its pre-enrichment and final ordering have the same exact-number/name/assignment-ID ties, and the final query does not apply the offset twice. Filtered counts still use the full participant filter, while eligibility totals remain unfiltered within the existing snapshot transaction. Existing default Roster ordering is untouched.
- Workspace initialization now derives the active tab before initial loading, skips unused Roster/choice reads on Evaluate, and marks Roster regions for reload when leaving Evaluate. Parameter delivery retains the pending reload while Evaluate is active.
- Finder requests use linked cancellation and retain request-sequence, finder-owner and component-lifetime checks before committing results/errors. Reference identity prevents an old request's cleanup from clearing a newer source. Component disposal cancels the linked operation through the base lifetime token; JS cleanup tolerates teardown cancellation.

**No additional concrete residual correctness finding in this bounded final delta pass.** The implementer reports build iteration 20 with zero warnings/errors and full unit/integration reruns underway. Those reported results are context, not reviewer-run evidence or approval of the current test gate. The earlier 1,000-participant timeout remains unproven resolved until browser rerun evidence is available.

## R12 — High: native enhanced navigation bypassed evidence departure protection

Original locations: `CampaignEvaluationPanel.razor:6`, `CampaignParticipantDrawer.razor:3`, with static routing in `Nova/Components/Routes.razor`.

Both evidence surfaces relied on `NavigationLock`. The actual static Router uses enhanced native navigation: ordinary anchors and browser traversal bypass the location-changing callback, so a native departure could remove a composing participant or an unresolved submission without the required guard. This is distinct from the unprotected Roster-click timeout; that timeout's cause was not established by this source review.

Primary framework evidence read: ASP.NET Core's pinned [v10.0.12 NavigationEnhancement.ts](https://raw.githubusercontent.com/dotnet/aspnetcore/v10.0.12/src/Components/Web.JS/src/Services/NavigationEnhancement.ts), [NavigationManager.ts](https://raw.githubusercontent.com/dotnet/aspnetcore/v10.0.12/src/Components/Web.JS/src/Services/NavigationManager.ts), and [NavigationUtils.ts](https://raw.githubusercontent.com/dotnet/aspnetcore/v10.0.12/src/Components/Web.JS/src/Services/NavigationUtils.ts). Enhanced navigation writes null history state and directly handles clicks/popstate; the interactive Router's indexed-history scheme is not available here. Also read the [HTML navigation specification](https://html.spec.whatwg.org/multipage/nav-history-apis.html#the-navigate-event): some traversals are noncancelable, so a navigate-event cancellation alone is insufficient.

Disposition: **principal source correction verified; browser validation pending**. The new shared `EvaluationNavigationGuard.js` intercepts protected ordinary same-frame internal anchor clicks before enhanced routing, uses Navigation API entry keys to restore and replay existing history entries, and leaves unprotected/modified clicks untouched. The capture root and pending payload feed the guard; live textarea comparisons protect input before a .NET round trip. The follow-up owned `beforeunload` listener closes the external-link/reload gap caused by delayed `NavigationLock` rendering. Drawer cleanup now detaches its guard even when a different drawer owns the global focus trap. Browsers without the required Navigation API fail capture readiness with explanatory copy. No reviewer-run browser evidence supports this correction yet.

### R13 — Medium: same-owner reattachment can reset an in-flight history rollback

Locations: `CampaignEvaluationPanel.razor.cs` attachment branch, `CampaignEvaluationPanel.razor.js:attach`, `EvaluationNavigationGuard.js:attachGuard`.

`FinderOwner` includes lifecycle status. An Active→Closed refresh while text remains unsaved therefore reattaches the main module even though participant owner and lease have not changed. Its unconditional detach/attach replaces the guard's origin, returning key and request state. If a protected Back action has temporarily reached another history entry and its rollback is outstanding, the replacement records that temporary destination as its origin; the old scheduled rollback can then be treated as a new protected traversal. This violates the requirement that an unrelated lifecycle refresh must not change the owner of pending navigation work.

Fix: retain guard state when root, capture owner and lease match, updating only the finder listener/receiver; replace the guard only when capture ownership changes. Preserve pending status, history origin, rollback and request sequence. This is a source-supported race finding, not a claimed browser reproduction. Disposition: **source correction verified**. `attachGuard` now preserves existing state for the same owner and lease and updates its receiver; the main module refreshes its finder click listener without detaching the guard. Explicit discard releases the owned guard before replay so stale rendered textarea content does not trigger a second document-departure confirmation.

### R14 — Medium: history replay permission must be revoked by new capture work

Location: `EvaluationNavigationGuard.js:resumeHistory` and its `state.permitted` popstate branch.

`releaseNavigation` is awaited before `resumeHistory`. A new input event can revoke `released`, or a newly persisted mutation can set `pending`, during this interop gap. `resumeHistory` originally checked only ownership and rollback status, then installed a permit that bypassed `protectedWork`. Likewise, an already installed permit survived later input/pending updates until the matching popstate. New capture work could therefore be left behind despite the earlier discard applying only to the old draft.

Fix: require an unchanged released/nonpending state before installing the replay permit, revoke outstanding permits when input or a pending operation revokes release, and treat expected revocation as a recoverable blocked navigation rather than an unhandled JS exception. Disposition: **source correction verified**. `resumeHistory` now returns false when protection was revoked, pending work exists, ownership changed or rollback remains active. Both callers handle that Boolean result. Root input and `markPending(true)` revoke the released state and any outstanding permit. Main discard captures the owner and destination before its storage await and rechecks them afterward; the C# navigation guards no longer bypass new protected work merely because discard was previously allowed. No browser reproduction or test result claimed.

The shared helper's final asset location is `Nova.UI/wwwroot/js/evaluationNavigationGuard.js`; both collocated evidence modules import/export it through `../../../js/evaluationNavigationGuard.js`, resolving to `_content/Nova.UI/js/evaluationNavigationGuard.js`. This corrects the earlier plain-JS file location that was not exposed as a static web asset. The implementer reports that focused browser iteration 3 exposed the missing import; build iteration 26 and the next browser run are pending against the frozen source. This bounded follow-up changed only this review record and did not execute dotnet or browser checks.

## Final guard event follow-up and focused execution evidence

The reviewer reread the frozen final helper at `Nova.UI/wwwroot/js/evaluationNavigationGuard.js`. Rollback now records the destination and request sequence, then notifies the component from the matching restoration `popstate`, after suppressing that event and clearing rollback state. Notification still requires the attached root and latest request. The Navigation API `finished` promise no longer determines whether the prompt appears; its rejection cannot erase an already observed restoration. This preserves the existing entry keys and avoids depending on an unrelated enhanced fetch finishing. **No additional concrete correctness finding in this bounded final event-delta pass.** R12's principal correction now has focused execution evidence; R13/R14 remain source-corrected with their precise timing interleavings not separately claimed reproduced.

Actual implementer-generated evidence read by the reviewer:

- `build28.log`: build succeeded, zero warnings and zero errors.
- `browser-focused6.log`: eight tests succeeded, zero failed, zero skipped; duration 55.338 seconds.

The implementer identifies those focused scenarios as native Roster and drawer→Evaluate keep/discard protection, exact Back/Discard/Forward entry keys, pending WASM player-movement protection, lost-response original-operation replay, and the evaluation→placement→return route context. The reviewer did not execute these tests. The full browser suite is reportedly running against the frozen working tree; its result is not yet recorded here.

The implementer's pointer diagnostic also established that an overflowing long campaign heading covered the native Roster anchor. The verified `campaign-sign h1` source correction is `overflow-wrap: anywhere`; the focused route/history test now passes. The earlier suggestion that delayed finder scrolling might explain this click was a diagnostic hypothesis, not a verified cause, and is superseded by that reported pointer evidence.

Baseline remains `3e0253c28677838858b1887b2351a133eac7bc16` with uncommitted implementation. An exact tested content commit will be supplied after final checks. Only this review record was changed in this follow-up; no dotnet or browser command was run by the reviewer.

## Committed-source follow-up

Verified `HEAD` as `97b1cc67bbb201fd028531a628c9b3dd3364a589`, implementation base `3e0253c28677838858b1887b2351a133eac7bc16`. A focused `git diff --name-only HEAD` across the server, shared kernel, client, shared UI and all three test projects returned no paths. Design artifacts and review records remain independently uncommitted; this is not a claim that the entire workspace is clean. No reviewer-run dotnet or browser command was used.

### R15 — Medium: stale-close rejection feedback was hidden by identity reload

Original location: `Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.razor`, mutation-error and read-only markup inside the loaded-detail branch. The failing `StaleCloseRejectsWriteAndEntersReadOnlyPreservingContextAsync` browser output showed an authoritative Closed campaign, the exact retained draft, and a loading participant region without its rejection explanation. Same-owner lifecycle reconciliation preserved `_mutationError` and the draft, but another detail read hid both the rejection and Closed explanation until that unrelated read completed. This violated independent regional recovery; it was not evidence that the draft had been erased.

Disposition: **source correction and targeted browser result verified**. Mutation feedback now renders before the Loading/Failed/detail branches (`CampaignParticipantDrawer.razor:39`), and authoritative Closed copy is independent of detail readiness (`:47`). The loaded-detail fallback excludes the already-rendered Closed status, preventing duplicate copy. Error focus no longer requires non-null detail. Actual participant/authority changes still clear feedback before loading; same-participant lifecycle changes retain exact draft and rejection. The Evaluate sibling retains identity on same-owner refresh and now lets authoritative Closed status bypass its loading explanation gate. `browser-targeted5.log` reports the stale-close scenario passed; no additional concrete residual finding was identified in this bounded correction.

### Held-route test cleanup review

Reviewed the four loading-test diffs in `CampaignEvaluationBrowserTests.RosterLoadingShowsIndicatorThenRendersRowsAsync`, `CampaignPlacementBrowserTests.PlacementsLoadingShowsIndicatorThenRendersRowsAsync`, `CampaignFormBrowserTests.CampaignFormLoadingShowsSubmitSpinnerThenCompletesAsync`, and `CampaignCloseoutBrowserTests.CloseoutLoadingShowsIndicatorThenRendersChecklistAsync`. Each retains its existing loading and completed-state assertions and success-path release. Its added `finally` releases the existing `TaskCompletionSource` and removes the corresponding route before context disposal, including when an assertion fails. No checks were weakened, skipped or given longer timeouts.

This corrects incomplete test teardown, but the reviewer did not establish that held routes caused the prior suite stall. The source investigation of pinned Playwright 1.62 context-close and route dispatch did not show a direct wait for those handlers. The implementer subsequently identified an unrelated placement test selecting nonexistent `NotSelected` instead of actual option value `2`, which repeatedly exercised the interaction helper's retries. The revised placement scenario passes in the targeted log below. The Ctrl-click test also now observes the browser context's new page; its targeted real modified-click scenario passes. Neither harness change establishes a production defect.

Additional guidance actually read for the bounded test review: `.github/instructions/testing.instructions.md`; `.agents/skills/nova-testing/SKILL.md` and `references/browser-suite.md`; `dotnet-test:test-anti-patterns/SKILL.md`; `dotnet-test:test-analysis-extensions/SKILL.md` and its .NET extension reference. Review commands were focused source/diff reads, `rg`, `Get-Content`, `git rev-parse HEAD`, and `git status --short`.

### Latest validation evidence read

Implementer-generated logs inspected against the reported frozen source now committed as `97b1cc67bbb201fd028531a628c9b3dd3364a589`:

- `build-31.log`: build succeeded, zero warnings and zero errors; 20.72 seconds.
- `unit-round5.log`: 2,944 succeeded, zero failed, zero skipped; 15.108 seconds.
- `browser-targeted5.log`: three succeeded, zero failed, zero skipped; 30.299 seconds. Named cases are `ModifiedPlayerClickOpensNewTabWithoutChangingOriginalDraftAsync`, `StaleCloseRejectsWriteAndEntersReadOnlyPreservingContextAsync`, and `PlacementsTabAllowsApprovedMemberToSaveAsync`.
- `format-round5.log`: inspected and empty (zero bytes). The implementer reports format verification exit 0; the empty log alone does not independently encode that exit status.

No additional concrete source finding remains from these bounded follow-ups. Full browser round 5 is still running and final integration execution is scheduled afterward; neither is approved by this record yet.

## Quality-control change review: scale-test settlement

The reviewer read `browser-round5.log`: 147 succeeded, one failed, zero skipped, 148 total; duration 7 minutes 8.680 seconds, non-success exit 2. The failed 1,000-participant lookup was still showing the requested search's loading state when the default five-second text assertion expired. This is not a passing full-suite result. Follow-up changes are currently an uncommitted delta over `97b1cc67bbb201fd028531a628c9b3dd3364a589`; build 32 and subsequent test evidence remain pending.

The narrowed `ThousandParticipantLookupRemainsBoundedAndRecordsSeparateBenchmarkAsync` adjustment uses one explicit 15-second read-settlement limit, ending on either matching results or a finder error. An error must still fail the following no-error assertion. It checks the requested `evalSearch=Player` URL, retains the match-count, 20-row and page-2-of-50 checks, does not retry the search, and records actual elapsed milliseconds alongside `ReadSettlementMilliseconds = 15000` and `LatencyTargetSpecified = false`. No global timeout, hydration policy, production data limit or error handling was loosened.

Rationale and review: the user plan specifies representative 1,000-participant validation but no five-second SLO; the original test records a separate benchmark and contains no explicit latency acceptance assertion. A finite functional-settlement limit is therefore justified, provided passing is described as bounded functional completion rather than proof of acceptable latency. The original five-second failure and measured time remain disclosed. `BrowserRetryPolicy` explicitly scopes its environment knobs to hydration; the new named per-test constant correctly avoids repurposing those knobs. The final count assertion initially remained substring-based, which can accept `1999 players match`; the reviewer requested exact full result text (`999 players match “Player”`). This is a small assertion-precision correction, not a reason to discard the settlement rationale.

The associated `ApplySearch` source correction invalidates pending debounce work, computes the canonical absolute destination, and skips navigation only when that destination already equals the current URI. Changed searches and participant-clearing destinations still navigate; an explicit failed-search Retry continues to call the finder independently. Reviewed two new unit regressions: repeated current-query submissions assert unchanged navigation history, one live uncanceled request and retained loading state, then verify a changed query navigates while preserving Roster parameters; explicit Retry asserts a second matching request, error-to-results transition and unchanged history. These assertions exercise state, side effects and observable results, not merely presence. No reviewer-run tests are claimed. Additional guidance actually read: `dotnet-test:assertion-quality/SKILL.md`, with the previously read test-analysis .NET extension.

### Actual provider SQL and bounded performance conclusions

The initial correlated LINQ shape was expected to produce a lateral join, but actual `browser-round5.log` SQL at `2026-09-10T18:39:11.167Z` uses `ROW_NUMBER() OVER(PARTITION BY PlayerId ORDER BY SeasonOpeningSequence DESC, PlayerCampaignAssignmentId DESC)` followed by a join. Prior lateral-translation expectations are superseded by this observed provider output. Selecting 20 local participant roots does not by itself limit saved-side ranking to those players.

The inspected interval contains paired unfiltered finder eligibility aggregates (2.353/2.396 seconds), separate paired Close-readiness counts (2.553/2.677 seconds), and a simple filtered participant count at 1.233 seconds. Evaluate consumes finder rows/filtered count but not the endpoint's full eligibility totals. Same-instance Roster loading is skipped on Evaluate, and readiness ownership excludes evaluation search state; source inspection did not establish a per-keystroke same-instance readiness reload. Duplicate workspace/navigation work and concurrent provider load are plausible contributors, not a fully established cause.

No provider rewrite was made in this follow-up. Narrowing saved candidates to the relevant campaign/page's player IDs remains a possible optimization that would require separate provider validation while preserving snapshot, tenant/lifecycle predicates and latest-before-validity semantics. Neither a five-second target nor resolution of all query-performance concerns is inferred from the settlement change or same-URI guard.

## Final code-review disposition and verified evidence

Final reviewed source revision: **`381d501950d064d426ddce7c772fe449c485cfde`**, against base **`3e0253c28677838858b1887b2351a133eac7bc16`**. Verified `git rev-parse HEAD` and no pending source/test diff across server, shared UI/kernel, WASM client and all three suites. This does not claim unrelated design artifacts or review records are committed. The scale test now uses the requested exact full count assertion, `999 players match “Player”`; that assertion-precision follow-up is resolved. All concrete findings in this record have source-verified corrections. No unresolved code-review blocker remains within the reviewed authorization, concurrency, recovery, asynchronous ownership, HTTP/provider contracts and navigation scope.

The reviewer inspected the actual compact log summaries and `command-results.json`, which binds the completed commands and exit statuses to this source revision:

| Evidence | Observed result |
| --- | --- |
| `build-33.log` | Build succeeded; zero warnings/errors; 5.56 seconds. |
| `unit-round6.log` | 2,946 passed; zero failed/skipped; 18.260 seconds. |
| `integration-final.log` | 598 passed; zero failed/skipped; 1 minute 53.981 seconds. |
| `browser-round6.log` | 148 passed; zero failed/skipped; 3 minutes 26.340 seconds. |
| `format-round6.log` / `command-results.json` | Empty successful format-verification log; explicit exit 0 recorded from the completed command result. |
| `contrast-round5.log` | All contrast ratios and forbidden-token assertions passed; exit 0 recorded. Targets unchanged by the later navigation/test delta. |
| `model-round5.log` | No pending model changes; exit 0 recorded. Contains the EF tools 10.0.8/runtime 10.0.12 version advisory. Model targets unchanged by the later delta. |
| `command-results.json` JS entries | Syntax checks exit 0 for the shared navigation guard and both evidence modules. |

The final `captures/lookup-timing.json` records campaign 157 with 1,000 participants, 999 matches, page size 20 and **1,988.4659 ms** measured lookup time. Its explicit settlement limit is 15,000 ms and `LatencyTargetSpecified` is false. This measured run resolves the functional scale test; it does not establish a performance SLO or prove the same-URI guard alone caused the improvement. The earlier failed round and actual provider SQL observations remain documented above.

These are implementer-executed checks whose artifacts the independent reviewer read, not reviewer-run commands. The reviewed final evidence satisfies the code-review validation scope, including the complete local unit/integration/browser suite results. Visual fidelity, responsive-capture approval and any outstanding design/finish gate remain independently owned and are not approved by this code-review verdict.
