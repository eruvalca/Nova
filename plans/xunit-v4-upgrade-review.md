# xUnit v4 (xunit.v3 4.0.0) Upgrade Review and Adoption

Review the upgrade of `xunit.v3.mtp-v2` from 3.2.2 to 4.0.0 across the three MTP test
projects (`Nova.Unit.Tests`, `Nova.Integration.Tests`, `Nova.Browser.Tests`), apply
the agreed adoption items (explicit `xunit.analyzers` pin, `IncludeTestCaseIndex` on
theories, doc updates), and document the features deliberately not adopted.

Decisions made with the user (2026-08-18):

- Keep the default parallel mode (`ParallelMode.Collections`); do **not** adopt
  `ParallelMode.All`.
- Update repo docs/instructions that say "xUnit v3" to xUnit v4.
- Pin `xunit.analyzers` 2.0.0 explicitly in all three test projects.
- Add `IncludeTestCaseIndex = true` to **all 112** `[Theory]` attributes (47 files).

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

## Phase 1: Baseline verification

Status: Complete

- [x] Build the solution with the 4.0.0 package versions already in the working tree
- [x] Run the unit test suite

### Verification Plan

- `dotnet build Nova.slnx` — expect success (only the pre-existing ASPIRE010 warning
  from the Aspire 13.5.0 AppHost update).
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — expect all tests pass.

### Phase Summary

Build succeeded with 0 errors (1 pre-existing ASPIRE010 warning unrelated to xUnit).
Unit tests: 1507/1507 passed in ~17s. No xunit.analyzers 2.0.0 diagnostics appeared
(2.0.0 already resolves transitively via `xunit.v3.mtp-v2` 4.0.0).

Gotcha re-confirmed: passing `--nologo` to `dotnet test` (MTP mode) yields
"Zero tests ran" with exit code 5. MTP rejects VSTest-only flags — matches the
existing rule in `.github/instructions/testing.instructions.md`.

## Phase 2: Breaking-change audit (analysis only — no code changes)

Status: Complete

- [x] Review the 4.0.0 release notes (core framework, assertion library, runners, MTP)
- [x] Grep the three test projects for every obsoleted/broken API surface
- [x] Check config files (`xunit.runner.json`, `testconfig.json`), assembly attributes, and CI flags

### Verification Plan

- Greps documented below; authoritative release notes:
  https://xunit.net/releases/v3/4.0.0

### Phase Summary

**No breaking-change impact on this codebase.** Findings:

- *Extensibility APIs* (the bulk of the breaking changes: runner contexts,
  orderers, comparers, `TestClassRunner`, `ExtensibilityPointFactory`,
  `TransformFactory`/XSL-T, etc.): the repo implements **no custom xUnit
  extensibility types** — no custom `DataAttribute`/`FactAttribute`/
  `BeforeAfterTestAttribute`/orderers/frameworks. Zero impact.
- *`[assembly: CollectionBehavior]`* (its parallelization members are now
  obsolete/un-callable): not used anywhere. `[Collection]` /
  `[CollectionDefinition]` (heavily used in integration + browser suites) are
  **not** affected.
- *`ParallelizeTestCollections` → `ParallelMode`* renames: repo sets no
  parallelization options anywhere (no config file, no assembly attribute) —
  default (`collections`) is preserved. Zero impact.
- *Microsoft Testing Platform v1 dropped*: repo was already MTP v2
  (`Microsoft.Testing.Extensions.TrxReport` 2.3.3 matches the new MTP v2 line).
  Zero impact.
- *MTP report switch renames* (`-report-ctrf` → `-report-xunit-ctrf`, etc.):
  CI (`ci.yml`) and all repo docs use none of these switches; TrxReport and
  CodeCoverage are Microsoft extensions, not xUnit report switches. Zero impact.
- *Attachments output folder change* (now the results directory): no test uses
  `TestContext` attachments. Zero impact.
- *`TestContext.Current.CancellationToken` after context disposal*: v4 bug fix
  (returns default instead of throwing) — behavior improvement, no action.
- *`SkipUnless`/`SkipWhen`*: not used. `Assert.Skip` used once (browser suite) —
  unaffected.

Conclusion: the 3.2.2 → 4.0.0 upgrade is drop-in for this repo (proven by Phase 1).

## Phase 3: Adoption changes

Status: Complete

Suggested executor: sub-agent w/ smaller model for the mechanical `[Theory]`
attribute sweep (item 3); orchestrator for items 1, 2, 4, and the verification runs.

