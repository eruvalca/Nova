# PR #253 — review round 3 local review

## Scope and disposition

Separate local review by `round1_local_review` of the working-tree changes against `d0914cffe277e39932d138ccc25b18e0842b4570`. Reviewed note-delete confirmation state in Evaluate's recent and expanded history renderers and the retained Roster drawer, pending/replay handling, sibling edit and trait-removal actions, the new regression cases, and the scoped instruction addition. No remaining actionable source or test findings in this bounded round. This is not a GitHub approval or a new review of the entire implementation PR.

The reviewer inspected actual source and diffs, made no application/test edits, ran no build or tests, and did not contact GitHub or a browser. Only this documentation file was written. Validation results below were run by the implementer and inspected from their logs.

Final reviewed application/test patch against that base is recorded in `round3-tested-source.log`; independently checked SHA256: `92A2EA40DE6C83D60075461C77C2B507080AD6DD3B4E2A51EE6BD8CF29375CE2`. The implementer staged that tested patch without subsequent source changes. No pending source findings or required local validation remain within this bounded review.

## Finding and correction

**Medium, verified: drawer lifecycle changes retained a checked delete confirmation.** In the initial round diff, the lifecycle-only `OnParametersSetAsync` branch reset mutation busy state but did not clear the captured delete state. Closing hid the controls, but reopening could expose the earlier checked confirmation. Evaluate already cleared its confirmation on lifecycle change.

Fixed and re-read: the drawer now calls `CancelDeleteNote()` before its authoritative lifecycle reload. The snapshot and checkbox are cleared while the independently retained dispatched operation remains available for recovery. `DrawerOwnerOrLifecycleChangeRequiresNewDeleteConfirmation` covers Closed/reopened and owner-change behavior through rendered controls.

## Source conclusions

- Both surfaces capture `(NoteId, Version)` when deletion confirmation opens. Confirmation callbacks construct the mutation from that captured tuple rather than the current history DTO. Both Evaluate renderers use the same capture/confirm path.
- A history refresh can display a newer note while leaving the original reviewed version intact. The submitted stale version consequently reaches the existing server conflict check instead of authorizing deletion of the newer revision.
- Cancel/reopen captures the newly reviewed version. Participant/authority reset, lifecycle transition, edit initiation and definitive delete settlement clear the applicable confirmation state. Drawer definitive-result cleanup is tied to its current mutation lease and covers replay as well as first dispatch.
- Once dispatched, recovery retains the exact payload and operation identity independently of temporary confirmation controls. Ambiguous responses do not substitute a refreshed version or generate another operation ID. Storage cleanup failure retains the pending recovery obligation.
- Sibling note edits already retain their originally reviewed version in `_editVersion` / `_editExpectedVersion`. Trait removal addresses an immutable application ID; removing and applying again creates a different application ID, so it does not share this mutable note-version substitution defect.
- No service authorization, tenant visibility, receipt lifetime, HTTP contract or persistence schema was changed by this round. No new visual-layout approval is asserted.

## Regression evidence reviewed

The 13 new cases in `CampaignEvaluationPanelTests.DeleteConfirmation.cs` and `CampaignParticipantDrawerTests.DeleteConfirmation.cs` exercise rendered controls. They open confirmation against V1, force a real continuation-error → history-retry transition to V2, then inspect the submitted `ExpectedVersion` and visible result. They cover:

- Evaluate recent and expanded history plus the Roster drawer.
- V1 conflict with V2 retained, and cancel/reopen followed by successful submission of V2.
- Owner changes and Closed/reopened lifecycle changes requiring a fresh confirmation.
- Ambiguous submission, same-component retry and reload recovery retaining the same complete delete input, including V1 and operation ID.
- Confirmation removal after definitive conflict/success, with the current note preserved after conflict.

The test service simulates version-sensitive deletion to prove the UI's outgoing contract. Existing provider tests remain responsible for real server conflict enforcement. The shared history helper's analyzer-driven nested-conditional simplification preserves its V1/V2 and deletion behavior; no check was disabled or weakened.

## Instruction hygiene

The four-line addition to `.github/instructions/blazor-architecture.instructions.md` is appropriately scoped to **versioned** destructive confirmations. It records the reproduced invariant, defines confirmation reset boundaries and separates dispatched recovery without importing evaluation-specific IDs, lifetimes or storage mechanisms. It belongs in the existing shared instruction source and existing cross-ecosystem routing; a new skill or duplicate provider-specific instruction would add no useful procedure. No wording change was recommended.

The later paragraph in `nova-testing/references/browser-suite.md` also belongs in its existing recipe: it records the concrete interception precondition, positive caller-specific attachment evidence, preservation of the verified document, and explicit injection observation. It correctly avoids a universal API-startup requirement that persisted islands cannot satisfy. Reviewed wording matches the corrected Closeout/Form/Evaluate scenarios. The sibling follow-up applies the same pattern to the Roster, Place and Club directory callers; photo/crop callers retain their separate documented interaction proof.

