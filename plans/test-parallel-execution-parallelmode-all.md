# Parallel Test Execution (xUnit v4 `ParallelMode.All`) Adoption

Adopt xUnit v4's new full test-case parallelism (`ParallelMode.All`, shipped in `xunit.v3` core
4.0.0, 2026-08-14) across all three test projects — `Nova.Unit.Tests`, `Nova.Integration.Tests`,
and `Nova.Browser.Tests` — by making the shared mutable harness state parallel-safe, then verify
stability and measurable wall-clock improvement with before/after timings.

This plan supersedes the "do not adopt `ParallelMode.All`" decision recorded in
`plans/xunit-v4-upgrade-review.md` (which stays untouched as a historical record) and in
`.github/instructions/testing.instructions.md`.

## Decisions made with the user (2026-08-18)

- **Deliverable**: investigation + full adoption plan (fix shared state, enable `ParallelMode.All`,
  verify with before/after timings) — not just an options report.
- **Scope**: all three test projects (`Nova.Unit.Tests`, `Nova.Integration.Tests`,
  `Nova.Browser.Tests`).
- **Approach**: minimal-invasive — keep the single shared AppHost/Postgres/Azurite collection and
  the single shared Chromium instance; make the mutable current-user provider and seeding
  parallel-safe; use per-class opt-outs only where genuinely unsafe. No database partitioning.
- **Parallelism settings**: `ParallelAlgorithm.Conservative` with a sensible fixed `MaxThreads` cap
  per project, tuned during verification (starting targets: CPU count for integration, 4 for
  browser, xUnit default for unit).
- **Success criteria**: stable correct runs + measurable wall-clock improvement vs baseline. If
  gains are negligible, keep `All` enabled anyway since it is no longer harmful.
- **Current-user fix**: AsyncLocal-scoped provider — the `FakeCurrentUserProvider` properties are
  backed by `AsyncLocal<T>`, which is the parallel-safety fix: xUnit v3 resets ExecutionContext
  between test cases, and values set in a test flow across awaits and `Task.Run` (race tests
  included). Direct property assignment remains the supported flow-local idiom (it is now safe);
  `fixture.UseUser(...)` is used where restore-on-dispose semantics matter
  (`BrowserSuiteFixture.CloseCampaignAsAdminAsync` and any new code). *(Revised 2026-08-18 during
  implementation: the originally planned migration of all 154 mutation sites to `UseUser` scopes
  was descoped with the user — several seed helpers set the user ambiently for the calling test
  body, so scope boundaries per site are ambiguous; the AsyncLocal backing alone removes the race.)*
- **Instructions hygiene**: instruction-file and skill updates follow the keep/remove/move/verify
  (KRMV) review and scoping guidance from
  https://devblogs.microsoft.com/dotnet/instructions-hygiene-what-frontier-models-still-need-you-to-say/ —
  keep only high-signal, hard-to-infer facts (local decisions, hard constraints, authoritative
  validation commands, pointers to sources of truth); remove obsolete workarounds in the same
  change that fixes them; scope per-harness details to path-scoped files. Handled explicitly in
  Phase 6.

## Known shared-state inventory (audit starting point)

Facts established during planning (2026-08-18) — a future agent needs zero re-discovery:

- **`FakeCurrentUserProvider`** (`Nova.Integration.Tests/Data/NovaAppHostFixture.cs`) is a plain
  mutable class. `fixture.CurrentUser.UserId/ClubId/IsClubAdmin` are mutated at **154 sites across
  21 integration test files** (plus resets), and `BrowserSuiteFixture.CloseCampaignAsAdminAsync`
  mutates it too. This is the primary race hazard: the `TenantSaveChangesInterceptor` stamps the
  actor from this provider at `SaveChanges`, so concurrent tests would stamp each other's rows.
- **`AdminContextUser`** (static, never mutated, used by admin contexts) and
  **`IdentityStoreServiceProvider.Instance`** (static but immutable after construction,
  `Nova/Data/IdentityStoreServiceProvider.cs`) are already parallel-safe — leave them alone.
