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

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator (cross-cutting reasoning; not delegatable)

- [x] Enumerate every pure policy under `Nova/Features` (`*Policy.cs` static classes) and, for each,
      enumerate its decision outcomes and boundaries from the source, then diff against the existing
      `Nova.Unit.Tests/**/*PolicyTests.cs`; record every uncovered outcome/boundary. Known policies:
      `CampaignClosurePolicy`, `CampaignPlacementPolicy`, `PlayerGraduationYearPolicy`,
      `TeamGraduationYearPolicy`, `DashboardActivityFeedPolicy`, `AccountDeletionPolicy` (also judge
      whether `EvaluatorAuthorizationPolicy` qualifies as a pure policy needing a matrix).
- [x] Enumerate every application service (`Nova/Features/**/*Service.cs` — 30 files — plus
      `ArchivalLifecycleService`) and map each to a direct SQLite service-shell test file
      (`*ServiceTests.cs` using `TenancyTestHarness` in `Nova.Unit.Tests/Data/TenancyTests.cs`);
      record services lacking one. Already-confirmed candidates with **no** direct shell test:
      `ClubService`, `PlayerLifecycleService`, `TeamLifecycleService` (their endpoint tests use
      fakes or URL-only assertions and never run the real service body).
- [x] Enumerate provider-sensitive query surfaces and map each to existing integration coverage;
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
- [x] Enumerate the race/retry matrix against existing coverage and record holes:
      - Execution-strategy retries + ambiguous-commit: 9 existing `*RetryTests.cs` files using
        `FailFirstCommittedTransactionInterceptor` (`ExecutionStrategyRetryTestSupport.cs`).
      - Uniqueness probes: `TeamManagementRetryTests` (`InsertAfterTeamExistsProbeInterceptor`),
        `TagDefinitionRetryTests` (`InsertAfterPlayerTagExistsProbeInterceptor`).
      - Advisory-lock lifecycle: `CampaignCreationPostgresTests` (`AdvisoryLockGateInterceptor`),
        `TagDefinitionRetryTests` (`AdvisoryLockGateInterceptor`), `TeamPlayerGraduationYearRaceTests`
        (`PlacementAfterLockSetInterceptor` + recorder); HTTP races in
        `CampaignLifecycleRaceHttpTests`, `CampaignPlacementLifecycleRaceTests`,
        `CampaignPlacementTokenRaceHttpTests`, `CampaignTagApplicationRaceHttpTests`.
- [x] Record the complete gap list in this phase's Phase Summary (table: policy → uncovered outcomes,
      service → shell test, query site → provider test) and update the Phase 2–4 checkboxes accordingly.
- [x] Baseline: run the unit suite green before touching any code.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — all green (baseline).
- Audit evidence recorded in the Phase Summary as the gap tables.

### Phase Summary

Baseline unit suite: **1708 passed, 0 failed** before any changes.

**Pure policies** (6 + 1 judged non-policy). `EvaluatorAuthorizationPolicy` is an ASP.NET
authorization policy (`Policies.RequireEvaluator`), not a pure domain policy; it is already covered by
`EvaluatorAuthorizationPolicyTests` — **no decision matrix needed**.

| Policy | Uncovered outcomes/boundaries |
| --- | --- |
| `AccountDeletionPolicy` | unauthenticated-but-user-exists `(false, true)` neutral case |
| `CampaignClosurePolicy` | single assignment failing both `eligibility` + `archivedTeams`; `playerYear > teamYear` eligible boundary |
| `CampaignPlacementPolicy` | closed > team-archived/team-ineligible; player-archived > team-archived/team-ineligible precedence |
| `PlayerGraduationYearPolicy` | none (full matrix + boundaries present) |
| `TeamGraduationYearPolicy` | none (empty / on-or-after / before / mixed-order / lowered-year present) |
| `DashboardActivityFeedPolicy` | `limit == 0` boundary |

