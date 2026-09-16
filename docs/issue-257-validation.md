# Issue 257 validation

Scope: [#257](https://github.com/eruvalca/Nova/issues/257), the in-app Closed campaign record. One PR on `codex/issue-257-closed-record`, based on `199b58b3` (#281). The user approved inline participant history and search/outcome discovery. `gh-stack` and its stack-design guidance were reviewed: this is one coherent UI/read-contract change with no useful independent prerequisite, so no stack is used.

## Contract and boundaries

- The existing Closed roster query returns local final decisions, live player/team archive metadata, whole-campaign outcome totals and the latest stored closing event inside its existing repeatable-read transaction. Integrity precedes filters. Each response has its own snapshot; separate pages and history/activity requests do not claim one shared snapshot.
- The Close board uses 50-row search/outcome discovery, inline campaign-only history in 20-event pages, and up to 50 recent close/reopen events. Native GET forms and links preserve sibling URL context; interactive attachment restores owned startup success/error state. Regions retry independently and stale owners cannot publish.
- `CampaignLifecycleActions` remains the only lifecycle command owner. A Closed-only history request conflicts after reopen. Existing evaluation navigation is read-only and returns to Close.
- No schema, dependencies, Sass, runtime configuration, export, printing or second activity store. Original decisions/actor snapshots remain historical; player/team display metadata are not new immutable profile snapshots. The import-error sentence in the intake brief remains owned by #218, as #257 explicitly directs; product and journey scope now exclude that download and retain import/template/in-app review.

## Guidance actually read

- [AGENTS.md](../AGENTS.md), [.github/pull_request_template.md](../.github/pull_request_template.md), PRODUCT.md, DESIGN.md, existing #256 Closed capture and validation, current closeout/campaign-spine briefs, and the [scoped direction](../.impeccable/surfaces/issue-257-closed-record.md).
- `.github/instructions/`: csharp-conventions, blazor-architecture, ui-design, bootstrap-theme, placement-decisions, season-lifecycle, service-layer, validation, api-endpoints, ef-core-tenancy, functional-core and testing instructions.
- `.agents/skills/`: add-feature-slice with input-validation/service-result/WASM-client references; add-api-endpoint with endpoint/client/contract references; add-blazor-ui with placement/render-mode/lifecycle-state references; nova-testing with transition, bUnit, SQLite, Aspire integration and browser-suite references; impeccable with shape/new-work/craft-floor/operate references.
- Installed `gh-stack` skill and stack-design; dotnet-test run-tests, filter-syntax, code-testing-agent and unit-test-generation prompt; find-untested-sources. Roslyn pairing completed once (566 source/387 test files); research/plan and raw pairing live in the worktree's Git-private `testagent` scratch directory. No coverage percentage is claimed.

## Current validation

Implementation revision: `b17ba2001710a9c7304330dbe23019b52f93fc7a`. This commit contains the inputs tested above, including the diagnostic-only browser addition. The following evidence-stamp commit changes only this record and the capture manifest; it reuses those results without changing application, test, build, runtime or generated inputs. `origin/main` still equals the base revision; no incoming changes were added after validation.

**Draft PR; merge remains blocked on the unexplained modified-click validation incident.** The latest full browser run passes, but that does not resolve the original failure. No merge is requested or performed.

Tested input: `199b58b35e26fdf44b65e5a3773e88bdc1cc391b` plus the issue-257 application/test changes in the implementation revision. The final unit and selected browser runs used an inventory of 1,225 application/test/build inputs, including generated theme CSS, with SHA-256 `1151abba52a0ad7547a61bc6e4a7d8f441e6870e1fa5dc09e0960f2985709146`. All entries still matched after the initial full browser run. Only `Nova.Browser.Tests/CampaignEvaluationCaptureBrowserTests.cs` subsequently changed to add failure diagnostics; its actions and assertions are unchanged. The rebuilt final browser inventory is `f3efcc288958ba8fa25ec6c0c4fcc058a0145a4633e48dede9a919681e314caf`; all 1,225 entries still match after the passing full run and implementation commit.

| Check | Command / evidence | Current result |
| --- | --- | --- |
| Build | `dotnet build Nova.slnx` | Pass, 0 warnings/errors after corrections |
| Format | `dotnet format Nova.slnx --verify-no-changes --verbosity diagnostic` | Final pass after browser diagnostic addition; 0 of 1,036 files formatted |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | 3,579 passed, 0 failed/skipped |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | 630 passed, 0 failed/skipped |
| Selected browser | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-class '*CampaignClosedRecordBrowserTests'` | 2 passed on final inputs, 0 failed/skipped; four curated captures enabled with `NOVA_CLOSED_RECORD_EVIDENCE` |
| Full browser | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | Final run: 199 passed, 0 failed, 8 existing opt-in capture skips; original failure disposition remains open |
| Modified-click diagnosis | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-method '*ModifiedPlayerClickOpensNewTabWithoutChangingOriginalDraftAsync'` | 1 passed, 0 failed/skipped; diagnostic evidence only |
| Independent local review | [Review artifact](../.impeccable/review/issue-257/local-code-review.md) | R1/R2 fixed and independently re-reviewed; no remaining in-scope production defect established |
| Visual finish/documentation | [Finish review](../.impeccable/review/issue-257/finish-review.md), [capture manifest](../.impeccable/review/issue-257/capture-manifest.json), DESIGN.md and `.impeccable/design.json` | Independent **ship (scoped PASS)**; scoped documentation merged and validated |

Aspire suites run serially, with each fixture starting its own AppHost. Source and generated inputs remained fixed during browser runs. No separately started AppHost is used. The integration pass precedes whitespace-only formatting of four fixture files and a unit-only timestamp correction; application and integration behavior are unchanged. The passing build, unit, and browser checks include those edits. The subsequent diagnostic-only browser change leaves all unit/integration inputs unchanged, so their full results are reused. The eight existing skips are opt-in accessibility/Place capture jobs, with their environment flags unset; no behavior scenario is skipped and the issue-257 captures ran explicitly.

## Behavioral evidence

| Requirement | Named evidence |
| --- | --- |
| Original local outcomes versus later effective decisions | `ClosedRosterPreservesLocalOutcomeTeamAttributionAndTokenAfterSupersessionAsync`; existing effective-placement query and consumer tests |
| Whole totals, stored closer and archived context | `ClosedRecordKeepsWholeTotalsAndOriginalCloserWithArchivedFilteredRowsAsync`; `FinalRecordShowsOriginalEvidenceArchiveContextAndIndependentTotals` |
| Integrity before discovery and strict client metadata | `MissingClosureIsIntegrityConflictEvenWhenDiscoveryWouldReturnNoRowsAsync`; existing incomplete-decision discovery tests; `ClosedRecordRejectsMissingRequiredSnapshotFieldsAsync`; `ClosedRecordRejectsContradictoryMetadataAsync` |
| Coherent lifecycle/decisions/totals/closing attribution per response | Extended `ClosedRosterSnapshotRetainsClosedLifecycleAndDecisionWhenReopenedBetweenReadsAsync` in PostgreSQL suite |
| Closed-only history with no Active broadening | `ClosedOnlyHistoryRejectsActiveCampaignWithoutBroadeningScopeAsync`; `ActiveHistorySpansSavedParticipationsWhileClosedHistoryKeepsItsCampaignAsync`; real HTTP 409/ProblemDetails guard in `PlacementContextHttpTests.AssertClosedContextGuardAsync` |
| Native filters, off-page selection, history cursor and evaluation handoff | `NativeAndInteractiveDiscoveryPreserveSiblingsAndResetSelection`; `SelectedHistoryUsesExactParticipantAndClosedScopeWithNativeCursorAndEvaluation`; both `CampaignClosedRecordBrowserTests` journeys |
| Independent failure/retry, empty/stale page and integrity display | `ActivityAndHistoryFailuresRetryIndependentlyOfVerifiedRecord`; `EmptyResultsDistinguishCampaignFilterAndStalePage`; `IntegrityFailureHidesFinalEvidenceAndDoesNotCreateRefreshLoop` |
| Async ownership and prerender/interactive restore | `LateRecordFromPreviousOwnerCannotReplaceNewOwnerAsync`; `MatchingPersistedSuccessOrFailureSkipsDuplicateStartupReads`; parent-composed `DelayedHistoryDenialAfterUnchangedParentRefreshSettlesAndRemainsRetryableAsync`; existing workspace authority and lifecycle suites |
| Reopen removes final view, reclose refreshes, sole command owner | Extended `AdministratorCancelsThenClosesAndReopensWithFocusAndRetainedOutcomesAsync`; existing lifecycle policy/service/retry/component suites |
| Member visibility and inaccessible/Draft/Active routes | Existing effective-placement query/service HTTP authorization tests and Closed readability suite; member browser journey |

## Material failure dispositions

- Initial build: Razor parsed `@page` as a directive; explicit expressions corrected it. Analyzer findings required documented disposal-only focus catches, extraction of reopen URL cleanup, and a safe source-validation ordering. Follow-up build passed.
- New test build: explicit `ServiceResult` payload types, nullable assertions, ordinal string comparisons and async test APIs corrected fixture compilation. No production checks or analyzers were suppressed.
- Initial multi-value class selection repeated `--filter-class` and ran zero tests; this is not a pass. A single exact class selection passed all 11 new component cases; the full suite is the authoritative result.
- Initial full unit run: 3,571 passed / four failed. The missing-closure fixture incorrectly attempted to delete append-only events; it now creates an otherwise Closed campaign without a closing event. Two numeric-sort cases retained an Assigned team archive value after switching the row to Not selected; fixtures now update team state and totals coherently. The directory link expectation now matches the absolute route. All four pass in the final full unit run. No application behavior was relaxed.
- The post-review unit run passed 3,578 cases, including the parent-composed delayed-denial regression, and failed one new client test because its UnixEpoch fixture violates existing history validation. Changing only that fixture to UnixEpoch plus one day preserves the foreign-campaign/Closed-only assertions. The final full run passes all 3,579 cases.
- Initial format verification found whitespace in four updated fixture files. `dotnet format whitespace Nova.slnx --verbosity minimal` applied the corrections; the subsequent build and final format verification pass. Missing fixture imports and a test helper exceeding the statement limit were also corrected before the passing build.
- The first full browser run passed 198 behavior cases and failed only `ModifiedPlayerClickOpensNewTabWithoutChangingOriginalDraftAsync`, with eight existing opt-in capture skips. The isolated diagnostic case and complete rebuilt suite subsequently pass. The original failure is still unexplained: see the separate review's R3 incident/disposition. No contention or hydration cause is inferred, and no retry, timeout, assertion or skip was changed.

## Review and remaining limitations

No migration/model delta is introduced; existing model checks remain part of the full suites. Original #256 comp-comparison history and its user-approved actual-shell disposition remain unchanged; this is a scoped extension of that composition, with no newly approved comp or new fidelity score claimed. Separate review findings and their evidence-backed dispositions are recorded once in the linked local review; current execution results in this record supersede its contemporaneous pending-check notes.

The first desktop/phone capture round passed both browser journeys. One batched visual correction expands phone filters to full width, prevents table header word splitting, and makes scrollable history lists keyboard-focusable. The second capture round passes both journeys and supplies the four final captures. The changed-target detector ran once on five Razor/CSS targets and returned zero findings. It also reported the inherited #256 local `COMP_ROUND_OPEN` hero state; that older comp comparison and approved actual-shell disposition remain preserved, and no new comp fidelity pass is claimed for this scoped extension. The separate finish reviewer returned ship with no material fixes against the incumbent Fieldhouse system; a separate quality-bar card was unavailable.

The fresh documenter merged only the implemented Closed record and Campaign-Local Evidence Rule into DESIGN.md and its sidecar. JSON parsing, the bundled schema-version reader and DESIGN parser, explicit component-shape checks, exact rule matching, evidence links, incumbent-preservation assertions, and diff checks pass. The skill does not ship a full JSON-schema validator; none is claimed. No unrelated sidecar/token drift was changed and no shipping raster asset was added.
