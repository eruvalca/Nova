# Issue 279 validation

Scope: member-authorized manual player commands, immutable creation receipts, exact-request recovery,
duplicate rejection, and minimal in-session consumer integration. CSV remains administrator-only.
Issue: [#279](https://github.com/eruvalca/Nova/issues/279). Base: `fc2c0053`.

## Revision and gate status

Current review-round inputs: `1eff16154fd9eae0452e0d42f62fa11679ad3ab0` plus the exact changes in
the commit containing this record, on `codex/279-player-command-recovery`, based on `fc2c0053`.
The delta comprises the player form/component and roster page, creation endpoint/route documentation,
`PlayerComponentsTests.CreationRecovery`, `PlayerFormBrowserTests.Recovery`, evaluation-history
test failure diagnostics, and this document.
All changes for Copilot review `5229673163`, including this evidence, are committed together once.
Earlier implementation, production-review and conformance dispositions remain recorded below.

Build, format, full unit/integration and the final full browser run pass on these inputs. The first
browser run's unexplained evaluation-history timeout remains an open validation incident below;
the green reruns do not establish a fix, so this record does not claim merge readiness.
Only failure diagnostics in a browser test and documentation changed after the unit/integration
runs; their application, fixture and suite inputs remain identical. Source diff and generated
Bootstrap CSS hashes remained unchanged during the final browser run. Remote `main` still equals
`fc2c0053`; there are no incoming merge-input differences. Migration-model evidence from `c9c1d7e`
remains applicable because model, migration and provider configuration inputs are unchanged.
Current remote checks and reviews are linked from [PR #283](https://github.com/eruvalca/Nova/pull/283).

## Guidance applied

- `AGENTS.md` and `.github/instructions/`: C# conventions, service layer, validation, API endpoints,
  EF/tenancy, functional core, season lifecycle, placement decisions, testing, Blazor architecture,
  UI design, and observability.
- `.agents/skills/add-feature-slice/SKILL.md`: input/validation, service-result, and WASM-client references.
- `.agents/skills/add-api-endpoint/SKILL.md`: route constants, handlers/results, metadata/authentication,
  validation/ProblemDetails references.
- `.agents/skills/add-domain-persistence/SKILL.md`: retrying mutations/locks and functional core references.
- `.agents/skills/add-blazor-ui/SKILL.md`: forms/validation and lifecycle/state references.
- `.agents/skills/nova-testing/SKILL.md`: transition evidence, SQLite harness, Aspire integration harness,
  browser suite, and bUnit references.
- `PRODUCT.md` and the player-intake surface brief: existing form retained; no design direction changed.
- Installed `dotnet-test` skills: code-testing-agent, run-tests, and find-untested-sources. Static Roslyn
  pairing completed; test-generation work was performed inline. Local pipeline artifacts reside under
  the worktree Git directory's `testagent` folder; this document is the authoritative durable record.
- PR-review fix pass: reused the relevant repository instructions and recipes above; applied the installed
  `code-review` skill's local-review doctrine/checklist and `dotnet-test` test-gap-analysis,
  assertion-quality, and test-analysis-extensions with `extensions/dotnet.md`. The separate reviewer
  read these sources independently. Existing source/test pairing was reused for the bounded fixes.
- Conformance-fix pass: reused the applicable C#, testing, tenancy, validation, and Blazor rules;
  `nova-testing` and its integration/persisted-membership references; and the form/lifecycle recipe.
  Applied the installed `skill-creator` and `dotnet-test/run-tests` skills for the narrow guidance edit
  and repository-compatible MTP execution. Existing behavioral tests cover the fixture-only changes.
- Copilot suppressed-comment pass: reused the applicable Blazor, validation, API, C#, tenancy and
  testing rules; `add-blazor-ui` with lifecycle/state and form references; and `nova-testing` with
  bUnit, transition and browser-suite references. Applied `dotnet-test/code-testing-agent`'s focused
  existing-suite extension path and `dotnet-test/run-tests`; no runner overlay exists. No new
  test project, generation pipeline, guidance rule or diagnostic suppression was needed.

## Behavior and evidence

| Requirement | Named evidence |
| --- | --- |
| Approved members can create/edit/archive/restore; stale or foreign membership cannot | [PlayerManagementServiceTests](../Nova.Unit.Tests/Features/Players/PlayerManagementServiceTests.cs), [PlayerLifecycleServiceTests](../Nova.Unit.Tests/Players/PlayerLifecycleServiceTests.cs), [PlayerManagementHttpTests](../Nova.Integration.Tests/Http/PlayerManagementHttpTests.cs), [PlayerLifecycleHttpTests](../Nova.Integration.Tests/Http/PlayerLifecycleHttpTests.cs), [PlayerRosterHttpTests](../Nova.Integration.Tests/Http/PlayerRosterHttpTests.cs) |
| Stable original result after mutable profile/campaign changes | [ExactReplayReturnsOriginalProfileAndEnrollmentAfterLaterChangesAsync](../Nova.Unit.Tests/Features/Players/PlayerManagementServiceTests.Creation.cs), [LostAcknowledgementUsesReceiptAfterLaterMutationAsync](../Nova.Integration.Tests/Data/PlayerCreationRecoveryPostgresTests.cs) |
| Actor/input mismatch and club-switch protection | [ReplayRejectsChangedPayloadActorAndClubWithoutDisclosingReceiptAsync](../Nova.Unit.Tests/Features/Players/PlayerManagementServiceTests.Creation.cs), [CreationReplaysOriginalEvidenceThroughFreshClientAsync](../Nova.Integration.Tests/Http/PlayerManagementHttpTests.Recovery.cs) |
| Duplicate trim/invariant case/DOB, archived matches, deterministic destination and durable rejection | [DuplicateRejectionRemainsSettledAfterMatchingPlayerChangesAsync](../Nova.Unit.Tests/Features/Players/PlayerManagementServiceTests.Creation.cs), [DuplicateSelectionPrefersActiveThenLowestPlayerIdentityAsync](../Nova.Unit.Tests/Features/Players/PlayerManagementServiceTests.Creation.cs) |
| Real PostgreSQL contention: same operation, distinct duplicate operation, edit, import, campaign, membership removal | [PlayerCreationRecoveryPostgresTests](../Nova.Integration.Tests/Data/PlayerCreationRecoveryPostgresTests.cs), [CampaignOpeningRosterRaceTests](../Nova.Integration.Tests/Data/CampaignOpeningRosterRaceTests.cs), [PlayerImportCommitPostgresTests](../Nova.Integration.Tests/Data/PlayerImportCommitPostgresTests.cs) |
| Precommit rollback and lost acknowledgement use fresh contexts and one durable outcome | [PlayerManagementRetryTests](../Nova.Integration.Tests/Data/PlayerManagementRetryTests.cs), [LostAcknowledgementUsesReceiptAfterLaterMutationAsync](../Nova.Integration.Tests/Data/PlayerCreationRecoveryPostgresTests.cs) |
| Receipt tenant isolation, immutability, cleanup after deletion, exclusive expiry | [PlayerManagementServiceTests.Creation](../Nova.Unit.Tests/Features/Players/PlayerManagementServiceTests.Creation.cs), [PlayerCreationOperationTests](../Nova.Unit.Tests/Features/Players/PlayerCreationOperationTests.cs), [ExpiryDuringRosterOrCampaignWaitRollsBackAsync](../Nova.Integration.Tests/Data/PlayerCreationRecoveryPostgresTests.cs), [CleanupDeletesAtMostFiveHundredExpiredReceiptsAcrossDeletedClubsAsync](../Nova.Integration.Tests/Data/PlayerCreationRecoveryPostgresTests.cs) |
| Complete HTTP success bodies, nullable evidence presence, strict contradictory-conflict rejection | [HttpPlayerManagementServiceTests.Receipts](../Nova.Unit.Tests/Players/HttpPlayerManagementServiceTests.Receipts.cs), [CreationReplaysOriginalEvidenceThroughFreshClientAsync](../Nova.Integration.Tests/Http/PlayerManagementHttpTests.Recovery.cs) |
| Frozen pending payload survives uncertainty and role-only changes, rejection permits correction | [PlayerComponentsTests.CreationRecovery](../Nova.Unit.Tests/Players/PlayerComponentsTests.CreationRecovery.cs), [PlayerFormBrowserTests.Recovery](../Nova.Browser.Tests/PlayerFormBrowserTests.Recovery.cs) |
| In-flight identity changes cannot redirect tenant queries, writes or audit attribution; fresh recovery rejects a changed actor | [CreationKeepsOriginalIdentityAcrossLockWaitAsync](../Nova.Integration.Tests/Data/PlayerCreationRecoveryPostgresTests.Identity.cs), [RecoveryRejectsChangedCircuitIdentityAsync](../Nova.Integration.Tests/Data/PlayerCreationRecoveryPostgresTests.Identity.cs), [ManualMutationKeepsOriginalIdentityAcrossLockWaitAsync](../Nova.Integration.Tests/Data/PlayerCreationRecoveryPostgresTests.Identity.cs) |
| Validation feedback never releases uncertain work; malformed validation is a protocol failure | [CreateRejectsMalformedValidationEvidenceAsync](../Nova.Unit.Tests/Players/HttpPlayerManagementServiceTests.Receipts.cs), [CreatePreservesValidValidationFeedbackAsync](../Nova.Unit.Tests/Players/HttpPlayerManagementServiceTests.Receipts.cs), [PlayersRetainsPendingCreationAfterValidationFailureAsync](../Nova.Unit.Tests/Players/PlayerComponentsTests.CreationRecovery.cs), [PlayerFormRetriesSameOperationAfterLostAcknowledgementAsync](../Nova.Browser.Tests/PlayerFormBrowserTests.Recovery.cs) |
| Existing graduation-year, lifecycle history, enrollment, CSV behavior | Full unit/integration/browser suites; CSV role denials retained |

## Execution results and failure dispositions

- Initial focused unit run: 161 passed, zero failed/skipped. This preceded the final additional protocol,
  contention, component, and browser cases; it is not final gate evidence.
- Initial focused integration run: 35 passed, 14 failed. Failures involved fixtures that asserted mutation success for random actor IDs absent from persisted
  membership, and contention cases observing the former first lock. Fixtures now seed real users; tests
  observe the actual membership/season/roster serialization boundary. Confirmed by 652 integration passes
  on `f25d19a2` and the current full-suite result below.
- Original implementation's first full unit pass found six legacy expectation/fixture failures: lifecycle endpoint metadata still
  expected administrator-only access, and a historical lifecycle fixture omitted persisted users. After
  adding users, its old member-denial assertion was updated to member success. Confirmed by 3,657 passing
  unit tests, zero skipped, on `f25d19a2`.
- First full integration run: 649 passed, three failed. Two additional legacy seeds omitted persisted
  users (cross-club enrollment and concurrent Draft creation); both now seed approved users. The HTTP
  receipt test expected the seed prefix instead of the actual uniquely suffixed campaign name; it now
  compares the receipt with the exact seeded database name. All new provider contention/recovery cases
  passed in that run. Confirmed by 652 integration passes on `f25d19a2` and the current full-suite result below.
- Formatting verification identified only initializer/switch layout and import ordering in the final
  added tests. These were corrected without changing behavior; full verification now passes.
- Intermediate compiler/analyzer failures were corrected: CSV still constructing manual inputs,
  generated migration formatting, unnecessary null-forgiving operators, explicit interface versus
  domain campaign-close result, and ordinal string comparisons in added tests. No diagnostic was disabled.

Current gate evidence for the implementation revision above:

| Check | Command | Current result | Tested revision |
| --- | --- | --- | --- |
| Build | `dotnet build Nova.slnx --no-restore` | Pass: zero warnings/errors | Current review-round inputs above |
| Formatting | `dotnet format Nova.slnx --verify-no-changes --no-restore` | Pass | Current review-round inputs above |
| Migration model | `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext --no-build` | Pass: no pending changes (tool 10.0.8 emits an informational runtime 10.0.12 version warning) | `c9c1d7e`, unchanged model inputs |
| Unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | 3,675 passed, zero failed/skipped | Current review-round inputs above |
| Integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | 661 passed, zero failed/skipped | Current review-round inputs above |
| Browser | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | Final run: 207 passed, zero failed, 8 existing opt-in captures skipped. Full suite selected because recovery and navigation span composed flows. Earlier unexplained timeout remains open below. | Current review-round inputs above |

The incremental [receipt migration](../Nova/Data/Migrations/20260916204134_AddPlayerCreationReceipts.cs)
was applied by the Aspire integration/browser fixtures. The browser skips were the existing accessibility
capture hooks for evaluation, team detail, dashboard, landing, campaign form, closeout, and player detail,
plus the Place surface capture hook.

## Separate local review

Fresh-context reviewer `review_player_commands` inspected production changes against `fc2c0053`,
including authorization, membership lock ordering, receipts, expiry, wire contracts, and component ownership.

| Finding | Disposition and evidence |
| --- | --- |
| P2: role-only changes discarded unresolved creation IDs | Preserve pending payload/form for the same actor and club while invalidating rendered authority and late response ownership. [PlayersRetriesExactPendingPayloadAcrossAdministratorDemotionAsync](../Nova.Unit.Tests/Players/PlayerComponentsTests.CreationRecovery.cs). |
| P2: nullable player fields could be omitted in creation success | `[JsonRequired]` on nullable profile snapshot properties; missing-field cases include null submitted values. [CreateRequiresEveryReceiptFieldIncludingNullableEnrollmentAsync](../Nova.Unit.Tests/Players/HttpPlayerManagementServiceTests.Receipts.cs). |
| P2: contradictory conflicts could release pending work | Validate conflict reason, matching operation marker and bounded duplicate evidence together; contradictions become protocol errors. [CreateRejectsContradictoryConflictEvidenceAsync](../Nova.Unit.Tests/Players/HttpPlayerManagementServiceTests.Receipts.cs). |
| Duplicate 401 endpoint metadata | Removed duplicate declarations. |

Follow-up review confirmed the three production fixes and found no further demonstrated production defect.
It identified test setup corrections (persisted administrator and correct first lock for campaign close,
and an existing Active player for campaign-opening readiness), plus an exact duplicate-operation-ID
assertion. All were applied. The requested delayed creation-success/failure regression now proves that
a late response cannot settle newer club-owned pending work, and passes in the full unit suite.
That review's evidence passed on `f25d19a2`. The subsequent PR review and its fixes are recorded below.

## PR review fixes

Source: [PR review at `430010e0`](https://github.com/eruvalca/Nova/pull/283#pullrequestreview-5228816142).
Both findings are addressed in `c9c1d7e`.

| Finding | Disposition and confirming evidence |
| --- | --- |
| [High: creation can switch tenant during a retained-circuit identity change](https://github.com/eruvalca/Nova/pull/283#discussion_r4031195802) | `PlayerMutationAuthorization` binds each fresh context's provider to a matching authenticated actor/club snapshot before membership-lock waits. Filters and interceptor stamping share that snapshot. Persisted membership is still checked under locks; fresh retries and verification reject a changed actor, including a different member of the same club. `CreationKeepsOriginalIdentityAcrossLockWaitAsync` proves original club, enrollment, player/receipt attribution, no other-club effects and replay. `RecoveryRejectsChangedCircuitIdentityAsync` proves denial after lost acknowledgement followed by original-actor recovery. `ManualMutationKeepsOriginalIdentityAcrossLockWaitAsync` covers edit/archive/restore siblings. |
| [Medium: malformed validation can discard unresolved creation](https://github.com/eruvalca/Nova/pull/283#discussion_r4031198864) | The creation client classifies empty/malformed field errors and validation carrying receipt markers as protocol failures, preserving trace extensions. Well-formed field errors remain available. The UI releases a dispatched command only after valid completion or matching receipt-backed rejection. Client, component and browser tests in the behavior table cover lost acknowledgement, invalid validation, exact retry payload and definitive duplicate correction. |

Failure dispositions and focused commands:

- Before applying the production fix, `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj
  --no-build --filter-method '*CreateRejectsMalformedValidationEvidenceAsync'
  --filter-method '*PlayersRetainsPendingCreationAfterValidationFailureAsync'` reproduced all 13 failures:
  invalid validation remained `Validation`, and the form became editable after uncertainty.
- Against the original production build, `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj
  --no-build --filter-method '*CreationKeepsOriginalIdentityAcrossLockWaitAsync'
  --filter-method '*RecoveryRejectsChangedCircuitIdentityAsync'
  --filter-method '*ManualMutationKeepsOriginalIdentityAcrossLockWaitAsync'` reproduced eight failures
  in nine cases: wrong tenant, wrong actor attribution, wrong duplicate scope, same-club receipt disclosure,
  and sibling operations losing their query scope. Cross-club verification denial already passed.
- After the fixes, `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build
  --filter-class '*HttpPlayerManagementServiceTests' --filter-class '*PlayerComponentsTests'`:
  89 passed, zero failed/skipped. `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj
  --no-build --filter-class '*PlayerCreationRecoveryPostgresTests'`: 24 passed, zero failed/skipped.
  The full unit/integration/browser passes above confirm the regressions and existing behavior together.
- The first draft of the new tests had six compiler/analyzer errors (nullable assertion overload,
  string comparisons and constant-array allocation). Corrected the assertions/initializers; no
  suppression or test weakening. Formatting identified the new test file's missing UTF-8 BOM;
  applied `dotnet format Nova.slnx --no-restore --include
  Nova.Integration.Tests/Data/PlayerCreationRecoveryPostgresTests.Identity.cs`, then full verification passed.

Fresh-context reviewer `review_279_fixes` reviewed the complete fix diff and new test file against
`430010e0`, including authorization, query/audit binding, retry/verification, protocol classification,
UI ownership and sibling paths. No further production findings or material test gaps. The focused
test-gap/assertion review found substantive tenant/count/audit, denial, exact-payload and browser
side-effect assertions; no assertion-free or trivial-only cases. It was a static review, with no
independent test executions or mutation-score claim.

Before publishing this record, `git diff --name-only c9c1d7e` identified only
`docs/279-validation.md`; the final documentation commit therefore changes none of the tested
application/browser inputs. `git diff --check` and all 17 distinct relative file links in this record
passed. Both findings from the original PR review were resolved by this fix pass; later review
findings are recorded separately below.

The PostgreSQL tests use the real `CurrentUserProvider` fallback and
`ServerAuthenticationStateProvider.SetAuthenticationState` while actual advisory-lock contention is
observed. They exercise the circuit principal replacement boundary directly; a physical browser
WebSocket reconnect/account-switch sequence is not separately automated.

## Agent-guidance conformance review

Source: [review at `d4c3fbc7`](https://github.com/eruvalca/Nova/pull/283#pullrequestreview-5229247759),
against `fc2c0053`. This source/evidence review reused the applicable instructions and recipes listed
above and applied the installed `code-review` skill's doctrine, PR workflow, and checklist. It checked
production invariants, guidance examples and routing, integration isolation, ecosystem copies,
prior review dispositions, and validation applicability. The prior two production fixes remain valid.

| Finding | Disposition and confirming evidence |
| --- | --- |
| [Medium: new integration user seeds assign random primary keys](https://github.com/eruvalca/Nova/pull/283#discussion_r4031583958) | Fixed in `c3b99cd6`. All eleven affected insert sites save users without explicit IDs, then use generated IDs for attribution and principals; membership is saved before service calls. The full integration pass covers `CampaignCreationPostgresTests`, `CampaignOpeningRosterRaceTests`, `PlayerEnrollmentPostgresTests`, `PlayerLifecycleRetryTests`, `PlayerManagementRetryTests`, and `TeamPlayerGraduationYearRaceTests`. Existing assertions and fault/lock gates are preserved. |
| [Medium: form-validation recipe still validates the manual creation command](https://github.com/eruvalca/Nova/pull/283#pullrequestreview-5229247759) | Fixed in `c3b99cd6`. The [form recipe](../.agents/skills/add-blazor-ui/references/forms-and-validation.md) now validates `PlayerProfileInput` and shows separate dispatch-time conversion with operation/club metadata. Focused comparison with `PlayerFormState` and `Players` confirms metadata is allocated once and the immutable pending command is retained for retries. Skill structure validation passes. |

The review itself ran no builds, tests, benchmarks, or formatting commands. The random-key collision
was established as an isolation risk by source inspection, not reproduced at runtime. The subsequent
fix pass ran the build, formatting, and full unit/integration checks in the current gate table; all
passed on the first run. Removed only obsolete `CA5394` suppressions beside deleted random actor-ID
allocation; no assertions, skips, or validation controls were weakened.

`python -X utf8 C:/Users/eruva/.codex/skills/.system/skill-creator/scripts/quick_validate.py
.agents/skills/add-blazor-ui` passed. Focused self-review checked all affected seed callers, the form
example and recovery link, and shared Codex/Copilot guidance placement. This small reference correction
adds no skill, mirror, or new policy; no independent forward-test was needed.

Browser rerun: **N/A for this fix**, with the prior full pass retained. Comparing `c9c1d7e` to the final
inputs identifies only six integration test classes, the form recipe, and this record. The browser
project references the integration assembly for shared helpers, but none of the six changed classes
is used by browser tests; shared fixtures/helpers, discovery, build/runtime configuration, application
source, and generated assets are unchanged. The bootstrap output also retained its pre-build timestamp
and size. Migration-model verification is reused because no model or migration input changed.
`git diff --check` and all 18 distinct relative file links pass. Both findings are addressed; no
unresolved conformance finding remains.

## Copilot suppressed-comment review

Source: [Copilot review at `1eff1615`](https://github.com/eruvalca/Nova/pull/283#pullrequestreview-5229673163).
Its "Needs a closer look" result contains four suppressed comments and no inline comments; suppression
does not resolve a finding. All four are evaluated here. There are no new inline threads to resolve.

| Finding | Disposition and evidence |
| --- | --- |
| Duplicate link drops roster return context | Fixed: the parent supplies the existing `BuildPlayerDetailUrl` result to `PlayerForm`. `PlayersDuplicateRejectionAllowsCorrectionWithNewOperationAsync` checks lifecycle/search/year/tag and nested correction/draft return context. `PlayerFormDuplicateDetailPreservesRosterReturnContextAsync` exercises the composed browser journey into duplicate details and back to the filtered roster. |
| Old duplicate evidence appears on a later form | Fixed for the demonstrated Cancel/reopen path: both form boundaries clear duplicate feedback. The review's correction/success example already cleared it before dispatch; that sibling remains covered. `PlayersClearsDuplicateWhenCancelledFormReopensAsync` proves the link clears and a corrected submission receives a new operation ID. The existing browser duplicate-correction scenario now includes Cancel/reopen; the component correction/success test also reopens a clean form. |
| Expiry/denial plus Cancel should retire unresolved work or allow a parallel new command | Inapplicable **as proposed to #279's approved contract**. The accepted plan requires retaining the exact pending payload, preventing replacement while unresolved, and allocating a new ID only after success or definitive rejection. Expiry, authorization denial and cancellation do not prove an earlier creation rolled back. Clearing the pending slot or enabling a new one here would violate that invariant. `PlayersRetainsUnresolvedCreationAfterDenialAndCancelAsync` covers lost acknowledgement followed by expiry or forbidden recovery, Cancel/reopen, and exact-command retention despite attempted edits. Post-expiry reconciliation/explicit abandonment belongs to the intentionally deferred #264 recovery interaction design; the current in-session limitation is retained explicitly below. |
| Creation docs promise enrollment in all Active campaigns | Fixed: endpoint registration, handler and shared route documentation describe the original optional enrollment in the single current Active campaign, including replay. Other plural lifecycle/blocker documentation remains valid and unchanged. |

Focused self-review includes every `PlayerForm` caller, ordinary detail links, Cancel/reopen,
correction/success, actor/club and role-change ownership, and endpoint/shared route documentation.
No pending-command settlement rule or authorization behavior changed. This is a bounded follow-up
to the existing separately reviewed recovery implementation.

Failure dispositions: the first build reported `CA1056` for a string URL component parameter.
The parameter now uses `Uri` with an explicitly relative parent-built destination; no diagnostic
was suppressed. The next build caught local-constant casing and a nested test ternary; both were
corrected to repository conventions. The confirming build passed with zero warnings/errors.
Formatting verification then identified an indentation error in the edited endpoint comment;
the corrected comment passes full solution formatting verification without changing behavior.
`dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build --filter-class
'*PlayerComponentsTests'` passed all 42 cases. Full-suite evidence is recorded in the current gate table.

The first full browser pass failed `ProtectedBackKeepsThenResumesExactHistoryEntriesAndForwardContextAsync`
at the finder-URL wait after "Discard and leave" (30-second timeout), before Forward navigation.
The changed player-form scenarios passed. The failure did not retain final browser URL/history/guard
diagnostics, so its cause is unresolved; no contention attribution is made. Added failure-only
diagnostics to that existing test without changing its actions, expectations, timeout or retry policy.
The isolated `--filter-method '*ProtectedBackKeepsThenResumesExactHistoryEntriesAndForwardContextAsync'`
run passed 1/1, and `--filter-class '*CampaignEvaluationCaptureBrowserTests'` passed 19/19, both using
`dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build`. These reruns do not
resolve the original incident. Source inspection covers `DiscardAndLeaveAsync`, departure ownership,
storage revision checks, `resumeHistory` and the native history guard; none changed in this review
round, and no causal production defect has been established. The original timeout remains open and
precludes claiming merge readiness without resolution or an explicit owner disposition.
The final full browser run passed 207/207 executed cases with the same eight opt-in skips. This is
current execution evidence, not a causal disposition of the first run. Diagnostic self-review confirms
the wrapper delegates to the original wait and enriches only the exception; all history-key, URL,
draft and selected-player assertions remain unchanged. No suppression or validation weakening was added.

## Guidance follow-up

Reviewed at `0e88128dd59a8b1d11ada23bb481c422d1db170b` plus the documentation changes in this commit to
`AGENTS.md`, API/Blazor instructions, and the existing feature, persistence, Blazor, and testing skill
references. No new skill or instruction file was added. Application and test inputs still match
`f25d19a2`; this follow-up changes only guidance and its validation record.

Applied the installed `skill-creator/SKILL.md` and the existing guidance listed above, using
[Microsoft's instructions-hygiene guidance](https://devblogs.microsoft.com/dotnet/instructions-hygiene-what-frontier-models-still-need-you-to-say/)
to keep additions specific to demonstrated gaps. Changes cover rejection-evidence validation,
in-memory recovery ownership, persisted membership in test fixtures, and formatter/edit
serialization. Updated the manual-creation duplicate example; feature-specific durations and
limits remain outside general instructions. The formatter rule addresses an implementation-session
overlap in which a source-writing format pass overwrote newer edits; those edits were restored
before the implementation's final gates.

- `git diff --check`: pass.
- Before committing/pushing this follow-up, `dotnet format Nova.slnx --verify-no-changes --no-restore`:
  pass; `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build`: 3,657 passed,
  zero failed/skipped. Both ran against the parent revision plus these documentation changes;
  the subsequent edits to this record changed no application/test inputs.
- `python -X utf8 C:/Users/eruva/.codex/skills/.system/skill-creator/scripts/quick_validate.py
  .agents/skills/<skill>`: pass for `add-blazor-ui`, `add-domain-persistence`, `add-feature-slice`,
  and `nova-testing`. This validates skill structure, not behavioral effectiveness.
- Focused self-review: relative links/anchors and named implementation/test examples checked;
  no mandatory browser persistence, authorization bypass, new feature-wide lifetime, or weakened
  validation gate introduced. The affected skills have no `.github/skills` mirrors; their shared
  `.agents/skills` sources and path-scoped instructions serve both Codex and Copilot.
- `git diff --name-only f25d19a2 0e88128d` identifies only this validation record; the working diff adds
  only the guidance/documentation paths above. Build, migration, integration and browser evidence
  for `f25d19a2` remains applicable to unchanged application/test inputs. Browser
  rerun: N/A for this documentation-only follow-up. Test commands and PR gates are unchanged.
- Limitation: no independent forward-test on a different feature was performed. Evaluate these
  instructions during future recovery work such as #264; existing regression tests establish the
  examples' behavior, not an improvement in future agent decisions.

## Consumer handoff to issues 263 and 264

- Manual create/update derive from `PlayerProfileInput`; CSV candidates use only that profile contract.
  Allocate a UUIDv7 once per logical creation and retain the exact typed command and original club.
- `POST /api/players` returns `201` with `PlayerCreationCompletion` for both first execution and recovery.
  The player and optional enrollment are immutable commitment snapshots. Explicit `enrollment: null`
  means no Active campaign existed at commitment; later campaign or player state must not replace them.
- Only a valid matching `possibleDuplicate` rejection with receipt-backed operation marker proves the
  operation did not create. Authorization denial, expiry, mismatch, timeout, cancellation and protocol
  failures leave earlier commitment unresolved. Do not allocate a replacement ID merely because one occurs.
- Recovery/execution ends exclusively at UUIDv7 creation time plus 24 hours, with a one-minute future-clock
  allowance. Receipt deletion cannot enable execution of an expired operation. Cleanup runs hourly and
  removes at most 500 expired receipts globally per pass, including deleted-club snapshots.
- Current UI retains an immutable pending command in the mounted page and freezes its fields until success
  or definitive rejection. Actor/club changes invalidate ownership; role-only changes preserve the command.
- #264 must persist **caller, original club, operation ID, exact payload and recovery deadline before
  dispatch**, restore ownership on reload, replay unchanged within the deadline, and distinguish committed,
  definitively rejected and unresolved outcomes. Persisted storage, recovery interaction design, and photos
  are intentionally outside #279. Directory role affordances remain with #263.
- #264's recovery interaction design must address reconciliation after expiry or denied recovery.
  In #279, Cancel hides the form but retains unresolved work; reopening does not allocate a new ID.
  Any future explicit abandonment/new-command flow must preserve the unresolved outcome and avoid
  presenting expiry or cancellation as rollback evidence.

## Limitations

This is a pre-release breaking contract with one incremental receipt-table migration and no compatibility
bridge. The evaluation-history timeout in the Copilot-round browser run remains unexplained and
merge-blocking despite successful focused/full reruns. Current browser recovery is in-memory only.
Eight existing environment-gated screenshot/manual evidence captures were not requested (`NOVA_A11Y_SCREENSHOTS`
and `NOVA_PLACE_EVIDENCE` unset); they are reported as skipped, not passed. Behavioral browser assertions,
including creation validation, duplicate correction and lost-response retry, ran and passed. No new skip
or suppression was introduced.
