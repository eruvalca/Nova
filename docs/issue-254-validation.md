# Issue #254 — placement correction and recovery

## Implementation and scope

Implemented on `codex/issue-254-place-recovery`, based on merged Place PR #269 at
`e6b1f382cefb5176ff509685df6554970a8fd1c8`. The user explicitly included the backend,
contracts, persistence, WASM client, and bounded history prerequisites in this slice.
The approved rail and selected-player sheet composition remains the visual authority.

- One placement command now carries a client UUIDv7, an expected local version, and an
  immutable operation receipt. Initial saves, confirmations, Keep, and recovery use it.
- Membership locks precede season, roster, campaign, player, and team locks. Receipt
  replay binds the tenant, actor, and exact request. Committed decisions and definitive
  business rejections are retained for 24 hours. Exact replay cannot create new activity.
- UUID timestamps prevent expired commands executing after receipt cleanup. Invalid or
  future IDs are validation failures, distinct from expiry. Cleanup is globally bounded
  to 500 rows and includes receipts whose club was deleted.
- Prior-season evidence follows advancement ancestry and campaign opening sequence.
  History reads the existing append-only placement activity using an indexed player
  snapshot key, with 20-item keyset pages; receipts are not the history ledger.
- Reassignment and terminal withdrawal require explicit consequence confirmation.
  Historical and local decisions remain separate. Only an administrator can deliberately
  supersede an inherited withdrawal in an Active campaign.
- Exact pending commands are stored in tab session storage before dispatch. Storage
  failure blocks saving. Unknown results remain recoverable; current-state agreement
  never substitutes for receipt proof. Expiry permits a deliberate review of the original
  participant before leaving the uncertain operation behind.
- Required authority/placement refresh failures retain the edit gate. Optional history
  loads independently. Identity changes invalidate pending interactions and mounted
  evidence; asynchronous operation/storage generations reject obsolete completions.
- Players, Teams, details, and Draft links retain normalized local return context.
  Queue scroll is tab-scoped. Closed campaigns expose no new placement mutation.

## Initial implementation validation

Tested base: `e6b1f382cefb5176ff509685df6554970a8fd1c8`; the tested implementation was
subsequently committed as `7d22a6953de62cded2fb0a1dccca4297c47e1e0b` on
`codex/issue-254-place-recovery` and opened as PR #270. Source fingerprint:
`f0a3e678ef6236561fc89b96973d882063db5270e6ed9ff1df446fa89df5c2bf`.
The fingerprint hashes the sorted `(repository path, SHA-256 file bytes)` list for 1,156
tracked/unignored source, project, style, and build files; the complete manifest is retained
locally at `.git/issue254-source-fingerprint.json`. Documentation/captures are excluded.

| Check | Current result |
| --- | --- |
| Full solution build | Passed; only three existing Sass import deprecation warnings |
| Full unit suite | 3,287 passed; 0 failed/skipped |
| Full integration suite | 614 passed; 0 failed/skipped |
| EF pending-model check | Passed; no model changes since incremental migration |
| Contrast | All ratios and compiled-token assertions passed |
| Impeccable detector | No new findings; one existing shared-filter small-font advisory, documented below |
| Full browser suite | 178 passed; 0 failed; 7 existing optional screenshot skips (185 total) |
| Format verification | Passed; no remaining changes requested |
| Code and finish review | All raised findings resolved; finish verification scoped to its three scored fixes |

The EF CLI reports version 10.0.8 against runtime 10.0.12; the pending-model check
succeeded. No package/tool changes were introduced for this warning.

## Regression evidence

- `CampaignPlacementServiceReplayTests`: original result after later save/closure,
  changed-payload rejection, actor binding, stale membership, durable business rejection,
  expiry, and no-op identity.
- `CampaignPlacementReplayPostgresTests` and `CampaignPlacementRetryTests`: concurrent
  duplicates, transaction failure, lost acknowledgement, later save, and deleted-club
  recovery. Postcommit fault gates release database locks before competing transactions.
- `PlacementContextQueryServiceTests` / `PlacementContextHttpTests`: 20-item paging,
  advancement ancestry despite editable-date order, unavailable historical teams, endpoint
  validation, and tenant/membership boundaries.
- `HttpCampaignPlacementServiceTests` / `HttpPlacementContextQueryServiceTests`: strict
  success shape and relationship validation; malformed responses do not prove a save.
- `CampaignPlacePanelTests.Recovery` and workspace authority tests: storage refusal on
  each dispatch route, exact replay/restoration, confirmation cancellation, failed conflict
  reload, delayed history, lifecycle receipt feedback, and identity invalidation.
- `CampaignPlaceBrowserTests`: reassignment/cancel/keyboard confirmation, correction
  return context, real lost HTTP acknowledgement and refresh recovery, two-session conflict,
  Closed direct links, bounded queue interaction, and desktop/mobile captures.

Existing effective-placement and lifecycle suites continue to assert single membership,
source/destination counts, no historical fallback, compatibility cutoffs, and the distinction
between Needs placement and close readiness.

## Review dispositions

Independent code review identified and drove these fixes:

1. Mounted workspace identity was only initialized once: authentication subscription now
   clears mounted/persisted evidence and reloads authority.
2. Reusable owner strings permitted late operation adoption after an identity round trip:
   monotonic operation and storage generations now guard completions and cleanup.
3. Expiry review could overlap replay or review the wrong player: busy phase and checks
   against the original pending participant now gate cleanup.
4. Keep navigation intent could outlive its save: intent is bound to the operation UUID.
5. Optional history delayed required team evidence: it now runs as an independent region.
6. Closure refresh erased confirmed receipt feedback: immutable success feedback is retained
   within the same user/club/campaign scope.
7. Withdrawal supersession copy falsely promised availability: terminal withdrawal is
   described before other supersession consequences.

Finish review required readable stacked rail filters, wording about retaining the save
request rather than the placement, and attribution inside the effective-decision region.
One batch fixed all three. The verification returned **ship** for those scored fixes, with
no visible regressions. The evidence packet records the original and final dispositions.

The real browser run also exposed a disconnected-circuit module-disposal exception.
Disposal now catches only `JSDisconnectedException`, consistent with the destroyed-browser
resource lifecycle. It does not suppress mutation, rendering, or live-circuit failures.

An expanded detector pass included the reused filter component and reported its existing
`.discovery-status` `.8125rem` declaration as an advisory outside the detected type ramp.
The declaration predates this implementation; no suppression or type-token change was added.

## Design evidence and measurement limitation

