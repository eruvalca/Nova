# Review round 3 — PR #253

Base: `d0914cffe277e39932d138ccc25b18e0842b4570`. Both CI checks passed. Copilot review `PRR_kwDOSz2VcM8AAAABNEqFNQ` reported two deletion-confirmation findings. Completed session `a19ee9a8-3a00-4e8b-a6a7-8c6565248978` was read through GitHub CLI, including its raw stored comments: both match the posted threads, with no additional withheld finding. All PR issue comments were read, including those authored through the user's account.

The final tested application/test patch against that base has SHA-256 `92A2EA40DE6C83D60075461C77C2B507080AD6DD3B4E2A51EE6BD8CF29375CE2` (`git diff --cached --binary` over `Nova`, `Nova.UI`, `Nova.Client`, `Nova.SharedKernel` and the three test projects). This identifies source before the single round-three commit adds finalized Markdown evidence. The initial note-only patch had hash `FE62B36EC51D97B5B06FCE3A5D5CBB962F29B8F57189A62C55E2B957E6CC0C5D`; subsequent browser-test corrections are included in the final hash.

| Thread | Correction |
| --- | --- |
| `PRRT_kwDOSz2VcM6hPuvf` | Evaluate recent and expanded history capture note ID/version when the confirmation opens. Confirmation submits that snapshot, independent of refreshed rows. |
| `PRRT_kwDOSz2VcM6hPuv5` | The retained Roster drawer captures the same pair and builds deletion from the snapshot. |

The invariant is that a destructive confirmation authorizes the revision the author reviewed. Refreshing evidence must not silently upgrade the expected version. A changed row therefore reaches the existing server conflict boundary rather than deleting the newer revision.

Both snapshots clear on cancellation, owner/lifecycle changes and definitive settlement. Starting a note edit or opening the drawer's add-note form clears the prior confirmation. Submitted operations remain separately retained with their exact original payload/version and operation ID for ambiguous-response recovery. The separate reviewer identified the drawer's lifecycle-only parameter path as an additional reset boundary; it now clears the confirmation while preserving pending recovery, so Closed → reopen does not restore old consent.

Sibling inspection covered both Evaluate render loops, drawer edit/delete and replay, and tag removal. Edits already retain expected versions independently of refreshed history. Tag applications are immutable identities; removing an application cannot target a newly reapplied row with a different ID. Draft-campaign deletion uses its separate lifecycle/tombstone contract, without a note revision token. No persistence or HTTP contract changed.

The existing Blazor architecture instructions gain one narrowly scoped rule for versioned confirmations. This documents the verified failure and its ownership/settlement boundaries without duplicating recipe instructions or creating a new skill. Both Codex and Copilot consume this same instruction source through the existing routing.

## Guidance and validation

Applied the replacement `AGENTS.md` supplied for this turn, including fixed application source/assets during browser validation. Reused the previously read Blazor, C#, validation, testing and API guidance and the `add-blazor-ui`, `nova-testing` and relevant test-running recipes; inspected lifecycle/state guidance, matching instruction inventory, actual destructive-action callers and existing recovery serialization. The separate reviewer and test author record their own sources and evidence.

| Regression | Behavioral evidence |
| --- | --- |
| `DeleteConfirmationRetainsReviewedVersionUntilCanceledAndReopened` | Recent and expanded Evaluate history retain V1 through a rendered V2 refresh; V1 conflicts, while cancel/reopen deliberately submits V2. |
| `EvaluationOwnerChangeInvalidatesDeleteConfirmationBeforeAnotherDelete` | A new authority scope cannot reuse the previous confirmation. |
| `EvaluationClosingAndReopeningRequiresNewDeleteConfirmation` | Reopening requires a new confirmation after authoritative refresh. |
| `AmbiguousDeleteRetriesAndReloadKeepOriginalReviewedVersionAndOperation` | Both Evaluate history renderers retain the same V1 payload and operation through immediate retry and reload. |
| `DrawerDeleteConfirmationRetainsReviewedVersionUntilCanceledAndReopened` | Drawer refresh preserves the reviewed version and allows deliberate V2 reconfirmation. |
| `DrawerOwnerOrLifecycleChangeRequiresNewDeleteConfirmation` | Owner changes and Closed/reopen clear both the reviewed snapshot and confirmation checkbox. |
| `DrawerAmbiguousDeleteRetriesAndReloadKeepOriginalReviewedVersionAndOperation` | Drawer retry and reload preserve V1 and its original operation; conflict leaves V2 visible. |

Tests use the rendered continuation/retry controls to refresh history while retaining the confirmation. The service double deletes only when the submitted expected version matches V2, so accidentally substituting V2 fails the preservation assertions. Thirteen new cases cover these paths. A nested conditional in the shared test helper was simplified after the formatter reported S3358; no assertion or diagnostic was suppressed.

## Command evidence

Build first, then tests with `--no-build`. Aspire suites run serially across the machine; application source and generated assets stay fixed during browser execution. Raw logs are ignored local artifacts.

| Command | Result |
| --- | --- |
| `dotnet build Nova.slnx` | Passed, 0 warnings/errors (`round3-build.log`). |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | 3,005 passed, 0 failed/skipped (`round3-unit.log`). |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | 600 passed, 0 failed/skipped (`round3-integration.log`); no timeout or retry policy changes. |

