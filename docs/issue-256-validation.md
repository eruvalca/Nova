# Issue #256 validation

Status: review-round-one fixes and required local validation complete. The tested tree is `5e64eb18907fa9e2b32e2fff122e0c636d704199` plus the complete round-one working diff. The capture manifest records normalized production/test fingerprints to identify those inputs without a second evidence-only commit. Actual PR base: `3486196375e01fc870ce4fba0ac918f3a317773d`.

Round-one production fingerprint: `d94fa7a31bf6651958a28aa0f391e6b49e6311bd36d558bb9bfde7254a0301be`; test fingerprint: `e0644c5792f5d0f1a61095aa4667dcd7361ea56e0ec9f5e19fa3509390aba9f9`. The manifest includes all 35 changed production files, 31 test files and nine curated captures. Later record/review/capture metadata edits do not change the tested application or browser inputs.

The original implementation (`542e28eccb81637208740fa8a813b0abf67eec33`) passed build/format, 3,449 unit tests, 628 integration tests and 193 browser behavior tests (eight existing opt-in capture skips). Those results are historical; the current results below identify the review-round inputs. A fresh automatic Copilot review and passing CI are still required after the single round-one push.

## Scope and handoff

Active Close roster review, authoritative local-outcome readiness, correction returns, and the single administrator close/reopen action shared with #257. One 50-row page, with local team/outcome groups and whole-campaign totals. No export, printing, receipt storage, schema migration or deployment. #257 retains ownership of the full Closed record and product-document reconciliation.

`CampaignLifecycleActions` is the sole UI mutation owner. Its `Owner`, `Evidence` and `RefreshEvidence` contract binds review/confirmation/dispatch to workspace identity and authoritative detail/readiness. #257 should consume this component rather than add another close/reopen path. `CampaignLifecycleEvidence` carries owner/generation/detail/readiness; the workspace owns refreshes, while roster loading is independent. The required `CampaignLifecycleCapabilities` wire fields and shared `CampaignReopenPolicy` are the preview/command handoff. The existing Closed metadata presentation remains until #257 replaces it.

Readiness uses one database snapshot for local totals, policy blockers, separately named Needs placement and lifecycle capabilities. Roster pages are independently coherent; navigation does not promise a shared cross-page snapshot. `closeSearch`, `closePage` and `closeBlocker` are independent of other destinations' filters, and `returnToClose` preserves the correction handoff.

## Guidance read

- AGENTS.md and all applicable `.github/instructions/` rules: C#, Blazor, UI/theme, service, API, validation, tenancy, lifecycle, placement, functional core, testing and observability.
- `add-feature-slice` and its input, service-result and WASM references; `add-api-endpoint` and its route, handler, authorization and validation references.
- `add-domain-persistence`, retrying-mutations-and-locks and query-construction; `add-blazor-ui`, render-mode-decision and lifecycle-and-state.
- `nova-testing`, transition-evidence and blazor-component-tests; Impeccable `SKILL.md`, shape, new-work, visualize and operate; imagegen `SKILL.md`.
- Issues #163, #256 and #257; PRODUCT.md, DESIGN.md, closeout brief and campaign-workspace-roster handoff; PR template.
- `nova-testing` unit/SQLite, Aspire integration and browser suite references; Aspire orchestration and one-off browser validation guidance; .NET run-tests and code-testing-agent guidance. Tests use the repository's xUnit v4/MTP commands.
- Impeccable craft-floor before UI edits; finish-review role and documenter's document reference. The documenter separately read the actual component/Razor/CSS/lifecycle code and Sass palette before updating the scoped DESIGN/sidecar rules.

## Transition evidence