- [x] Add `<PackageVersion Include="xunit.analyzers" Version="2.0.0" />` to `Directory.Packages.props`
      (alphabetical order, alongside the other test packages; version managed centrally per repo convention).
- [x] Add explicit `<PackageReference Include="xunit.analyzers" PrivateAssets="all" />` to
      `Nova.Unit.Tests/Nova.Unit.Tests.csproj`, `Nova.Integration.Tests/Nova.Integration.Tests.csproj`,
      and `Nova.Browser.Tests/Nova.Browser.Tests.csproj` (makes the already-transitive analyzer
      dependency explicit and pinned).
- [x] Mechanical sweep: replace bare `[Theory]` with `[Theory(IncludeTestCaseIndex = true)]`
      in all **112 sites across 47 files** in the three test projects (exact token
      replacement `[Theory]` → `[Theory(IncludeTestCaseIndex = true)]`; the repo uses no
      `[Theory(...)]` variants and no `CulturedTheory`/`CulturedFact`, so no merge logic is needed).
      This adds zero-padded `_001`, `_002` suffixes to each data row's display name so failing
      rows map directly to `[InlineData]`/`[MemberData]`/`[ClassData]` entries
      (xunit/xunit#3472, implemented in #3523).
- [x] Update docs from "xUnit v3" to "xUnit v4":
      - `.github/instructions/testing.instructions.md` — lines 14, 36, and 93
        ("xUnit v3 on Microsoft.Testing.Platform (MTP)", "xUnit v3/MTP setup",
        "xUnit v3: fixtures implement..."), and add a convention bullet:
        "Theories use `[Theory(IncludeTestCaseIndex = true)]` so failing data rows
        are identifiable by their `_NNN` display-name suffix."
      - `.github/skills/nova-testing/SKILL.md` — line 42 ("xUnit v3 on MTP").
      - `.github/skills/nova-testing/references/unit-sqlite-harness.md` — line 63.
      - `.github/skills/nova-testing/references/aspire-integration-harness.md` — line 125.
      - Do **not** edit historical `plans/*.md` files (records of past work).
- [x] Add a short "xUnit v4" note to `testing.instructions.md` listing the new
      assertion APIs available for future tests: `Assert.All`/`Assert.AllAsync(strict: true)`,
      per-test `Assert.OverrideMax*` formatting controls, and the fixture lifecycle
      notification interfaces (`INotifyTestCollectionLifecycle` etc.), plus the documented
      `ParallelMode.All` non-adoption rationale.

### Verification Plan

- `dotnet build Nova.slnx` — 0 errors, 0 new warnings (xunit.analyzers 2.0.0 must add none).
- `dotnet format Nova.slnx --verify-no-changes` — passes.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — 1507/1507 pass.
- Spot-check the new display names:
  `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --list-tests` —
  theory rows must show `_001`, `_002` suffixes (e.g. pick a class with InlineData
  rows and grep the output).
- Before merge: `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj`
  (requires the Aspire AppHost) and
  `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` — display-name
  changes are not expected to affect either suite, but run them per repo policy
  (CI covers build + unit tests only).

### Phase Summary

All adoption changes applied:
- `Directory.Packages.props`: added `xunit.analyzers` 2.0.0 (central, alphabetical).
- All three test csprojs: explicit `xunit.analyzers` `PrivateAssets="all"` reference.
- Swept exactly 112 `[Theory]` → `[Theory(IncludeTestCaseIndex = true)]` across 47 files.
  Executor gotcha: the first PowerShell sweep rewrote 183 files (no BOM preservation).
  Reverted test projects via `git checkout` and re-swept only the 47 files containing
  `[Theory]`, preserving each file's original BOM state (repo mixes BOM/no-BOM files;
  keep as-is). Confirmed via byte comparison + `git diff --numstat`.
- Docs: `testing.instructions.md` (v4 wording ×3, `IncludeTestCaseIndex` convention
  bullet, v4 API note + `ParallelMode.All` non-adoption rationale),
  `nova-testing/SKILL.md`, and both harness reference files updated. Historical
  `plans/*.md` untouched.

Verification results:
- Build: success, 0 errors (pre-existing ASPIRE010 warning only; xunit.analyzers 2.0.0
  produced zero new diagnostics).
