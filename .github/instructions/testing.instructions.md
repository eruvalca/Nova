---
applyTo: "Nova.Unit.Tests/**,Nova.Integration.Tests/**,Nova.Browser.Tests/**"
description: "Testing rules: project and harness selection, HTTP/UI boundary coverage, MTP commands, browser suite conventions, and core test conventions."
---

# Testing Rules

> Declarative rules only. For the **harness internals and step-by-step workflow** (SQLite
> `TenancyTestHarness`, Aspire `NovaAppHostFixture`, HTTP e2e bootstrap), use the **`nova-testing`**
> skill (`.agents/skills/nova-testing/`).

## Which project

All three test projects use **xUnit v4 on Microsoft.Testing.Platform (MTP)** with **Shouldly** assertions.

| Test shape    | Project                  | Database                                    | Use for                                                                                                                                                |
| ------------- | ------------------------ | ------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Pure policy   | `Nova.Unit.Tests`        | None                                        | Deterministic business decisions over constructed immutable facts; no harness, DI, mocks, or logger                                                    |
| Service shell | `Nova.Unit.Tests`        | Shared in-memory SQLite (`EnsureCreated()`) | Query-filter composition, interceptor branching, authorization, tenancy, effects, OneOf state                                                          |
| HTTP boundary | `Nova.Integration.Tests` | Real app through the Aspire AppHost | Route registration, middleware, policy enforcement, binding, response metadata/serialization, and usable Location headers |
| Provider/race | `Nova.Integration.Tests` | Real PostgreSQL 18 via the Aspire AppHost   | Production migrations, mappings, constraints, advisory locks, transaction races, execution-strategy retries, ambiguous commits, filter SQL translation |
| Browser flow  | `Nova.Browser.Tests`     | Real app via the Aspire AppHost + Playwright Chromium | Interactive UI flows that cross the server boundary: multi-user/role behavior, lifecycle conflicts, URL/history state, responsive layouts, keyboard/focus, and contrast/touch-target checks |

**Default new tests to `Nova.Unit.Tests`.** Use integration tests for the real HTTP boundary or
provider behavior (type mappings, migrations, constraints, advisory locks, transaction races,
execution-strategy retries, ambiguous commits, SQL translation, collation). SQLite will not catch
`timestamptz` offsets, identity-column semantics, collation, advisory-lock behavior, provider retry
semantics, or SQL-translation limits.

## Run commands

- Use the explicit MTP form: `dotnet test --project <project>`.
- Prefer the project path form that includes `--project`, for example:
    - `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj`
    - `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignParticipantHttpTests"`
    - `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj`
- Bare invocation such as `dotnet test <project>.csproj` can fail to discover tests in this xUnit v4/MTP setup, so avoid it in repo instructions and scripts.
- **Do NOT pass VSTest-only flags** (`--nologo`, `--collect`, `--logger`) — MTP rejects them.
- Filter by class with `--filter-class "*Name"`.
- Follow the authoritative verification commands and PR gate in `AGENTS.md`. Development checks
  use the affected tests; PR/merge verification includes all three suites. A successful selected
  run proves execution, not coverage of a new behavior. The proposed full CI job is authoritative
  only after its hosted-runner proof and required-check configuration are verified; until then,
  retain local integration/browser evidence and do not infer their success from other green jobs.
- Broad test-generation workflows may create `.testagent/` as temporary local state. The directory is
  gitignored and must not be committed; durable evidence belongs in the tests and the PR validation
  summary.

## Browser suite

Browser-specific hydration, navigation, accessibility, and seeding constraints live in
`.github/instructions/browser-testing.instructions.md`; load it for browser work. The shared
AppHost and HTTP helper rules also appear in the integration/browser skill references.

## Aspire + Playwright validation (manual browser pass)

Use only when a ticket explicitly requires browser-level behavior validation (interactive auth, UI mutation controls, or flow-level UX that unit/integration tests cannot prove). For the procedure, use the **`aspire-playwright-validation`** skill.

Route first: if the flow should be repeatable regression coverage, add a `Nova.Browser.Tests` scenario instead — this manual pass is for one-off acceptance passes and exploratory checks the suite does not cover.

Rules: never guess the frontend URL (always read it from `aspire describe --format Json`); keep the pass focused and scenario-based (admin happy path + read-only role checks); clean temporary browser artifacts from repo paths afterward.

## Conventions

- For recoverable commands, contextual form validation, or async state whose user/club/permission
  can change, map the relevant transitions to tests before implementation. Use
  `.agents/skills/add-feature-slice/references/stateful-transitions.md`; ordinary CRUD does not
  require a transition document.
- When fixing a behavioral finding, reproduce it, inspect sibling entry points/consumers, and
  protect both the fix and a neighboring valid path. Use
  `.agents/skills/nova-testing/references/review-and-finding-closure.md` for bounded closure and
  an independent review brief. Passing the whole suite does not substitute for reproducing the miss.
- One behavior per test; name `Subject_Outcome_Condition` (e.g. `Interceptor_Throws_OnCrossTenantAdd`).
  Use Shouldly (`ShouldBe`, `Should.Throw<T>`) and `[Theory]`/`[InlineData]` for case matrices.
  Theories use `[Theory(IncludeTestCaseIndex = true)]` (xUnit v4) so a failing data row is
  identifiable by its zero-padded `_NNN` display-name suffix.