**Services** (30 `*Service.cs` + `ArchivalLifecycleService`). `ArchivalLifecycleService` does **not**
exist as a class — `ArchivalLifecycleServiceTests` composes `PlayerLifecycleService` +
`TeamLifecycleService` + `TagDefinitionLifecycleService`. Gap (no direct SQLite shell test):
`ClubService`, `PlayerLifecycleService`, `TeamLifecycleService`. `ProfilePhotoService` has a Postgres
test (`Data/ProfilePhotoServiceTests.cs`); all other 30 services already have a direct shell test.

**Provider surfaces.** ILIKE defect confirmed in `ClubService.SearchClubsAsync` (raw `%{query}%`) and
`PlayerService.GetPlayerRosterAsync` (raw `%{normalizedSearch}%`). Existing escaping coverage is
HTTP-only (`TeamRosterHttpTests`, `CampaignParticipantHttpTests`), so direct service-level Postgres
escaping tests are missing for all five ILIKE sites. Bounded-ordering provider coverage missing for
`TagDefinitionQueryService` (overflow probe), `CampaignQueryService` (season-choice max), and
`TeamDetailQueryService` (placement-history cap/active-first).

**Race/retry.** `PlayerLifecycleService` is the only lifecycle mutation without ambiguous-commit
verification (it uses the single-lambda `ExecuteAsync` overload); `TeamLifecycleService` already has
the `CommitAttemptTracker` + `verifySucceeded` pattern.

## Phase 2: Tier 1 — decision-matrix gap closure for pure policies

Status: Complete

Suggested executor: sub-agent w/ smaller model (mechanical matrix fills from Phase 1's gap list;
orchestrator reviews the diff)

- [x] For every uncovered outcome/boundary from Phase 1, add `[Fact]`/`[Theory]` cases to the existing
      `*PolicyTests.cs` — constructed immutable values only, no harness/DI/mocks, per
      `.github/instructions/functional-core.instructions.md` and
      `.github/instructions/testing.instructions.md`. Use
      `[Theory(IncludeTestCaseIndex = true)]` for tabular matrices; one behavior per test,
      `Subject_Outcome_Condition` naming; Shouldly assertions.
- [x] If a new case reveals a policy defect (wrong outcome, wrong precedence order, wrong message),
      fix the **policy** — never weaken the assertion (issue requirement).
- [x] Confirm each policy's outcome set has ≥ 1 explicit case (e.g., for `CampaignPlacementPolicy`:
      campaign-closed precedence over player-archived, team unavailable, team archived, team
      ineligible at `playerYear < teamYear`, allow at equal year, allow when no team requested —
      the existing 7 tests cover the main matrix; add any missing precedence combination).
- [x] Re-run all policy test classes.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*PolicyTests"` — green.
- Every outcome variant of each audited policy has an explicit passing case (assert by reading the
  tests against the audit table, not by coverage tooling).

### Phase Summary

Added 8 cases, no policy defects found (no production policy changes):
- `AccountDeletionPolicyTests`: `(false, true)` neutral case.
- `CampaignClosurePolicyTests`: combined `eligibility`+`archivedTeams` for one assignment; `playerYear > teamYear` eligible boundary.
- `CampaignPlacementPolicyTests`: 4 precedence cases (closed > team-archived / team-ineligible; player-archived > team-archived / team-ineligible).
- `DashboardActivityFeedPolicyTests`: `limit == 0` boundary.

`--filter-class "*PolicyTests"` → **70 passed, 0 failed**.

## Phase 3: Tier 2 — SQLite service-shell tests for uncovered services

Status: Complete

Suggested executor: one sub-agent per service (independent files, parallelizable); orchestrator
reviews each diff

- [x] `Nova.Unit.Tests/Clubs/ClubServiceTests.cs` — direct shell tests on the `TenancyTestHarness`
      (SQLite): `SearchClubsAsync` (case-insensitive match across Name/City/State, blank query returns
      all, ordering by `Name`, tenant visibility), `CreateClubAsync` (success + member/admin role
      effect, `DbUpdateException` → `ServiceProblem.ServerError` mapping, authorization/validation
      states).
- [x] `Nova.Unit.Tests/Players/PlayerLifecycleServiceTests.cs` — direct shell tests: lifecycle
      mutation paths, authorization states, OneOf success/problem variants, effect application
      (mirror `CampaignLifecycleServiceTests` structure).
