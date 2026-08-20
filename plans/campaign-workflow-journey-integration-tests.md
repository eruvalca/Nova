# Campaign Workflow Journey Integration Tests (Issue #116)

Add focused PostgreSQL integration tests that walk each of the six primary campaign
workflows (creation, late-player enrollment, evaluation, placement, close, reopen) as a
full HTTP journey — contract validation, service execution, and persisted-state
assertions — including multi-user administrator/evaluator cooperation. Test-only work;
no production code changes.

**Confirmed scope decisions (with the user):**
1. **Full HTTP journeys** — every workflow step is driven through the real HTTP API
   (`CampaignEndpoints` / `PlayerEndpoints` constants); EF is used only for identity/club
   prerequisites and final persisted-state assertions via `fixture.CreateAdminContext()`.
2. **Fill verified gaps only** for duplicate-race, stale-close, and ambiguous-commit
   retry scenarios — do not re-add Data-level race/retry coverage that already exists.

**Cross-child boundaries (do not drift into sibling sub-issues of epic #13):**
- #115 owns authorization matrices and tenant-isolation matrices → no new auth/tenancy matrix tests.
- #119 owns pure-policy decision matrices, SQLite service-shell tests, and PostgreSQL
  provider/lock/race mechanics → no Data-level provider-internals probes.
- #118 owns browser coverage → nothing in `Nova.Browser.Tests`.
- No production code changes.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set
its status to `Complete` and write its **Phase Summary** (what was done, key decisions,
anything needed to continue with zero context); run the phase's **Verification Plan** and
record the result before moving on. When all phases are done, fill in **Final Recap** and
**Deployment Plan**. During implementation, load the **`nova-testing`** skill
(`.github/skills/nova-testing/`) for harness internals — the testing instructions
(`.github/instructions/testing.instructions.md`) delegate the step-by-step recipe to it.

Harness facts (verified 2026-08-19):
- `NovaAppHostFixture` **self-starts** the Aspire AppHost in-process
  (`DistributedApplicationTestingBuilder`): PostgreSQL 18 container + Nova web app +
  migrations. Tests run with plain `dotnet test --project Nova.Integration.Tests/...`;
  Docker must be running. No separate `aspire start` needed.
- xUnit v4 + MTP + Shouldly. Test classes use `[Collection(NovaAppHostCollection.Name)]`
  and primary-constructor injection. Use `TestContext.Current.CancellationToken`.
- Multi-user pattern: `fixture.CreateNovaHttpClient()` per user →
  `IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync` →
  `SeedingHelpers.UpdateUserAsync(fixture, email, clubId, ...)` →
  `SeedingHelpers.RefreshClubMembershipCookieAsync` (see `CampaignEvaluationSharedStateHttpTests`).
- Never assert global unfiltered counts; each test seeds its own data (unique emails via
  `SeedingHelpers.UniqueEmail`, unique names via `Guid` suffixes). Parallelism is
  `ParallelMode.All`; the simulated user is AsyncLocal-backed — no static user state.
- File placement: HTTP-boundary journey tests → `Nova.Integration.Tests/Http/`; anything
  needing the execution-strategy fault-injection harness
  (`ExecutionStrategyRetryTestSupport`) → `Nova.Integration.Tests/Data/`.

---

## Phase 0: Coverage audit and gap list

Status: Complete

Suggested executor: orchestrator (requires judgment about cross-child boundaries)

Verify the gap analysis below against the live code before writing tests. Produce (in the
Phase Summary) a final per-workflow table: existing coverage, verified gaps, and the exact
list of new tests. If an item below is already covered, drop it and record why.

Known existing coverage (audit starting point, verified 2026-08-19):
- **Creation**: `Http/CampaignCreationHttpTests` (7: auth, validation, malformed JSON,
  cross-tenant season, duplicate name, created-aggregate) + `Data/CampaignCreationPostgresTests`
  (11: op-id uniqueness, FK constraints, ambiguous commit, transient retry, rollback, concurrent
  lock race).
- **Late enrollment** (player create auto-enrolls into every Active campaign): HTTP create
  covered in `Http/PlayerManagementHttpTests` (no enrollment assertion) + `Data/PlayerEnrollmentPostgresTests`
  (3: concurrent enrollment correctness) + `Data/PlayerManagementRetryTests` (3: op-id
  duplicate, ambiguous commit, transient retry — *audit whether the ambiguous-commit test
  asserts enrollment-row integrity*).
- **Evaluation**: `Http/EvaluationNoteHttpTests` (17), `Http/CampaignEvaluationSharedStateHttpTests`
  (3 multi-user shared-state, EF-seeded), `Data/EvaluationNotePostgresTests` (3).
- **Placement**: `Http/CampaignPlacementHttpTests` (25 incl. replacement-token flow, stale
  token, closed/archived conflicts), `Http/CampaignPlacementTokenRaceHttpTests` (1),
  `Data/CampaignPlacementLifecycleRaceTests` (1), `Data/CampaignPlacementRetryTests` (2).
- **Close/reopen**: `Http/CampaignLifecycleHttpTests` (9 incl. condition-keyed blockers,
  already-transitioned conflict, reopen-restores-editing), `Http/CampaignCloseoutHttpTests`
  (6 incl. evaluator+admin read surfaces, bounded ordered activity),
  `Http/CampaignLifecycleRaceHttpTests` (1 concurrent close), `Data/CampaignLifecyclePostgresTests`
  (10), `Data/CampaignLifecycleRetryTests` (6 — covers all three retry outcomes for close and
  reopen: fresh-context retry, ambiguous-commit success, transient-before-commit conflict).

- [x] Confirm the six workflow HTTP surfaces and input contracts (`CampaignEndpoints`,
      `PlayerEndpoints`, `CreateCampaignInput`, `CreatePlayerInput`, `UpdateCampaignPlacementInput`,
      `AddEvaluationNoteInput`, `CreateCampaignResult`, placement/replacement-token DTOs,
      `CampaignParticipantDetailDto`, close/reopen inputs and blockers).
- [x] Decide whether the audit confirms the gap list below; adjust items accordingly.
- [x] Check `Data/PlayerManagementRetryTests.Create_VerifiesCommittedOperation_AfterAmbiguousCommitFailure`
      — does it assert the player's participation rows (enrollment) survive? Record the answer.
- [x] Check whether an HTTP-level duplicate late-enrollment race exists anywhere (expected: no).
- [x] Check whether `Http/CampaignEvaluationSharedStateHttpTests` (EF-seeded) plus
      `EvaluationNoteHttpTests` already make the proposed evaluation journey redundant;
      keep the journey only if the HTTP-created campaign/player path adds uncovered
      contract coverage.
- [x] Record the final test list (file + method names) in the Phase Summary.

### Verification Plan

- `dotnet build Nova.slnx` succeeds (audit is read-only; build proves the repo is in a
  buildable state before new tests are added).

### Phase Summary

**Audit result (verified against live code, 2026-08-19):**

1. **Six HTTP surfaces + contracts confirmed.** `CampaignEndpoints` (create/list/detail/roster/participant
   detail/placement roster+summary/closeout-readiness/activity/close/reopen/add+edit+delete evaluation
   note/placement) and `PlayerEndpoints` (`POST /api/players` create, `PUT` update) are all present and
   match the input/output DTOs listed. `CreateCampaignInput` carries a client-supplied `OperationId`
   (idempotent create); `CreatePlayerInput` does **not**.

2. **Ambiguous-commit enrollment assertion — YES, already covered.**
   `PlayerManagementRetryTests.Create_VerifiesCommittedOperation_AfterAmbiguousCommitFailure`
   (lines 138–142) asserts `verify.PlayerCampaignAssignments.Where(a => a.PlayerId == result.Value.PlayerId)`
   yields `[activeCampaignId]` — the player's participation row survives the ambiguous-commit replay.
   → **The Phase 3 Data-level gap-fill is NOT needed; skipped.**

3. **No HTTP-level duplicate late-enrollment race (confirmed "no").** `PlayerManagementService.CreateAsync`
   generates `creationOperationId = Guid.CreateVersion7()` server-side per request (line 45);
   `CreatePlayerInput` exposes no operation id, the endpoint reads no idempotency header, and the only
   unique index (`PlayerEntityConfiguration`: `(ClubId, CreationOperationId)` with a NOT NULL filter) is
   internal-only. Two concurrent `POST /api/players` with the same payload therefore produce **two 201s**
   (two distinct players), never "one 201 / one 409".

   → The proposed `LateEnrollment_ConcurrentCreates_YieldOneCreatedOneConflict_WithSingleDurableRow`
   premise is **incorrect**; it is replaced by
   `LateEnrollment_ConcurrentCreates_BothPersistWithSingleDurableRow` (both 201, each with one durable
   enrollment row).

4. **Evaluation journey is NOT redundant.** `CampaignEvaluationSharedStateHttpTests` and
   `EvaluationNoteHttpTests` seed the campaign/participant via EF
   (`SeedingHelpers.SeedCampaignWithParticipantsAsync`). The new `EvaluationJourney_...` is the only test
   that creates the campaign **and** player through HTTP before exercising the evaluator→admin note
   write→read loop, so it adds uncovered contract coverage.

5. **Plan correction (tryout numbers).** Production code never assigns `TryoutNumber`
   (only `PlayerCampaignAssignmentEntity` declares it; test/browser seeds set it manually). The HTTP
   auto-enrollment path leaves `TryoutNumber == null`, so the CreationJourney assertion is "2 undecided
   rows, tryout numbers null" — not "distinct tryout numbers".

**Final test list (one class):** `Nova.Integration.Tests/Http/CampaignWorkflowJourneyHttpTests.cs`
1. `CreationJourney_AutoEnrollsPreExistingActivePlayers`
2. `LateEnrollmentJourney_NewPlayerEntersActiveCampaign`
3. `EvaluationJourney_EvaluatorNote_IsConsumedByAdmin`
4. `PlacementJourney_ReplacementTokenChain_UpdatesRosterAndSummary`
5. `CloseJourney_ReadinessToClosed_EvaluatorReadSurfacePreserved`
6. `ReopenJourney_RestoresWritability_PreservesOutcomes`
7. `LateEnrollment_ConcurrentCreates_BothPersistWithSingleDurableRow`

**Verification:** `dotnet build Nova.slnx` succeeded (0 warnings, 0 errors).

---

## Phase 1: Creation and late-enrollment journey tests

Status: Complete

Suggested executor: general-purpose sub-agent (invoke the `nova-testing` skill; well-specified
mechanical work with a write-run-fix loop against real PostgreSQL)

New file: `Nova.Integration.Tests/Http/CampaignWorkflowJourneyHttpTests.cs` (name may be
split per workflow if the audit prefers; keep one journey class unless it grows unwieldy).

- [x] `CreationJourney_AutoEnrollsPreExistingActivePlayers`: club admin registers + creates
      club via HTTP; creates 2 players via HTTP (`POST PlayerEndpoints.Create`, both 201);
      creates campaign via HTTP with inline season (`POST CampaignEndpoints.Create`);
      assert `CreateCampaignResult` (`EnrolledPlayerCount == 2`, `Status == Active`);
      `GET CampaignEndpoints.GetCampaignParticipantRoster` shows both players; EF
      (`fixture.CreateAdminContext()`) asserts exactly 2 participation rows with distinct
      tryout numbers and `PlacementOutcome.Undecided`. _(Implemented with the corrected
      tryout-number assertion: the HTTP auto-enrollment path leaves `TryoutNumber == null`.)_
- [x] `LateEnrollmentJourney_NewPlayerEntersActiveCampaign`: admin creates club + campaign
      via HTTP; then creates a player via HTTP (201); assert the new player appears in the
      campaign roster (HTTP) and has a participation row (EF) — enrollment happened at
      player-create time, after campaign creation; assert the player detail endpoint
      (`GetCampaignParticipantDetail`) is reachable for the new assignment.
- [x] Only if the audit finds `CampaignEvaluationSharedStateHttpTests` insufficient for the
      evaluation slice: an evaluation journey (see Phase 2) — do not duplicate that suite's
      three tests. _(Audit found it insufficient — EF-seeded — so the evaluation journey is
      added in Phase 2.)_

### Verification Plan

- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignWorkflowJourneyHttpTests"` — all new tests pass (Docker must be running; the
  fixture self-starts Postgres 18).