- Test pure policies directly with real policy types and constructed values. Do not use a database
  harness, DI, mocks, or substitute policy implementations; use `[Theory]` for tabular rule matrices.
- Prefer `Xunit.TestContext.Current.CancellationToken` over `CancellationToken.None` whenever the async
  API accepts a token; otherwise leave the call as-is rather than forcing refactors.
- xUnit v4: fixtures implement `IAsyncLifetime` with `ValueTask`; test classes receive fixtures via
  primary-constructor injection.
- When adding a tenant-owned entity (`ITenantOwnedEntity`), add unit filter coverage: visible to its
  club, invisible to another club, cross-tenant writes rejected. Bespoke-filtered entities
  (`ClubJoinRequestEntity`, `NovaUserEntity`, `NovaUserPhotoEntity`) need one test per visibility rule.
- Never assert on global, unfiltered counts in integration tests (the database is shared across the
  collection — each test seeds its own data with database-generated ids).
- Prefer explicit seed helpers for entities with database-enforced lifecycle constraints. If a
  compatibility normalizer is needed for older direct seeds, it must be bypassable; provider
  constraint tests must use the unnormalized context so deliberately invalid state reaches the
  database unchanged.
- For every new HTTP endpoint, add boundary coverage for route registration, auth policy behavior, success serialization, and each declared ProblemDetails shape that cannot be proven by service or client unit tests. Keep provider-specific assertions separate.
- When a query requirement promises an exact asynchronous relational reader count or no N+1 reader
  queries, assert `ReaderExecutionCount` with `CountingCommandInterceptor`; context-factory
  invocations are not reader-command evidence. The interceptor does not observe synchronous,
  scalar, or non-query commands, so do not use it to claim an exact total SQL-command count.
- Exercise every route independently; prove the least-privileged role (a creator or admin does not establish ordinary-member access). Test independent query-validation paths separately.
- For clients validating success bodies: cover a populated payload, explicit nested nulls, malformed JSON, invalid ID/date/count relationships, shared-bound violations, and incorrect ordering. Use exact expected counts when proving lifecycle or tenant exclusion.
- For `CreatedAtRoute`, assert `201 Created`, the exact `Location`, and a successful GET after
  following it. Route metadata alone cannot prove the generated URL is usable.
- For uniqueness-probe patterns, add a PostgreSQL race test that commits a conflicting row through an independent context after the probe, asserting the unique constraint is the final guard and the exception maps to `Conflict`.
- For interactive pages with event handlers, include a render-mode assertion or a focused Aspire/Playwright scenario; bUnit can invoke callbacks even when the deployed page would render as static SSR.
- Build culture-sensitive expected display strings (dates, numbers, currencies) with the same explicit culture the component uses. Do not hard-code an English rendering unless the product contract fixes that culture.
- bunit and NSubstitute are available in the unit and integration test projects for component/service tests; the browser suite does not use them.
- xUnit v4 additions available for future tests: `Assert.All`/`Assert.AllAsync(strict: true)` (fail on
  empty collections), per-test `Assert.OverrideMax*` message-formatting overrides, and fixture
  lifecycle notification interfaces (`INotifyTestCollectionLifecycle` and friends).
- **Parallel execution**: all three test projects run `ParallelMode.All` via per-project
  `TestAssemblyParallelization.cs`. Unit uses `ParallelAlgorithm.Aggressive` at the CPU-thread
  default; integration and browser use `ParallelAlgorithm.Conservative` (integration at the
  CPU-thread default, browser capped at `MaxThreads = 4`). The simulated current user is
  AsyncLocal-backed, so direct `fixture.CurrentUser.X = ...` assignment is flow-local and
  parallel-safe — never introduce static/shared mutable user state. Use `fixture.UseUser(...)`
  when restore-on-dispose semantics are needed. Opting out of parallelism
  (`[TestClass(DisableParallelism = true)]`, `[Fact]/[Theory(DisableParallelism = true)]`, or a
  collection definition with `DisableParallelization = true`) is allowed only with an inline reason;
  opt-out cannot be reversed at a lower level.
  - **Do NOT switch integration or browser to Aggressive.** Every test in those suites seeds the
    shared PostgreSQL via a DbContext before its first await, and Aggressive *starts* every test
    case up front, so the shared connection pool is exhausted regardless of the `MaxThreads` cap
    (`Npgsql.PostgresException 53300 "sorry, too many clients already"`; ~63 integration / ~12-22
    browser failures). Conservative bounds how many tests START, keeping concurrent seeding within
    the pool. Unit has no database and is safe under Aggressive (in-memory SQLite).
- Do not pass `null` or `null!` for required mock dependencies; supply `Substitute.For<T>()` (or a real implementation) and `Array.Empty<T>()` for empty collections. Reserve nulls for tests that intentionally exercise nullable behavior.

## Related

- `.agents/skills/nova-testing/` — harness internals, the write/run workflow, `references/blazor-component-tests.md` for bUnit and render-mode assertions, and `references/browser-suite.md` for the browser workflow suite.
- `.github/instructions/functional-core.instructions.md` — policy boundary and layered test coverage.
- `Nova.Unit.Tests/Data/TenancyTests.cs`, `Nova.Integration.Tests/Data/NovaAppHostFixture.cs`, `Nova.Browser.Tests/CampaignEvaluationBrowserTests.cs`.