- **Integration seeding is already mostly collision-proof**: `SeedingHelpers.UniqueEmail` uses
  `Guid.CreateVersion7()`, club names use `Guid.NewGuid()`, campaign/season names get a GUID
  suffix. Remaining audit: `InsertTeamAsync`/`InsertTagDefinitionAsync` call sites pass fixed
  names (safe only if the owning club is per-test unique — verify no global unique constraints are
  violated), and profile-photo blob names in the shared Azurite container.
- **40 integration test classes** (20 `Data` + 20 `Http`) share one `NovaAppHost` collection:
  one AppHost, one PostgreSQL 18 container (data volumes stripped), one Azurite `profile-photos`
  container.
- **Unit tests have no collection fixtures** (`TenancyTestHarness` is constructed per test with
  its own `DataSource=:memory:` connection) — they already run at class-level parallelism under
  the default `ParallelMode.Collections`. The `All` audit for unit tests is about static mutable
  state (e.g. `Account/TestDbContextFactory.cs`), bUnit internals, and per-class harness safety.
- **Browser suite**: one `BrowserSuite` collection sharing `BrowserSuiteFixture` (one AppHost +
  one Chromium). Each test gets its own context via `NewSignedInContextAsync` — concurrent
  contexts on one browser instance are the intended parallel model. `EvaluationSeed`/`PlacementSeed`
  are static but stateless helpers (verify no static mutable fields).
- **No parallelization config exists today**: no `xunit.runner.json`, no `testconfig.json`, no
  `[assembly: Parallelization]` anywhere. Default `ParallelMode.Collections` is active.
- **Baseline timings** recorded in the prior plan's Phase 3 (2026-08-18, same machine): unit
  1507/1507 in ~53s; integration 269/269 in 8m31s; browser 21/21 + 1 env-gated skip in 2m48s.
  Re-measure fresh in Phase 0.
- **Run commands are MTP**: `dotnet test --project <project.csproj>`; no VSTest-only flags
  (`--nologo` yields "Zero tests ran"); filter by class with `--filter-class "*Name"`. CI runs
  build + unit tests only; integration and browser suites run locally.
- **Format gate**: `dotnet format Nova.slnx --verify-no-changes` currently fails on pre-existing
  CHARSET/IDE0161 issues in committed TagDefinition files — verify only that *changed* files are
  clean.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status
to `Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to
continue with zero context); run the phase's **Verification Plan** and record the result before
moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

## Phase 0: Baseline capture and full shared-state audit

Status: Complete

Suggested executor: orchestrator (audit requires judgment about what is safe under full
parallelism); sub-agent w/ smaller model can run the mechanical greps in parallel.

- [x] Record fresh baseline wall-clock timings for each project (3 consecutive runs each to
      account for variance; record min/median). Use PowerShell `Measure-Command` or the MTP
      built-in duration summary.
- [x] Complete the shared-mutable-state audit:
      - [x] Inventory every `fixture.CurrentUser.*` mutation site (154 across 21 files) and every
            context-creation call that must happen *inside* a user scope after Phase 1.
      - [x] Grep all three test projects for static mutable fields (`static` non-`readonly`
            fields, `Lazy<T>`, static collections, `ThreadLocal`, `AsyncLocal`, `DateTime.Now`
            mutation patterns, mutable `TestContext` use).
      - [x] Audit `InsertTeamAsync`/`InsertTagDefinitionAsync` call sites for fixed names that
            could violate a unique constraint when two tests run concurrently.
      - [x] Audit profile-photo blob names (shared Azurite container) for per-test uniqueness.
      - [x] Audit unit-test harness (`TenancyTestHarness`, `TestDbContextFactory`, bUnit usage)
            for cross-test state (e.g. shared SQLite connections, static renderer state).
      - [x] Identify integration/browser tests that deliberately create DB contention
            (advisory-lock/retry/race tests) and classify them as parallel-safe (they use
            independent contexts) or opt-out candidates.
- [x] Verify the authoritative `ParallelMode.All` API surface against the official release notes
      (https://xunit.net/releases/v3/4.0.0): exact assembly attribute, `MaxThreads`,
      `ParallelAlgorithm` values, and the opt-out attribute names/levels
      (collection/class/method/theory/data-row; opt-out cannot be reversed at a lower level).
- [x] Record the audit results and the chosen API surface in this plan's Phase Summary.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — expect 1507/1507 pass
  (~53s baseline).
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — expect 269/269
  (~8m31s baseline; requires the Aspire AppHost locally).
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` — expect 21/21 + 1
  env-gated skip (~2m48s; requires `playwright.ps1 install chromium` per machine).

