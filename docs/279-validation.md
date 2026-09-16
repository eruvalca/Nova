# Issue 279 validation

Scope: member-authorized manual player commands, immutable creation receipts, exact-request recovery,
duplicate rejection, and minimal in-session consumer integration. CSV remains administrator-only.
Issue: [#279](https://github.com/eruvalca/Nova/issues/279). Base: `fc2c0053`.

## Revision and gate status

Implementation revision: `f25d19a2f149e145988cafe846c2cca3071b7fa4` on
`codex/279-player-command-recovery`, based on `fc2c0053`. Build, unit, integration, model and format checks
ran against `fc2c0053` plus the exact source changes committed as `f25d19a2`; committing changed no build
inputs. The browser run used that same build and revision. The subsequent validation-record commit
changes only this document; application and browser-suite inputs match `f25d19a2` exactly. No source,
configuration, dependencies or generated assets changed during the browser run.

Remote `main` was verified still at `fc2c0053` before the browser run; no merge-input differences exist.

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
| Existing graduation-year, lifecycle history, enrollment, CSV behavior | Full unit/integration/browser suites; CSV role denials retained |

## Execution results and failure dispositions

- Initial focused unit run: 161 passed, zero failed/skipped. This preceded the final additional protocol,
  contention, component, and browser cases; it is not final gate evidence.
- Initial focused integration run: 35 passed, 14 failed. Failures involved fixtures that asserted mutation success for random actor IDs absent from persisted
  membership, and contention cases observing the former first lock. Fixtures now seed real users; tests
  observe the actual membership/season/roster serialization boundary. Confirmed by the full 652-test integration pass below.
- First full unit pass found six legacy expectation/fixture failures: lifecycle endpoint metadata still
  expected administrator-only access, and a historical lifecycle fixture omitted persisted users. After
  adding users, its old member-denial assertion was updated to member success. Confirmed by 3,657 passing
  unit tests, zero skipped, on the current implementation.
- First full integration run: 649 passed, three failed. Two additional legacy seeds omitted persisted
  users (cross-club enrollment and concurrent Draft creation); both now seed approved users. The HTTP
  receipt test expected the seed prefix instead of the actual uniquely suffixed campaign name; it now
  compares the receipt with the exact seeded database name. All new provider contention/recovery cases
  passed in that run. Confirmed by the full 652-test integration pass below.
- Formatting verification identified only initializer/switch layout and import ordering in the final
  added tests. These were corrected without changing behavior; full verification now passes.
- Intermediate compiler/analyzer failures were corrected: CSV still constructing manual inputs,
  generated migration formatting, unnecessary null-forgiving operators, explicit interface versus
  domain campaign-close result, and ordinal string comparisons in added tests. No diagnostic was disabled.

Current gate evidence for the implementation revision above:

| Check | Command | Current result |
| --- | --- | --- |
| Build | `dotnet build Nova.slnx --no-restore` | Pass: zero warnings/errors |
| Formatting | `dotnet format Nova.slnx --verify-no-changes --no-restore` | Pass |
| Migration model | `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext --no-build` | Pass: no pending changes (tool 10.0.8 emits an informational runtime 10.0.12 version warning) |
| Unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | 3,657 passed, zero failed/skipped |
| Integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | 652 passed, zero failed/skipped |
| Browser | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | 205 passed, zero failed, 8 existing opt-in evidence captures skipped; full suite selected because shared input, authorization, enrollment and HTTP behavior span flows |

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
Full unit, provider, and browser evidence passes as recorded above. There are no unresolved in-scope findings.

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

## Limitations

This is a pre-release breaking contract with one incremental receipt-table migration and no compatibility
bridge. Current browser recovery is in-memory only. Eight existing environment-gated screenshot/manual evidence captures were not requested (`NOVA_A11Y_SCREENSHOTS`
and `NOVA_PLACE_EVIDENCE` unset); they are reported as skipped, not passed. Behavioral browser assertions,
including creation validation, duplicate correction and lost-response retry, ran and passed. No new skip
or suppression was introduced.