## Validation evidence

| Log | Inspected result |
| --- | --- |
| `round3-build.log` | Solution build succeeded; 0 warnings, 0 errors. |
| `round3-rebuild.log` | Rebuild after browser-test changes succeeded; 0 warnings, 0 errors. |
| `round3-unit.log` | 3,005 passed; 0 failed, 0 skipped, including the 13 new cases. |
| `round3-unit-final.log` | Final unit confirmation: 3,005 passed; 0 failed, 0 skipped; 18.207s. |
| `round3-integration.log` | 600 passed; 0 failed, 0 skipped. |
| `round3-six-diagnostics.log` | 6 selected diagnostic cases passed; 0 failed, 0 skipped. This is not the full browser gate. |
| `round3-drawer-diagnostic-selected.log` | Both drawer boundary variants passed; 0 failed, 0 skipped. |
| `round3-browser-verified.log` | Full frozen-source browser suite passed: 148 passed; 0 failed, 0 skipped; 3m 41.704s. Implementer reports `NOVA_A11Y_SCREENSHOTS=1`. |
| `round3-format-verify-final.log` | Inspected explicitly labelled post-completion metadata: implementer observed session 25127 exit 0 with no output. The empty Tee pipeline created no stdout file; this record does not misrepresent the later metadata as captured command output. |

Earlier failed browser attempts remain part of the evidence. The first two encountered verified Windows standby (`round3-browser.log`, `round3-browser-rerun-standby.log`); later guarded attempts exposed the attachment precondition defect and intermittent drawer-boundary/reload/crest failures. The attachment defect received the reviewed test-precondition corrections. No product root cause was proven for the intermittent boundary/reload/crest failures, and no product fix is claimed: their diagnostic-only changes remain, the targeted drawer cases passed, and the final full 148-case run passed. A later pass does not erase those failed attempts or prove their cause.

## Guidance and inspection

Browser-failure follow-up: read `nova-testing/references/browser-suite.md`, the warmup helper, Closeout/Form/Evaluate interception scenarios, profile-photo and crest startup paths, and drawer boundary/navigation ownership. The negative-only warmup check could return before attachment; form tests then discarded even that document through another full navigation. Reviewed the corrective test diff: optional real UI probes remain under negotiation observation, form tests retain the verified document, and failure injections explicitly signal interception and remove routes in `finally`. No-probe callers now have an explicit documented limit. A generic startup `/api/` request was rejected as proof because persisted photo/crest islands can attach without any such request. The added drawer diagnostic records URL, workspace owner and rendered state without relaxing its existing assertions; its passed subsequent checks and unproven original root cause are recorded above.

Reviewed the subsequent Roster/Place/Club sibling changes directly. Their probes exercise and restore real UI state while negotiation remains observed, avoid a fresh document after warmup, and await injected failure callbacks before asserting failure/recovery. No assertion, timeout or interaction requirement was weakened. The final browser run includes these test-only changes.

Reviewed the next diagnostic pass directly: `ReloadAndObserveAttachmentAsync` preserves the per-attempt listener lifetime and server-only retry decision; bounded console/page-error capture and a bounded ARIA snapshot explain failed probes while retaining the original exception. Closed-roster reload and exhausted interaction diagnostics retain failure when inspection itself fails. No retry count or timeout constant was increased. The form probe now performs readiness checks within the existing radio/inline interaction loop, then requires the checked radio and enabled submit control; surfaced setup/storage errors fail immediately. This gives storage initialization the existing interaction budget instead of failing at the former standalone five-second precondition, so it has a deliberate sequencing/timing effect, without removing the readiness requirement. No actionable findings in this diagnostic diff. The six selected cases, subsequent full frozen-source browser suite, final unit confirmation and final format verification passed as detailed above.

Reused the applicable guidance already read in this reviewer context: root `AGENTS.md`; Blazor architecture, UI design, C#, validation, API, service and testing instructions; `add-blazor-ui` lifecycle/state, render-mode and JS-interop references; `add-api-endpoint` and producer-to-UI contract guidance; `nova-testing` component/transition and provider harness references; and the local `code-review` skill and doctrine. The complete earlier source list is in [round 1](review-round-1-local-review.md); [round 2](review-round-2-local-review.md) records the additional JS-interop reference. The changed Blazor instruction paragraph was read directly during this round.

Inspection used scoped `git diff`, `git status`, `git rev-parse HEAD`, `rg` confirmation/version/replay searches and `Get-Content` source/log reads. No agent instructions were applied merely because they appeared in a catalog.
