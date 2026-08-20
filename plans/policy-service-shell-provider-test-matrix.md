# Policy, Service-Shell, and Provider Test Matrix (#119)

Complete Nova's three-tier test matrix: database-free decision-matrix coverage for every pure policy,
a representative SQLite service-shell test for every application service, and PostgreSQL
provider/lock/race tests (ILIKE escaping, bounded ordering, advisory-lock races, uniqueness probes,
execution-strategy retries, ambiguous commits). Fix defects found along the way rather than weakening
assertions. No new features, no HTTP/WASM contract changes, no UI/browser work.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to
`Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue
with zero context); run the phase's **Verification Plan** and record the result before moving on. When
all phases are done, fill in **Final Recap** and **Deployment Plan**. The audit in Phase 1 produces
the authoritative gap list — update Phases 2–4 checkboxes from it before closing Phase 1.

## Phase 1: Coverage audit and baseline

Status: Not started <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator (cross-cutting reasoning; not delegatable)

- [ ] Enumerate every pure policy under `Nova/Features` (`*Policy.cs` static classes) and, for each,
      enumerate its decision outcomes and boundaries from the source, then diff against the existing
      `Nova.Unit.Tests/**/*PolicyTests.cs`; record every uncovered outcome/boundary. Known policies:
      `CampaignClosurePolicy`, `CampaignPlacementPolicy`, `PlayerGraduationYearPolicy`,
      `TeamGraduationYearPolicy`, `DashboardActivityFeedPolicy`, `AccountDeletionPolicy` (also judge
      whether `EvaluatorAuthorizationPolicy` qualifies as a pure policy needing a matrix).
- [ ] Enumerate every application service (`Nova/Features/**/*Service.cs` — 30 files — plus
      `ArchivalLifecycleService`) and map each to a direct SQLite service-shell test file
      (`*ServiceTests.cs` using `TenancyTestHarness` in `Nova.Unit.Tests/Data/TenancyTests.cs`);
      record services lacking one. Already-confirmed candidates with **no** direct shell test:
      `ClubService`, `PlayerLifecycleService`, `TeamLifecycleService` (their endpoint tests use
      fakes or URL-only assertions and never run the real service body).
- [ ] Enumerate provider-sensitive query surfaces and map each to existing integration coverage;
      record the uncovered ones:
      - ILIKE sites: `TeamRosterQueryService:53` (escaped), `TagDefinitionQueryService:55` (escaped),
        `CampaignParticipantQueryService:77-79` (escaped), `ClubService:129-131` (**unescaped —
        defect**), `PlayerService:66/70` (**unescaped — defect**). Note: `grep` confirms **zero**
        ILIKE/escaping assertions anywhere in `Nova.Integration.Tests` today.
      - Bounded-ordering sites: `TeamRosterQueryService:70` (`Take(limit)`), `TeamDetailQueryService:99`,
        `TagDefinitionQueryService:62/75/98` (max + 1 overflow probe), `PlayerService:123/133/149`
        (paging), `DashboardQueryService` (8 `Take(limit)` sites), `CampaignCloseoutQueryService:135/158`,
        `CampaignParticipantQueryService:138`, `CampaignPlacementQueryService:86`,
        `CampaignQueryService:66/215` (season-choice max). Some already have Postgres ordering coverage
        (e.g., `CampaignPlacementHttpTests` bounded/ordered page, `CampaignCloseoutHttpTests` activity
        ordering) — the audit decides which still lack it.
- [ ] Enumerate the race/retry matrix against existing coverage and record holes:
      - Execution-strategy retries + ambiguous-commit: 9 existing `*RetryTests.cs` files using
        `FailFirstCommittedTransactionInterceptor` (`ExecutionStrategyRetryTestSupport.cs`).
      - Uniqueness probes: `TeamManagementRetryTests` (`InsertAfterTeamExistsProbeInterceptor`),
        `TagDefinitionRetryTests` (`InsertAfterPlayerTagExistsProbeInterceptor`).
      - Advisory-lock lifecycle: `CampaignCreationPostgresTests` (`AdvisoryLockGateInterceptor`),
        `TagDefinitionRetryTests` (`AdvisoryLockGateInterceptor`), `TeamPlayerGraduationYearRaceTests`
        (`PlacementAfterLockSetInterceptor` + recorder); HTTP races in
        `CampaignLifecycleRaceHttpTests`, `CampaignPlacementLifecycleRaceTests`,
        `CampaignPlacementTokenRaceHttpTests`, `CampaignTagApplicationRaceHttpTests`.
- [ ] Record the complete gap list in this phase's Phase Summary (table: policy → uncovered outcomes,
      service → shell test, query site → provider test) and update the Phase 2–4 checkboxes accordingly.
- [ ] Baseline: run the unit suite green before touching any code.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — all green (baseline).
- Audit evidence recorded in the Phase Summary as the gap tables.

### Phase Summary

_(write when phase completes)_

## Phase 2: Tier 1 — decision-matrix gap closure for pure policies

Status: Not started

Suggested executor: sub-agent w/ smaller model (mechanical matrix fills from Phase 1's gap list;
orchestrator reviews the diff)

- [ ] For every uncovered outcome/boundary from Phase 1, add `[Fact]`/`[Theory]` cases to the existing
      `*PolicyTests.cs` — constructed immutable values only, no harness/DI/mocks, per
      `.github/instructions/functional-core.instructions.md` and
      `.github/instructions/testing.instructions.md`. Use
      `[Theory(IncludeTestCaseIndex = true)]` for tabular matrices; one behavior per test,
      `Subject_Outcome_Condition` naming; Shouldly assertions.
- [ ] If a new case reveals a policy defect (wrong outcome, wrong precedence order, wrong message),
      fix the **policy** — never weaken the assertion (issue requirement).
- [ ] Confirm each policy's outcome set has ≥ 1 explicit case (e.g., for `CampaignPlacementPolicy`:
      campaign-closed precedence over player-archived, team unavailable, team archived, team
      ineligible at `playerYear < teamYear`, allow at equal year, allow when no team requested —
      the existing 7 tests cover the main matrix; add any missing precedence combination).
- [ ] Re-run all policy test classes.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*PolicyTests"` — green.
- Every outcome variant of each audited policy has an explicit passing case (assert by reading the
  tests against the audit table, not by coverage tooling).

