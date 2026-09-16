# Issue #256 validation

Status: implementation and required local validation complete. Base revision: `8e89f7d3d6889f7a9779cd0efe96a1c2c1a2123e`. The final tested production fingerprint is `0b0acf81be5507d7f18f7b4b2ab4e3ecee5406e8d1a2c2bf3c833ada4cc3dd8e`; test-source fingerprint is `937a12ae032dbeb5f2478a6d74d058b12178a0dc0dfe7227db5a09edb2559d87`. Per-file normalized hashes and representative-capture hashes are in the capture manifest linked below.

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
| Format | `dotnet format Nova.slnx --verify-no-changes` | Passed, empty diagnostic output |
| Unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | Passed 3,449/3,449, no skips |
| Integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | Passed 628/628, no skips, final production source |
| Browser | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | Passed 193/193 behavior tests; 8 existing opt-in capture jobs skipped (201 total) |
| Diff hygiene | `git diff --check` | Passed before final record update |
| Migration model | No model/entity/configuration/migration change | Not applicable; PostgreSQL migration/schema regressions included in integration suite |

Build-capable commands were serial. Integration and browser suites ran serially; both provisioned their own Aspire host. App source/generated assets were fixed throughout each browser run. No Sass/package change; theme rebuild/contrast-specific gate is not newly applicable.

The eight browser skips are existing environment-gated screenshot jobs, not skipped behavior tests. Issue-specific representative captures were produced separately by the passing Close group with `NOVA_CLOSE_EVIDENCE` set. Final source-hash comparison reported zero mismatches. Documentation/evidence curation after the tests does not change application or test source.

## Material failures and review dispositions

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
