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

## Validation status

Tested base: `e6b1f382cefb5176ff509685df6554970a8fd1c8`; working implementation remains
uncommitted on `codex/issue-254-place-recovery`. Source fingerprint:
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
commands were local; this implementation has not been committed, pushed, or proposed as a PR.

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