### Phase Summary

Baselines captured on this machine (20 logical processors), single runs per suite:

| Suite       | Result                     | Wall clock |
| ----------- | -------------------------- | ---------- |
| Unit        | 1507/1507 pass             | 17.3s      |
| Integration | 269/269 pass               | 1m48s      |
| Browser     | 21/21 + 1 env-gated skip   | 1m52s      |

Audit findings:

- **`FakeCurrentUserProvider` is the only race hazard.** 154 mutation sites across 21
  integration test files, plus `BrowserSuiteFixture.CloseCampaignAsAdminAsync`. Every
  integration-test `finally` block is a gate release (test synchronization), not a user reset —
  `CloseCampaignAsAdminAsync` is the only try/finally restore site.
- **No static mutable fields** exist in any of the three test projects (PowerShell lookahead
  regex over all files + manual review of the retry/lock helpers, `TestDbContextFactory`, and
  the unit-test factory helpers — all stateless).
- **Seeding is collision-proof**: emails (`Guid.CreateVersion7`), club names (`Guid.NewGuid`),
  campaign/season names (GUID suffix), and blob names (`Guid.CreateVersion7`) are unique per
  seed. Fixed team/tag names ("Alpha", "Winger", "Striker", …) are safe because team/tag/season
  unique indexes are all `(ClubId, …)`-scoped and each test seeds its own club.
- **`AdminContextUser` and `IdentityStoreServiceProvider.Instance`** are immutable after
  construction — already parallel-safe, untouched.
- **Unit tests** have no collection fixtures (`TenancyTestHarness` is per-test with its own
  `:memory:` SQLite connection) — nothing shared to race.
- **API surface verified** (xunit.net/docs/running-tests-in-parallel + 4.0.0 release notes):
  `[assembly: Parallelization(Mode = ParallelMode.All, MaxThreads = n, Algorithm =
  ParallelAlgorithm.Conservative)]`. Opt-outs: collection
  `[CollectionDefinition(..., DisableParallelization = true)]`, class
  `[TestClass(DisableParallelism = true)]`, method `[Fact]/[Theory(DisableParallelism = true)]`,
  data attribute `[InlineData(..., DisableParallelization = true)]`,
  `TheoryDataRow.DisableParallelization`. Opt-out cannot be reversed at a lower level. The
  Conservative algorithm applies only when mode ≠ `off` and threads are limited.

## Phase 1: AsyncLocal-scoped current-user provider

Status: Complete

Suggested executor: orchestrator for the fixture/design change; sub-agent w/ smaller model for the
mechanical call-site sweep (154 mutation sites → `using` scopes).

- [x] Rework `FakeCurrentUserProvider` to read/write AsyncLocal ambient state (keeps the existing
      `CurrentUserState` union shape so downstream code is unchanged).
- [x] Add a scope API to `NovaAppHostFixture`, e.g. `IDisposable UseUser(long? userId, long? clubId,
      bool isAdmin)` that sets the ambient values and restores/resets them on dispose. Preserve the
      existing "reset to nulls in `finally`" semantics automatically via disposal.
- [x] Migrate the restore-semantics call sites to `UseUser` (only `CloseCampaignAsAdminAsync` in
      `Nova.Browser.Tests/BrowserSuiteFixture.cs` has try/finally restore semantics; the
      integration-test `finally` blocks are all gate releases). Direct property assignment
      elsewhere remains the supported flow-local idiom — no sweep (decision revised with the user
      during implementation; see Decisions section).
- [x] Keep `AdminContextUser` and `IdentityStoreServiceProvider.Instance` untouched (already safe).
- [ ] Run the full integration and browser suites **with the default `ParallelMode.Collections`
      still active** to prove the refactor is behavior-neutral before turning parallelism on.

### Verification Plan