- `dotnet format Nova.slnx --verify-no-changes` — no formatting drift.

### Phase Summary

Added `Nova.Integration.Tests/Http/CampaignWorkflowJourneyHttpTests.cs` with
`CreationJourney_AutoEnrollsPreExistingActivePlayers` and `LateEnrollmentJourney_NewPlayerEntersActiveCampaign`.
Both drive club/campaign/player creation through the real HTTP API and assert the persisted enrollment
snapshot via `fixture.CreateAdminContext()`.

**Verification:** `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignWorkflowJourneyHttpTests"` — 7/7 passed (1m 15s).

---

## Phase 2: Evaluation and placement journey tests (multi-user)

Status: Complete

Suggested executor: general-purpose sub-agent (invoke the `nova-testing` skill)

Same journey class as Phase 1. Two members in one club: an administrator and an evaluator
(second approved member) — seed per `CampaignEvaluationSharedStateHttpTests` seeding pattern,
but create the campaign and player through HTTP, not EF.

- [x] `EvaluationJourney_EvaluatorNote_IsConsumedByAdmin`: admin creates club, campaign
      (HTTP), and player (HTTP, late-enrolled); evaluator joins the club; evaluator adds an
      evaluation note (`POST CampaignEndpoints.AddEvaluationNote`, 201); admin reads
      `GetCampaignParticipantDetail` and sees the note with the evaluator's actor metadata;
      evaluator edits the note (`PUT EditEvaluationNoteTemplate`, 204) and admin sees the
      edit — role-crossing write→read loop.