The first browser attempt (`round3-browser.log`) was interrupted after Windows Modern Standby disconnected the network. Kernel-Power 506/507/172 events confirm standby/disconnection from 17:52 through 19:13 local time (`round3-browser-standby.log`). The first failure was `ERR_NETWORK_IO_SUSPENDED`, with cases reporting 54-minute and 81-minute durations; subsequent cases also failed. The run was canceled and its failed evidence retained.

The second attempt (`round3-browser-rerun.log`) encountered another verified idle standby/network disconnection, 19:21:47–19:28:00 (`round3-browser-rerun-standby.log`). A dashboard navigation timed out during that interruption; a later drawer page-boundary locator also timed out, without proof that standby caused that second failure. This disrupted attempt was canceled and retained. The next full run starts with a temporary thread-scoped keep-awake request, cleared in `finally`, using the same built source, capture flag, assertions and timeout/retry settings. Detailed progress output is enabled. No persistent power policy is changed. The .NET P/Invoke skill's declaration/type-mapping guidance and Microsoft's [SetThreadExecutionState reference](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate) were read for the local validation wrapper; it is not application code.

No provider/schema or theme changes were made; the contrast and migration-model checks recorded at `af999242` remain applicable. The 600-test integration pass covers all production changes in this round; only browser tests and documentation changed afterward.

The third full browser attempt (`round3-browser-final.log`) completed with 140 passed, 8 failed and 0 skipped in 9m55s. No Modern Standby occurred during this guarded run. Failures include missing attachment before intercepted requests, a drawer page-boundary transition, a Closed-roster reload and two club-create continuations. Independent review confirmed that the warmup helper's absence-of-negotiation check alone does not prove attachment, and two form scenarios navigate away from the checked document. Caller-specific positive probes and retained-document corrections are in progress; the drawer failure needs URL/owner/rendered-state diagnostics before a product or test correction is justified. These failed results are not treated as a passing browser gate.

The reviewed attachment correction now covers Closeout, Form, Evaluate capture and sibling Roster, Place and club-directory callers. Real interaction probes execute while server-negotiation observation remains active. Tests keep the verified document and explicitly await injected HTTP failures, with route removal in `finally`. Existing photo/crest islands retain their actual upload/crop interaction proof; no unsolicited startup API read is assumed. The existing `nova-testing` browser reference records this procedure and passes `quick_validate.py`; no new skill or ecosystem copy was added. A rebuild after these changes passed with 0 warnings/errors (`round3-rebuild.log`). The initial exact method filter selected zero tests (exit 8); the corrected wildcard filter must execute both drawer landing cases before its result is accepted.

The targeted drawer filter selected both route variants and passed (2 passed, 0 failed/skipped; `round3-drawer-diagnostic-selected.log`). The next full run (`round3-browser-corrected.log`) completed with 142 passed, 6 failed, 0 skipped. Closeout error/retry and both drawer boundary variants passed. Remaining failures were both campaign-form preconditions, Evaluate composer readiness, the Closed-roster reload and two club-create continuations. Passing focused cases alone does not establish their cause or replace the full gate.

Diagnostic changes retain bounded console/page errors and rendered state on warmup failure, plus URL/DOM state for reload and continuation failures. Original failures remain terminal and available as inner exceptions where wrapped; no retry budget was increased. Campaign-form readiness now belongs to the existing bounded radio-interaction loop and fails immediately on a surfaced alert, with final checked/enabled assertions retained. This does allow more elapsed initialization time than the former standalone five-second precondition; the rationale is to establish the required interactive state before testing submission, not a five-second performance promise. Independent review accepted this sequencing and the diagnostic changes.

`dotnet format whitespace Nova.slnx --no-restore --include` over the changed browser files passed. The diagnostic build first failed MA0051 on the expanded warmup method; extracting one observed reload/probe into a helper corrected it without a suppression. The following `dotnet build Nova.slnx` passed with 0 warnings/errors (`round3-diagnostics-build-final.log`). The six previously failing cases then passed together (6 passed, 0 failed/skipped, 1m07s; `round3-six-diagnostics.log`). The full final browser run uses default output to avoid dumping the complete Aspire fixture log; assertions, capture flag and suite parallelism remain enabled and unchanged.

## Final browser evidence

The full frozen-source run passed all 148 cases, with 0 failures and 0 skips in 3m41.704s (`round3-browser-verified.log`, `NOVA_A11Y_SCREENSHOTS=1`). All previously failing scenarios executed in that run. The drawer boundary, Closed reload and club-create failures were intermittent; their diagnostic additions preserve the original assertions, and no unsupported product root cause is claimed. Two targeted drawer cases and the six diagnostic cases are supplementary evidence, not substitutes for the complete pass. The separate reviewer inspected the correction diff and logs; see [local review](review-round-3-local-review.md).

Final verification: `dotnet format Nova.slnx --verify-no-changes` exited 0 (`round3-format-verify-final.log`); `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` passed 3,005/3,005, 0 failed/skipped in 18.207s (`round3-unit-final.log`). Final build passed with 0 warnings/errors. Browser passed 148/148 with captures enabled, and the unchanged provider/HTTP implementation passed all 600 integration tests earlier in this round. No checks are unavailable. All three suites must run again before a later merge under the repository gate. This round is ready for its single commit; fresh CI and automatic review remain required afterward.