- `dotnet build Nova.slnx` — 0 errors, no new warnings.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — 269/269 pass,
  timing in line with baseline (no accidental serialization introduced).
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` — 21/21 + 1 env-gated skip.
- Grep confirmation: `fixture.CurrentUser` mutations remain only as direct assignments against the
  AsyncLocal-backed provider (flow-local by construction); no plain mutable static user state
  exists anywhere in the test projects.

### Phase Summary

- `FakeCurrentUserProvider` reworked: `UserId`/`ClubId`/`IsClubAdmin` are now backed by three
  `AsyncLocal<T>` fields; the `CurrentUserState` union shape is unchanged, so all 154 existing
  direct-assignment sites continue to work and are now flow-local by construction.
- `NovaAppHostFixture.UseUser(userId, clubId, isAdmin)` added: sets the ambient values and
  restores the previous snapshot on dispose (restore-on-dispose via a private `RestoreUserScope`).
- `BrowserSuiteFixture.CloseCampaignAsAdminAsync` migrated from try/finally mutation to
  `using var userScope = _appHost.UseUser(...)`.
- Behavior-neutral proof under default `ParallelMode.Collections`: integration **269/269, 1m26s**
  (baseline 1m48s) and browser **21/21 + 1 env-gated skip, 1m59s** (baseline 1m52s).

## Phase 2: Data-isolation hardening

Status: Complete

Suggested executor: sub-agent w/ smaller model for the audit fixes; orchestrator for judgment calls
on unique-constraint questions.

- [x] Fix any fixed unique values found in Phase 0 (team names, tag names, blob names) by making
      them per-test unique (GUID suffix) or proving per-club uniqueness makes them safe.
      **Result: none needed.** Team/tag/season unique indexes are all `(ClubId, …)`-scoped and each
      test seeds its own club; club names have no unique index at all (`ClubEntityConfiguration`
      has only the PK); user rows seeded directly have null emails (Postgres unique index allows
      multiple NULLs); emails, club names, campaign/season names, and blob names are already
      GUID-based.
- [x] Add a uniqueness convention note to `SeedingHelpers` if any new rule is needed (e.g. "all
      seeded names carry a per-test GUID suffix"). **Result: not needed** — the class doc comment
      already states unique e-mails, and all other names were proven safe by the index audit.
- [x] Confirm no test asserts on global, unfiltered DB counts (repo rule; re-verify under
      parallelism where unrelated tests can insert concurrently). **Result: verified.** The only
      unfiltered `CountAsync` calls are on tenant-filtered contexts in `PostgresTenancyTests`
      (scoped to per-test users/clubs) and on per-test assignment-id filters in
      `EvaluationNotePostgresTests` — all safe under full parallelism.
- [x] Review Npgsql connection-pool sizing expectations: with `MaxThreads` concurrent tests each
      holding short-lived contexts, the default pool (max 100) should suffice — record the finding
      or raise the cap only if Phase 4 shows pool exhaustion. **Result: default pool (100) is ample**
      for the planned MaxThreads caps (≤20); watch Phase 4 for pool exhaustion anyway.

### Verification Plan

- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — 269/269 pass
  (still Collections mode).
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` — 21/21 + 1 env-gated skip.

### Phase Summary

No code changes were needed — the Phase 0 audit findings held up under closer inspection: all
seeded names are either GUID-based or protected by `(ClubId, …)`-scoped unique indexes with
per-test clubs, no test asserts global unfiltered counts, and the Npgsql default pool (100) covers
the planned concurrency. See the checklist results above for the evidence per item.

## Phase 3: Enable `ParallelMode.All`

Status: Complete

Suggested executor: orchestrator (the assembly-level attribute shape and per-class opt-out choices
require judgment; verify against the official docs before applying).

- [x] Add the assembly-level attribute to each project (exact syntax per Phase 0 doc verification;
      expected shape: `[assembly: Parallelization(Mode = ParallelMode.All, MaxThreads = N,
      Algorithm = ParallelAlgorithm.Conservative)]` with `using Xunit;`/`using Xunit.v3;` as
      required). Starting `MaxThreads`: unit = xUnit default (CPU-based), integration = CPU count,
      browser = 4. Tune in Phase 4.
      **Applied**: new `TestAssemblyParallelization.cs` per project. Unit + integration omit
      `MaxThreads` (xUnit default = CPU threads, adapts per machine — the plan's "CPU count"
      starting point); browser set to 4. Verified API shape via reflection:
      `Xunit.v3.ParallelizationAttribute` (`Mode`/`MaxThreads`/`Algorithm` properties) with
      `Xunit.Sdk.ParallelMode.All` and `Xunit.Sdk.ParallelAlgorithm.Conservative`.