See [the #254 packet](../.impeccable/review/issue-254/README.md) and
[the Place direction contract](../.impeccable/surfaces/placement.md).
The approved #255 comp is retained as the direction decision. Its documented 57% whole-frame
comparison did not faithfully represent the locked rail-and-sheet geometry; no new approval
or successful image-diff score is claimed. The unrelated local Evaluate build-state warning
from the detector does not describe this Place slice. No composition change is introduced.

## Guidance applied

Read and applied `AGENTS.md`; C# conventions, API endpoints, service layer, validation,
EF tenancy, season lifecycle, placement decisions, Blazor architecture, UI design, testing,
and observability instruction files under `.github/instructions/`.
Recipes read: `add-feature-slice`, `add-api-endpoint`, `add-domain-persistence`, `add-blazor-ui`,
`nova-testing`, and applicable references for locks/retries, query construction, strict WASM
contracts, component lifecycle/state, parameters, render modes, forms, and browser/provider
verification. Impeccable context, harden, craft-floor, and polish guidance were applied.
The dotnet test generation/run skills were applied for regression work and command selection.

## Commands and execution order

- `dotnet build Nova.slnx --no-restore`
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build`
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build`
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build`
- `dotnet format Nova.slnx --verify-no-changes --no-restore`
- `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext --no-build`
- `npm run check:contrast` from `Nova/`
- `node .agents/skills/impeccable/scripts/detect.mjs` against the changed Place,
  Players/Teams/Entry markup and the compact filter markup/styles, with `--json`
- `git diff --check`

Build completed before suites. Unit, integration, and browser runs were serialized; no
integration/browser run shared Docker capacity with another suite. Application/test source
was frozen during browser execution. `NOVA_PLACE_EVIDENCE` pointed at the curated packet's
`captures/` directory for review capture runs. Final full-suite captures went to
`.git/issue254-final-captures` so the reviewed packet remained unchanged. Local command logs are under `.git/issue254-*`.
Formatting repaired encoding/import/whitespace issues; it did not change behavior.

During development, the first full runs found outdated endpoint registrations, durable
rejection counts, and precommit fault gates. Browser fixture corrections covered an
incompatible test team and an obsolete conflict-copy expectation. The final correction
return test compares exact query keys and values, allowing canonical query ordering.
A subsequent full-run timeout at the Teams handoff had no saved DOM trace, so its exact
cause is not claimed. The correction test now first proves Place attachment via its
filter control, then checks the Teams path, heading, and target link; failure diagnostics
include the actual URL and main content. Its targeted rerun passed without longer timeouts
or retrying the navigation click.

Test-generation scratch is retained under `.git/testagent/`, outside the change. Its static
source-pairing helper could not load Roslyn 4.14; no coverage claim or dependency change was
made from that failed helper. No tests were disabled or assertions weakened. The new
PostgreSQL duplicate-operation fixture narrowly suppresses CA5394 for a random fixture ID,
with a local rationale: the value isolates test data and is not a secret or security token.
The exception does not affect production randomness, operation UUIDs, or behavioral coverage.


## Observed test-environment timing

One full run passed every Place case but timed out in the unchanged Evaluate thousand-player
page-two check while its UI still showed “Finding players…”. That exact method passed
unchanged in isolation. No assertion, timeout, or performance threshold was changed. The
subsequent full run passed all 178 enabled browser tests on an idle machine, with no
concurrent format/build work (185 total, seven existing optional capture skips). The earlier
run had a concurrent read-only format check; contention is possible, but its precise effect
is not established. The original log is retained locally at
`.git/testagent/browser-page-two-timeout.log` and the isolated pass at
`.git/testagent/scale-browser-isolated.log`.


Final full-run durations: unit 37.444s; integration 1m26.972s; browser 3m37.539s.
Final source fingerprint was rechecked after browser completion and is unchanged. All
commands were local and preceded the initial PR commit. The tested source manifest was checked
against the clean committed checkout before correcting the PR body's validation metadata.

## Guidance hygiene review

Reviewed the implementation findings against Microsoft's
[Instructions Hygiene](https://devblogs.microsoft.com/dotnet/instructions-hygiene-what-frontier-models-still-need-you-to-say/)
approach: retain consequential repository knowledge, repair stale examples, and keep conditional
detail in skill references. No new skill, global instruction, or design-workflow exception is warranted.

| Decision | Evidence and disposition |
| --- | --- |
| Update existing recovery guidance | Owner keys could repeat after an identity round trip; expiry review could target another selection; Keep intent could outlive its operation. Clarified generations and command-owned subjects/navigation in `add-blazor-ui`'s lifecycle reference. Closure feedback now has a scoped regression pointer. |
| Replace stale testing advice | The integration reference still recommended a receipt-query gate even though receipt lookup now precedes execution. Replaced it with the verified postcommit gate, including current-membership limits on result disclosure. |
| Repair persistence examples | Pointed receipt execution and bounded placement cleanup at their current owners, with a link to the fault-injection procedure. |
| Clarify browser evidence | Return URLs preserve exact state despite canonical key ordering. Added the existing semantic assertion example and a controlled rerun procedure that does not infer a timeout's cause from an isolated pass. |
| Keep existing rules | Authentication subscriptions, optional-region independence, strict nested HTTP validation, attachment proof, and separate review already have guidance. Their implementation misses call for applying those rules, not duplicating them. |
| Keep incident-specific details here | Formatter/analyzer fixes, fixture mistakes, the unproven timing cause, and the comp/detector limitations do not justify permanent instructions or relaxed checks. |

The four edited references belong to three existing skills (`add-blazor-ui`,
`add-domain-persistence`, `nova-testing`). Their existing triggers and reference links already route
this work. Both Codex and Copilot consume these canonical `.agents/skills` sources; none of these
three skills has a separate `.github/skills` copy. Guidance reviewed included `AGENTS.md`, the
Blazor/testing instructions, those skills and their applicable references, and the system
`skill-creator` recipe. Implementation and regression sources were checked before updating examples.

Documentation-only verification: all three skill validators passed, edited-reference relative links
and named examples resolved, and `git diff --check` passed. Application suites were not rerun for
these guidance edits; the implementation results above remain the behavioral evidence. Whether the
clarifications prevent recurrence remains to be observed on a subsequent independent feature.

## PR review round 1

Addressed Copilot review `5205221195` on initial PR head
`7d22a6953de62cded2fb0a1dccca4297c47e1e0b`. All six threads and all three suppressed
comments were inspected. The changes form one review-round commit; no new review was requested.

| Finding | Disposition and behavioral evidence |
| --- | --- |
| Valid unavailable prior assignment rejected by WASM | Fixed the validator to accept null decision team ID and null team with `CanKeep=false`. `ContextClientRequiresConsistentPriorAssignmentEvidenceAsync` now uses the actual producer shape and rejects missing-ID-with-team, CanKeep-without-team, and invalid IDs. |
| `BeginDecision` allegedly guarded by `CanRecordDecision` | Inapplicable: the reviewed head already uses pre-open saving/pending/lifecycle/authority/freshness guards. `AssignedPlayerRequiresDeliberateReassignmentAndConfirmation`, `ReassignmentRequiresConfirmationAndCancelPreservesTheSavedTeamAsync`, and `PriorCampaignWithdrawalRequiresAdministratorSupersessionAndLeavesClosedDecisionImmutableAsync` exercise the correction flows. |
| Invalid browser state accepted for replay | Added shared read/write validation for UUIDv7, nonempty GUID token, positive safe-integer participant/team IDs, allowed numeric outcome, and outcome/team relationship. `RetainedPlacementStorageRejectsMalformedCommandsAndPreservesEvidenceAsync` exercises the real module, verifies invalid bytes remain intact, and round-trips valid and expired commands. It tests storage directly; existing browser workflow tests cover UI dispatch/replay. |
| Nonpositive history cursor emitted by URL builder | Builder omits invalid cursors. `ContextUrlIncludesOnlyPositiveCursors` checks null, zero, negative, one, and maximum values. The HTTP rejection test sends the invalid query directly, independently of normalization. |
| Endpoint name and role literals | Mapping consumes `PlacementContextEndpoints.GetPlacementContextRouteName`; the persisted-role query derives its value from `Roles.ClubAdmin`. |
| Suppressed: Teams correction route literal | Uses `ClubRoutes.Teams`; the existing correction-return browser scenario exercises the link and retained context. |
| Suppressed: unavailable team option filtered out | Retains the saved option as disabled. `AnUnavailableSavedTeamStaysEvidenceAndCannotBeSelected` covers archived, incompatible, and unavailable reasons and verifies no dispatch through a disabled target. |
| Suppressed: validation record contradicted PR metadata | The PR body was already corrected to the repository template after verifying the initial source manifest against the committed head. This record now identifies that initial commit and the subsequent review round. |

Separate local reviewer `/root/recovery_review` inspected the complete round diff and both new
test files, then checked the HTTP test follow-up. Disposition: **no actionable findings**.
The review was read-only and did not request an external review or run suites. Existing route,
role, URL-builder, validation, selection/save, and replay callers were inspected for the same
invariants. Applied the existing feature/API/Blazor/testing recipes and their contract, lifecycle,
interop, and provider/browser references; no new agent rules or diagnostic suppressions were added.

The first integration run passed 613 cases and exposed one obsolete test assumption: its invalid
cursor passed through the newly normalizing builder and therefore never reached the endpoint.
The fixture now constructs the malformed wire URL directly; the server rejection assertion remains.
Compiler-guided repairs used a normalized role variable and a `Uri` overload, and corrected the
browser test constant's naming. No checks or retry budgets were relaxed.

Final tested source fingerprint for this round:
`c23f87952c51b76d90af2496639a41990c9ea4bb0d6e0537d4bc78a3058ad2ff`.
The manifest at `.git/pr270-round1-source.json` covers the original 1,156 source files plus the
two new test files; its fingerprint hashes the UTF-8 compact JSON of sorted path/SHA-256 pairs.
Documentation is excluded. All 1,158 source hashes were rechecked unchanged after browser completion.

| Check / command | Final round result |
| --- | --- |
| `dotnet build Nova.slnx --no-restore` | Passed; three existing Sass deprecation warnings |
| Focused unit: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build --filter-class '*HttpPlacementContextQueryServiceTests' --filter-class '*PlacementContextEndpointTests' --filter-class '*CampaignPlacePanelTests'` | 94 passed |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | 3,296 passed; 0 failed/skipped; 19.917s |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | 614 passed; 0 failed/skipped; 1m21.330s |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | 179 passed; 0 failed; seven existing optional screenshot skips; 3m36.014s |
| `dotnet format Nova.slnx --verify-no-changes --no-restore` | Passed |
| `npm run check:contrast` from `Nova/` | Passed |
| `node .agents/skills/impeccable/scripts/detect.mjs Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor.js --json` | No findings in inspected files; previously documented unrelated Evaluate build-state warning remains |
| `git diff --check` | Passed |

Build preceded tests. Integration and browser suites ran serially with no competing suite;
browser source/assets stayed fixed and no build/format command ran alongside it. This round changes
no schema; the initial migration-model evidence remains applicable. Temporary new captures went to
`.git/pr270-round1-captures`, leaving the curated reviewed packet unchanged. Logs, the failed first
integration run, and the source manifest remain under `.git/pr270-round1-*`. The retained comp and
finish-review limitations above remain explicit; no new composition approval is claimed.

## PR review round 2

Reviewed Copilot review `5205450720` on `f6d1fb1a2b870a7977bd892d6877a5cdbd6a12f9`,
including both inline findings and all four suppressed findings. This round is one commit.

| Finding / requirement | Disposition and behavioral evidence |
| --- | --- |
| Terminal null history cursor rejected | Inapplicable: the code uses `NextEventId is <= 0`, which does not match null. `ContextClientRequiresBoundedOrderedHistoryAsync("valid")` supplies a null continuation and succeeds; the HTTP history test also verifies terminal null. |
| Administrator role required again for receipt recovery | Inapplicable to the approved contract: persisted club membership, original actor, tenant, and exact payload gate receipt access; administrator authority gates a new withdrawal override. `DemotedAdministratorCanRecoverAnOverrideButCannotExecuteANewOverrideAsync` proves both sides using persisted role removal and stale administrator claims, unchanged prior withdrawal/token, and no duplicate activity. |
| Suppressed: earlier page replaces newer items | Retained the requested bounded 20-item paging and added **Latest changes** for a return within the history region. `HistoryPagesStayBoundedAndCanReturnToLatestChanges` and `PlacementHistorySupportsBoundedEarlierPagesAndKeyboardReturnToLatestAsync` exercise page sizes and keyboard navigation. No unbounded accumulation is introduced. |
| Suppressed: URL duplicates the mapped route | URL generation substitutes IDs into `PlacementContextEndpoints.Relative`. `ContextUrlIncludesOnlyPositiveCursors` still verifies exact paths and cursor normalization; the HTTP suite verifies mapping. |
| Suppressed: malformed saved history reaches server-rendered UI | Both projection and WASM use `PlacementHistoryValidation`. `PlacementContextSkipsMalformedEvidenceWithoutLosingRawPageBoundaryAsync` covers seven malformed shapes, interleaved invalid rows including the raw boundary, ordered remaining items, continuation, and a terminal page. |
| Suppressed: SQLite placement cleanup materializes every receipt | Both placement-cleanup callers now execute the same filtered, ordered, capped SQL deletion. The existing SQLite model customizer maps receipt expiry to sortable UTC ticks, leaving the production PostgreSQL model unchanged. `PlacementCleanupRemovesAtMostFiveHundredExpiredReceiptsPerPassAsync` verifies both callers, oldest-first selection, retained fresh receipts, and no tracked receipt materialization. `GlobalPlacementCleanupDeletesOnlyFiveHundredOldestExpiredReceiptsInPostgresAsync` verifies the production provider and absent-club snapshots. Existing tenancy/deleted-club tests remain green. |

Separate reviewer `/root/recovery_review` found no actionable issues in the complete diff,
then separately checked the added browser scenario and its fixture interactions. It independently
confirmed the two inapplicable inline findings. The finish review requested fresh captures for
the history controls, then identified touching button borders. A wrapping flex row with an
8px gap resolves that local finish issue without changing composition. The first full browser
run passed before the spacing fix; its captures and log are retained locally. Final captures
and the reviewer's disposition are recorded with the evidence packet.

Guidance applied: the existing feature/API/domain/Blazor/testing recipes and their query,
retry/locking, contract, lifecycle, interop, and provider/browser references; matching repository
instructions listed above; the .NET test execution and generation recipes. Sibling checks covered
both cleanup callers, server/WASM history validation, and ownership of regional history results.
No new skill, permanent instruction, migration, or diagnostic suppression was required.

The initial build exposed new-test compilation/style mistakes (nullable struct assertion,
explicit result construction, ordinal string comparisons); those were corrected before running
tests. No assertions, retry budgets, or checks were weakened.

The first full integration run used a stale incremental assembly: the new PostgreSQL method
was added during the initial build, before the output timestamp but after compilation read its
source. The unchanged test count and a zero-test focused discovery exposed this. A complete
`dotnet build Nova.slnx --no-restore --no-incremental` rebuilt the assembly; the method was
verified present and the full suites rerun. The earlier passing run is not accepted as evidence
for the new test. Its log and the zero-test discovery log remain under `.git/pr270-round2-*`.
The browser setup also restores its temporary current-user values in `finally`.

Source fingerprint: `af01d89302b13b698e06b653daab6908569d3b830410a21b431b5f228cc7a2b2`.
The local manifest `.git/pr270-round2-source.json` covers 1,160 source files using the same
path/SHA-256 procedure as round 1, including both new files. Documentation and captures are excluded.
The PR body identifies the commit containing this tested source.

| Check / command | Result |
| --- | --- |
| `dotnet build Nova.slnx --no-restore` | Passed; three existing Sass deprecation warnings |
| Focused unit: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build --filter-class '*CampaignPlacementServiceTests' --filter-class '*HttpPlacementContextQueryServiceTests' --filter-class '*PlacementContextEndpointTests' --filter-class '*CampaignPlacePanelTests'` | 178 passed |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | 3,307 passed; 0 failed/skipped; 19.955s |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | 615 passed; 0 failed/skipped; 1m23.552s |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | 180 passed; 0 failed; seven existing optional screenshot skips; 3m33.243s |
| `dotnet format Nova.slnx --verify-no-changes --no-restore` | Passed |
| `npm run check:contrast` from `Nova/` | Passed |
| `node .agents/skills/impeccable/scripts/detect.mjs Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor.js --json` | No findings in inspected files; the previously documented unrelated Evaluate build-state advisory remains |
| `git diff --check` | Passed |

Build preceded tests. Integration/browser execution was serialized across the machine. The browser
run kept source/assets fixed with no concurrent build/format process. Logs and temporary captures
are under `.git/pr270-round2-*`; curated history-control screenshots supplement the original packet.
No production schema changed in this review round, so the initial migration-model evidence remains applicable. The
existing comp measurement limitation remains explicit; this round claims no new whole-frame score.

All 1,160 source hashes were verified unchanged after the final browser run. The refreshed
finish review returned **ship** for the history spacing fix, with no remaining findings in that
scope. Final viewport screenshots and their source/checksum sidecars are curated in the packet.

## PR review round 3

Reviewed Copilot review `5205773923` on `fad30c3e66efc918a5ddb15c1b5d369292b8d328`,
including its inline finding and all three suppressed findings. This round is one commit.

| Finding | Disposition and behavioral evidence |
| --- | --- |
| Prior-season lookup should discard an earlier Assigned decision after a later non-assignment | Inapplicable to the approved historical-evidence contract. The locked [Place brief](../.impeccable/surfaces/placement.md#selected-player-working-sheet) explicitly requires the most recent prior-season `Assigned` outcome. `PreviousPlacementRetainsTheLastAssignedEvidenceWithoutReplacingCurrentSeasonDecisionsAsync` covers later prior-season NotSelected and Withdrawn, then proves that a current-season saved decision disables Keep and remains the persisted outcome without a team. This history does not participate in the current-season effective-placement query or create fallback roster membership. |
| Suppressed: contradictory previous history transition | Shared server/WASM validation now rejects previous Undecided and a previous team name without previous Assigned, while allowing Assigned with a missing historical team name. The HTTP theory covers valid and invalid partitions; five additional stored-history corruption shapes verify skipped items without losing the raw twenty-row cursor boundary. |
| Suppressed: settlement ignores PageCorrected/Obsolete | Settlement explicitly handles both outcomes, tracks requested page corrections, and gates feedback/editing/Keep advancement on adopted queue and selected evidence. Eight component cases cover early and delayed parameters, failed corrected reads, closure, repeated Back/Forward correction to an already-applied key, and Keep success/failure. `SavingTheLastParticipantOnPageTwoAdoptsPageOneBeforeEnablingEditingAsync` exercises the real last-row save and URL/count reconciliation. |
| Suppressed: SQLite cannot execute bounded cleanup | Inapplicable in this repository: the SQLite harness maps receipt expiry to sortable UTC ticks, and both cleanup entry points already have passing provider tests. `PlacementCleanupRemovesAtMostFiveHundredExpiredReceiptsPerPassAsync` verifies oldest-first capped deletion with no tracked materialization for both callers. `GlobalPlacementCleanupDeletesOnlyFiveHundredOldestExpiredReceiptsInPostgresAsync` verifies the production provider. A provider split would reintroduce the behavior corrected in round 2. |

Separate reviewer `/root/recovery_review` inspected the complete round diff and new tests.
It identified a repeated-correction case where Back/Forward could return to the applied page
while a selected-player read was pending, and a browser URL wait requiring `WaitUntilState.Commit`.
Both were fixed and the navigation case received its own regression. Follow-up review, including
the Keep navigation theory, returned **no actionable findings**. No external review was requested.

Applied the existing feature/API/domain/Blazor/testing recipes and matching instructions already
listed in this record, including their contract, async ownership, SQLite, and browser references.
Sibling inspection covered queue loading, selected reads, conflict retry, expired recovery,
posture cleanup, both receipt cleanup callers, and server/WASM history projection. The existing
browser Commit guidance covered the local review finding; no new skill or instruction was needed.
No markup, style, composition, schema, diagnostic suppression, or retry budget changed.

Initial builds exposed method-length and new-test compilation errors. Queue failure handling was
extracted, a redundant posture branch removed, and the assertions/types corrected. No checks or
assertions were weakened. Formatting ran before the final build; source remained fixed during tests.

Source fingerprint: `8e586e04343c3cc7918b50bd490f568bce6a4e1b44168e3d47c0f69f59089da1`.
The local manifest `.git/pr270-round3-source.json` covers 1,163 source files using the same sorted
path/SHA-256 procedure as prior rounds. The PR body identifies the commit containing this source.

| Check / command | Result |
| --- | --- |
| `dotnet build Nova.slnx --no-restore` | Passed; three existing Sass deprecation warnings |
| Focused unit: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build --filter-class '*CampaignPlacePanelTests' --filter-class '*CampaignPlacementServiceTests' --filter-class '*HttpPlacementContextQueryServiceTests'` | 199 passed |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | 3,333 passed; 0 failed/skipped; 54.197s |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | 615 passed; 0 failed/skipped; 4m39.839s |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | 180 passed; 0 failed; eight existing optional capture skips; 5m26.412s |
| `dotnet format Nova.slnx --verify-no-changes --no-restore` | Passed |
| `npm run check:contrast` from `Nova/` | Passed |
| `node .agents/skills/impeccable/scripts/detect.mjs Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor.js --json` | No findings in inspected files; the previously documented unrelated Evaluate build-state advisory remains |
| `git diff --check` | Passed |

Build preceded tests. The read-only format verification overlapped unit/integration execution;
integration/browser execution was serialized across the machine. Browser source/assets stayed
fixed with no concurrent build/format process. No production schema changed in this review round, so initial migration-model
evidence remains applicable. Existing curated captures and their finish-review/comp limitations
remain applicable: this round changes behavior without changing the locked composition.
Logs and the source manifest are under `.git/pr270-round3-*`. All 1,163 source hashes were rechecked unchanged after browser completion. The Place capture opt-in was not set this round, so its existing capture case joins the seven optional accessibility-capture skips; the new browser regression passed. Existing curated evidence was retained.

## PR review round 4

Reviewed Copilot review `5206168370` on `94414d2cb47b2899e6d70acb9599b3982697e373`.
Its “Needs a closer look” body had no inline comments but four suppressed findings; all four
were actionable and are addressed together in one commit.

| Finding | Disposition and behavioral evidence |
| --- | --- |
| Receipt recovery expiry has no upper bound | The strict placement client now permits at most 24 hours plus the existing Evaluate one-minute clock allowance after commitment. Boundary cases cover 24h, 24h1m, one second beyond, and 48h. Invalid success bodies remain unproven server errors, not evidence that the operation did not commit. |
| Malformed session data permanently blocks recovery | A read distinguishes unavailable storage, a valid pending command, and exact invalid bytes. The explicit **Discard invalid data and refresh** action requires fresh authority/placement evidence and compare-and-delete of those same invalid bytes. It cannot discard a repaired/replaced valid operation, execute a save, or imply rollback. Failures and ownership changes retain the editing gate. Component, JavaScript module, and real keyboard/browser tests cover these transitions and a subsequent deliberate save. |
| Player detail keeps an obsolete return URL | `OnParametersSet` now normalizes changed return context on the reused component, retaining the initial-load value. Component and hydrated browser tests exercise query-only changes and Back/Forward without reloading player detail; unsafe return URLs remain rejected. |
| UUIDv7 validation omits the RFC variant | Placement and its Evaluate sibling now require both version 7 and RFC variant 8/9/a/b before execution. Service cases cover accepted and rejected variants; four real HTTP cases prove invalid requests return validation errors without placement, receipt, or activity effects. |

Separate reviewer `/root/recovery_review` inspected the complete round diff and tests. It found
that navigation during asynchronous recovery cleanup could leave the newly selected participant
with stale evidence. Both explicit invalid-data discard and expired-save cleanup now apply
deferred navigation and preserve a conflict gate if replacement evidence fails. Four regression
cases cover both cleanup paths with successful and failed reads. Follow-up review, including
null-response handling and the final fixture/initial-return adjustments, found **no actionable findings**.
No external review was requested.

The independent finish reviewer requested current captures, then found that Retry storage was
shorter than the discard action and stretched across the desktop field. Both actions now share
a wrapping group with the existing 8px gap and 44px minimum-height rule. A separate mobile viewport
capture makes the controls inspectable above the fixed navigation bar. This is a regional recovery
state, with no material change to the locked composition or tokens.

Applied the existing feature/API/domain/Blazor/testing recipes and their contract, interop,
retry/locking, SQLite, provider, and browser references; matching repository instructions listed
above; the .NET test execution/generation recipes; and Impeccable's hardening reference.
Sibling inspection covered both UUID executors, valid/invalid/expired storage cleanup, deferred
navigation, query-return normalization, and the Evaluate receipt lifetime contract. No new skill
was warranted. The existing bUnit reference received a narrow delayed-JS fixture
clarification after the reproduced missing-result timeout described below.
No schema change or diagnostic suppression was introduced, and no assertion or retry budget was weakened.

Source fingerprint: `724d9f12b43419dcbe59aad1b0927fff6fcf975529922b9f8fc3c48f91dfca28`.
The local manifest `.git/pr270-round4-source.json` covers 1,169 source files. Documentation and
captures are excluded. The PR body identifies the commit containing this tested source.

| Check / command | Result |
| --- | --- |
| `dotnet build Nova.slnx --no-restore` | Passed; three existing Sass deprecation warnings; 27.96s |
| Focused unit: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build --filter-class '*CampaignPlacePanelTests' --filter-class '*CampaignPlacementServiceTests' --filter-class '*HttpCampaignPlacementServiceTests' --filter-class '*PlayerDetailComponentsTests'` | 213 passed before the final recovery-button grouping |
| Focused browser: `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-method '*InvalidRecoveryDataCanBeDiscardedExplicitlyBeforeANewDeliberateSaveAsync' --filter-method '*PlayerDetailReturnContextFollowsInteractiveQueryNavigationAndBrowserHistoryAsync' --filter-method '*RetainedPlacementStorageRejectsMalformedCommandsAndPreservesEvidenceAsync'` | 3 passed; 0 skipped; 58.240s before the final recovery-button grouping |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | 3,356 passed; 0 failed/skipped; 44.522s |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | 619 passed; 0 failed/skipped; 4m00.413s |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` with `NOVA_PLACE_EVIDENCE` set | 183 passed; 0 failed; seven existing optional accessibility-capture skips; 5m34.541s |
| `dotnet format Nova.slnx --verify-no-changes --no-restore` | Passed before later focused changes; final changed-file verification and encoding correction are detailed below |
| `npm run check:contrast` from `Nova/` | Passed; theme sources unchanged by the final grouping |
| `node .agents/skills/impeccable/scripts/detect.mjs Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor.js --json` | No findings in inspected files; the previously documented unrelated Evaluate build-state advisory remains |

The first full browser run failed two existing cases (181 passed, 2 failed, 7 optional skips).
The Place search test typed before a proven Blazor attachment; its anchor-only probes could
navigate while prerendered. Both search-entry siblings now exercise the Filters toggle, retain
keyboard typing and full input/URL assertions, and wait for same-document navigation at Commit.
The log proves character loss but does not distinguish hydration from a render race. Independent
review cleared the readiness correction. The Evaluate WASM storage test exceeded its five-second
attachment assertion; the later failure snapshot contained the expected Retry storage control.
Its timeout, assertion, and production behavior were left unchanged for the focused/full reruns.
The failed log is retained locally as .git/pr270-round4-browser-final.log; it is not passing evidence.

The next focused run passed all four unchanged Evaluate cases. Both Place cases reached the
new interactive-readiness probe and exposed an accessibility defect: a Boolean Razor value
emitted empty ria-expanded instead of the required string. Filters now emits explicit
true/false strings. The same defect in Draft aria-busy and three tag-lifecycle aria-pressed
bindings was corrected after a sibling scan; native Boolean attributes remain unchanged.
Independent review confirmed all five bindings and retained the stronger browser assertions.
The intermediate failed focused run remains in .git/pr270-round4-browser-readiness-focused.log.

The final focused browser rerun passed all six cases (0 failed/skipped; 1m51.506s):
dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-method '*QueueSearchWidensBeyondTheOpenSectionAndKeepsFilterTruthAsync' --filter-method '*PlayerAndTeamsCorrectionReturnsPreserveSelectedPlaceAndDiscoveryAsync' --filter-method '*UnreadableCaptureCanLeaveExplicitlyWithoutErasingRecoveryDataAsync'.

A subsequent full browser run passed both corrected Place tests but repeated the same Evaluate
WASM attachment failure (182 passed, 1 failed, 7 optional skips; 5m40.849s). The previous 15-second
WASM preparation occurs before reload; the failing caller then used Playwright's default
five-second assertion after reload. The new recovery-blocked attachment helper observes the
exact storage-unavailable alert and Retry control using the existing 60-attempt/250ms hydration
policy. That alert is produced only after the interactive JS recovery read fails. It never clicks
Retry, modifies the draft, changes retained bytes, or adds recovery-operation retries. The WASM
negotiation listener stays active throughout, and exact blocking-copy, disabled-save, retained-byte,
and no-mutation assertions remain enforced. Independent review explicitly assessed this timing
adjustment and found it preserves enforcement. The failed run remains in
.git/pr270-round4-browser-verified.log.

After this final test-helper change, format verification also passed with
dotnet format Nova.slnx --verify-no-changes --no-restore --include Nova.Browser.Tests/EvaluationInteractionHelpers.cs Nova.Browser.Tests/CampaignEvaluationCaptureBrowserTests.cs.

A later unit run under concurrent validation load exposed three failures in the newly added
delayed-cleanup tests (3,353 passed; 3 failed). Installed bUnit 2.10.3 documentation confirmed
that an unconfigured JS result uses DefaultWaitTimeout, one second by default. The intended
delay was therefore a missing-result fault timer. The fixture now wraps a configured module
with explicit entered/release tasks, preconfigures the eventual result, and releases gates
during cleanup. Both invalid and expired cleanup plus ownership changes retain their assertions.
Independent review found no issues; focused verification passed all five cases (1.930s), then
the isolated full unit suite passed. The failed run remains in `.git/pr270-round4-unit-attachment.log`.
The first wrapper build needed a missing dependency-injection using directive; it was fixed
before tests. No timeout setting or assertion was weakened.

Applied the skill-creator recipe to add this non-obvious, version-qualified pitfall to the existing
`nova-testing/references/blazor-component-tests.md`, with pointers to the working fixtures.
The shared `.agents/skills` copy serves both Codex and Copilot; no separate copy exists.
`python C:/Users/eruva/.codex/skills/.system/skill-creator/scripts/quick_validate.py .agents/skills/nova-testing`
passed. No new skill or repo-wide rule was introduced.

The final fixture format check found only the new file's missing UTF-8 BOM. Applied the repository's
required encoding without changing C# behavior, then verified it with
`dotnet format whitespace Nova.slnx --verify-no-changes --no-restore --include Nova.Unit.Tests/Campaigns/CampaignPlacePanelTests.CleanupInterop.cs`.
The earlier scoped check included both cleanup-fixture files; its other checks passed. Build and
unit verification were repeated after the encoding change. Integration inputs were unchanged.

Additional exact verification commands for the later changes:

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build --filter-method '*InvalidDataDiscardCannotUnlockANewStorageOwnerAsync' --filter-method '*RecoveryCleanupAppliesDeferredNavigationOrKeepsAConflictGateAsync'`: five passed after the explicit handshake fix.
- `dotnet format Nova.slnx --verify-no-changes --no-restore --include Nova.Browser.Tests/CampaignPlaceBrowserTests.cs Nova.Browser.Tests/CampaignPlaceRecoveryBrowserTests.cs Nova.UI/Features/Campaigns/Components/CampaignRosterFilters.razor Nova.UI/Features/Campaigns/Pages/CampaignEntry.razor Nova.UI/Features/Tags/Components/TagDefinitionManager.razor`: passed.
- `dotnet format Nova.slnx --verify-no-changes --no-restore --include Nova.Unit.Tests/Campaigns/CampaignPlacePanelTests.CleanupInterop.cs Nova.Unit.Tests/Campaigns/CampaignPlacePanelTests.InvalidRecovery.cs`: found only the subsequently corrected encoding issue described above.
- `node .agents/skills/impeccable/scripts/detect.mjs Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor.js Nova.UI/Features/Campaigns/Components/CampaignRosterFilters.razor Nova.UI/Features/Campaigns/Pages/CampaignEntry.razor Nova.UI/Features/Tags/Components/TagDefinitionManager.razor --json`: no findings in the expanded five-file scan; the unrelated Evaluate build-state advisory remains.

Final browser verification passed every functional case, including the formerly failing Place
search and Evaluate WASM recovery scenarios. The exact final desktop/mobile images and their
geometry/source/checksum sidecars are curated in the existing packet. The independent finish
review reopened those images and returned **ship** for the recovery-control fix, with no remaining
findings in that scope. The comp-measurement limitation remains unchanged.

Build preceded tests; integration/browser suites ran serially across the machine. Final unit
verification ran in isolation. Browser runs kept application source/assets fixed with no concurrent
build or format process. Logs and manifests are under `.git/pr270-round4-*`; failed attempts above
are not accepted as passing evidence. All 1,169 source hashes and curated image checksums were
verified unchanged after final browser completion. `git diff --check` and the staged check passed.
No production schema changed in this review round, so the original migration-model evidence remains applicable.

## PR review round 5

Copilot review `5207232289` on `88db70740593b4028540c08e7fbfe60e5d38bc7a`
contained no inline comments and one suppressed finding. The missing authenticated-without-club
403 HTTP boundary test was actionable and is now covered by
`PlacementContextRouteReturnsForbiddenForAuthenticatedUserWithoutClubAsync`.
The test registers a real Identity user, clears persisted club membership, refreshes the
authentication cookie, and calls the placement-context route with valid positive IDs.
It asserts 403 from the membership policy before placement lookup. No production change was needed.

The existing anonymous 401, member success, malformed cursor, and missing-participant cases
remain in the same file. Related placement/effective-placement no-club tests and the Identity
registration, persisted membership, and cookie-refresh helpers were inspected. This round applies
the previously read testing, API, and tenancy instructions, `nova-testing` and its Aspire integration
harness reference, `add-api-endpoint`, and the .NET `run-tests` recipe. No repository runner overlay
exists. The existing recipes already cover this boundary; no new instruction or skill is warranted.

Independent read-only reviewer `/root/recovery_review` inspected the complete round diff,
production authorization policy, real authentication helpers, and neighboring tests. It reported
no findings and confirmed the exact 403 assertion covers the missing boundary.

Validation is associated with base `88db70740593b4028540c08e7fbfe60e5d38bc7a` plus the
test change above and source fingerprint
`7726f3da5f64b3d2748e560aa1744a0b6ae3f0e495cf76d12d7e64d141bd8feb`
(1,169 files). The PR body identifies the resulting tested commit. Documentation changes do not
alter these source inputs.

Round-5 verification:

- `dotnet build Nova.slnx`: passed (2m58.49s; three existing Sass import deprecation warnings).
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build`: 3,356 passed,
  zero failed/skipped (48.892s).
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build`:
  620 passed, zero failed/skipped (3m16.048s), including the new 403 case.
- `dotnet format Nova.slnx --verify-no-changes`: passed, no diagnostics.
- `git diff --check`: passed. Source fingerprints were verified unchanged after validation.

Build, unit, integration, and format ran sequentially; no other Aspire-backed suite overlapped.
The only source change is this HTTP test. Unaffected browser evidence is retained from
`88db70740593b4028540c08e7fbfe60e5d38bc7a`: 183 passed, seven existing optional capture skips,
zero failures, as recorded in round 4. No application markup, CSS, JS, persistence, or contracts
changed. The earlier visual, contrast, detector, and migration-model evidence retains its original
revision and limitations. All three suites must run again before merge. Local logs and source
manifest are `.git/pr270-round5-*`; these local files are not the durable validation record.

## PR review round 6

Copilot review `5207636688` on `d52c431da9a1538f78aa7982207c340a5e0a77f0`
contained one inline finding and one suppressed scope finding. Both were inspected.

### SQLite cleanup: inapplicable

The review asserted that the ordered, bounded `ExecuteDeleteAsync` could not run in the unit
and membership-pruning paths. `TenancyTestHarness.Options` registers
`CampaignClosureSqliteModelCustomizer` for all three contexts; it maps placement receipt expiry
to UTC ticks. The query therefore orders a sortable scalar in SQLite. Existing
`PlacementCleanupRemovesAtMostFiveHundredExpiredReceiptsPerPassAsync` exercises both direct
and membership cleanup, verifies the exact three survivors from 503 receipts, and requires an
empty receipt change tracker. Both cases passed again on the reviewed source. PostgreSQL has
separate bounded-deletion and deleted-club receipt coverage. Evaluation's materializing fallback
is not needed for this mapped placement query; no production change was made.

Independent reviewer `/root/recovery_review` checked the harness registration, model conversion,
both callers, exact survivor assertions, and PostgreSQL sibling evidence. It confirmed the finding
is inapplicable and that copying the materializing fallback would weaken bounded cleanup.

### Approved scope reconciled

The original implementation request explicitly approved including the missing backend, contract,
persistence, and WASM work for replayable placement operations and bounded placement history
within #254. The old issue boundary and parent slice rule had not recorded that amendment.
[Issue #254](https://github.com/eruvalca/Nova/issues/254) now quotes the approved amendment and
names the receipts/replay/24-hour cleanup and prior-season/20-item-history prerequisites.
[Parent #199](https://github.com/eruvalca/Nova/issues/199) records the same narrow exception.
The PR summary also records it. No separate #163 foundation issue is needed for those approved
prerequisites. The independent reviewer confirmed this scope evidence.

Both edited issue bodies were fetched immediately before mutation, exact-match guarded, and read
back after writing. Native membership remains #255 closed and #254 open; #199 remains one of two
children complete. PR #270 is unmerged, and no delivery checkbox, issue state, or parent integration
gate was changed. #163's area-level scope already accommodates the work and needed no body edit.

### Guidance and verification

Applied the previously read testing/API/tenancy and placement guidance, `nova-testing`, the .NET
`run-tests` recipe, `add-domain-persistence` and its query-construction reference. Applied
`sync-epic-roadmap` and read its roadmap-checks reference before updating the parent block.
The existing instruction to verify review findings against behavior was sufficient; no permanent
rule or skill was added for this false-positive report.

- Source is unchanged from `d52c431da9a1538f78aa7982207c340a5e0a77f0`; this round changes only
  this validation record and external issue/PR scope text. The source fingerprint remains
  `7726f3da5f64b3d2748e560aa1744a0b6ae3f0e495cf76d12d7e64d141bd8feb` (1,169 files).
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build --filter-method '*PlacementCleanupRemovesAtMostFiveHundredExpiredReceiptsPerPassAsync'`:
  both cases passed, zero failures/skips (3.158s).
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build`: 3,356 passed,
  zero failures/skips (43.016s), using the unchanged build from round 5.
- Build and 620 passing integration cases remain on `d52c431da9a1538f78aa7982207c340a5e0a77f0`;
  unaffected browser results remain on `88db70740593b4028540c08e7fbfe60e5d38bc7a` (183 passed,
  seven optional capture skips). No source, schema, UI, or test behavior changed this round.
- The #199 roadmap checker passed all seven mechanical checks before the update. The initial CLI
  positional invocation misparsed the repository argument and returned 404; the successful run
  calls the checker's exported functions directly, without changing or bypassing any checks:

  ```powershell
  node --input-type=module -e 'import { createGhIo, runChecks, formatReport } from "./.agents/skills/sync-epic-roadmap/scripts/check-roadmap.mjs"; const args = { repo: "eruvalca/Nova", epic: 199 }; const result = runChecks(createGhIo(args.repo), args); console.log(formatReport(result, args)); process.exitCode = result.findings.length ? 1 : 0;'
  ```

`dotnet format Nova.slnx --verify-no-changes` passed with no diagnostics. The same roadmap
command passed all seven checks after the update. All 1,169 source hashes remain unchanged;
`git diff --check` passed. No required checks were disabled or weakened. Earlier visual/comp
limitations and optional browser skips remain recorded above. All three suites must run again
before merge. Local round-6 logs remain under `.git/pr270-round6-*`.

## PR review round 7

Copilot review `5207922567` on `78c2cdfce28c3a620001eab8b34a382bbd0d00d3` contained
no new inline comments and five suppressed findings. Four were actionable:

- Invalid-data discard, settled-command cleanup, and expired-command cleanup now handle
  `InvalidOperationException` as well as `JSException`. Cancellation and operation-ownership
  checks still prevent obsolete completions from changing the current sheet. A cleanup failure
  retains the pending command or invalid bytes, keeps editing blocked, and exposes retry.
- Settlement cleanup retains the confirmed-save or definitive-rejection message; it no longer
  falls into the outer pre-dispatch storage-failure message for an unavailable module.
- An administrator eligible to supersede a prior withdrawal sees the deliberate action without
  the contradictory message that recovery is unavailable. The stale XML summary was corrected.
  Members without that capability still see the unavailable explanation; archived and local
  withdrawal behavior remains covered by existing tests.

The fifth finding is inapplicable: an all-zero GUID has no letters, so uppercasing it cannot
bypass an ordinal equality check. The actual browser storage-contract probe already rejects that
exact empty token, retains its bytes, exposes invalid recovery, and verifies explicit discard.
No case-normalization or validator change was needed.

New component coverage exercises both interop exception types after confirmed saves and durable
rejections, verifies replay retains the exact operation and payload, and tests failed expired
cleanup followed by retry without another mutation. Invalid-data discard gains the unavailable
module case. The prior-withdrawal test now covers both available administrator supersession and
unavailable ordinary-member posture. The real browser supersession test asserts the contradictory
copy is absent before opening the controls and captures the corrected state on desktop and mobile
when the existing `NOVA_PLACE_EVIDENCE` option is enabled.

Independent reviewer `/root/recovery_review` inspected the complete change, ownership guards,
regional reload handling, sibling cleanup paths, and tests, and reported no actionable findings.
The finish reviewer requested fresh captures of this specific conditional state; round-four
invalid-storage captures alone do not prove it. No material composition change or new whole-frame
comp approval is claimed.

Applied the previously read C#, Blazor, testing, API, tenancy, and UI guidance, `add-blazor-ui`
with lifecycle/state and JS-interop references, `nova-testing` with Blazor/browser references,
the .NET test-writing/run recipes, and the existing Impeccable direction and finish constraints.
The existing guidance already requires truthful recovery, ownership checks, and behavioral
verification of review findings; no additional instruction or skill is warranted by this round.

The first build found test-only compile/analyzer errors: generic exception type inference,
ordinal string comparison requirements, and synchronous event dispatch in an async test. These
were corrected without suppressions or weakened checks; the failed log remains in
`.git/pr270-round7-build.log` and is not passing evidence.

The first full browser run had one failure: reassignment timed out waiting for the team selector
after selecting Assigned (182 passed, one failed, seven optional capture skips; 5m44.094s).
`OpenFirstPlacementAsync` proved only native navigation and an enabled prerendered field, so the
change event could be sent before its handler attached. Independent review confirmed the mechanism
and inspected all five callers. The shared helper now uses the existing Filters open/close
handshake and reasserts the enabled outcome field; the correction-return caller's duplicate
handshake was removed. No timeout, retry policy, or save/recovery assertion changed. The failed log
is `.git/pr270-round7-browser.log` and is not accepted as passing evidence. Application source and
assets remained fixed throughout that run; the helper was changed only after it exited.

The second full browser run passed Place but failed the existing mobile Evaluate lookup case after
Enter on a result link (182 passed, one failed, seven optional capture skips; 5m49.195s).
Its unchanged focused rerun passed (one case, 43.665s). The failure snapshot retained the correct
result link and search URL but no selected sheet. Inspection found that delayed `restoreFinder`
unconditionally focused the search input even if the user had already focused a result link.
That explains a possible race but the log lacks event/focus traces to prove that exact occurrence.

Fixed the independently verified focus invariant: finder restoration now preserves a deliberately
focused interactive descendant instead of stealing focus or scrolling it away. It still restores
the finder when focus is outside the workspace or on the input itself. A deterministic real-browser
module test verifies normal restoration, then focuses a result link and calls restoration in the
same JavaScript turn, asserting that focus stays on the link before one Enter selects the player.
The independent reviewer found this regression sound. Its first build required adding `partial`
to the existing test class declaration; that mechanical error was corrected without suppressions.
The failed full run remains `.git/pr270-round7-browser-final.log`; the diagnostic focused result is
`.git/pr270-round7-evaluate-focused.log`. Neither replaces the required final full-browser gate.

### Final round-7 verification

Tested source: base `78c2cdfce28c3a620001eab8b34a382bbd0d00d3` plus the round-7 changes,
identified by fingerprint `469a3d1714d0649ff868d1c9c4e53e635d3478b354419d3bd4c1a761c92b910a`
(1,171 files). The PR body records the resulting commit. All hashes were verified unchanged after
the final browser run. Documentation and capture curation do not alter those source inputs.

- `dotnet build Nova.slnx`: final build passed (28.61s), three existing Sass import warnings.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build`: final run passed
  3,364 cases, zero failed/skipped (43.220s).
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build --filter-method '*SettledSaveCleanupFailureRetainsTheExactCommandAndTruthfulResultAsync' --filter-method '*ExpiredRecoveryCleanupFailureKeepsTheOperationBlockedAndRetryableAsync' --filter-method '*InvalidStorageRequiresExplicitDiscardAndFreshEvidenceAsync' --filter-method '*APriorCampaignWithdrawalRequiresAvailableAdministratorSupersession'`:
  13 focused cases passed (2.133s).
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build`: all
  620 passed, zero failed/skipped (3m14.929s). The later browser helper, Evaluate JS focus guard,
  and browser-test-only changes did not alter integration inputs or server/HTTP/persistence code.
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-method '*PhoneLookupRequiresSelectionAndRepeatedCaptureKeepsEachPlayerOpenAsync' --filter-method '*FinderRestorationPreservesDeliberateResultLinkFocusAsync'`:
  both cases passed (42.860s) after the focus guard.
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build`, with
  `NOVA_PLACE_EVIDENCE=D:/repos/Nova/.git/pr270-round7-captures`: final full run passed 184 cases,
  zero failures, seven existing optional accessibility-capture skips (191 total; summary duration
  5m37.945s). Place capture generation was enabled. All five shared-helper callers and the new
  finder-focus regression are included. Earlier failed runs above remain separate evidence.
- `dotnet format Nova.slnx --verify-no-changes`: passed. After later browser-only edits, these
  follow-up checks also passed without diagnostics:
  - `dotnet format Nova.slnx --verify-no-changes --no-restore --include Nova.Browser.Tests/CampaignPlaceRecoveryBrowserTests.cs`
  - `dotnet format Nova.slnx --verify-no-changes --no-restore --include Nova.Browser.Tests/CampaignEvaluationCaptureBrowserTests.cs Nova.Browser.Tests/CampaignEvaluationFinderFocusBrowserTests.cs`
- `npm run check:contrast` from `Nova/`: all ratios and compiled token assertions passed.
- `node .agents/skills/impeccable/scripts/detect.mjs Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor Nova.UI/Features/Campaigns/Components/CampaignEvaluationPanel.razor.js --json`:
  no findings in the two changed files. The existing unrelated Evaluate build-state advisory remains.
- `git diff --check`: passed; source and curated image checksums verified. No schema changed in this review round,
  so the earlier migration-model evidence remains applicable.

Build preceded tests; full unit runs were isolated, and all integration/browser runs were serialized
across the machine. No source, generated asset, build, or format changes occurred during browser
execution. No checks, assertions, or timeouts were weakened.

The curated packet adds four supersession page/board captures and two geometry/source/checksum
sidecars. The independent finish reviewer accepted the refreshed desktop/mobile evidence with
**ship** for the conditional-copy correction, with the mobile board capture showing the action
unobscured. The existing comp measurement and missing design-input limitations remain explicit;
no new whole-surface comparison or approval is claimed. Independent code review also accepted the
recovery changes, shared readiness correction, and deterministic finder-focus fix after resolving
the test-class declaration finding. The one commit for this review round includes all dispositions.

## PR review round 8

Copilot review [5208601584](https://github.com/eruvalca/Nova/pull/270#pullrequestreview-5208601584)
reviewed `511c912ece7b073605dd1a67724928bd8142ea5e`, reported no new inline comments, and
included four suppressed findings. Each was inspected; suppression was not treated as resolution.
All four are inapplicable to the current implementation:

1. **Backfill historical activity without `PlayerId`:** `AGENTS.md` explicitly states that Nova
   has no production data or deployed users and prohibits legacy compatibility work. The current
   `ActivityEventWriter` snapshots `PlacementContext.PlayerId`; the history query filters that
   indexed player key before reading at most 21 rows for a 20-item page. A fallback scan of old
   null-key payloads would undermine the bounded-history requirement. No backfill is required by
   the approved scope.
2. **Deserialize migration sentinel receipts:** `PlacementMutationExecutor.RecoverAsync` checks
   the original actor and request fingerprint before deserialization, returning a conflict for
   mismatches. It then rejects an expired receipt before reading `ResultJson`. The migration's
   zero actor, empty fingerprint, and minimum expiry therefore cannot take the alleged JSON
   deserialization path. UUIDv7 operation age is also checked before execution. Together with the
   no-legacy-data policy, this does not warrant rewriting the migration or adding compatibility.
3. **`OrderBy`/`Take` translation with `ExecuteDeleteAsync`:** this is already exercised against
   both actual providers. `PlacementCleanupRemovesAtMostFiveHundredExpiredReceiptsPerPassAsync`
   tests direct cleanup and membership cleanup in SQLite with 503 receipts, exact three survivors,
   and an empty change tracker. The harness maps expiry to UTC ticks. PostgreSQL test
   `GlobalPlacementCleanupDeletesOnlyFiveHundredOldestExpiredReceiptsInPostgresAsync` exercises
   the same ordered, bounded delete and exact survivors, including receipts without an owning
   club. It passed in the round-7 full integration suite. No speculative provider workaround is
   needed; this repeats the provider concern resolved in round 6.
4. **Reinitialize expansion when `Compact` changes:** all production callers were inspected.
   `CampaignPlacePanel.razor` passes literal `true`; `CampaignWorkspace.razor` omits the parameter
   and uses its `false` default. Neither switches the value on an existing component or binds it
   to viewport changes. `OnInitialized` establishes each surface's default and ordinary parameter
   updates preserve the user's Filters toggle. The proposed responsive-parameter scenario does
   not occur in these surfaces.

Independent reviewer `/root/recovery_review` inspected the four claims, implementation gates,
provider tests, and both filter callers in a separate read-only context and found no actionable
issues. No product, test, schema, styling, or generated-asset change was made in this round.
Existing repo guidance already requires checking findings against actual behavior, bounded
provider evidence, and avoiding compatibility work; no new instruction or skill is warranted.
Applied the previously read repo, tenancy, placement, Blazor, and testing guidance and the
`add-domain-persistence`, `add-blazor-ui`, `nova-testing`, and .NET test-run recipes.

### Round-8 verification

Tested source remains `511c912ece7b073605dd1a67724928bd8142ea5e`; all 1,171 source-file hashes
in fingerprint `469a3d1714d0649ff868d1c9c4e53e635d3478b354419d3bd4c1a761c92b910a`
were verified unchanged. This round's sole commit adds this documentation record.

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build`: 3,364 passed,
  zero failed/skipped (46.016s), including both SQLite cleanup cases. The existing round-7 build
  preceded this run; no build inputs changed. Log: `.git/pr270-round8-unit.log`.
- `dotnet format Nova.slnx --verify-no-changes`: passed with no diagnostics after the unit run.
  Log: `.git/pr270-round8-format.log`. `git diff --check`: passed.
- Unaffected PostgreSQL integration and browser results remain the passing round-7 evidence on
  `511c912ece7b073605dd1a67724928bd8142ea5e`: 620 integration cases and 184 browser cases, with
  seven existing optional accessibility-capture skips. No changed source requires rerunning these
  suites for this documentation-only push. All three suites must run again before merge.
- Existing migration-model, contrast, finish-review, and comp-measurement limitations remain as
  recorded above. No new visual approval or test coverage is claimed.

The PR explanation records all four dispositions. There were no new inline threads to resolve;
the ten existing threads remain resolved. This review contains suppressed findings and therefore
does not satisfy the user's stop condition. After the single round-8 commit is pushed, wait for
current-head CI and a fresh automatic review without requesting one.

## PR review round 9

Copilot review [5208902003](https://github.com/eruvalca/Nova/pull/270#pullrequestreview-5208902003)
on `281619696c732d616a3c0e183df261445c70e33a` contained one inline finding and three
suppressed findings. Three reports were actionable; the history-cursor report was inapplicable.
All findings were inspected regardless of suppression.

- **Expiry belongs to its pending command and owner.** Clear expired-recovery evidence when
  posture changes, storage attaches a replacement command, or a command settles. A previous
  expiry can no longer expose the destructive review/clear path for a different valid pending
  operation. Regression cases cover another owner, same-scope closure, and storage replacement
  after an attachment retry; each verifies the replacement is replayed before its cleanup.
- **A confirmed refusal survives authoritative reconciliation.** Record its detail before
  refreshing evidence, preserve it through selection projection and same-scope closure, and
  use it in the defensive obsolete-reconciliation branch. Explicit new drafts, completed conflict
  review, ordinary navigation, and new dispatch dismiss the retained refusal. Leaving its scope
  clears both the metadata and old conflict feedback, preventing an A → B → A identity switch
  from resurrecting it. Held-read tests prove the real closure/ownership paths. The reviewers
  did not find a reliable ordinary interaction that reaches the same-owner `Obsolete` branch;
  its defensive change is not claimed as independently reproduced browser coverage.
- **Closed campaigns retain local recovery controls.** Retry storage and explicit invalid-data
  discard are independent of placement-edit permission. Discard still requires fresh authorized
  campaign, queue, and selected-placement evidence, ownership checks, and exact-byte deletion.
  All new-save gates remain Closed. Component cases cover denied reads, unavailable storage,
  exact-command replay, and successful cleanup with editing disabled. A browser case exercises
  keyboard discard in a Closed sheet and compares all final decisions, tokens, teams, attribution,
  receipt count, and activity count before and after.
- **History cursor report is inapplicable.** The Latest changes button requires nonnull `_context`.
  Fresh reads clear that value, failures leave it null, and a successful callback assigns the new
  context and null cursor together. There is no visible stale button in the reported sequence.
  Two component cases page one participant backward, select another while history is delayed,
  then return success or failure. The previous cursor stays hidden throughout. History production
  code was not changed to address an unobserved defect.

Independent reviewer `/root/recovery_review` reviewed the full change and sibling paths. The
initial review found that selection projection still cleared the refusal and that retained
metadata could survive an owner round trip. Both were fixed and the revised review found no
remaining actionable issues. The first focused test run independently reproduced the closure
problem (nine passed, one failed); the final full unit suite includes its passing regression.

Applied the previously read repo, Blazor, testing, tenancy, API, placement, and UI guidance;
`add-blazor-ui` with lifecycle/state and JS-interop references; `nova-testing` with component and
browser references; the .NET test-writing/run recipes; and the retained Impeccable direction and
finish constraints. These findings reinforce the existing ownership, truthful settlement, and
behavioral-evidence rules. No additional instruction or skill is warranted.

### Round-9 verification

Tested source: base `281619696c732d616a3c0e183df261445c70e33a` plus this round's changes,
identified by fingerprint `142809f94d54656fa78fa4a2bff5ad1003e6248e93028adff7a176600828522d`
(1,172 source files). The PR body identifies the resulting single commit. Documentation and
capture curation are excluded from the source fingerprint.

- `dotnet build Nova.slnx`: final build passed (37.84s), three existing Sass import warnings.
  The first build failed a nested-ternary test analyzer rule; the test was simplified without a
  suppression. Logs: `.git/pr270-round9-build.log`, `-build-fixed.log`, `-build-final.log`, and
  `-build-verified.log`, and `-build-readiness.log`. The final build includes the Closed-specific
  read-only warning and the browser attachment correction described below; independent code
  review accepted both.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build`: final run passed
  3,374 cases, zero failed/skipped (42.096s). Log: `.git/pr270-round9-unit-final.log`.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build --filter-method '*ExpiryEvidenceCannotDiscardADifferentAttachedOperationAsync' --filter-method '*DurableRefusalSurvivesClosureButCannotLeakIntoAnotherOwnerAsync' --filter-method '*ClosedInvalidStorageCleanupRequiresFreshEvidenceAndNeverSavesAsync' --filter-method '*ClosedUnavailableStorageCanRetryAndRecoverTheExactCommandAsync' --filter-method '*ChangingParticipantHidesThePreviousHistoryCursorThroughoutReloadAsync'`:
  the earlier focused run of the ten new component cases had one closure failure (2.267s),
  recorded in `.git/pr270-round9-focused.log`. This failed attempt is distinct from the final
  passing full suite and is not counted as passing evidence.
- `dotnet format Nova.slnx --verify-no-changes`: passed. After the final conditional warning,
  `dotnet format Nova.slnx --verify-no-changes --no-restore --include Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor Nova.Browser.Tests/CampaignPlaceInvalidStorageBrowserTests.cs`
  also passed. Logs: `.git/pr270-round9-format.log` and `-format-copy.log`.
  After the browser attachment correction,
  `dotnet format Nova.slnx --verify-no-changes --no-restore --include Nova.Browser.Tests/CampaignPlaceBrowserTests.cs`
  passed (`.git/pr270-round9-format-readiness.log`).
- `npm run check:contrast` from `Nova/`: passed. No theme or CSS source changed.
- `node .agents/skills/impeccable/scripts/detect.mjs Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor --json`:
  no changed-file findings (`[]`). The existing unrelated build-state `COMP_ROUND_OPEN` advisory
  remains; no whole-surface finish is inferred from this detector result.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build`:
  all 620 passed, zero failed/skipped (3m09.649s). Log: `.git/pr270-round9-integration.log`.
  The later browser-test-only readiness correction did not change integration inputs or application code.
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build`, with
  `NOVA_PLACE_EVIDENCE=D:/repos/Nova/.git/pr270-round9-captures-final`: final full run passed
  185 cases, zero failures, seven existing optional accessibility-capture skips (192 total;
  5m40.325s). Log: `.git/pr270-round9-browser-final.log`.
- `git diff --check`: passed. All 1,172 source hashes were verified unchanged after the final
  browser run. Five curated Closed-recovery images and three sidecars retain source/geometry/image
  checksums. No schema changed in this review round; earlier migration-model evidence remains applicable.

Build preceded tests. Full unit runs were isolated; integration/browser suites ran serially
across the machine, and application source/assets stayed fixed throughout each browser run.
The independent finish reviewer requested Closed-specific evidence, then reopened the exact
final desktop/mobile/page/board/action captures and returned **ship**, limited to the conditional
recovery presentation. No remaining material fixes were identified. Existing quality-bar-card,
design-input, and comp-measurement limitations remain; no new whole-surface comparison is claimed.
All three suites must run again before merge.

The first full browser run passed the new Closed cleanup case but failed the existing
`CompatibleTeamChoicesExcludeIncompatibleTeamsAsync` (184 passed, one failed, seven optional
capture skips; 5m58.493s). After selecting Assigned, the failure snapshot showed Undecided and
"Choose Assigned to select a team." The test treated native row navigation and an enabled
prerendered outcome control as proof that its change handler had attached. No event trace was
captured, but independent code review confirmed this readiness defect and inspected its siblings.

Compatible-team, missing-team, and capture tests now reuse `OpenFirstPlacementAsync`; member,
mobile, and both concurrent sessions use the existing Filters open/close handshake before
selection. The mobile check runs while the queue is visible. Both concurrent pages establish
attachment and select the same player before either save, preserving the stale-decision test.
No production behavior, timeouts, retry budgets, or save/conflict assertions changed. The failed
run remains `.git/pr270-round9-browser.log` and is not accepted as passing evidence. Application
source and assets were fixed during that run; the browser helper correction happened after exit.

## PR review round 10

Copilot review `5209509862` on `f843f7c405fc60d47919ac0e4613161fb646cb6d` reported one
inline finding and three suppressed findings. All four were inspected; suppression was not
treated as resolution.

- **Delegate mismatch is inapplicable.** Both declarations of the executor callback accept
  `NovaDbContext`, actor ID, club ID, administrator status, and recovery deadline. Its invocation
  supplies all five arguments, matching the five-parameter service lambda. The successful
  current-head CI build and local build agree with the source. No signature change is warranted.
- **History metadata validation fixed.** A placement payload must match the persisted event-kind
  family and the row's campaign snapshot key before projection. Unknown kinds and mismatches are
  skipped. The SQL limit remains 21 rows, projection consumes at most 20, and continuation uses
  the twentieth raw row even when omitted. Four additional SQLite cases corrupt rows inside and
  at the page boundary, asserting exact retained IDs and the next page. The sibling activity
  feed already validates kind/family; its club-wide projection does not use the Closed sheet's
  campaign restriction. The append-only writer supplies consistent kind/player/campaign keys.
- **Unexpected read logging fixed.** Context-read exceptions producing a server error now log
  at Error with the original exception, user, club, input operation, campaign, and participant.
  This follows the neighboring effective-placement query and existing service/observability
  guidance. A failing-context regression asserts severity, exception identity, and structured
  fields. No personal names, payloads, or custom correlation IDs were added.
- **Migration wording clarified.** This PR includes incremental migration
  `20260915015851_PlacementRecoveryAndHistory`. Later review rounds did not change that schema;
  their statements now explicitly identify the review-round scope. The original migration-model
  check remains recorded above. No new migration or compatibility layer is needed for these fixes.

Independent reviewer `/root/recovery_review` inspected the complete code/test diff and the
subsequent private history-helper extraction, with no remaining actionable findings. The helper
keeps database authorization, limits, and cursor construction in the query method. The extraction
addresses a method-length analyzer failure without a suppression.

Applied the previously read C#, service, API, validation, tenancy, placement, functional-core,
and testing guidance; `add-feature-slice`, `add-api-endpoint`, `add-domain-persistence`, and
`nova-testing` with the SQLite and Aspire references; and the .NET test-writing/run recipes.
Also read the observability rules and checked the structured-logging guidance and sibling query.
These fixes enforce existing rules; no new instruction or skill is warranted. No UI composition
changed, so the round-9 curated evidence and its explicit finish/comp limitations are retained.

### Round-10 verification

Tested source: base `f843f7c405fc60d47919ac0e4613161fb646cb6d` plus this round's changes,
identified by fingerprint `d1bd13bb704ae3bb7e0d95a3623a6309eeed1a1b84ea98b188f8fcfb68ab562c`
(1,172 source files). The PR body identifies the resulting single commit. Documentation is
excluded from the source fingerprint.

- `dotnet build Nova.slnx`: pre-binding build passed (29.01s), with three existing Sass import warnings.
  Log: `.git/pr270-round10-build-logger.log`. The first attempt failed the method-length rule;
  the next failed a nullable assertion in the new logging test. Both were corrected without
  suppressions (`-build.log`, `-build-fixed.log`). An intermediate build also passed
  (`-build-final.log`, 32.24s).
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build`: pre-binding full run passed
  3,379 cases, zero failed/skipped (45.669s), including all four metadata cases and the structured
  logging regression. Log: `.git/pr270-round10-unit-final.log`. The earlier full run passed
  3,378 and failed the new logging fixture (46.482s, `-unit.log`): inspecting the generated
  logger's reusable state after emission lost its fields. The final fixture snapshots them
  inside `Log`, preserving every severity/exception/field assertion. Production did not change
  for that fixture correction, and independent review accepted it.
- `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext --no-build`:
  passed; no model changes since the existing migration (`.git/pr270-round10-model.log`). The
  existing EF tools 10.0.8/runtime 10.0.12 warning remains. This is additional current-round
  evidence, not a claim that the full PR contains no migration.
- `npm run check:contrast` from `Nova/`: passed (`.git/pr270-round10-contrast.log`).
- `dotnet format Nova.slnx --verify-no-changes`: passed (`.git/pr270-round10-format.log`).
- `node .agents/skills/impeccable/scripts/detect.mjs Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor --json`:
  no findings in the inspected file (`[]`, `.git/pr270-round10-detector.json`). The previously
  documented unrelated `COMP_ROUND_OPEN` advisory remains; no whole-surface finish is inferred.

- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build`: all
  620 passed, zero failed/skipped (3m11.990s), `.git/pr270-round10-integration.log`.

### Browser search finding during round 10

The first full browser run passed 184 cases, failed
`QueueSearchWidensBeyondTheOpenSectionAndKeepsFilterTruthAsync`, and skipped seven existing
optional capture cases (5m49.399s, `.git/pr270-round10-browser.log`). The Filters open/close
handshake had proved attachment, but one-shot typing of `Player 01` left `P01` in the field.
The failure snapshot also recorded a later disabled/empty frame. Source and assets stayed fixed
throughout that run; the failure is retained separately from final evidence.

Independent review identified a product binding defect in the shared `CampaignRosterFilters`
search input. Raw `value` plus `@oninput` does not identify the event-updated value to Blazor's
renderer, allowing older input echoes to overwrite newer browser typing. The field now uses
`@bind:get`, `@bind:set`, and `@bind:event="oninput"`; the setter forwards to the existing callback.
Both Place and Roster synchronously update their parent-owned draft before awaiting debounce, so
no second child draft or synchronization mechanism is needed. The browser typing assertion,
timeouts, retry budgets, and debounce interval are unchanged.

The mechanism was checked against [ASP.NET Core 10 binding guidance](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/data-binding?view=aspnetcore-10.0#use-bindgetbindset-modifiers-and-avoid-event-handlers-for-two-way-data-binding)
and [the matching renderer source](https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Components/Components/src/Rendering/RenderTreeUpdater.cs#L30-L40).
The later disabled/empty transition has a separate applied-state reset and does not explain the
already missing characters; it was not changed speculatively. Existing Blazor guidance and the
`add-blazor-ui` recipe apply. No visual composition changes or new permanent instructions are needed.

The integration result above identifies source fingerprint
`f98839b31e4396e26ac049000bd46be7863cf0a9113ac86d7f67f282a898f8bf`; the later change touches only
the shared UI binding. Backend, HTTP, persistence, and integration-test inputs are unchanged.

The first unit run after the binding change passed 3,378 and failed the existing
`CampaignWorkspaceDebouncesSearchToSingleRequestWithFinalTerm` fixture (43.310s,
`.git/pr270-round10-unit-binding.log`). It cached an element across three inputs and dispatched
an event using a handler ID replaced by a render. The async fixture now reacquires the element
for each consecutive dispatch inside the renderer context, without awaiting each debounce.
The exact two-total/one-final-term request assertions remain. Independent review checked this
fixture and sibling search paths. Converting it to async also required awaited assertion helpers;
the analyzer caught the initial synchronous calls (`-build-binding-test.log`), and they were fixed.

Final verification of the complete round-10 source:

- `dotnet build Nova.slnx`: passed (33.21s), three existing Sass warnings,
  `.git/pr270-round10-build-verified.log`. The earlier binding-only build also passed (42.70s,
  `-build-binding.log`).
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build`: all 3,379 passed,
  zero failed/skipped (48.102s), `.git/pr270-round10-unit-verified.log`.
- `node .agents/skills/impeccable/scripts/detect.mjs Nova.UI/Features/Campaigns/Components/CampaignRosterFilters.razor --json`:
  no changed-file findings (`[]`), with the same unrelated build-state advisory
  (`.git/pr270-round10-detector-binding.json`).

- `dotnet format Nova.slnx --verify-no-changes --no-restore --include Nova.UI/Features/Campaigns/Components/CampaignRosterFilters.razor Nova.UI/Features/Campaigns/Components/CampaignRosterFilters.razor.cs Nova.Unit.Tests/Campaigns/CampaignWorkspaceTests.cs`:
  passed (`.git/pr270-round10-format-binding.log`), covering every source file changed after
  the full format pass above.

- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build`: all
  620 passed, zero failed/skipped (3m13.405s), `.git/pr270-round10-integration-final.log`, on
  source fingerprint `37ea98e57f189573b992ac224dd24897d5e82a8a66e8b8c3450be22e12758842`.
  The later two browser-test-only corrections below leave all application and integration-test
  inputs unchanged; this passing run remains applicable to those inputs.

### Evaluate browser fixture findings during round 10

The second full browser run passed the unchanged Place search case, but finished with 183 passed,
two Evaluate failures, and seven existing optional skips (5m49.671s,
`.git/pr270-round10-browser-final.log`). All application source and assets remained fixed.

- The archived-tag case read `AllTextContentsAsync` immediately after the drawer heading became
  visible. Optional tag choices load afterward, so the empty list was an early snapshot. The
  test now awaits the visible Tag-to-apply picker before retaining both active-tag assertions
  and the archived-tag exclusion. Sibling tag tests already wait through their select interaction
  or visibility assertion.
- The discard-navigation case timed out in `WaitForURLAsync` after the explicit click. Its
  retained output did not include the final URL or guard state, so the cause remains unproven.
  It now uses [Playwright's retrying URL assertion](https://playwright.dev/dotnet/docs/api/class-pageassertions#page-assertions-to-have-url)
  for the exact fixture origin and roster path, with carried query/fragment allowed and the same
  30-second maximum. It still checks roster visibility and zero saved notes. Failure now reports
  URL, navigation events, pointer/history diagnostics, and ARIA. The action is not repeated, and
  no product navigation logic or timeout budget changed. The sibling canonical-roster helper
  already carries detailed failure diagnostics; no broad rewrite of unfailed waits was made.

Independent reviewer `/root/recovery_review` accepted both fixture corrections without findings.
These apply existing readiness and behavioral-evidence guidance; no new skill or instruction is
warranted. Only `CampaignEvaluationBrowserTests.cs` and `CampaignEvaluationCaptureBrowserTests.cs`
changed after the preceding application/integration validation.

- `dotnet build Nova.slnx`: latest build passed (30.68s), three existing Sass warnings,
  `.git/pr270-round10-build-navigation.log`. The earlier diagnostic-only build passed (44.97s,
  `-build-evaluate.log`).

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build`: latest full run passed
  all 3,379 cases, zero failed/skipped (46.923s), `.git/pr270-round10-unit-navigation.log`.

- `dotnet format Nova.slnx --verify-no-changes --no-restore --include Nova.Browser.Tests/CampaignEvaluationBrowserTests.cs Nova.Browser.Tests/CampaignEvaluationCaptureBrowserTests.cs`:
  passed (`.git/pr270-round10-format-evaluate.log`), covering the final browser-test edits.

The third full browser run passed 184 cases, failed the new URL assertion, and skipped the same
seven optional cases (6m08.626s, `.git/pr270-round10-browser-verified.log`). Playwright rejected
the .NET `RegexOptions.CultureInvariant` flag before evaluating the assertion. It now uses
`RegexOptions.None`, matching existing repository Playwright regex assertions. The anchored,
case-sensitive pattern and 30-second assertion deadline are unchanged. This was a test API
compatibility mistake, not evidence of another product navigation failure.

- `dotnet build Nova.slnx`: passed (40.28s), three existing Sass warnings,
  `.git/pr270-round10-build-regex.log`.

- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-method '*DraftProtectsNativeRosterLinkUntilExplicitDiscardAsync'`:
  passed one case, zero failed/skipped (39.848s), `.git/pr270-round10-browser-focused.log`.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build`: final full unit run
  passed all 3,379 cases, zero failed/skipped (44.198s), `.git/pr270-round10-unit-complete.log`.
  Independent review accepted the supported regex flag correction with no remaining findings.

- `dotnet format Nova.slnx --verify-no-changes --no-restore --include Nova.Browser.Tests/CampaignEvaluationCaptureBrowserTests.cs`:
  passed (`.git/pr270-round10-format-regex.log`), covering the final flag change.

- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build`, with
  `NOVA_PLACE_EVIDENCE=D:/repos/Nova/.git/pr270-round10-captures-complete`: final full run passed
  185 cases, zero failures, seven existing optional accessibility-capture skips (192 total;
  5m32.036s), `.git/pr270-round10-browser-complete.log`. This includes the unchanged search-typing
  case, archived-tag choices, and explicit discard navigation with zero saved notes.
- `git diff --check`: passed. All 1,172 source hashes were verified unchanged after the final
  browser run. Build preceded tests; integration and browser runs were serialized across the
  machine, with source/assets fixed throughout each browser run.

The existing curated desktop/mobile packet remains representative of the unchanged composition.
New diagnostic captures remain local; no new comp measurement or whole-surface finish is claimed.
All review-round findings have a disposition, including the suppressed comments. No checks were
disabled or weakened. All three suites must run again before merge.

## PR review round 11

Copilot review `5210422852` on `e199aa24e5337eba88d7b3a01fe77bb23a34bd2f`
reported “Needs a closer look” with four suppressed findings. Suppression is not resolution;
all four were inspected. The no-findings stopping exception therefore did not apply.

### Dispositions and behavioral evidence

- **Incomplete prior-season team evidence — fixed.** A positive saved team ID now requires
  the matching team summary. The server intentionally clears both fields when the team is
  inaccessible; that null/null/non-Keep shape remains valid. WASM cases cover missing summaries
  with either Keep value and a visible team that cannot currently be kept.
- **Route naming — fixed.** `PlacementContextEndpoints.GetPlacementContextRelative` replaces
  the generic constant in both the URL builder and server mapping. The HTTP route is unchanged;
  no compatibility alias was added.
- **Prior Assigned/null team name — inapplicable.** The assignment's team foreign key is keyed
  by team ID, while tenant filtering can make that team inaccessible. The placement writer then
  records a real correction with `PreviousOutcome=Assigned` and no visible previous team name.
  Requiring a name would hide that valid event. A new relational SQLite regression seeds this
  supported invalid-placement state, corrects it through the real command, and verifies that
  the query retains the exact activity event, new team and actor while the earlier assignment
  remains unchanged. The existing WASM case accepts the same shape. Blank names and impossible
  non-assignment/team combinations remain rejected. A local code comment records the reason.
- **Recovery equality — fixed.** Pending commands compare all five validated contract fields;
  GUID comparison is case-insensitive. Property order and GUID letter casing may change through
  typed deserialization without changing the logical command. The real browser storage-module
  probe covers both representations for all three outcomes, rejects changes to operation,
  participant, token, outcome or team, and asserts that refusal preserves the stored bytes.
  Invalid-data discard continues to require an exact comparison of the original raw bytes.

### Guidance and independent review

Applied the previously recorded API, Blazor/interop, placement, tenancy and testing guidance and
feature recipes. Rechecked the API route-naming convention, actual context producer and writer,
assignment/team mapping, effective-placement unavailable-team tests, and the Evaluate storage
sibling. Evaluate uses its own owner/revision guard rather than raw JSON equality between two
commands, so that path needs no matching change.

Independent session `/root/recovery_review` inspected all four findings against producers and
constraints, then reviewed the complete eight-file code/test diff: no remaining actionable
findings. Existing rules already cover these invariants; no new skill or broad instruction is
warranted. The existing curated desktop/mobile evidence and documented comp/finish limitations
remain applicable because this round does not change the composition.

### Validation

Tested source: base `e199aa24e5337eba88d7b3a01fe77bb23a34bd2f` plus this round's eight-file
code/test diff. SHA-256 `5afaca5a9ffeca6ad842d9e6a353b900c7195cef68ba93dcc2b1e23c9d26c785`
identifies the sorted 1,172 source-path/raw-content-hash pairs. The PR body names the resulting
single commit. Only this validation document was edited after the final build.

| Command | Result |
| --- | --- |
| `dotnet build Nova.slnx` | Passed, 17.02s; three existing Sass deprecation warnings |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | 3,383 passed, zero failures/skips; 42.203s |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | 620 passed, zero failures/skips; 3m09.298s |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | 185 passed, zero failures, seven existing optional capture skips (192 total); 5m44.201s |
| `dotnet format Nova.slnx --verify-no-changes` | Final full check passed |
| `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext --no-build` | Passed; no pending model changes. Existing tools 10.0.8/runtime 10.0.12 warning remains |
| `npm run check:contrast` from `Nova/` | All contrast ratios and token assertions passed |
| `node .agents/skills/impeccable/scripts/detect.mjs Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor.js --json` | No findings (`[]`); existing unrelated Evaluate `COMP_ROUND_OPEN` advisory remains |

Browser execution used `NOVA_PLACE_EVIDENCE=D:/repos/Nova/.git/pr270-round11-captures`.
Integration and browser suites ran serially across the machine; application source and generated
assets stayed fixed during the browser run. All 1,172 source hashes matched after the final run;
`git diff --check` passed before committing.

Preserved intermediate results: the first build passed in 2m58.99s and the first unit run passed
all 3,383 tests in 48.327s. The first full format check rejected whitespace around an explanatory
comment embedded in the validation expression. Moving it into XML remarks fixed formatting
without changing behavior; full format, build and unit checks then passed on the final source.
Local logs use `.git/pr270-round11-` with `build-final.log`, `unit-final.log`, `integration.log`,
`browser.log`, `format-final.log`, `model.log`, `contrast.log`, and `detector.json`; the initial
build/unit/format logs remain separately recorded without the `-final` suffix.

This PR includes migration `20260915015851_PlacementRecoveryAndHistory`; this review round changes
no schema. No check was disabled or weakened. The prior comp measurement, limited finish and
optional-capture qualifications remain. All three suites must run again before merge.

## PR review round 12

Copilot review `5210974003` on `7553c9c9ed6ca5d01b8cdf99e0c9da910c9ac712`
reported “Needs a closer look” with four suppressed findings. All four were inspected and
addressed in one review round; the review did not qualify for the no-findings stopping exception.

### Dispositions and behavioral evidence

- **Native summary focus — fixed.** Evaluate finder restoration now preserves focus on a native
  `summary`, alongside its existing interactive-element checks. A real trait-summary browser
  regression keeps both the search input and summary visible, focuses the summary immediately
  before restoration, and verifies that one Enter opens its details. Independent review caught
  the first test's mobile false-positive: the selected mobile sheet hides the finder, making
  focus theft impossible. The final desktop test explicitly asserts the finder is visible.
- **Pending module import — fixed.** Place teardown awaits its retained import task and disposes
  the resulting reference once, including when import completes after disposal starts. Expected
  failed/cancelled imports produce no reference to release. Late attachment failures are consumed
  without changing an obsolete or disposed owner. Five component cases cover import before/after
  teardown and JavaScript, unavailable-runtime and cancellation failures. The recording wrapper
  forwards real bUnit interop and asserts disposal before fixture cleanup. Evaluate already awaits
  its retained module task; role/participant changes continue to share the live Place module.
- **Future retained ID — fixed.** Browser storage rejects UUIDv7 timestamps more than one minute
  ahead of its clock before persistence/dispatch, or preserves existing raw bytes as invalid
  recovery data. Old IDs remain readable for the existing expiry flow. The real module probe
  checks the inclusive 60-second boundary, rejection at 60,001ms, unchanged invalid bytes, and
  refusal to discard if clock advancement makes those same bytes valid before the action.
  A browser reload case exercises explicit discard, authoritative refresh and a new deliberate
  save, with no placement receipt before that save.
- **Server clock disagreement — fixed without inventing settlement proof.** A future timestamp
  now returns an exact-operation Validation diagnostic. It carries neither durable non-commit
  nor expiry proof and creates no rejection receipt: the same ID could become valid later.
  Place retains the exact command, explains the clock mismatch and exposes Retry storage.
  Correcting a fast device clock lets that retry classify the retained future ID as invalid and
  use the existing explicit discard/refresh flow. If the server clock is behind, recovery remains
  uncertain until the clock condition is corrected. Eleven marker-shape cases, two component
  cases and a repeated real HTTP/PostgreSQL request verify strict binding, no automatic replacement,
  no effects/receipt, and no false proof of failure.

### Guidance and review

Applied the previously recorded API, service, validation, Blazor/interop, placement, tenancy and
testing guidance and feature recipes. Inspected both placement and evaluation executors/storage
modules, the base component's cancellation/disposal order, invalid-data compare-and-delete,
expiry recovery and the real trait markup/responsive rules. Evaluate's different expiry handling
does not supply a durable non-commit marker; it is not a precedent for falsely settling Place.

Independent session `/root/recovery_review` diagnosed all four findings and reviewed production,
new tests and later fixture corrections. The desktop-test issue was fixed; final review found no
remaining actionable findings. No composition changed. Existing evidence and its limited finish
disposition remain applicable; no new whole-surface finish or comp measurement is claimed.
These are applications of existing ownership, recovery-proof and behavioral-test rules, so no
new skill or permanent instruction was added.

### Validation

Tested source: base `7553c9c9ed6ca5d01b8cdf99e0c9da910c9ac712` plus this round's
15-file code/test diff. SHA-256
`c8a497aa5f42cf315d68b74ba6bba17234a20fd5f0ef6a3b2c040ea8b0bf186a`
identifies the sorted 1,174 source-path/raw-content-hash pairs. The PR body names the resulting
single commit. Only this validation document was edited after the final build.

| Command | Result |
| --- | --- |
| `dotnet build Nova.slnx` | Final build passed, 18.73s; three existing Sass deprecation warnings |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | 3,401 passed, zero failures/skips; 42.783s |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | 621 passed, zero failures/skips; 3m12.667s |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | 187 passed, zero failures, seven existing optional capture skips (194 total); 5m00.993s |
| `dotnet format Nova.slnx --verify-no-changes` | Full check passed; after the final one-line browser-readiness addition, `dotnet format Nova.slnx --verify-no-changes --include Nova.Browser.Tests/CampaignEvaluationFinderFocusBrowserTests.cs` also passed |
| `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext --no-build` | Passed; no pending model changes. Existing tools 10.0.8/runtime 10.0.12 warning remains |
| `npm run check:contrast` from `Nova/` | All contrast ratios and token assertions passed |
| `node .agents/skills/impeccable/scripts/detect.mjs Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor.js Nova.UI/Features/Campaigns/Components/CampaignEvaluationPanel.razor.js --json` | No findings (`[]`); existing unrelated Evaluate `COMP_ROUND_OPEN` advisory remains |

Browser execution used `NOVA_PLACE_EVIDENCE=D:/repos/Nova/.git/pr270-round12-captures`.
Integration and browser suites ran serially across the machine. Application source and generated
assets remained fixed during browser execution. All 1,174 source hashes matched after the final
run; `git diff --check` passed before committing.

Preserved intermediate results: the targeted formatting pass reported non-automatic S3358 and
CA2012 fixes. The nested conditional became explicit branches. The first build failed on the
new lifetime fixture's ambiguous test-context name and `ValueTask` mocking setup (3m05.21s).
A recording interop wrapper replaced that setup; its missing failure-path cleanup then produced
CA2000 in the next build (32.51s). Await-using fixture cleanup fixed that finding without weakening
the one-component-disposal assertion. The following build passed in 30.62s and all 3,401 unit
tests passed in 44.202s. Adding the existing non-submitting composer-attachment helper to the
desktop browser test was the only later source edit; final build/unit results appear above.
No diagnostics were suppressed to obtain these results.

Local logs use `.git/pr270-round12-`: `build-complete.log`, `unit-final.log`, `integration.log`,
`browser.log`, `format.log`, `format-readiness.log`, `model.log`, `contrast.log`, and
`detector.json`. Initial build failures are retained in `build.log` and `build-final.log`;
the first passing build/unit logs are `build-verified.log` and `unit.log`.

This PR includes migration `20260915015851_PlacementRecoveryAndHistory`; this review round changes
no schema. Existing evidence remains curated at the previously recorded revisions; new diagnostic
captures remain local. No check was disabled or weakened. All three suites must run again before merge.

## PR review round 13

Review [5211666763](https://github.com/eruvalca/Nova/pull/270#pullrequestreview-5211666763)
on `2f9ce828f96cbd062ffc8305d15f1642602cd8a5` contained two suppressed findings and no
new inline comments. Both findings were actionable and were addressed together in one commit.

### Findings and dispositions

- **Premature unavailable-withdrawal copy — fixed.** A prior-campaign withdrawal's administrator
  recovery capability remains unknown while context is absent, loading, or failed. The definitive
  unavailable message requires a successful, settled context read. Existing loading/error/retry
  feedback remains in the history region. Archived-player and local-withdrawal reasons still render
  immediately from required placement evidence, independently of optional context. Component tests
  hold the read pending, fail it, retry it, and assert both allowed and unavailable results; two
  further cases preserve the immediate terminal reasons through loading and failure.
- **Keep action on an existing decision — fixed.** Markup and handler now share an eligibility
  predicate that requires fresh context, an initial decision without either local or effective
  saved evidence, and the editable phase. Storage/saving state continues to disable dispatch.
  Prior-season evidence remains visible on inherited/local assignments while the inapplicable
  fast path is absent, including after opening Reassign player. Component cases cover initial,
  inherited and local placement. The initial action submits once without confirmation. A real
  browser case seeds both prior-season and inherited current-season assignments, requires
  confirmation for reassignment, and checks one receipt/event plus unchanged prior decisions and
  tokens. Existing Keep-and-advance browser coverage remains in the full suite.

### Guidance and review

Applied the previously read C#, Blazor/state, placement, lifecycle, tenancy, testing and UI
instructions, `add-blazor-ui`, `nova-testing` and their state/component/browser references.
Rechecked instruction routing and the PR template. Inspected capability rendering, context paging
and retry, selection invalidation, the shared mutation entry points, terminal-state branches and
the existing initial-Keep and inherited-withdrawal tests. The server's historical assignment
evidence remains distinct from current-season effective placement.

Independent read-only session `/root/recovery_review` reviewed the complete production/test diff,
including the new component file, and reported no findings. The subsequent synchronous-to-async
test-event correction received focused self-review. The layout and design composition are unchanged;
existing curated evidence and its limited finish disposition remain applicable. No new whole-surface
finish or measurable comp claim is made. Existing guidance already covers truthful unknown states,
shared action eligibility and behavioral tests; no new instruction or skill was warranted.

### Validation

Tested source: base `2f9ce828f96cbd062ffc8305d15f1642602cd8a5` plus this round's five-file
code/test diff. SHA-256 `a53098d3af13cdee1eb33cad9ea58f12516bbd3652c6748582169992997516e9`
identifies the sorted 1,175 source-path/raw-content-hash pairs. The PR body identifies the resulting
single commit. Only this validation document was edited after the build.

| Command | Result |
| --- | --- |
| `dotnet build Nova.slnx` | Passed, 2m44.74s; three existing Sass deprecation warnings |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | 3,408 passed, zero failures/skips; 1m03.494s |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | 621 passed, zero failures/skips; 2m35.281s |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | 188 passed, zero failures, seven existing optional capture skips (195 total); 3m29.263s |
| `dotnet format Nova.slnx --verify-no-changes` | Passed |
| `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext --no-build` | Passed; no pending model changes. Existing tools 10.0.8/runtime 10.0.12 warning remains |
| `npm run check:contrast` from `Nova/` | All contrast ratios and token assertions passed |
| `node .agents/skills/impeccable/scripts/detect.mjs Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor --json` | No findings (`[]`); existing unrelated Evaluate `COMP_ROUND_OPEN` advisory remains |

Browser execution used `NOVA_PLACE_EVIDENCE=D:/repos/Nova/.git/pr270-round13-captures`.
Integration and browser suites ran serially across the machine, with source and generated assets
fixed during browser execution. All source hashes matched after the final run; `git diff --check`
passed before committing. New diagnostic captures remain local; the existing curated packet is unchanged.

The initial targeted formatting pass reported CA1849/S6966 for synchronous event calls in the new
async tests. Those calls now await `TriggerEventAsync`; no diagnostic was suppressed. Local logs use
`.git/pr270-round13-`: `format-apply.log`, `build.log`, `unit.log`, `integration.log`, `browser.log`,
`format.log`, `model.log`, `contrast.log`, and `detector.json`.

This PR includes migration `20260915015851_PlacementRecoveryAndHistory`; this review round changes
no schema. No check was disabled or weakened. All three suites must run again before merge.