- [x] `PlacementJourney_ReplacementTokenChain_UpdatesRosterAndSummary`: admin (from the
      evaluation journey setup, or fresh HTTP journey) updates an `Undecided` assignment
      with `PUT CampaignEndpoints.UpdateCampaignPlacement` (200, returns replacement
      token); assert `GetCampaignPlacementRoster` and `GetCampaignPlacementSummary` reflect
      the outcome; apply a second update using the returned token (200) and assert the final
      state; EF asserts the persisted outcome and team on the assignment row.
      _(Implemented as Undecided → NotSelected → Assigned+team, team created via
      `TeamEndpoints.Create` over HTTP.)_
- [x] Optional (audit-gated): if the audit finds no existing journey that crosses roles
      during placement, have the evaluator read the placement roster after the admin's
      update and assert visibility (least-privileged read surface).
      _(Already covered by `CampaignPlacementHttpTests.GetPlacementRoutes_ReturnPayload_ForLeastPrivilegedClubMember`;
      not duplicated in the journey.)_

### Verification Plan

- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignWorkflowJourneyHttpTests"` — all tests from Phases 1–2 pass.
- Confirm no new test re-asserts a decision rule already covered by a policy matrix in
  `Nova.Unit.Tests` (#119 territory): each new assertion must be journey/contract-shaped,
  not rule-shaped.

### Phase Summary

Added `EvaluationJourney_EvaluatorNote_IsConsumedByAdmin` and
`PlacementJourney_ReplacementTokenChain_UpdatesRosterAndSummary` to the same journey class. Both create
the campaign and player through HTTP (per the `CampaignEvaluationSharedStateHttpTests` multi-user seeding
pattern), and the placement journey additionally creates the team through HTTP so the Assigned outcome's
team is a real HTTP-created aggregate.

**Verification:** targeted journey run — 7/7 passed (Phases 1–2 combined). No new test re-asserts a
`Nova.Unit.Tests` policy-matrix rule; each assertion is journey/contract-shaped.

---

## Phase 3: Close/reopen journey tests and verified race/retry gap fills

Status: Complete

Suggested executor: general-purpose sub-agent (invoke the `nova-testing` skill; the retry
gap fill uses `ExecutionStrategyRetryTestSupport` — read it first)

- [x] `CloseJourney_ReadinessToClosed_EvaluatorReadSurfacePreserved`: admin creates
      club/campaign/player via HTTP; evaluator joins; `GET GetCampaignCloseoutReadiness`
      returns blocked readiness (Undecided assignment); admin places the assignment via
      HTTP; readiness now unblocked; admin closes (`POST CampaignEndpoints.Close`, 204);
      EF asserts `Status == Closed` with closure provenance and a lifecycle event; evaluator
      can still GET campaign detail and participant detail (read surfaces); evaluator's
      `POST AddEvaluationNote` and admin's `PUT UpdateCampaignPlacement` both return
      409 Conflict.
- [x] `ReopenJourney_RestoresWritability_PreservesOutcomes`: continuing the closed campaign,
      admin reopens (`POST CampaignEndpoints.Reopen`, 204); EF asserts `Status == Active`,
      provenance cleared, `Reopened` event; placement outcome preserved on the assignment
      row; evaluator can add a note again (201); `GET GetCampaignActivity` returns bounded,
      ordered events (Close then Reopen).
- [x] `LateEnrollment_ConcurrentCreates_YieldOneCreatedOneConflict_WithSingleDurableRow`
      (verified gap — no HTTP duplicate-enrollment race exists) — **RENAMED and corrected**
      to `LateEnrollment_ConcurrentCreates_BothPersistWithSingleDurableRow`. The original
      "one 201 / one 409" premise is not reproducible over HTTP (see Phase 0): player create
      generates its operation id server-side and exposes no idempotency key. The corrected
      test proves the actual contract — two concurrent identical `POST PlayerEndpoints.Create`
      requests both return 201 as two distinct players, each with exactly one durable
      participation row (no duplicate enrollment).
- [x] Gap-fill (only if the Phase 0 audit found the existing retry test lacking enrollment
      assertions): **SKIPPED** — the Phase 0 audit confirmed
      `PlayerManagementRetryTests.Create_VerifiesCommittedOperation_AfterAmbiguousCommitFailure`
      already asserts the participation row survives (lines 138–142).
- [x] Do NOT add: new close/reopen retry tests (`CampaignLifecycleRetryTests` covers all
      three outcomes), new stale-close/provider race mechanics (`CampaignLifecyclePostgresTests`,
      `CampaignLifecycleRaceHttpTests` cover them) — **verified present and left untouched**;
      recorded here.

### Verification Plan

- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignWorkflowJourneyHttpTests"` — Phases 1–3 journey tests pass.
- If the Data-level gap fill was needed: `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*PlayerManagementRetryTests"` passes.

### Phase Summary

Added `CloseJourney_ReadinessToClosed_EvaluatorReadSurfacePreserved`,
`ReopenJourney_RestoresWritability_PreservesOutcomes`, and the corrected
`LateEnrollment_ConcurrentCreates_BothPersistWithSingleDurableRow`. The close/reopen retry and
stale-close/provider race suites were verified present (`CampaignLifecycleRetryTests`,
`CampaignLifecyclePostgresTests`, `CampaignLifecycleRaceHttpTests`) and left untouched; the
ambiguous-commit enrollment gap-fill was skipped (already covered).

**Verification:** targeted journey run — 7/7 passed (Phases 1–3 combined). The
`PlayerManagementRetryTests` gap-fill verification was not required (skipped, not run).

---

## Phase 4: Full verification and wrap-up

Status: Complete

Suggested executor: orchestrator

- [x] Run the complete integration suite: `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj`
      — all tests (new and pre-existing) pass against PostgreSQL 18 via the self-started
      AppHost (Docker required).
- [x] Run unit tests to prove no accidental impact: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj`.
- [x] Run the format gate: `dotnet format Nova.slnx --verify-no-changes`.
- [x] Re-check the acceptance criteria mapping: six workflows each have a focused journey
      test; late-enrollment and close/reopen transactional integrity under retry are proven;
      no test duplicates a #119 decision matrix or provider mechanic.
- [x] Commit on this branch with the Co-authored-by trailer; update this plan's Final Recap
      and Deployment Plan; close out issue #116 (comment summarizing the coverage added and
      the gap-fill decisions recorded in Phase 3).

### Verification Plan

- All three commands above exit 0 with the expected suites green (record counts).

### Phase Summary

- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignWorkflowJourneyHttpTests"` → **7/7 passed** (1m 15s).
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` → **313/313 passed** (3m 31s).
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` → **1708/1708 passed** (30s).
- `dotnet format Nova.slnx --verify-no-changes` → **passed** (exit 0; one CHARSET/BOM fix applied to the new file before the final run).
- Acceptance criteria mapping: six workflows each have a focused journey test; late-enrollment
  transactional integrity is proven by the existing `PlayerManagementRetryTests` + `PlayerEnrollmentPostgresTests`
  (left untouched per cross-child boundary) and the corrected HTTP concurrent-create journey; close/reopen
  transactional integrity under retry is proven by the existing `CampaignLifecycleRetryTests` (left untouched).

---

## Final Recap

Test-only change adding `Nova.Integration.Tests/Http/CampaignWorkflowJourneyHttpTests.cs` — seven
full-HTTP journeys covering the six primary campaign workflows (creation, late-player enrollment,
evaluation, placement, close, reopen) plus an HTTP-level concurrent late-enrollment check. Every
workflow step is driven through `CampaignEndpoints` / `PlayerEndpoints`; EF is used only for
identity/club prerequisites and final persisted-state assertions.

Phase 0 audit corrections recorded in this plan:
1. The proposed `LateEnrollment_ConcurrentCreates_YieldOneCreatedOneConflict_WithSingleDurableRow`
   premise is impossible over HTTP (player create generates its operation id server-side and exposes no
   idempotency key) — replaced by `..._BothPersistWithSingleDurableRow`.
2. The "distinct tryout numbers" assertion was corrected to "tryout numbers are null" (production never
   assigns them).
3. The ambiguous-commit enrollment gap-fill was skipped (already covered by
   `PlayerManagementRetryTests.Create_VerifiesCommittedOperation_AfterAmbiguousCommitFailure`).

No production code, browser tests, auth/tenancy matrix tests, or pure-policy decision-matrix tests were
added.

## Deployment Plan

Test-only change — no runtime deployment. CI runs build + unit tests only; the integration suite is
local-only and was verified locally against PostgreSQL 18 via the self-started Aspire AppHost (Docker)
before opening the PR: 313/313 integration tests and 1708/1708 unit tests passed, and
`dotnet format Nova.slnx --verify-no-changes` is clean.