- Format: `dotnet format --verify-no-changes` fails — **27 pre-existing CHARSET
  (encoding) violations** in the committed TagDefinition feature files
  (`Nova.Client/Services/Tags/*`, `Nova.Shared/Features/Tags/*`,
  `Nova.UI/Features/Tags/*`, `Nova/Features/Tags/*`, `Nova/Extensions/Tags/*`,
  `Nova/Features/Shared/CommitAttemptTracker.cs`, 3 recent migrations, and their
  tests) plus 3 IDE0161 warnings on those migrations. **Zero overlap with files
  changed by this work** (verified by cross-referencing the formatter output against
  `git diff --name-only`). Out of scope here; flag to the user separately.
- Unit tests: 1507/1507 pass (~53s).
- Theory index spot check (`--list-tests`): display names show `_001`, `_002`, ...
  suffixes (e.g. `NotWhitespaceAttributeTests.IsValid_WithWhitespaceOnly_ReturnsFalse_001(value: "   ")`).
- Integration tests: first run 268/269 with one AppHost resource health-check failure
  during startup (infra flake); re-run 269/269 pass (8m31s).
- Browser suite: 21/21 pass + 1 env-gated skip (`NOVA_A11Y_SCREENSHOTS` unset) in 2m48s.

## Non-adoptions and rationale (documented decisions)

- **`ParallelMode.All`** (full test parallelization): not adopted. Default
  `ParallelMode.Collections` is unchanged in v4 and already parallelizes across
  collections. Enabling `All` would run tests in parallel *within* classes sharing
  mutable harness state: the SQLite `TenancyTestHarness` shares one `:memory:`
  connection + mutable `FakeCurrentUserProvider`, `IdentityStoreServiceProvider` is
  static, and the integration/browser suites share one Postgres/apphost collection.
  If ever revisited: audit shared state, then set
  `[assembly: Parallelization(Mode = ParallelMode.All)]` and opt out per
  class/method with the new `[NonParallelizable]` attribute layers (opt-out cannot
  be reversed at a lower layer).
- **Native AOT testing** (`xunit.v3.core.aot` packages): not adopted. Infeasible for
  these suites — bUnit, NSubstitute (dynamic proxies), EF Core + SQLite in-memory,
  `Aspire.Hosting.Testing` app-host fixtures, and Playwright are not AOT-compatible.
- **Fixture lifecycle notification interfaces** (`INotifyTestCollectionLifecycle`
  etc.): noted as a future opportunity (e.g. a `NovaAppHostFixture` reset-before-
  each-test hook, since `BeforeAfterTestAttribute` is sync-only). No action now.
- **New MTP filters/reports** (`--filter-display-name`, `--xunit-list`,
  `-report-xunit-*` renames): available if ever needed; CI needs none.
- **`xunit-console-tool` .NET tool**: not needed; the repo runs tests via `dotnet test` (MTP).
- **`shutdownForegroundThreadWaitSeconds` / `testconfig.json`**: repo has no config
  file and no foreground-thread issues; not needed.

## Final Recap

Upgraded `xunit.v3.mtp-v2` 3.2.2 → 4.0.0 (plus Aspire 13.5.0 and
`Microsoft.Testing.Extensions.CodeCoverage` 18.10.0 already in the working tree).
Audit conclusion: **no breaking changes affect this repo** — it implements no custom
xUnit extensibility, uses no obsoleted configuration attributes, and was already on
MTP v2. Adopted three low-risk improvements: explicit `xunit.analyzers` 2.0.0 pin,
`[Theory(IncludeTestCaseIndex = true)]` on all 112 theories (row-identifiable
display names), and doc updates from xUnit v3 → v4 with a convention bullet.
Deliberately did not adopt `ParallelMode.All` (shared mutable harness/database state
would race) or Native AOT testing (incompatible with bUnit/NSubstitute/Aspire/EF
SQLite). Full verification: build clean, 1507/1507 unit tests, 269/269 integration
tests, 21/21 browser tests (1 env-gated skip).

## Deployment Plan

1. Review the diff (`git diff`) — 47 theory files, 3 test csprojs,
   `Directory.Packages.props`, 4 doc files, plus this plan.
2. Nothing runtime-facing changes: all edits are test projects, docs, and test
   package metadata. No deployable artifacts, no migrations, no server changes.
3. Commit when the user asks (not automatic), with the standard Co-authored-by
   trailer. CI runs build + unit tests; integration and browser suites were run
   locally (see Phase 3 results).
4. Note for the user: `dotnet format Nova.slnx --verify-no-changes` currently fails
   on **pre-existing** CHARSET/IDE0161 issues in committed TagDefinition files —
   unrelated to this work; fix separately if the format gate matters for this PR.
