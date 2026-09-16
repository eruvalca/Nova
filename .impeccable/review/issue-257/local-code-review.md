# Issue 257 separate local code review

Reviewed the working-tree change against `199b58b35e26fdf44b65e5a3773e88bdc1cc391b` on `codex/issue-257-closed-record`, including the initially untracked Closed record component, contracts, and unit/browser tests. This is an independent source review; no build, test, or Aspire process was started by the reviewer. Current execution evidence belongs in [the authoritative validation record](../../../docs/issue-257-validation.md).

## Findings

### R1 — Medium / verified: unchanged-authority history denial can repeatedly refresh the workspace

Original location: `Nova.UI/Features/Campaigns/Components/CampaignClosedRecord.razor.cs:203`.

```csharp
private Task ReconcileAsync(ServiceProblem problem)
    => problem.Kind is ServiceProblemKind.NotFound or ServiceProblemKind.Forbidden
        || problem.Kind == ServiceProblemKind.Conflict && problem.Errors?.ContainsKey(ClosedCampaignRecordErrors.Integrity) != true
        ? OnLifecycleChanged.InvokeAsync() : Task.CompletedTask;
```

The new history read checks fresh database membership (`PlacementContextQueryService.cs:57–59`). A mounted member removed from the club can therefore receive Forbidden while their old authentication claims still let `CampaignQueryService.GetCampaignDetailAsync` return the same Closed campaign (`CampaignQueryService.cs:169–181`, `TryGetClubId` at 385). Each reconciliation invokes `CampaignWorkspace.LoadDetailAsync`, which calls `InvalidateCloseEvidence` and increments the generation passed to this component (`CampaignWorkspace.razor.cs:701`, `CampaignWorkspace.Close.cs:72`). That change automatically starts another history read. If its denial completes after the parent refresh finishes and clears `_reconcilingLifecycle` (`CampaignWorkspace.razor.cs:1123`), it starts another full refresh. The parent's concurrent-call latch cannot stop this sequential loop.

Reproduction by controlled tasks: render the composed workspace with a selected participant; return a history Forbidden; let detail/readiness/roster refresh settle unchanged; only then release the replacement history Forbidden. Repeat the last step and observe another detail call and generation increment. This is a new failure path introduced by the Closed child, rather than a claim that all existing service authorization should be redesigned here.

Fix: bound automatic reconciliation for one unchanged authority/failure episode, independent of refresh generation. Preserve intentional manual retry and later real lifecycle transitions; do not reset a history-denial latch merely because a neighboring roster read succeeds. Add the delayed, parent-composed regression so the callback actually changes the generation. The existing isolated integrity test correctly protects the structured integrity-conflict case, but does not exercise this composition.

Disposition: resolved. `CampaignClosedRecord` now keeps a per-region reconciliation set under stable `Owner` + `CampaignId`; refresh generations do not clear it, successful neighboring regions cannot clear it, and explicit regional retry or that region's successful read re-enables reconciliation. `CampaignWorkspaceTests.DelayedHistoryDenialAfterUnchangedParentRefreshSettlesAndRemainsRetryableAsync` supplies delayed history results through the composed parent and asserts bounded detail/history calls plus successful retry. The reviewer re-inspected the final guard and test; the implementing agent reports this regression passed in the full unit run. The remaining unit rerun concerns the separate timestamp fixture described below, not this finding.

### R2 — Medium / verified: existing Closed read fixtures omit newly required closure events

Original changed behavior: `Nova/Features/Campaigns/EffectivePlacementQueryService.cs:181`.

```csharp
if (closingEvent is null || string.IsNullOrWhiteSpace(closingEvent.ActorDisplayName))
{
    return ServiceProblem.Conflict("The Closed campaign contains an incomplete closure record.",
        new Dictionary<string, string[]>(StringComparer.Ordinal) { [ClosedCampaignRecordErrors.Integrity] = ["Incomplete closure record."] });
}
```

The stricter invariant is appropriate, but these existing fixtures still manufacture Closed state without its durable event:

