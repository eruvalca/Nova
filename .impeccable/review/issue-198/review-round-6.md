# Review round 6 — recovery corrections and evidence retention

## Scope and review evidence

Base: `1a9cb8bed98d251a3b6ca69e510411ca8e058e5d`. Both remote CI checks passed.
Copilot review `5174740093` failed because the PR exceeded its 300-file limit.
Session `2b25a61b-c8d8-413c-bd2b-135878e734cf`, workflow `34557847482`, nevertheless
recorded four findings. The exposed session log contains no `store_comment` calls;
the workflow supplies locations/types but not full wording. The assessments below
are independently verified source findings, not invented quotations from Copilot.

The user initially instructed: address the visible findings, do not commit yet, and
stop watching. They subsequently authorized the recommended evidence-retention cleanup
and a push together with these fixes. The heartbeat remains paused. This round uses
one combined commit; no review request, merge or issue-status change is authorized.
The failed review is not represented as approval or a clean review.

## Dispositions

| Located finding | Assessment and correction |
| --- | --- |
| `CampaignEvaluationPanel.Mutations.cs:291–295`, bug | A generic rejection does not prove an earlier request failed to commit. Preserve the exact pending operation unless the response identifies its durable noncommit receipt. |
| `CampaignParticipantDrawer.Recovery.cs:169–171`, bug | Same invariant in the shared drawer submission/recovery path. Clear retained storage only on success or an operation-matched durable rejection. |
| `CampaignWorkspace.razor.cs:593`, conventions | Intentional: capture storage uses user/club identity, while the separate authority scope includes administrator role. Both child surfaces add campaign/participant identity, refresh authorization-derived state on role changes, and the server checks current membership before returning a receipt. Adding role to storage would strand original operations after a role change. No source change. |
| `README.md:42–44`, description discrepancy | Label the original implementation table by revision instead of “Latest result.” Qualify the original suppression claim; later round records explicitly document the narrow reviewed test-construction exceptions. |

## Recovery invariant and sibling paths

A lost success response followed by membership removal returns Forbidden before
receipt lookup. Expiry and fingerprint conflicts also cannot prove noncommit.
Previously those statuses cleared the operation ID, allowing a later Save to create
a second note under a new ID. Both surfaces now require positive, exact-operation
evidence, carried through the existing ProblemDetails extensions and strictly
decoded from native or HTTP values.

An absent receipt under a lock alone is insufficient: a delayed original request
could acquire that lock after a retry is rejected and the campaign reopens. The
executor therefore stores the rejected outcome under the same immutable identity,
actor, tenant, and fingerprint. A savepoint rolls back rejected mutation effects
and clears tracked changes before the rejection receipt is saved, while the outer
membership/roster locks remain held. Later identical attempts recover that rejection.
Transport failures, prelookup authorization/validation, expiry, fingerprint mismatch,
and server errors remain unresolved; their original payload stays retained.

The existing `ResultJson` column now stores a success/problem outcome envelope.
No EF model or schema changes are needed. No compatibility layer or old-data
conversion is added, consistent with the repository's pre-release policy. Existing
receipt expiry, immutable storage, tenant isolation, and indexed global cleanup apply
to both outcomes. The shared executor covers all six Evaluate operations and the
five operations supported by the retained drawer.

## Guidance and validation

Read: root `AGENTS.md`; C#, Blazor, validation, API, service, tenancy, and testing
instructions; `add-blazor-ui` with lifecycle/state, JS interop, and form references;
`add-feature-slice` with service results and WASM contract reference;
`add-api-endpoint` with handlers/results, validation/ProblemDetails, and metadata;
`add-domain-persistence` and retry/lock reference; `nova-testing` with browser and
integration references; installed .NET test generation and run-tests skills.
Separate reviewer and test author record their applicable guidance independently.

Final validation results are recorded below. Do not treat the previous round's passing
results as proof of these uncommitted corrections. The independent review record is
[review-round-6-local-review.md](review-round-6-local-review.md).