### Phase Summary

_(write when phase completes)_

## Phase 3: Tier 2 — SQLite service-shell tests for uncovered services

Status: Not started

Suggested executor: one sub-agent per service (independent files, parallelizable); orchestrator
reviews each diff

- [ ] `Nova.Unit.Tests/Clubs/ClubServiceTests.cs` — direct shell tests on the `TenancyTestHarness`
      (SQLite): `SearchClubsAsync` (case-insensitive match across Name/City/State, blank query returns
      all, ordering by `Name`, tenant visibility), `CreateClubAsync` (success + member/admin role
      effect, `DbUpdateException` → `ServiceProblem.ServerError` mapping, authorization/validation
      states).
- [ ] `Nova.Unit.Tests/Players/PlayerLifecycleServiceTests.cs` — direct shell tests: lifecycle
      mutation paths, authorization states, OneOf success/problem variants, effect application
      (mirror `CampaignLifecycleServiceTests` structure).
- [ ] `Nova.Unit.Tests/Teams/TeamLifecycleServiceTests.cs` — direct shell tests, same shape.
- [ ] Any further services Phase 1 adds to the gap list (one checkbox per service, added during the
      audit).
- [ ] Keep every test parallel-safe: AsyncLocal-backed current user, per-test seeded data, no global
      counts (`.github/instructions/testing.instructions.md` conventions).

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*ClubServiceTests"` — green.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*PlayerLifecycleServiceTests"` — green.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*TeamLifecycleServiceTests"` — green.

### Phase Summary

_(write when phase completes)_