| Start and action | Observable result | Boundary/evidence |
| --- | --- | --- |
| Active participant has inherited placement but no local outcome | Missing local decision still blocks; zero Needs placement never substitutes for readiness | `CampaignCloseReviewQueryTests.ExactBlockersIncludeInheritedAndArchivedPlayersAndOverlapWithoutFilteringTotalsAsync`; `CampaignCloseoutPanelTests.BoardKeepsZeroNeedsPlacementSeparateFromLocalOutcomesAndExactBlockerLinks`; member browser journey |
| Missing, incompatible and archived-team conditions overlap | Exact condition-keyed affected sets; whole-campaign totals remain unfiltered | `CampaignCloseoutQueryServiceTests` blocker cases; `CampaignCloseReviewQueryTests` |
| Duplicate names, teams and terminal outcomes span pages | SQL ordering has stable IDs; literal search and blocker binding respect API bounds | `CloseoutOrderGroupsLocalTeamsThenTerminalOutcomesAcrossBoundedPagesAsync`; `EffectivePlacementHttpTests.CloseReviewBindsExactBlockersAndStableSqlPagesWithoutNarrowingCountsAsync` |
| Ordinary member, foreign tenant, Draft, malformed client payload | Authorized reads only; required capability fields and count consistency fail closed | `CampaignCloseoutHttpTests`, `EffectivePlacementHttpTests`, `HttpCampaignCloseoutQueryServiceTests.MissingCapabilityFieldFailsClosedAsync` |
| Closed campaign against current season/opening order/another Active | Shared policy gives structured reason and command enforcement | `CampaignReopenPolicyTests`; existing lifecycle/season foundation tests |
| Review, then Cancel | Fresh read precedes confirmation; no mutation | `CampaignCloseoutPanelTests.ReviewRefreshesAndCancelSendsNoMutation`; administrator browser journey |
| Pending command, duplicate action, authority/evidence replacement | One dispatch; obsolete feedback and confirmation rejected | `DuplicateCommitAndDelayedAuthorityResponseCannotReappearAsync`, `NewEvidenceInvalidatesAnOpenConfirmation`; workspace authority regressions |
| Pending command, actual renderer disposal, late success ignoring cancellation | Captured token canceled; no further evidence refresh or replay | `DisposedLifecycleAttemptCannotRefreshOrPublishLateSuccessAsync` |
| Required preflight or post-command refresh fails | No available action until read recovery; attempt feedback retained separately | `FailedPreflightLeavesActionsUnavailableUntilReadRetry`; composed `CampaignWorkspaceTests.Close` cases |
| Transport/server uncertainty or unrelated timeout | Discard confirmation; read current state; never replay or infer success | `UnknownServerResultRefreshesStateWithoutReplayOrFalseSuccess`, timeout/transport component cases, lost-response browser journey |
| Close → Place correction → Return to Close; search/history navigation | Exact affected set, explicit inherited reassignment, independent Close state and refreshed counts | `CampaignCloseBrowserTests.MemberReviewsGroupedLocalOutcomesAndReturnsFromInheritedCorrectionAsync`; native Evaluate hidden-field regression |
| Two administrators or lifecycle/placement/enrollment contenders | Locked readiness and one-Active rules; no duplicate lifecycle events | `CampaignLifecyclePostgresTests`, `SeasonFoundationPostgresTests`, `PlayerImportCommitPostgresTests` |
| Persisted role removed while lifecycle command waits | Rechecked authority rejects close/reopen; status and activity unchanged | `PersistedAdministratorRoleIsRecheckedAfterWaitingForLifecycleLockAsync` |
| Transient failure before commit | Fresh-context safe retry | `CampaignLifecycleRetryTests` close/reopen retry cases |
| Commit succeeds, acknowledgement lost, then opposite transition occurs | Unknown attempt result; no automatic replay or state-based success inference; exactly one event per transition | `LostLifecycleAcknowledgementAfterOppositeTransitionDoesNotReplayOrInferSuccessAsync` |
| Administrator closes/reopens; second member session reloads | Focus current lifecycle heading, retain outcomes, remove Closed editing | Administrator and lost-response browser journeys |

Primary test files: [query cases](../Nova.Unit.Tests/Campaigns/CampaignCloseReviewQueryTests.cs), [component cases](../Nova.Unit.Tests/Campaigns/CampaignCloseoutPanelTests.cs), [composed workspace cases](../Nova.Unit.Tests/Campaigns/CampaignWorkspaceTests.Close.cs), [lost acknowledgement](../Nova.Integration.Tests/Data/CampaignCloseRecoveryTests.cs), [authority race](../Nova.Integration.Tests/Data/CampaignLifecycleAuthorityTests.cs), [browser journeys](../Nova.Browser.Tests/CampaignCloseBrowserTests.cs), [lost response](../Nova.Browser.Tests/CampaignCloseRecoveryBrowserTests.cs).

## Checks and tested revision

| Check | Exact command | Current result |
| --- | --- | --- |
| Build | `dotnet build Nova.slnx` | Passed, 0 warnings/errors |
| Format | `dotnet format Nova.slnx --verify-no-changes` | Passed, 0 files require changes; diagnostic run |
| Unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | Passed 3,481/3,481, no skips, round-one inputs |
| Integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | Passed 628/628, no skips, round-one inputs |
| Browser | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | Passed 195/195 behavior tests; 8 existing opt-in capture jobs skipped (203 total), round-one inputs |
| Diff hygiene | `git diff --check` | Passed before final record update |
| Migration model | No model/entity/configuration/migration change | Not applicable; PostgreSQL migration/schema regressions included in integration suite |