| Requirement | Regression evidence |
| --- | --- |
| Unknown denial, generic conflict, or wrong/malformed proof retains original payload across reload | `EvaluationUncertainRejectionAfterLostResponseRetainsOriginalOperationAcrossReloadAsync`; `DrawerUncertainRejectionAfterLostResponseRetainsOriginalOperationAcrossReloadAsync` |
| Expired operations stay copyable and never become a fresh Save | `EvaluationExpiredOperationRemainsCopyableAndCannotBecomeFreshSubmissionAsync`; `DrawerExpiredOperationRemainsCopyableAndCannotBecomeFreshSubmissionAsync` |
| Definitive rejection permits a deliberately corrected submission | `DrawerConfirmedNoncommitAllowsDeliberatelyReopenedCorrectedEditAsync`; retained Evaluate validation/version-review cases now supply definitive proof |
| Native and HTTP proof requires the exact operation | `EvaluationMutationRejectionTests`, including `HttpProblemParsingPreservesStrictOperationProofAsync` |
| Membership denial cannot discard a previously committed result | `CommittedHttpOperationSurvivesUnmarkedMembershipDenialAndRecoversOriginalReceiptAsync` |
| Delayed original request cannot execute after rejection and reopening | `DurableClosedRejectionPreventsDelayedOriginalFromCommittingAfterReopenAsync` |
| No definitive proof escapes failed receipt commit | `FailedRejectionReceiptCommitCannotPublishNoncommitProofAsync` |
| Negative receipt survives lost commit acknowledgement | `NegativeReceiptRecoversAfterLostCommitAcknowledgementAsync` |
| Rejected effects are undone before durable rejection | `RejectedMutationRollsBackFlushedEffectsBeforeCommittingNegativeReceiptAsync` |

The initial build passed with zero warnings/errors. The preliminary unit run passed
all 33 added cases but failed nine older fixtures: eight expected no rejection receipt
and one validation mock omitted definitive proof. Those fixtures now assert the
operation-bound rejection receipt while retaining unchanged-note, no-evidence-write,
and no-activity assertions. Draft test names distinguish evidence writes from receipt
storage. The first formatting check found initializer whitespace and new-file encoding;
the scoped formatter completed successfully. Failed attempts are retained in ignored `round6-*.log`
files and are not counted as final passing validation.

The build/unit target was the uncommitted working tree based on `1a9cb8be`, identified
by source manifest SHA-256
`77CF6074EFAFC823BE31054D807070B7B6D96A7ABC60582E7E3C6CB62590B776`.
`round6-source-manifest.log` contains the base plus sorted changed application/test
paths and each file's SHA-256, including untracked files (20 source files). No files
were staged to produce this identity. Documentation is excluded from the source
manifest so final test results can be recorded afterward.

The first integration run completed 601/605 passing, with zero skips. All five new
integration cases passed; four older tag fixtures still assumed rejection leaves no
receipt. The two affected files now distinguish durable lifecycle/archive rejection
from prelookup membership denial, assert the matching operation marker, and retain
no-evidence-write assertions. The rerun uses `round6-source-tested-manifest.log`,
SHA-256 `0EF79F09AF2D2EE6D2FEDACF2CFB0FFBF5F6AC26BB4764650A01432DBDCC6093`:
22 changed source files. Every file in the earlier 20-file manifest remains byte-identical;
only these two additional integration-test files changed after the passing unit run.

| Checks before the retention/push follow-up | Result |
| --- | --- |
| `dotnet build Nova.slnx` | Passed after the final fixture corrections, zero warnings/errors, 42.11s (`round6-build-tested.log`). |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | 3,099 passed, zero failed/skipped, 55.004s (`round6-unit-final.log`). |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | 605 passed, zero failed/skipped, 3m 17.189s (`round6-integration-final.log`). |
| Browser suite | Both full runs failed the same initial-readiness assertion: 171 passed, one failed, zero skipped. First run 6m 12.515s (`round6-browser.log`); final unchanged recheck 5m 56.518s (`round6-browser-final.log`). Focused responsive class: two passed, zero failed/skipped, 50.430s (`round6-browser-responsive-recheck.log`). |
| `dotnet format Nova.slnx --verify-no-changes` | Passed, exit 0 with no diagnostics (`round6-format-final.log`). |

The earlier browser failure was the initial five-second Save-button readiness assertion in
`ThousandParticipantLookupRemainsBoundedAndRecordsSeparateBenchmarkAsync`, before
any mutation or benchmark. The initial attachment/storage path and browser test are
unchanged at that stage. The focused responsive class passed without source, timeout,
or assertion changes. This does not establish the original failure's root cause;
both failed runs are retained. The failure recurred under normal full-suite
concurrency. Those results are failed validation, superseded only by the completed
follow-up checks below; they are not retroactively counted as passing.

Independent review verified all 22 source hashes and the final build/unit/integration logs with
no substantive findings. Source is frozen during the Aspire-backed runs. The full
Aspire-backed runs use a temporary process-scoped keep-awake request, cleared in `finally`; no persistent
power setting changes are made. Theme, CSS, JS, and EF model are unchanged, so the prior
contrast/JS/model checks are not claimed as newly rerun checks.