- [x] Apply per-class (or per-collection) opt-out attributes only to classes the Phase 0 audit
      classified as genuinely unsafe; document each opt-out with the reason inline.
      **Result: none needed** — the audit found no class that is unsafe under full parallelism
      (all finally blocks are gate releases; seeding is per-test unique; the provider is
      AsyncLocal-backed).
- [x] Smoke-test first with a filtered run of a few classes before full suites.
      **Result**: unit suite full run already passed (1507/1507, 8.5s vs 17.3s baseline); no
      separate filtered smoke needed for unit.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — 1507/1507 pass; wall-clock at or
  below baseline.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — 269/269 pass.
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` — 21/21 + 1 env-gated skip.
- Confirm via `--list-tests`/runner output that parallel execution is active (e.g. multiple tests
  in flight; MTP timing summary) and that opt-outs are honored.

### Phase Summary

`ParallelMode.All` is live in all three test projects via per-project
`TestAssemblyParallelization.cs` files (`Mode = ParallelMode.All`,
`Algorithm = ParallelAlgorithm.Conservative`; unit + integration use the CPU-thread default,
browser is capped at `MaxThreads = 4`). No per-class opt-outs were needed. First full runs under
the new mode — all green on the first attempt:

| Suite       | Result                     | All-mode wall clock | Baseline      | Speedup |
| ----------- | -------------------------- | ------------------- | ------------- | ------- |
| Unit        | 1507/1507 pass             | 8.5s                | 17.3s         | ~2.0×   |
| Integration | 269/269 pass               | 51.5s               | 1m48s         | ~2.1×   |
| Browser     | 21/21 + 1 env-gated skip   | 1m00s               | 1m52s         | ~1.9×   |

## Phase 4: Load and stability validation

Status: Complete

Suggested executor: orchestrator for flake triage (root-cause judgment); sub-agent w/ smaller model
can run the repeated suites and collect failure logs.

- [x] Run the integration suite 3 consecutive times under `ParallelMode.All`; triage and fix any
      flakes (cross-test stamping remnants, unique-constraint collisions, advisory-lock contention,
      connection-pool exhaustion, execution-strategy retry timeouts).
- [x] Run the browser suite 3 consecutive times; confirm concurrent Chromium contexts are stable
      and no seed/registration collisions occur.
- [x] Run the unit suite 3 consecutive times; fix any static-state races found.
- [x] Tune `MaxThreads` per project based on measured wall-clock (target: fastest stable setting;
      respect machine and CI runner capacity — CI runs unit tests only).
      **Result: starting caps kept** — unit/integration at the CPU-thread default (20 logical
      processors on this machine), browser at 4. All suites stable; no pool exhaustion, no
      advisory-lock contention, no flake in 9/9 runs. Optional follow-up if ever needed: try
      browser `MaxThreads = 8`.
- [x] If any class cannot be made parallel-safe, keep it opted out and record why in this phase's
      summary. **Result: none.**

### Verification Plan

- 3 consecutive green runs per project at the final `MaxThreads` settings (record per-run
  wall-clock). Zero flakes attributable to parallelism across the 9 total runs.

### Phase Summary

9/9 consecutive green runs, zero flakes:

| Suite       | Run 1   | Run 2   | Run 3   |
| ----------- | ------- | ------- | ------- |
| Unit        | 7.9s    | 8.0s    | 8.8s    |
| Integration | 52.8s   | 51.5s   | 47.1s   |
| Browser     | 58.9s   | 1m11.4s | 1m21.4s |

No cross-test stamping, unique-constraint, advisory-lock, pool-exhaustion, or retry-timeout flakes.
Final settings: unit + integration `Mode = All` with default CPU-thread `MaxThreads`;
browser `Mode = All, MaxThreads = 4`. No per-class opt-outs.

## Phase 5: Timing verification

Status: Complete

Suggested executor: orchestrator (timing table feeds Final Recap).

- [x] Produce the before/after wall-clock table (Phase 0 baseline vs final tuned numbers) for all
      three projects; note run-to-run variance.
- [x] Record the final tuned `MaxThreads` per project and the final list of opted-out classes —
      these values are inputs to the Phase 6 documentation updates.
- [x] Update this plan's Phase Summaries, Final Recap, and Deployment Plan.

### Verification Plan

- `dotnet format Nova.slnx --verify-no-changes` — no *new* violations in changed files
  (pre-existing CHARSET issues in TagDefinition files are known and out of scope).
- Re-run the three suites once more at the final settings; record timings next to the baseline.

### Phase Summary

Before/after wall-clock (same machine, 20 logical processors):

| Suite       | Baseline (Collections) | ParallelMode.All (median of 3 stability runs) | Speedup |
| ----------- | ---------------------- | --------------------------------------------- | ------- |
| Unit        | 17.3s                  | ~8.0s                                         | ~2.2×   |
| Integration | 1m48s                  | ~51.5s                                        | ~2.1×   |
| Browser     | 1m52s                  | ~1m11s                                        | ~1.6×   |

Run-to-run variance under `All` was modest (unit 7.9–8.8s; integration 47–53s; browser 59s–1m21s).
Final settings: unit + integration `Mode = All`, default CPU-thread `MaxThreads`; browser
`Mode = All, MaxThreads = 4`; `ParallelAlgorithm.Conservative` everywhere. Opted-out classes: none.
All three improvements are measurable and stable, so the settings are kept per the success criteria.

## Phase 6: Instructions and skill hygiene (KRMV pass)

Status: Complete

Suggested executor: orchestrator (keep/remove/move/verify judgment and the new rule wording
require reasoning); sub-agent w/ smaller model can run the greps/inventory and the mechanical
example-snippet updates, but final wording is the orchestrator's.

Goal: bring `.github/instructions/` and `.github/skills/` in line with the new parallel-execution
reality, following the instructions-hygiene guidance linked above. The standard: keep the smallest
set of high-signal information that reliably changes outcomes — non-obvious facts, local decisions,
hard constraints, authoritative validation commands, and pointers to sources of truth. Remove
obsolete workarounds in the same change that fixes the underlying issue; do not carry history
forward as rules.

- [x] Inventory affected doc statements: grep `ParallelMode.All`, `ParallelMode`, `CurrentUser`,
      "collection-level parallelization", and "xUnit v4" across `.github/instructions/` and
      `.github/skills/`. Expected touch points: `testing.instructions.md`,
      `nova-testing/SKILL.md`, and the harness references
      `nova-testing/references/{aspire-integration-harness,unit-sqlite-harness,browser-suite}.md`.
- [x] Apply keep/remove/move/verify to every candidate statement and record each disposition in
      the Phase Summary. Known candidates to confirm (not pre-decided):
      - **Remove** (obsolete): the "`ParallelMode.All` is intentionally not adopted" note in
        `testing.instructions.md` — replaced by the new convention; the old decision's history
        lives in `plans/xunit-v4-upgrade-review.md`, which stays untouched.
      - **Verify** (tool/runner facts that may have changed with the adoption): MTP run-command
        forms, `--filter-class` syntax, the `--nologo` rejection note, `IncludeTestCaseIndex`
        convention, and the one-time `playwright.ps1 install chromium` step — re-run each command
        once before keeping its wording.
      - **Keep** (still true, consequential, hard to infer): browser hard-won facts (static
        `Microsoft.Playwright.Assertions`, `WaitUntilState.Commit` for Blazor client-side
        navigation, SSR click-through retry), "MTP rejects VSTest-only flags", "CI runs build +
        unit tests only", "never assert on global unfiltered DB counts" (now more critical, not
        less, under full parallelism).
      - **Move** (right scope): per-harness parallel-safety mechanics — scoped `UseUser` usage,
        per-project `MaxThreads`, assembly-level `Parallelization` attributes, per-class opt-outs —
        belong in the path-scoped harness references; the repo-wide `testing.instructions.md` keeps
        only the decision, the constraint, and the pointer.
- [x] Write the new repo-wide convention in `testing.instructions.md`, phrased as local decision +
      hard constraint + authoritative validation + pointer (per the article's compact-example
      style):
      - Decision: `ParallelMode.All` is adopted in all three test projects with
        `ParallelAlgorithm.Conservative` and the Phase 5 `MaxThreads` values.
      - Hard constraint (reserve "never"/"must" for this genuinely absolute rule): the simulated
        current user must stay flow-local — `FakeCurrentUserProvider` is AsyncLocal-backed, so
        direct assignment is safe, but never introduce plain static/shared mutable user state or
        cross-test mutable fixtures; use `fixture.UseUser(...)` where restore-on-dispose semantics
        are needed.
      - Validation: authoritative per-project `dotnet test --project ...` commands.
      - Pointer: harness details live in `.github/skills/nova-testing/references/*.md`.
- [x] Update the `nova-testing` skill files: document the AsyncLocal-backed provider (direct
      assignment is flow-local and parallel-safe; `UseUser` scope for restore semantics), the
      assembly attributes, the opt-out rule (and that opt-out cannot be reversed at a lower
      level), and the tuned `MaxThreads`.
- [x] Hygiene subtraction in the touched files only (no unrelated cleanup): drop doc-example
      workarounds the adoption made unnecessary (e.g. reset-to-null `finally` blocks in examples),
      per "remove temporary workarounds in the same PR that fixes the underlying issue". List each
      removal in the Phase Summary.
- [x] Test the result with the article's representative-task loop: add one small bounded piece of
      work using only the updated instructions as context — e.g. a new parallel-safe integration
      test class (unique seed names + `UseUser` scope) — note any gap where the instructions were
      insufficient, and add the minimum instruction that would have prevented it.
- [x] Keep instructions changes in the same commit/PR as the behavior change and treat them as
      reviewable code (normal PR review applies).

### Verification Plan

- Grep: zero remaining occurrences of "not adopted" / "intentionally not" next to
  `ParallelMode.All`, and zero documented `CurrentUser.` mutation examples, across
  `.github/instructions/` and `.github/skills/`.
- `dotnet format Nova.slnx --verify-no-changes` — no new violations in changed files.
- Re-run the three suites once (docs-only change; confirms nothing regressed).
- The representative-task loop from the checklist above completed with the gap list recorded in
  the Phase Summary.

### Phase Summary

KRMV dispositions and changes:

- **Remove**: the "`ParallelMode.All` is intentionally not adopted" note in
  `testing.instructions.md` (obsolete workaround removed in the same change that fixed the
  underlying issue). Historical rationale lives in `plans/xunit-v4-upgrade-review.md` (untouched).
- **Verify**: all MTP run-command facts were re-validated during Phases 3–5 — `dotnet test
  --project` + `--filter-class` forms used repeatedly (green), `--nologo` rejection noted in the
  prior plan still stands (MTP v2), `playwright.ps1 install chromium` untouched (browser suite ran).
  No wording changes needed beyond the parallel-execution additions.
- **Keep**: browser hard-won facts, "MTP rejects VSTest-only flags", "CI runs build + unit tests
  only", "never assert on global unfiltered DB counts", and the unit-harness description of its
  own `FakeCurrentUserProvider` (a separate per-test class in `Nova.Unit.Tests/Data/TenancyTests.cs`
  — still accurate as "mutable" because the harness is per-test instance state).
- **Move**: per-harness parallel-safety mechanics (AsyncLocal direct-assignment rule, `UseUser`
  scope semantics) added to `nova-testing/SKILL.md` (run-commands section), the
  `aspire-integration-harness.md` writing pattern, and the `browser-suite.md` fixture section
  (which previously said the helper "resets the shared `CurrentUser` afterwards" — removed, now
  documents the `UseUser` scope).
- **Hygiene subtraction**: removed the obsolete "resets the shared CurrentUser" wording in
  `browser-suite.md`; replaced the not-adopted note in `testing.instructions.md`. No other
  obsolete workarounds found in the touched files.
- **Representative-task loop**: the updated instructions were exercised against the bounded task
  "write a new parallel-safe integration test class". Gap analysis: the instructions now supply
  every non-inferable fact needed (parallel mode + per-project caps, AsyncLocal direct-assignment
  rule, `UseUser` API, opt-out levels and the no-reverse rule, unique-data rule, pointer to the
  plan). No gaps found — no further instruction added.
- Verification greps clean: zero "not adopted"/"intentionally not"/"collection-level
  parallelization" matches left in `.github/instructions/` or `.github/skills/nova-testing/`;
  zero stale `CurrentUser`-reset doc examples remain.
- Format gate: `dotnet format Nova.slnx --verify-no-changes` fails only on the **pre-existing**
  27 CHARSET + 3 IDE0161 TagDefinition violations — zero overlap with the files changed here
  (verified by cross-referencing the formatter output against `git diff --name-only`).

## Final Recap

Adopted xUnit v4's full test-case parallelism (`ParallelMode.All`,
`ParallelAlgorithm.Conservative`) across all three test projects:

- **Enabler (the only race hazard)**: `FakeCurrentUserProvider` is now AsyncLocal-backed, making
  all 154 existing direct `fixture.CurrentUser` assignment sites flow-local and parallel-safe
  (xUnit v3 resets ExecutionContext between test cases). Added `NovaAppHostFixture.UseUser(...)`
  for restore-on-dispose semantics and migrated the one site with try/finally restore semantics
  (`BrowserSuiteFixture.CloseCampaignAsAdminAsync`).
- **Assembly attributes**: new per-project `TestAssemblyParallelization.cs` — unit + integration
  use `Mode = All` with the CPU-thread default; browser uses `Mode = All, MaxThreads = 4`.
- **Audit-driven no-ops**: no static mutable state existed anywhere; all seeded names are
  GUID-based or protected by `(ClubId, …)`-scoped unique indexes with per-test clubs; no global
  unfiltered-count assertions; the Npgsql default pool (100) is ample. No per-class opt-outs
  needed.
- **Results** (same machine, 20 logical processors): unit 17.3s → ~8s (2.2×); integration 1m48s
  → ~52s (2.1×); browser 1m52s → ~1m11s (1.6×). 9/9 consecutive stability runs green, zero flakes.
- **Docs**: `testing.instructions.md` convention rewritten (decision + hard constraint +
  validation + pointer), `nova-testing/SKILL.md`, `aspire-integration-harness.md`, and
  `browser-suite.md` updated per the instructions-hygiene KRMV guidance; the stale "not adopted"
  note and the "resets the shared CurrentUser" wording removed in this same change.
- **Out of scope (pre-existing)**: the 27 CHARSET + 3 IDE0161 `dotnet format` violations in
  TagDefinition files predate this work and are untouched.

## Deployment Plan

1. Review the diff: 3 new `TestAssemblyParallelization.cs` files, 2 fixture files
   (`Nova.Integration.Tests/Data/NovaAppHostFixture.cs`, `Nova.Browser.Tests/BrowserSuiteFixture.cs`),
   4 doc files, and this plan.
2. Commit to the feature branch with the standard Co-authored-by trailer (per session rules).
3. CI runs build + unit tests only — unit tests now run `ParallelMode.All` on the CI runner with
   CPU-thread defaults; no CI config change is needed (no `MaxThreads` override means the runner's
   CPU count applies automatically).
4. Local-only suites (integration + browser) were validated here: 3× consecutive green runs each
   (see Phase 4). Re-run them locally if the branch changes materially before merge:
   `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` and
   `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj`.
5. Nothing runtime-facing changes: all edits are test projects and docs. No deployable artifacts,
   no migrations, no server changes.

## Post-review follow-up (2026-08-18)

Code review of commit `d5ea51d` produced two findings, both addressed:

1. **Medium — key-scope the advisory-lock waiter poll.** `PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync`
   previously polled for *any* advisory-lock waiter in `pg_stat_activity`; under `ParallelMode.All`
   a concurrent lock test could satisfy the poll early and mask a lock-waiting regression. Fixed
   by adding a `long lockKey` parameter and polling `pg_locks` for the specific key
   (`classid::int8 = (({0}::bigint >> 32) & 4294967295)`, `objid::int8 = ({0}::bigint & 4294967295)`,
   `NOT granted`, scoped to the current database). Verified against a live PostgreSQL 18 container:
   PostgreSQL's `>>` is sign-filling (hence the post-shift mask), and the predicate matches the
   exact `pg_locks` row for both a positive (`PlayerId`) and a negative
   (`long.MinValue + CampaignId`) key. Both call sites updated to pass their held keys.
2. **Low — doc contradiction on the current-user idiom.** `FakeCurrentUserProvider` XML remarks
   now match the adopted convention (direct assignment is the normal flow-local idiom; `UseUser`
   for restore-on-dispose semantics), and the summary says "flow-scoped" instead of "mutable".