| Seed | Concrete affected consumer |
| --- | --- |
| `Nova.Integration.Tests/Http/EffectivePlacementHttpTests.cs:403`, `SeedAsync` | Closed variants of `OrdinaryMemberReadsPopulatedPlacementBodyWithOmittedOptionalQueriesAsync`, discovery/paging tests, and foreign-identifier tests expect 200/404 but now encounter closure-integrity 409. |
| `Nova.Browser.Tests/CampaignWorkspaceBrowserTests.cs:220`, `PrepareClosedRecordAsync` | `ClosedRosterKeepsArchivedLocalEvidenceAndReportsIncompleteHistoryAsync` expects 50 Closed rows and archived participant placement context; both use the stricter read. |
| `Nova.Browser.Tests/PlacementSeed.cs:155` and `:172` | `CampaignPlaceBrowserTests.ClosedCampaignStaysReadOnlyForEveryRoleAsync` expects Closed queue rows; `CampaignPlaceInvalidStorageBrowserTests.ClosedInvalidRecoveryDataCanBeDiscardedWithoutChangingFinalPlacementsAsync` reads the same Closed campaign through queue/selection. |
| `Nova.Integration.Tests/Http/SeedingHelpers.cs:247`, automatic previous-campaign closure | `CloseoutSeed` creates its ready campaign, then another campaign, leaving `ReadyCampaignId` Closed without an event. `CampaignCloseoutBrowserTests.cs:476` navigates to its Close board. Existing assertions inspect the lifecycle restriction and would miss the broken final-record region. |

The test `CampaignTestSeedInterceptor` repairs opening metadata and decision attribution only; it does not append events. The provider-specific fixture was updated in the original diff, but these sibling consumers were missed.

Fix: explicitly create matching immutable closure evidence in fixtures representing a valid Closed campaign, or close through the real lifecycle service when that fixture satisfies its prerequisites. Keep deliberately incomplete records invalid, and do not relax the production integrity guard or globally conceal invalid seeds. Run the affected HTTP/browser scenarios under the repository's serial suite gate.

Disposition: resolved in source, with integration confirmation. Explicit tenant/campaign-scoped closure events now accompany the affected HTTP fixture, `PrepareClosedRecordAsync`, both `PlacementSeed` closures, and the shared previous-campaign closure. Existing activity-count assertions inspected either scope to the fresh campaign/player or capture an initial baseline; no additional count defect was established. The reviewer confirmed the missing `Nova.Entities` import is present. The implementing agent reports the build, full integration suite, and final full browser behavior run passed. R3 below remains an unresolved validation incident despite that passing rerun.

### R3 — Unresolved validation incident: modified-click new tab was not observed

`CampaignEvaluationCaptureBrowserTests.ModifiedPlayerClickOpensNewTabWithoutChangingOriginalDraftAsync` timed out in the initial full browser run waiting for the context's new-page event after Ctrl-click. The original URL and draft were unchanged and only the original page remained. That run did not capture this test's pointer/modifier events, so it cannot establish whether native activation was absent, mis-targeted, or canceled. Bounded inspection found a plain native result anchor (`CampaignEvaluationPanel.razor:64`), a guard that explicitly ignores modified clicks (`evaluationNavigationGuard.js:40–41`), an attachment-dependent enabled Save button, and a context-scoped page-event wait installed before the click. No causal production defect was established; unchanged source is not proof that the failure is harmless.

The reviewer inspected the exact diagnostic-only diff in `CampaignEvaluationCaptureBrowserTests.cs`: it attaches the existing navigation/pointer diagnostic helpers, includes their output and the ARIA snapshot in the timeout report, and adds button/modifier fields. It changes no user action, assertion, timeout, retry, or skip. The existing history wrappers delegate to the original methods. This improves failure evidence without weakening enforcement, but instrumentation can change timing and is not a demonstrated fix.

The implementing agent reports both an isolated pass and a passing final full browser behavior run. Green reruns do not resolve the original unexplained failure or establish contention as its cause. The remaining diagnostic limitation is that `defaultPrevented` is sampled in document capture, not after dispatch; an earlier `stopImmediatePropagation` can also hide that click from the listener. A recurrence needs final-event cancellation plus actual target/composed-path and pointer geometry evidence to distinguish the causes.