## Authorized retention and push follow-up

The two responsive scenarios now prove initial interactive attachment through an
editable composer, a handled input with rendered character count, and clearing that
probe before the original Save-enabled assertion. They use the existing shared
`InteractionHelpers`/`BrowserRetryPolicy`; surfaced startup alerts fail immediately.
This broadens the effective startup allowance from a bare five-second assertion to
the shared bounded hydration policy. It does not change the scale-read deadline,
counts, paging, save behavior or screenshot assertions. Independent review approved
the distinction: the scenario is a functional scale/capture check, not a five-second
startup SLO. No application startup code, timeout constants or retry policy changed.

Untracked 141 generated artifacts introduced by this PR, preserving local files.
[artifact-archive.json](artifact-archive.json) records each path, SHA-256 and Git blob
in immutable commit `1a9cb8bed98d251a3b6ca69e510411ca8e058e5d`. A local ZIP also
preserves the original evidence. Failed comparisons and rejected concepts remain
accessible in that historical commit; this is not a history rewrite or a claim that
ignored local files alone provide shared retention. Previously tracked artifacts
from other work are deliberately not migrated here. Approved comp/provenance,
representative captures, design metadata and concise review records remain tracked.

`AGENTS.md` owns retention policy and explicit exceptions for future approved artifacts.
Both Impeccable skill copies contain the same delivery reminder. Read the installed
`skill-creator` guidance for this edit. Checked all 141 local archive hashes, ignore
behavior for generated and retained paths, skill-copy parity, and Markdown link
targets: no removed artifact was the target of a retained Markdown link. Historical
diagnostic paths in prose/JSON are explicitly identified in the evidence index.

The Codex skill passed `quick_validate.py`. That Codex-specific validator rejects the
Copilot copy's existing `argument-hint`, `user-invocable` and `version` frontmatter;
validation of a temporary copy excluding only those provider metadata keys passed.
The committed Copilot metadata was preserved, and the changed retention paragraph
was compared directly across both copies. This is a scoped compatibility check,
not a claim that the unmodified Codex validator accepts Copilot metadata.

Push source identity: 23 changed application/test files in
`round6-push-source-manifest.log`, SHA-256
`A8B588DB191E8514A914F2F0CE7EF96AC6163E6AAC4F0F95769D420355309F89`.
All 22 application/unit/integration files from the preceding passing provider run
remain byte-identical; only the browser readiness helper and retention/docs changed.

| Push check | Result |
| --- | --- |
| `dotnet build Nova.slnx` | Passed, zero warnings/errors, 2m 54.86s (`round6-push-build.log`). |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | 3,099 passed, zero failed/skipped, 1m 55.180s (`round6-push-unit.log`). |
| Integration | Existing round-six run: 605 passed, zero failed/skipped. Source is byte-identical; no additional provider run is claimed. |
| Full browser | Interrupted after about 15 minutes without a final summary; three reported failures (`round6-push-browser.log`). Not passing; total completed-case count unavailable. |
| Changed responsive browser class | Two passed, zero failed/skipped, 1m 45.347s (`round6-push-responsive.log`), using `--filter-class '*CampaignEvaluationResponsiveBrowserTests' --no-build`. |
| `dotnet format Nova.slnx --verify-no-changes` | Passed, exit 0, no diagnostics (`round6-push-format.log`). |

The full push run reported failures in
`DrawerNavigationCrossesPageBoundaryPreservingSequenceAsync_002` (Roster page 2
retained the prior selected participant and the position probe timed out),
`LostHttpResponseReloadReplaysOriginalReceiptWithoutDuplicatingNoteAsync` (WASM
warm-up attachment), and `ClubCrestNonSquareUploadClubDetailPreservesAspectRatioAsync`
(club onboarding interaction). No final summary arrived after several minutes
without output; the owned test run was canceled with Ctrl+C and its runner processes
exited. No root cause is claimed, no full-suite success is inferred, and these
unresolved limitations do not justify weakening assertions or expanding this push
into unrelated UI changes. A separate run of the two changed responsive tests is
recorded below. Full local browser validation remains required before merge.

The final combined tree reduces the PR from 403 to 271 changed files, with 73
Impeccable paths remaining in the PR. All 23 source hashes still match the tested
manifest. Format and staged whitespace checks pass. The complete correction and
retention batch is delivered in one commit; the PR body records that commit's SHA
and leaves the full-browser checkbox unchecked. Continuous watching remains paused.
