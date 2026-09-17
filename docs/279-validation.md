# Issue 279 validation

Scope: member-authorized manual player commands, immutable creation receipts, exact-request recovery,
duplicate rejection, and minimal in-session consumer integration. CSV remains administrator-only.
Issue: [#279](https://github.com/eruvalca/Nova/issues/279). Base: `fc2c0053`.

## Revision and gate status

Current review-round inputs: `56d9ab79aaa85b9336665d930a18da6ba8c479eb` plus the exact changes in
the commit containing this record, on `codex/279-player-command-recovery`, based on `fc2c0053`.
This delta corrects malformed/future operation-ID classification with shared input validation,
service classification, unit/HTTP/consumer regressions and this record. All changes for Copilot
review `5230668363` are committed together once. The preceding SQLite cleanup disposition was
completed in the documentation-only commit `56d9ab79`.
Earlier implementation, production-review and conformance dispositions remain recorded below.

Current gates and tested-input comparisons are recorded in the operation-ID review disposition.
The changed shared input and HTTP classification have passing new full integration/browser evidence.
The prior round's diagnosed browser-observation
failures remain resolved with the reproduced evidence and dispositions preserved below.
Remote `main` remains `fc2c0053`; there are no incoming merge-input differences.
Migration-model evidence from `c9c1d7e`
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
- Cleanup-round follow-up: applied `add-domain-persistence` with provider-safe query construction and
  receipt-retention references; `nova-testing` with transition, SQLite and PostgreSQL harness guidance;
  and the existing C#, service, tenancy and testing rules. Used the focused existing-test workflow
  from `dotnet-test/code-testing-agent` and the native MTP commands from `dotnet-test/run-tests`.
- Expiry-message follow-up: applied `add-blazor-ui` with lifecycle/state and form references, and
  `nova-testing` with transition, component and browser guidance. Reused the applicable C#, Blazor,
  API/validation, tenancy and testing rules; the focused `code-testing-agent` workflow and `run-tests`
  native MTP syntax apply. This corrects existing feedback without changing layout or design direction.
- History-failure diagnosis: applied the repository `dotnet-inspect` skill and its installed 0.25.0
  guide to inspect `Microsoft.Playwright@1.62.0`; checked the matching upstream `Frame` source and
  official page-assertion documentation linked in the incident disposition below.
- Cross-club HTTP follow-up: reused the applicable C#, service, API, tenancy and testing rules;
  `nova-testing` with transition and HTTP/PostgreSQL harness references; the focused existing-suite
  `dotnet-test/code-testing-agent` workflow and `dotnet-test/run-tests` native MTP syntax. No application
  behavior, runner policy, shared harness or guidance changed.

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
| Browser | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | Final run: 208 passed, zero failed, 8 existing opt-in skips; both prior failures have causal dispositions below | Current review-round inputs above |

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
`dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build`. Those reruns did not
resolve the original incident. Source inspection covers `DiscardAndLeaveAsync`, departure ownership,
storage revision checks, `resumeHistory` and the native history guard; none changed in this review
round, and no causal production defect was established. At that stage the original timeout remained
open and prevented claiming merge readiness. A later reproduction supplies the
[causal disposition below](#browser-history-validation-incident).
The final full browser run passed 207/207 executed cases with the same eight opt-in skips. This is
current execution evidence, not a causal disposition of the first run. Diagnostic self-review confirms
the wrapper delegates to the original wait and enriches only the exception; all history-key, URL,
draft and selected-player assertions remain unchanged. No suppression or validation weakening was added.

## Copilot receipt-cleanup review

Source: [Copilot review at `09aa2be5`](https://github.com/eruvalca/Nova/pull/283#pullrequestreview-5229944610).
Its single suppressed finding correctly identifies full receipt materialization in the non-Npgsql
fallback. Production already used a bounded SQL delete; `Program.cs` configures only Npgsql.
SQLite cannot translate this `DateTimeOffset` ordering, so moving its LINQ operators before
`ToListAsync` would not provide a valid fallback.

Removed the test-only fallback and retained one database-side expiration predicate, deterministic
`(RecoveryExpiresAt, PlayerCreationReceiptId)` order, `Take(500)` and `ExecuteDeleteAsync`, following
`PlacementReceiptCleanupService`. No receipt entities are materialized by retention. The older
evaluation and import cleanup fallbacks were inspected as siblings; both predate this change and
have the same test-provider limitation. Their configured Npgsql paths already use bounded
database-side deletion. No unrelated receipt family or provider contract is changed here.

| Requirement | Evidence |
| --- | --- |
| Limit each global deletion to 500 expired receipts without reading receipt rows into the process | `CleanupDeletesAtMostFiveHundredExpiredReceiptsAcrossDeletedClubsAsync` asserts exact remaining identities, zero synchronous/asynchronous reader executions in an instrumented maintenance context, and an empty receipt change tracker after each pass against real PostgreSQL. |
| Deterministic expiry/identity ordering, cutoff inclusivity, and retention across club deletion | The same test inserts an older receipt later in another club, 501 tied expirations, an exact-cutoff receipt, and live receipts in existing/deleted clubs. A one-microsecond-after-cutoff receipt survives both passes. The fixed historical cutoff isolates global maintenance from parallel tests' recovery windows. |
| Receipt deletion cannot restart an expired operation | `ExpiredCreationCannotRestartAfterReceiptCleanupAsync` retains its original-operation rejection and one-player assertions. Its SQLite arrangement explicitly deletes only that operation's receipt; it no longer invokes a provider-specific cleanup query. |

Coverage disposition: the duplicate SQLite cleanup-count test was removed only after its deleted-club,
live-receipt and 500-row guarantees were incorporated into the stronger PostgreSQL test above.
Cleanup translation and batching now run against the configured runtime provider. No assertion,
timeout, retry or skip was relaxed, and the SQLite expiry/tenant-isolation/immutability tests remain.
Focused self-review checks all cleanup callers, registration, index shape, cutoff precision and sibling
retention patterns. No schema or migration input changed.
The full suites in the current gate table cover the changed inputs, including all player and history
browser flows. `git diff --check` passes. No local check failed in this cleanup round; the preceding
round's unexplained browser incident remained open then. The latest review bodies, conversation comments
and all three existing resolved threads were rechecked with pagination; this finding has no inline
thread. At that push, fresh remote CI and automatic review were pending.

## Copilot expiry-message review

Source: [Copilot review at `14f36eeb`](https://github.com/eruvalca/Nova/pull/283#pullrequestreview-5230165851).
The single suppressed finding is valid: appending retry instructions after accepted expiry contradicts
the closed execution/recovery window. `PlayerCreationProblems.IsExpired` recognizes only a valid expiry
conflict, including JSON-decoded extension values. The page keeps the server's directory guidance and
states that the original addition is retained, without promising recovery through another retry.
Other unresolved failures keep the existing retry instruction. Receipt settlement, pending payload
ownership, cancellation and allocation of new operation IDs are unchanged.

| Requirement | Evidence |
| --- | --- |
| Lost response → expiry uses directory guidance without a retry promise; direct and HTTP-decoded reasons agree | `PlayersRetainsUnresolvedCreationAfterDenialAndCancelAsync` covers typed and JSON expiry, plus the existing non-expiry denial. |
| Expiry and Cancel/reopen retain the exact original command without unlocking edits | The same component test asserts reference-identical commands and original input after an attempted model replacement. `PlayerFormExpiryRetainsCommandWithoutRetryGuidanceAsync` verifies identical HTTP bodies and disabled fields across Cancel/reopen. |
| WASM renders the accepted expiry without losing already committed effects | `PlayerFormExpiryRetainsCommandWithoutRetryGuidanceAsync` commits one real player, loses its response, injects a valid expiry response on retry, and checks the guidance plus one player/receipt. This proves consumer behavior; the existing service/provider tests own real clock boundaries. |
| Recoverable failures still permit exact replay and success; duplicate correction remains available | Existing `PlayerFormRetriesSameOperationAfterLostAcknowledgementAsync` and duplicate browser scenarios remain in the selected full suite. |

Failure dispositions: before the production fix, the focused component run failed both expiry cases
at the new negative retry-text assertion; its non-expiry denial case passed. Initial test compilation
also identified missing ordinal dictionary comparers and the browser method-size limit. Added the
comparers and extracted the unchanged WebAssembly warmup into a helper; the next build passed without
warnings. After the production fix, all three focused component cases passed with
`dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build --filter-method '*PlayersRetainsUnresolvedCreationAfterDenialAndCancelAsync'`.
No analyzer, test, timeout or assertion was suppressed or relaxed. Full unit and integration suites
pass; the full browser suite is selected because the changed shared/UI feedback participates in command
recovery and mounted form state. Its selection includes existing player creation, duplicate correction,
lost-response recovery, validation, identity and navigation flows alongside the new expiry case.
The earlier evaluation-history incident reproduced in the next full run; its disposition follows below.

The first full browser run passed 207, failed one and retained eight existing opt-in skips. The
new expiry case passed. `PlayerFormDuplicateCanBeCorrectedWithoutOverrideAsync` failed after
Cancel/reopen: `#player-first-name` was absent at the enabled-field assertion (line 144 in those
inputs), and the failure's ARIA snapshot showed the roster with only `Original Recovery`, not a form.
The test did not wait for cancellation to render. `OpenCreationFormAsync` delegates to
`InteractionHelpers.ActUntilAsync`, which checks visibility before clicking; the still-visible old
form could satisfy that check, then disappear when the preceding Cancel completed. Added an explicit
zero-field assertion after Cancel and before reopening in both the duplicate and expiry cases.
This closes the observed transition gap without increasing waits, changing the generic retry helper,
or removing the enabled/disabled-field, feedback, exact-payload and database-effect assertions.
Only browser test synchronization changed after the successful unit/integration runs; application,
unit and integration inputs are unchanged. The rebuilt player-form browser class passed all ten
scenarios, with its one existing opt-in capture skipped, using
`dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-class '*PlayerFormBrowserTests'`.
The final full browser suite also passed all 208 executed scenarios, with the same eight opt-in skips,
confirming the fixed close/reopen boundary in the composed suite.

Sibling inspection covered both direct-server and WASM result paths, generic failure feedback,
duplicate settlement, Cancel/reopen and the form's read-only fields. No HTTP producer, persistence,
schema, layout or operation-retention rule changed. No new agent instruction or skill is warranted.
The full review bodies, conversation comments and all three existing resolved threads were rechecked
with pagination; no additional actionable finding or inline thread appeared in this round.
`git diff --check` and all 36 relative links in this record pass. All six changed files, including
this record, are included in the same review-round commit. Fresh remote CI and automatic review
of that commit remain pending at push time.

## Copilot cross-club HTTP review

Source: [review 5230457872](https://github.com/eruvalca/Nova/pull/283#pullrequestreview-5230457872)
at `9239d4d2`. Its one inline finding and one suppressed finding are both actionable coverage or
naming corrections. The suppressed comment points to the creation partial, but the outdated name
is in the main service-test file; repository search found no other reference to that name.

| Requirement | Evidence |
| --- | --- |
| A live approved member cannot choose another club for POST execution or recovery | [`CreateRejectsAnotherClubsApprovedMemberWithoutDisclosingOrWritingAsync`](../Nova.Integration.Tests/Http/PlayerManagementHttpTests.Recovery.cs) runs with and without an existing target-club receipt, through real Identity cookies and the deployed HTTP endpoint. It asserts 403/ProblemDetails/trace ID, no player/enrollment/conflict/settlement disclosure, and exact operation-scoped player/receipt club sets unchanged after denial. The same member and operation then create successfully in the member's own club, proving live membership and absence of a denial-created receipt. |
| Test naming states the single-current-campaign contract | [`CreateEnrollsPlayerInCurrentActiveCampaignAsync`](../Nova.Unit.Tests/Features/Players/PlayerManagementServiceTests.cs) retains its exact-one participation assertion, current campaign ID and Undecided outcome. Only the obsolete `EveryActiveCampaign` method name changed. |

Sibling inspection: existing HTTP coverage already exercises same-club member/admin success,
anonymous and removed-membership denial, and other-club update isolation. Service and PostgreSQL
tests cover actor/input/club mismatches and identity changes during lock waits. The added theory
fills the live-outsider POST boundary gap without changing production authorization. Removing the
request/authorized-club guard would return success in the caller's tenant instead of the asserted
403; the positive control prevents an authorization failure unrelated to the requested club from
passing unnoticed. No mutation experiment was needed for this missing boundary coverage.

All results below cover `9239d4d2` plus this round's two test-file changes; only this record was
finalized afterward. Build-capable commands were serialized, and no other Aspire suite ran during
either integration execution.

| Check | Command and result |
| --- | --- |
| Build | `dotnet build Nova.slnx --no-restore` — pass, zero warnings/errors. |
| Focused HTTP regression | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build --filter-method '*CreateRejectsAnotherClubsApprovedMemberWithoutDisclosingOrWritingAsync'` — two passed, zero failed/skipped. |
| Full unit suite | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — 3,675 passed, zero failed/skipped; includes the renamed enrollment test. |
| Format | `dotnet format Nova.slnx --verify-no-changes --no-restore --verbosity diagnostic` — pass, zero files changed. |
| Full integration suite | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — 663 passed, zero failed/skipped. |

The first build reported S2325 and CA1822 because the new authentication setup helper did not use
instance state. Marking it static corrected both diagnostics; the subsequent solution build and
format verification pass without suppression. No behavioral test failed in this round.

Browser rerun is N/A for this isolated test-only delta. `git diff --name-only 9239d4d2` identifies
only the two named unit/integration files and this record; neither is a shared/browser helper.
The full `9239d4d2` pass (208 passed, eight existing opt-in skips) therefore covers the unchanged
application and browser inputs. Generated theme CSS retains SHA-256
`559EC45DA0B8540B2DA715B171FC23B565F133D733E9A4C2E727F265495807DC` after the build.
Migration-model evidence from `c9c1d7e` remains applicable. Remote `main` was rechecked as `fc2c0053`.
No tests or assertions were removed, skipped, suppressed or weakened; production behavior is unchanged.

Focused self-review checked the complete three-file delta, valid real-cookie membership, operation-
scoped database assertions, the positive control, and unchanged sibling tests. All review bodies,
conversation comments and inline threads were inspected with pagination, including suppressed and
resolved findings. This round's inline finding is fixed and will receive the commit/evidence link
before resolution in the PR; the suppressed naming finding is fixed in the same commit.
`git diff --check` and all 38 relative links in this record pass. Fresh CI and the next automatic
review remain pending at push time; no review is manually requested.

## Copilot SQLite cleanup follow-up

Source: [review 5230586090](https://github.com/eruvalca/Nova/pull/283#pullrequestreview-5230586090)
at `55af7cf6`, with [one inline finding](https://github.com/eruvalca/Nova/pull/283#discussion_r4032745283)
and no suppressed findings. It requests an `IsNpgsql()` branch and the import cleanup's
materialize/filter/remove fallback because SQLite cannot translate the current query.

Disposition: **inapplicable to the supported cleanup execution paths**. The SQLite translation
limitation is accurate; SQLite support for this provider-specific maintenance method is not required
by a current caller or by #279. Checked all references, not only the successful test:

- [`PlayerCreationReceiptCleanupService.RunPassAsync`](../Nova/Features/Players/PlayerCreationReceiptCleanupService.cs)
  is the only application caller of `PruneAsync`; it obtains the admin-context factory from DI.
  [`Program.cs`](../Nova/Program.cs) registers that factory with `UseNpgsql` and registers the worker.
- The only direct test calls are in
  [`CleanupDeletesAtMostFiveHundredExpiredReceiptsAcrossDeletedClubsAsync`](../Nova.Integration.Tests/Data/PlayerCreationRecoveryPostgresTests.cs),
  whose maintenance factory uses the live PostgreSQL connection. No SQLite test invokes this worker
  or method. SQLite still proves receipt expiry, isolation and immutability at its appropriate boundary.
- [`PlacementReceiptCleanupService`](../Nova/Features/Campaigns/PlacementReceiptCleanupService.cs) uses
  the same PostgreSQL maintenance pattern. Import invokes its cleanup inside `CommitAsync`, which
  is exercised through SQLite service tests; its fallback serves an actual harness caller.
  That difference does not establish a requirement to add a second provider to this isolated worker.

Re-read tenancy/provider instructions, `add-domain-persistence` with query construction and
retry/receipt references, and `nova-testing`'s provider-selection guidance. The SQLite fallback rules
govern paths exercised on SQLite; provider-sensitive cleanup translation, batching and retention
are tested on the configured PostgreSQL provider. This is not a claim that the method works on SQLite.
The [earlier cleanup disposition](#copilot-receipt-cleanup-review) explains why its duplicate SQLite
coverage was moved to PostgreSQL and why reintroducing full-table materialization would undo that
review fix. No runtime code, test, guidance policy, assertion or supported provider changed here.

Validation against `55af7cf6` plus this documentation-only delta:

- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build --filter-method '*CleanupDeletesAtMostFiveHundredExpiredReceiptsAcrossDeletedClubsAsync'`:
  one passed, zero failed/skipped. The existing provider regression confirms translation, exact batch
  membership, expiry/deleted-club retention and zero receipt-reader executions.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build`: 3,675 passed, zero failed/skipped.
- `dotnet format Nova.slnx --verify-no-changes --no-restore --verbosity diagnostic`: pass, zero files changed.
- The full build and 663-case integration pass at `55af7cf6`, the 208-case executed browser pass at
  `9239d4d2` (eight existing opt-in skips), and migration-model evidence at `c9c1d7e` cover unchanged
  inputs. `git diff --name-only 55af7cf6` contains only this record, so browser rerun is N/A.
- `git diff --check` and all 42 relative file links pass. Remote `main` still points to `fc2c0053`.
  No local check failed in this round. CI Build and Unit Tests for `55af7cf6` both passed in
  [run 35176674518](https://github.com/eruvalca/Nova/actions/runs/35176674518).

Focused self-review traced every cleanup caller and the sibling distinction above. All review bodies,
conversation comments and inline threads, including suppressed/resolved content, were inspected with
pagination. The PR reply will link this evidence-backed inapplicability explanation before the thread
is resolved. This record is the round's single commit; fresh CI and automatic review of it remain
pending at push time. No review is manually requested.

## Copilot operation-ID classification review

Source: [review 5230668363](https://github.com/eruvalca/Nova/pull/283#pullrequestreview-5230668363)
at `56d9ab79`, with one suppressed finding and no new inline thread. The finding is valid: the
previous input only rejected an empty GUID, and every `TryGetDeadline` failure returned an expiry
conflict. A non-v7 GUID, invalid UUID variant, unrepresentable timestamp, or excessive future clock
skew could therefore be described as an elapsed recovery window.

Added input-owned `CustomValidation` using the existing deterministic `TryGetCreatedAt` parser,
so structural rejection is shared by endpoint and direct-service validation. Shape validation does
not read the clock or allocate an ID. The service now distinguishes the helper's documented failure
outputs: an expired valid identity retains its computed deadline, while malformed/too-far-future
identities return no deadline. Future-clock rejection is an `OperationId` validation problem, with
no expiry or durable-rejection marker. A valid elapsed UUIDv7 window remains an expiry conflict.

| Requirement | Named evidence |
| --- | --- |
| Reject malformed identity before creation or receipt effects | [`ValidateWithMalformedOperationIdReturnsError`](../Nova.Unit.Tests/Features/Players/CreatePlayerInputValidationTests.cs) and [`CreateRejectsMalformedOperationIdentityAsync`](../Nova.Unit.Tests/Features/Players/PlayerManagementServiceTests.Creation.cs) cover empty, v4, invalid-variant and unrepresentable IDs; assert the `OperationId` field, no settlement extensions and no player/receipt insertion. |
| Separate immutable shape from time-dependent eligibility | `ValidateOperationShapeDoesNotDependOnCurrentTime` accepts well-formed historical/future UUIDv7 inputs. [`PlayerCreationOperationTests`](../Nova.Unit.Tests/Features/Players/PlayerCreationOperationTests.cs) verifies the failure-output contract and exclusive expiry; `CreateHonorsFutureOperationClockToleranceAsync` proves 59,999/60,000 ms acceptance and 60,001 ms validation, then advances the injected clock one millisecond and successfully replays the same original input. |
| Real HTTP classification, correlation and absence of effects | [`CreationDistinguishesInvalidOperationsFromExpiryAsync`](../Nova.Integration.Tests/Http/PlayerManagementHttpTests.Recovery.cs) covers the four malformed IDs, excessive future skew and valid expiry. Invalid/future requests return 400 with `OperationId` errors; expiry returns 409/`expired`. All include trace IDs, no receipt-backed rejection marker, and zero player/receipt writes. |
| Validation feedback cannot settle unresolved browser work | [`CreatePreservesValidValidationFeedbackAsync`](../Nova.Unit.Tests/Players/HttpPlayerManagementServiceTests.Receipts.cs) now covers `OperationId` alongside profile feedback for both 400/422. [`PlayersRetainsPendingCreationAfterValidationFailureAsync`](../Nova.Unit.Tests/Players/PlayerComponentsTests.CreationRecovery.cs) adds an operation-field case after lost acknowledgement, retaining the exact original object and profile through the next successful retry. |

The review's classification correction does not authorize releasing a pending command based on a
validation response alone. Existing receipt, expiry, membership and exact-request recovery rules
remain unchanged; malformed or contradictory responses still cannot prove rollback. The form still
validates only `PlayerProfileInput`, independently of operation allocation.

Guidance applied: existing C#, validation, service, API, tenancy, Blazor and testing rules;
`add-feature-slice` with input/validation, service-result and WASM references; `add-api-endpoint`
with validation/ProblemDetails guidance; `add-blazor-ui` form/state guidance; and `nova-testing`
with transition, SQLite, HTTP/PostgreSQL, component and browser references. Used the focused
`code-testing-agent` workflow and `run-tests` native MTP commands. No new guidance or exception was added.

Sibling review: placement already distinguishes invalid/future IDs from expiry. The evaluation
executor has the earlier combined classification too, but both its executor and input are unchanged
from `fc2c0053`; it is not a dependency of player creation. **Separate follow-up:** correct
[`EvaluationMutationExecutor`](../Nova/Features/Campaigns/EvaluationMutationExecutor.cs) and
[`EvaluationOperationInput`](../Nova.SharedKernel/Features/Campaigns/EvaluationOperationInput.cs)
with equivalent input/service/HTTP classification and pending-command evidence. This finding is
source-verified, not newly reproduced at runtime; it remains outside #279's player-command scope.

Failure disposition: before the production fix, the focused unit run passed 112 and failed seven:
three malformed IDs passed input validation, the same three returned service conflicts, and the
60,001 ms future case returned a conflict. After correction, all 121 focused cases pass (the run also
includes the two added WASM feedback cases). These are reproduced expected regression failures,
resolved by the input/service changes; no assertion, retry budget, skip or suppression was weakened.

All results below cover `56d9ab79` plus the nine source/test changes in this round; this record is
the only later documentation edit. Build-capable commands and machine-wide Aspire suites were
serialized. The same focused unit command produced the expected pre-fix failures recorded above.

| Check | Command and result |
| --- | --- |
| Build | `dotnet build Nova.slnx --no-restore` — pass, zero warnings/errors. |
| Focused unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build --filter-class '*PlayerManagementServiceTests' --filter-class '*CreatePlayerInputValidationTests' --filter-class '*PlayerCreationOperationTests'` — 121 passed, zero failed/skipped. |
| Focused HTTP | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build --filter-method '*CreationDistinguishesInvalidOperationsFromExpiryAsync'` — six passed, zero failed/skipped. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — 3,691 passed, zero failed/skipped. |
| Format | `dotnet format Nova.slnx --verify-no-changes --no-restore --verbosity diagnostic` — pass, zero files changed. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — 669 passed, zero failed/skipped. |
| Full browser | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` — 208 passed, zero failed, eight existing opt-in capture skips. |

Selected the full browser suite because the shared creation input and HTTP classification are used
across server-rendered and WASM workflows. Existing `PlayerFormBrowserTests` cover valid creation,
validation, duplicates and retained retries; lifecycle, enrollment, import and history siblings also
ran. Source and generated assets stayed fixed during the run. Migration-model evidence at `c9c1d7e`
remains applicable because model, migration and provider configuration inputs are unchanged.

The source patch excluding this record has matching before/after browser SHA-256
`36E67EB08F5F28699F975DA3D54A0FC8FECA7A6284FE0EC06B5089D94160FCFE`; generated theme CSS remains
`559EC45DA0B8540B2DA715B171FC23B565F133D733E9A4C2E727F265495807DC`. Only this record was finalized
after the gates. Remote `main` still points to `fc2c0053`, with no incoming merge-input changes.

Focused self-review checked annotation/service ordering, helper failure outputs, unchanged receipt
serialization/fingerprints, authorization and enrollment, consumer non-settlement, and the sibling
follow-up above. Full review bodies, conversation comments and inline replies were compared with the
previous inspection using paginated APIs; this is the only new finding and it has no inline thread.
All five existing threads remain resolved. `git diff --check` and all 50 relative file links pass.
The ten-file delta, including this record, is committed once for this review round. Fresh CI and
automatic review of the pushed commit remain pending; no review is manually requested.

## Browser history validation incident

The second expiry-round full browser run passed 207, failed one and skipped the eight existing
opt-in captures. The duplicate-correction and expiry scenarios passed. The original
`ProtectedBackKeepsThenResumesExactHistoryEntriesAndForwardContextAsync` failure reproduced at its
post-Discard finder wait with the previously added failure diagnostics, on the current round's inputs
before the history-assertion change. Unlike the original failure, this run records the settled state:

| Observation | Captured evidence |
| --- | --- |
| Expected and actual URL match | Both were `https://localhost:57035/campaigns/20?tab=evaluate&evaluation=true&evalSearch=60`. |
| Original finder history entry restored | Current key `3fb4939a-a2b2-481b-9406-35dda36d9cfa` matches the finder entry; the later selected entry remains `e4933ea1-e4e8-44b3-817c-4e1685281f20`. |
| Traversal and rendering completed | Diagnostic `popstate` at 657 ms and `enhancedload` at 705 ms both report the finder URL. ARIA contains search value `60`, one matching player, and no selected-player editor. |
| Failure is in the event waiter | `WaitForURLAsync` still reported its 30,000 ms navigation-to-Commit timeout, despite the exact destination already being current. |

The [version-matched Playwright implementation](https://github.com/microsoft/playwright-dotnet/blob/v1.62.0/src/Playwright/Core/Frame.cs)
checks the current URL once and otherwise subscribes to a navigation event; it does not keep asserting
the current URL while waiting. The installed package was checked with
`dnx dotnet-inspect -y -- member Microsoft.Playwright.Core.Frame WaitForURLAsync:4 --package Microsoft.Playwright@1.62.0 --all -S 'Decompiled Source'`.
The exact event-subscription scheduling gap was not instrumented, but the recorded URL, history and
rendered state establish an event-wait false negative, not an absent application transition or a
failure attributable to shared-resource contention.

Changed the shared destination assertion and its protected-origin sibling to
[`Expect(page).ToHaveURLAsync`](https://playwright.dev/dotnet/docs/api/class-pageassertions#page-assertions-to-have-url),
which retries the exact state assertion. It retains the same 30-second deadline and the existing
failure diagnostics. All history-key, finder, selected-player, empty-draft, Keep and Forward assertions
remain. No application navigation code, retry budget, timeout, skip or suppression changed. This is
a correction to how the test observes successful navigation; it does not waive the navigation outcome.
The capture class passed all 19 scenarios with
`dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-class '*CampaignEvaluationCaptureBrowserTests'`.
The final full suite passed all 208 executed scenarios, with the same eight opt-in skips. The incident
is closed by the reproduced diagnostic evidence, corrected state assertion, preserved outcome checks,
and confirming class/full-suite passes. No owner waiver or unsupported contention attribution is used.

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
bridge. Current browser recovery is in-memory only; persisted recovery and reconciliation remain
with #264. The diagnosed browser wait incidents are resolved as recorded above.
Eight existing environment-gated screenshot/manual evidence captures were not requested (`NOVA_A11Y_SCREENSHOTS`
and `NOVA_PLACE_EVIDENCE` unset); they are reported as skipped, not passed. Behavioral browser assertions,
including creation validation, duplicate correction and lost-response retry, ran and passed. No new skip
or suppression was introduced.