Readiness follow-up: the original failing tool output was recovered and independently inspected; it confirms the recorded timeout and unchanged source state but contains no additional causal evidence. The new diagnostic-only enhancement moves observation to window capture, records the actual target/composed path/hit target/geometry, and uses a later task to observe final cancellation and anchor connection/geometry. This closes the identified observation gaps for a recurrence without changing actions, assertions, timeouts, retries or skips. Independent review found no demonstrated causal defect; instrumentation can perturb timing and remains evidence collection, not a fix. Current execution results are in the authoritative validation record.

Disposition: open and merge-blocking until the original failure is explained and appropriately resolved. A draft PR may expose the completed work and evidence for review; this disposition does not authorize merging or weakening the test. Current execution details and the original failure remain in the authoritative validation record.

## Checked paths and limits

- Main roster identity, outcome totals, latest close event, filtering, and rows share the existing repeatable-read transaction on PostgreSQL. Integrity precedes discovery. Archive state is projected separately from saved outcome authority.
- The new client presence/relationship checks match the producer shape, including nullable non-assignment team lifecycle. Structured integrity errors survive the existing HTTP ProblemDetails conversion.
- `RequireClosed` prevents a reopened campaign from broadening selected history to the Active cross-campaign scope. The client also rejects foreign-campaign history for that request. Re-inspected `PlacementContextHttpTests.AssertClosedContextGuardAsync`, called by `MemberReadsPlacementHistoryWithoutCursorAndInvalidCursorIsRejectedAsync`: a real member request uses `RequireClosed = true` against the Active fixture and asserts HTTP 409, `application/problem+json`, body status 409, and a nonblank trace ID. The implementing agent reports that boundary test passed in the full integration suite. No new mutation owner or schema was introduced.
- Empty selected-participant results are a local error, not an automatic lifecycle refresh; the initially suspected invalid-participant-link refresh loop was disproved by following `FilterDiscovery` and `LoadHistoryAsync`.
- Request counters plus owner/campaign/generation/query keys prevent prior async completions from publishing into replacement state. Persisted region keys cover success and error ownership. The existing parent handles authority changes and disposes/clears prior tenant content.
- Native GET filters, roster/history links, bounded counts, and the evaluation return preserve Closed discovery. Effective interactivity is inherited from `CampaignEntry`'s `InteractiveAuto`; shared `CampaignLifecycleActions` remains the command owner.
- Other direct historical browser seeds in the notebook, Place correction, and Active Close review supply inherited placement context; no current navigation into those historical Closed records was found, so they are not independently reported as broken.
- The HTTP client Active-scope control originally used `DateTimeOffset.UnixEpoch`, which violates the existing `PlacementHistoryValidation.IsValid` requirement `OccurredAt > DateTimeOffset.UnixEpoch`. The reviewer confirmed its fixture now uses `UnixEpoch.AddDays(1)` while production validation remains unchanged. The implementing agent reports the confirming full unit rerun passed.
- Execution results above are attributed to the implementing agent's tool-result report, not independently executed by this reviewer. Exact commands, counts, tested inputs, and final results belong in the authoritative validation record. Final formatting, full unit/integration results, the final full browser behavior run, and design finish are reported complete; R3's unexplained initial browser failure still prevents merge.

## Guidance consulted

- `AGENTS.md`; applicable C#, API, Blazor, service, validation, EF/tenancy, season, placement, UI-design, and testing instructions in `.github/instructions/`.
- `code-review/SKILL.md` and its local-review, doctrine, and checklist references.
- `.agents/skills/add-feature-slice/SKILL.md` and input/validation, service-result, and WASM contract references; `add-api-endpoint/SKILL.md` and route/handler/metadata/validation references.
- `.agents/skills/add-blazor-ui/SKILL.md` and placement, render-mode, lifecycle/state, parameter/event references.
- `.agents/skills/nova-testing/SKILL.md` and transition, bUnit, SQLite, Aspire integration, and browser-suite references; relevant test-anti-pattern/assertion guidance.
- Provider-safe query-construction reference; the issue 257 surface brief and changed product/journey/surface scope documentation. Export/print remain outside the agreed scope.

Review verdict: no remaining in-scope production defect was established after re-inspecting the fixes. R1 and R2 are resolved. R3 remains an unexplained, merge-blocking validation failure despite passing diagnostic reruns; keep the PR in draft pending its explanation and disposition.
