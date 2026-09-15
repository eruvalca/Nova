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
No production schema changed, so the initial migration-model evidence remains applicable. The
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
fixed with no concurrent build/format process. No production schema changed, so initial migration-model
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
No production schema changed, so the original migration-model evidence remains applicable.

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