- [x] `Nova.Unit.Tests/Teams/TeamLifecycleServiceTests.cs` — direct shell tests, same shape.
- [x] Any further services Phase 1 adds to the gap list (one checkbox per service, added during the
      audit).
- [x] Keep every test parallel-safe: AsyncLocal-backed current user, per-test seeded data, no global
      counts (`.github/instructions/testing.instructions.md` conventions).

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*ClubServiceTests"` — green.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*PlayerLifecycleServiceTests"` — green.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*TeamLifecycleServiceTests"` — green.

### Phase Summary

Added three direct SQLite shell test classes (per-test seeding, mutable `TenancyTestHarness` current user):
- `ClubServiceTests` (9): search blank/ordered, case-insensitive across fields, LIKE-metacharacter literals; create validation / conflict / forbidden / server-error / success + membership / ClubAdmin role.
- `PlayerLifecycleServiceTests` (8): forbidden (non-admin, no-club), not-found (other club), archive success + provenance, archive conflict (already archived, undecided blocker), restore success + clears provenance, restore conflict (already active).
- `TeamLifecycleServiceTests` (8): same shape for team archive/restore + active-placement blocker.

Each class passed individually; full unit suite is green.

## Phase 4: Tier 3 — PostgreSQL provider/lock/race matrix

Status: Complete

Suggested executor: orchestrator for 4.1 (production defect fix); sub-agent w/ smaller model for the
mechanical provider tests after 4.1 lands (orchestrator reviews)