## Phase 4: Tier 3 — PostgreSQL provider/lock/race matrix

Status: Not started

Suggested executor: orchestrator for 4.1 (production defect fix); sub-agent w/ smaller model for the
mechanical provider tests after 4.1 lands (orchestrator reviews)

- [ ] **4.1 ILIKE escaping defect fix (user-approved scope).** Add escaped-pattern handling to
      `ClubService.SearchClubsAsync` and `PlayerService` roster search, mirroring
      `TeamRosterQueryService` (`EscapeLikePattern`: `\` → `\\`, `%` → `\%`, `_` → `\_`, with
      `EF.Functions.ILike(..., @"\")` on the Npgsql branch and the existing `ToUpper().Contains` /
      tryout-number logic preserved on the SQLite branch). `PlayerService` keeps the tryout-number
      disjunction; escape only the name pattern. Add unit coverage for any new helper.
- [ ] **4.2 ILIKE escaping provider tests.** New integration test classes (Postgres via
      `NovaAppHostFixture`) seeding rows whose names/cities/states contain literal `%`, `_`, `\` and
      asserting: escaped search terms match only the literal rows for `TeamRosterQueryService`,
      `TagDefinitionQueryService`, `CampaignParticipantQueryService`, `ClubService`, and `PlayerService`;
      plain substring and case-insensitive matching still work. Place per feature
      (e.g., a `*SearchEscapingPostgresTests` class per feature folder in `Nova.Integration.Tests/Data`).
- [ ] **4.3 Bounded-ordering provider tests.** For every bounded-ordering site Phase 1 found without
      provider coverage, add a Postgres test asserting exact order and exact row cap (including the
      `MaxTagDefinitions + 1` overflow probe for `TagDefinitionQueryService` and season-choice max for
      `CampaignQueryService`).
- [ ] **4.4 Race/retry gap closure.** Add only the scenarios Phase 1 recorded as missing from:
      advisory-lock lifecycle races, uniqueness-probe races (unique constraint is the final guard;
      exception maps to `Conflict` — `.github/instructions/testing.instructions.md`), execution-strategy
      retries, and ambiguous-commit verification (`FailFirstCommittedTransactionInterceptor`). Reuse
      the interceptors in `ExecutionStrategyRetryTestSupport.cs` and `PostgresAdvisoryLockTestHelper.cs`.
- [ ] If any provider test exposes a production defect, fix the defect rather than weakening assertions.
- [ ] Run each new integration test class, then the full integration suite.

### Verification Plan

- Per new class: `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*<NewClass>"` — green
  (`NovaAppHostFixture` self-starts the AppHost with PostgreSQL 18; Docker must be running).
- Full suite: `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — green.
  CI only runs build + unit tests, so the full integration run is a local gate.

### Phase Summary

_(write when phase completes)_

## Phase 5: Full verification, issue linkage, and PR

Status: Not started

Suggested executor: orchestrator

- [ ] `dotnet format Nova.slnx --verify-no-changes` (apply with `dotnet format Nova.slnx` if needed).
- [ ] `dotnet build Nova.slnx` — clean.
- [ ] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full unit suite green.
- [ ] `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — full integration
      suite green (local; AppHost fixture).
- [ ] Record results in **Final Recap** and post an issue comment on #119 mapping each acceptance
      criterion to its evidence.
- [ ] Open a pull request linked to #119 (user-approved PR flow), with the
      Co-authored-by trailer on commits.

### Verification Plan

- All four commands above green; PR CI (build + unit) green.
- Issue comment lists: policy → decision-matrix evidence, service → shell-test evidence,
  provider/race scenario → test class evidence.

### Phase Summary

_(write when phase completes)_

## Final Recap

_(write when all phases complete: summary of the entire piece of work)_

## Deployment Plan

_(write when all phases complete: step-by-step deployment instructions — expected to be a normal PR
merge; note the user-visible change that club/player search now treats `%`, `_`, `\` as literals)_