Build-capable commands were serial. Integration and browser suites ran serially; both provisioned their own Aspire host. App source/generated assets were fixed throughout each browser run. No Sass/package change; theme rebuild/contrast-specific gate is not newly applicable.

The eight browser skips are existing environment-gated screenshot jobs, not skipped behavior tests. Round-one representative captures are produced by the full browser run with `NOVA_CLOSE_EVIDENCE` set; the retained comparison input is unchanged. Documentation/evidence curation after the tests does not change application or test source.

## Material failures and review dispositions

| Round-one source | Disposition and evidence |
| --- | --- |
| Self-review lifecycle acknowledgement | Require the existing HTTP 204 completion contract for close/reopen. Unexpected 200/202 responses become unknown server results; HTTP client cases and the browser 202 interception prove no false completion or replay. |
| Self-review + suppressed blocker validation | Reject healthy local Assigned rows for blocker-filtered results; validate archived-team membership separately. `TeamArchived` can conceal an overlapping incompatibility, so eligibility validation rejects only what the returned evidence disproves. HTTP client cases retain valid overlap and prove unfiltered rows still pass. |
| Self-review + suppressed roster startup | Persist the owned bounded page or retryable error across interactive attachment; keep refresh generation stable when readiness finishes. Restore/owner/search component cases and composed roster call counts cover startup reuse and reload. |
| Self-review phone targets | Retry roster/readiness, empty-result recovery and Return to Close now have minimum 44px height. Browser measurements exercise the phone states; curated captures retain the affected composition. |
| Self-review + published surface contract | Restore the workspace THESIS/OWN-WORLD/STORY/FIRST VIEWPORT/FORM comment and describe approved Close composition B within the existing shell. |
| Suppressed ready-state copy | Scope the compatible active-team statement to Assigned participants, preserving the distinct Not selected and Withdrawn outcomes. The settled administrator confirmation capture shows the corrected explanation with whole-campaign totals. |
| Published roster/preflight + suppressed dispatch cancellation | Preserve component-owned cancellation in all three paths. Existing owner guards already prevented teardown error publication; explicit propagation now also satisfies the cancellation rule. Renderer-disposal tests cover canceled preflight, dispatch and roster retry. |
| Published refresh availability + suppressed stale confirmation | New parent evidence restores availability; observed replacement evidence cannot reopen an obsolete confirmation. Independent review additionally found stale failed-refresh cleanup, fixed across review/retry/post-command recovery. Controlled delayed success/null cases and composed synchronous/delayed refresh tests pass. Exact evidence identity remains required at dispatch. |
| Published blocker URL boundary | Normalize recognized values by trimming/casing and discard unknown values before querying/serializing. The API already accepted recognized case variants; the fix aligns the selected control and prevents unknown bookmarks from causing avoidable read failures. URL and composed query tests cover these cases. |
| Suppressed duplicate return read | Evaluate-to-Close full refresh consumes the pending roster reload once. The composed regression asserts exactly one standard roster read and one independent Close roster read. |

- The first round-one build exposed test-construction, test-context and async-event analyzer errors; corrected without weakening rules. The focused run then exposed the valid synchronous preflight case rejected by an over-strict identity check, plus a null HTML attribute assertion. The final ownership predicate accounts for the parent’s queued render while retaining dispatch identity; all 314 focused cases pass. The independent reviewer reinspected this adjustment and the narrow reflection-only CA1812 exception; see [original local review and reinspection](../.impeccable/review/issue-256/local-code-review.md).