- [x] **4.1 ILIKE escaping defect fix (user-approved scope).** Add escaped-pattern handling to
      `ClubService.SearchClubsAsync` and `PlayerService` roster search, mirroring
      `TeamRosterQueryService` (`EscapeLikePattern`: `\` → `\\`, `%` → `\%`, `_` → `\_`, with
      `EF.Functions.ILike(..., @"\")` on the Npgsql branch and the existing `ToUpper().Contains` /
      tryout-number logic preserved on the SQLite branch). `PlayerService` keeps the tryout-number
      disjunction; escape only the name pattern. Add unit coverage for any new helper.
- [x] **4.2 ILIKE escaping provider tests.** New integration test classes (Postgres via
      `NovaAppHostFixture`) seeding rows whose names/cities/states contain literal `%`, `_`, `\` and
      asserting: escaped search terms match only the literal rows for `TeamRosterQueryService`,
      `TagDefinitionQueryService`, `CampaignParticipantQueryService`, `ClubService`, and `PlayerService`;
      plain substring and case-insensitive matching still work. Place per feature
      (e.g., a `*SearchEscapingPostgresTests` class per feature folder in `Nova.Integration.Tests/Data`).
- [x] **4.3 Bounded-ordering provider tests.** For every bounded-ordering site Phase 1 found without
      provider coverage, add a Postgres test asserting exact order and exact row cap (including the
      `MaxTagDefinitions + 1` overflow probe for `TagDefinitionQueryService` and season-choice max for
      `CampaignQueryService`).
- [x] **4.4 Race/retry gap closure.** Add only the scenarios Phase 1 recorded as missing from:
      advisory-lock lifecycle races, uniqueness-probe races (unique constraint is the final guard;
      exception maps to `Conflict` — `.github/instructions/testing.instructions.md`), execution-strategy
      retries, and ambiguous-commit verification (`FailFirstCommittedTransactionInterceptor`). Reuse
      the interceptors in `ExecutionStrategyRetryTestSupport.cs` and `PostgresAdvisoryLockTestHelper.cs`.
- [x] If any provider test exposes a production defect, fix the defect rather than weakening assertions.
- [x] Run each new integration test class, then the full integration suite.

### Verification Plan

- Per new class: `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*<NewClass>"` — green
  (`NovaAppHostFixture` self-starts the AppHost with PostgreSQL 18; Docker must be running).
- Full suite: `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — green.
  CI only runs build + unit tests, so the full integration run is a local gate.

### Phase Summary

**4.1** Fixed ILIKE escaping in `ClubService.SearchClubsAsync` (Name/City/State) and
`PlayerService.GetPlayerRosterAsync` (display-name pattern only; tryout-number disjunction preserved).
Both now escape `\`→`\\`, `%`→`\%`, `_`→`\_` and use `EF.Functions.ILike(..., @"\")` on Npgsql with
`ToUpper().Contains` on SQLite. `ClubService` also gained the SQLite fallback it was missing (previously
it issued `ILike` unconditionally, which SQLite cannot translate).

**4.2** New direct service-level escaping Postgres tests:
`TeamRosterSearchEscapingPostgresTests`, `TagDefinitionSearchEscapingPostgresTests`,
`CampaignParticipantSearchEscapingPostgresTests`, `ClubSearchEscapingPostgresTests`,
`PlayerSearchEscapingPostgresTests`.

**4.3** New bounded-ordering Postgres tests:
`TagDefinitionOrderingPostgresTests` (cap + `HasMore` overflow probe and exact-cap no-overflow),
`CampaignQueryOrderingPostgresTests` (season-choice cap + newest-first),
`TeamDetailOrderingPostgresTests` (placement-history cap + active-first + truncation).

**4.4** `PlayerLifecycleService` now has ambiguous-commit verification (mirrors `TeamLifecycleService`:
`CommitAttemptTracker` + `verifySucceeded` → `VerifyTransitionCommittedAsync`). Added
`PlayerArchive_ReportsSuccess_WhenCommitSucceedsButTransientFailureSurfaces` and
`PlayerRestore_ReportsSuccess_WhenCommitSucceedsButTransientFailureSurfaces` to
`PlayerLifecycleRetryTests`.

## Phase 5: Full verification, issue linkage, and PR

Status: Complete

Suggested executor: orchestrator

- [x] `dotnet format Nova.slnx --verify-no-changes` (apply with `dotnet format Nova.slnx` if needed).
- [x] `dotnet build Nova.slnx` — clean.
- [x] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full unit suite green.
- [x] `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — full integration
      suite green (local; AppHost fixture).
- [x] Record results in **Final Recap** and post an issue comment on #119 mapping each acceptance
      criterion to its evidence.
- [x] Open a pull request linked to #119 (user-approved PR flow), with the
      Co-authored-by trailer on commits.

### Verification Plan

- All four commands above green; PR CI (build + unit) green.
- Issue comment lists: policy → decision-matrix evidence, service → shell-test evidence,
  provider/race scenario → test class evidence.

### Phase Summary

- `dotnet format Nova.slnx --verify-no-changes` → clean (0 of 655 files changed).
- `dotnet build Nova.slnx` → 0 warnings, 0 errors.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` → **1741 passed, 0 failed**.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` → **317 passed, 0 failed**.
- Issue comment posted on #119; PR opened with `Closes #119` and Co-authored-by trailer.

## Final Recap

Completed the three-tier test matrix for #119 with no new features and no HTTP/WASM/UI contract
changes.

**Tier 1 (pure policies)** — closed every uncovered decision-matrix boundary across the six pure
policies (8 new cases); no policy defects found.

**Tier 2 (service shells)** — added direct SQLite `TenancyTestHarness` shell tests for the three
services that lacked them (`ClubService`, `PlayerLifecycleService`, `TeamLifecycleService`).

**Tier 3 (provider/lock/race)** — fixed the unescaped `ILIKE` defects in `ClubService` and
`PlayerService`; added direct service-level escaping Postgres tests for all five ILIKE sites; added
bounded-ordering Postgres tests for the tag overflow probe, season-choice max, and placement-history
cap/active-first; closed the `PlayerLifecycleService` ambiguous-commit gap (production fix + two
retry tests).

**User-visible change:** club search and player roster search now treat `%`, `_`, and `\` as literal
characters rather than `LIKE` wildcards.

## Deployment Plan

Normal PR merge — no migrations, configuration, or infra changes.

1. Merge the PR after CI (build + unit) is green; the full integration suite was run locally against
   the Aspire AppHost + PostgreSQL 18.
2. No data migration or rollback steps are required.
3. Note for users: club/player search queries containing `%`, `_`, or `\` now match those characters
   literally (previously they acted as wildcards in `ILIKE`).