- The [reloaded-guidance self-review](https://github.com/eruvalca/Nova/pull/281#pullrequestreview-5218594527) found five defects, all addressed in round one. The [Copilot review](https://github.com/eruvalca/Nova/pull/281#pullrequestreview-5218588916) published five comments and six suppressed findings. All eleven were inspected; its workflow stored-comment locations and classifier count agree. Overlapping findings are combined in the dispositions below; suppression was not treated as resolution. Each thread will be resolved only after the verified fix and explanation are published.
- This review reloaded AGENTS.md, all 13 repository instruction files, all repository SKILL.md entrypoints, applicable feature/API/persistence/Blazor/testing references, the code-review skill and references, and all canonical/Codex/Copilot agent definitions. Separate fresh contexts reviewed backend contracts and browser/design evidence. Agent copies agree after documented provider substitutions. All 58 source hashes and six capture hashes match; retained suite logs agree with the recorded results. Current CI Build and Unit Tests passed. No suites were rerun for this static review. The original implementation base `8e89f7d` to actual PR base `34861963` changes guidance/template/documentation only, with no application or browser-suite input change; the existing browser evidence remains applicable.

- Initial PostgreSQL run: 620/625. A changed conflict phrase and fixtures watching the former lock order caused five failures. Restored the expected phrase, seeded persisted administrator identities/roles, and aligned contention gates with membership-before-lifecycle locking. Confirmed by 628/628 full integration pass.
- Initial browser run: inherited correction did not await the changed form or confirm its existing reassignment checkpoint; the lost-response fixture expected HTTP 200 instead of the existing 204 contract, leaving interception pending. Corrected fixtures; all three focused journeys passed. Native history uses `WaitUntilState.Commit`, followed by observable page/URL assertions, as required by the independent reviewer.
- The first broad browser run was interrupted after eight legacy Close assertion failures (old checklist selectors, direct Close, and old confirmation/alert labels). Updated those scenarios to the approved review/commit and exact blocker/participant flow; the complete Close group then passed 18 behavior tests. No behavioral test was skipped. Review required—and reinspected—explicit non-admin Review-control denial and eligible phone lifecycle touch-target measurements so the replacements retain the former coverage.
- The disposal test initially called bUnit wrapper `Dispose`, which does not run component async disposal. Reviewer inspection of installed bUnit/ASP.NET source established the correct renderer disposal boundary. Awaiting `DisposeComponentsAsync`, asserting the dispatched token is canceled, then releasing the delayed result proves the intended invariant; the full 3,449-test unit suite passes.
- Five independent implementation findings were fixed and reinspected: shared empty-filter validation, postcommit context-disposal guard, bound owner/error parameters, completed-refresh rendering, and feedback retention through failed required refresh. See the [independent local review](../.impeccable/review/issue-256/local-code-review.md) for original scope and dispositions. Final production-delta review found no material production issue.
- Build/format findings were corrected without weakened rules: missing imports/serializer reuse, standard bUnit disposal API and an overlong browser test split into a named history helper. No checks were disabled.

## Design evidence and limitations

- Approved [composition B and provenance](../.impeccable/mocks/issue-256-compact-review.json), [surface brief](../.impeccable/surfaces/issue-256-closeout.md) and [representative captures](../.impeccable/review/issue-256/capture-manifest.json).
- Exact-frame comparison: **71.3%, failed** the 72% gate; [retained report](../.impeccable/review/issue-256/diff/final/report.json). `captures/comparison.png` retains the original comparison input. A later user decision explicitly permitted retaining B as critique reference while preserving the real shell/type: “Yes — preserve B and the actual Nova shell.” Hero/responsive gate overrides record that authority; no numerical pass is claimed.
- Detector run once on changed Close components/workspace: no findings. Independent [finish review](../.impeccable/review/issue-256/finish-review.md): **ship**, scoped to captured blocked-member desktop/mobile composition. Admin confirmation, recovery, inherited rows and horizontally scrolled mobile columns were not visually reviewed by that agent; browser assertions/captures are separate evidence.
- Required documenter updated DESIGN.md and `.impeccable/design.json` only with actual Active Close layout, Scoped Totals and Review Before Commit rules. No broad product reconciliation or new global token/exception.
- Supplemental settled admin/Closed captures were reviewed. The Ready-state explanation was contradictory; neutral requirement wording fixed the single material finding. The reviewer scored it resolved with disposition **ship**, limited to that fix, and the documenter rechecked the unchanged rules. Original full-review limits remain explicit.
- Historical tracked build/diff artifacts are preserved at their original revision; issue-specific comparison and representative evidence are curated separately. Intermediate scaffolds, rejected comps, logs and repeated captures remain local. Existing design-sidecar drift was reported at planning; unrelated repair is out of scope.

- Round-one bounded finish review: **ship** for corrected Ready copy, phone recovery/empty/return targets and restored surface contract, with no material regression in seven supplied captures. Retry readiness is covered by source inspection and a browser target measurement, not a new capture. See the retained [finish verdict](../.impeccable/review/issue-256/finish-review.md). All 195 browser behavior tests passed on the fixed inputs; source and generated assets stayed unchanged during the run.
