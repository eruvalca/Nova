---
applyTo: "Nova.Unit.Tests/**,Nova.Integration.Tests/**,Nova.Browser.Tests/**"
description: "Testing rules: project and harness selection, HTTP/UI boundary coverage, MTP commands, browser suite conventions, and core test conventions."
---

# Testing Rules

> Declarative rules only. For the **harness internals and step-by-step workflow** (SQLite
> `TenancyTestHarness`, Aspire `NovaAppHostFixture`, HTTP e2e bootstrap), use the **`nova-testing`**
> skill (`.agents/skills/nova-testing/`).

## Behavior-based routing and evidence

- Read the rules for the production behavior under test even when the test's filename does not
  match their globs. Tests using EF, a context factory, or `TenancyTestHarness` must read
  `ef-core-tenancy.instructions.md`; database-free bUnit and pure-policy tests do not need it.
  HTTP/serialization tests need API rules; component tests need the applicable Blazor rules.
- Use the [transition evidence guide](../../.agents/skills/nova-testing/references/transition-evidence.md)
  for the changed behavior. Keep its results in the single validation record defined by
  [AGENTS.md](../../AGENTS.md#completion-and-review); the recipes provide boundary-specific mechanics.

## Which project

All three test projects use **xUnit v4 on Microsoft.Testing.Platform (MTP)** with **Shouldly** assertions.

| Test shape    | Project                  | Database                                    | Use for                                                                                                                                                |
| ------------- | ------------------------ | ------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Pure policy   | `Nova.Unit.Tests`        | None                                        | Deterministic business decisions over constructed immutable facts; no harness, DI, mocks, or logger                                                    |
| Service shell | `Nova.Unit.Tests`        | Shared in-memory SQLite (`EnsureCreated()`) | Query-filter composition, interceptor branching, authorization, tenancy, effects, OneOf state                                                          |
| Provider/race | `Nova.Integration.Tests` | Real PostgreSQL 18 via the Aspire AppHost   | Production migrations, mappings, constraints, advisory locks, transaction races, execution-strategy retries, ambiguous commits, filter SQL translation |
| Browser flow  | `Nova.Browser.Tests`     | Real app via the Aspire AppHost + Playwright Chromium | Interactive UI flows that cross the server boundary: multi-user/role behavior, lifecycle conflicts, URL/history state, responsive layouts, keyboard/focus, and contrast/touch-target checks |

**Default new tests to `Nova.Unit.Tests`.** Use integration tests for real HTTP-boundary coverage or
behavior that depends on the provider (type mappings, migrations, constraints, advisory locks,
transaction races, execution-strategy retries, ambiguous commits, SQL translation, collation). SQLite will not catch
`timestamptz` offsets, identity-column semantics, collation, advisory-lock behavior, provider retry
semantics, or SQL-translation limits.

## Execution

[AGENTS.md](../../AGENTS.md#build--validation) owns build order, machine-wide suite serialization,
commands, and PR gates. The [nova-testing recipe](../../.agents/skills/nova-testing/SKILL.md#run-tests)
owns MTP filtering syntax. Broad test-generation workflows may create gitignored `.testagent/`
scratch state; it is not durable evidence.

## Local Aspire workflow

- `dotnet run --project Nova.AppHost` is the supported manual developer entry point. The
  `AspireUseCliBundle` setting delegates it to `aspire run` through `dnx`; the first run on a
  machine may acquire the CLI bundle before the dashboard opens. Agents and automation use
  `aspire start --isolated --non-interactive` for a **manual** Aspire session.
- **The Aspire-backed test suites are not that session.** `NovaAppHostFixture` starts its own AppHost
  through `DistributedApplicationTestingBuilder` and waits for the `nova` resource to report healthy,
  so run `dotnet test` directly and do not start an AppHost first. A separately running one competes
  for the shared Docker capacity the suites are already sensitive to, and a suite stopped mid-run
  leaves a `Nova` process holding `Nova/bin`, which fails the next build with MSB3027/MSB3021.
- The dashboard exposes **Reset nova database** on the `postgres` resource and **Clear profile
  photos** on the `storage` resource. Both require selecting `yes` in the confirmation dialog.
  The CLI equivalents are:
    - `aspire resource postgres reset-db --confirm yes`
    - `aspire resource storage clear-profile-photos --confirm yes`
- `aspire stop --force` is the broader destructive reset. It permanently deletes the
  Postgres and Azurite volume data; prefer the targeted commands when only the database or profile
  photos need resetting.
- The VS Code Aspire extension no longer opens the dashboard automatically. Opt in with its
  `dashboardBrowser` setting or configure dashboard launch behavior in `launch.json`.

## Browser suite (`Nova.Browser.Tests`)

- Use the existing `BrowserSuiteFixture`, real Identity login, shared seeding and
  `InteractionHelpers`/`BrowserRetryPolicy`; do not create per-file hydration retry loops.
- Prove interactive attachment through an observable action before testing filters, focus or
  geometry. A visible prerendered control alone does not prove it can handle an event.
- Keep accessibility assertions in the scenario that exercises the control. Use the surface's
  target-size contract (Nova phone controls: 44px), plus readable contrast and visible focus.
  An older 24px baseline assertion is not permission to shrink a 44px control.
- Env-gated checks must explicitly skip when disabled, never silently pass. Enable applicable
  checks for final evidence; see the reference for flags and capture locations.
- The [browser-suite reference](../../.agents/skills/nova-testing/references/browser-suite.md)
  owns navigation/action timeout behavior, fixture setup, retry bounds and diagnostic commands.
  Machine-wide Aspire serialization and the local PR gate remain in `AGENTS.md`.

## Aspire + Playwright validation (manual browser pass)

Use only when a ticket explicitly requires browser-level behavior validation (interactive auth, UI mutation controls, or flow-level UX that unit/integration tests cannot prove). For the procedure, use the **`aspire-playwright-validation`** skill.

Route first: if the flow should be repeatable regression coverage, add a `Nova.Browser.Tests` scenario instead — this manual pass is for one-off acceptance passes and exploratory checks the suite does not cover.

Rules: never guess the frontend URL (always read it from `aspire describe --format Json`); keep the pass focused and scenario-based (admin happy path + read-only role checks); clean temporary browser artifacts from repo paths afterward.

## Conventions

- One behavior per test; use PascalCase names such as `InterceptorThrowsOnCrossTenantAdd`. Append `Async` for async methods.
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
- For clients validating success bodies, follow the
  [producer-to-UI contract check](../../.agents/skills/add-feature-slice/references/wasm-client.md#producer-to-ui-contract-check)
  across serialization, client validation, and rendered use. Use exact expected counts when proving
  lifecycle or tenant exclusion.
- For `CreatedAtRoute`, assert `201 Created`, the exact `Location`, and a successful GET after
  following it. Route metadata alone cannot prove the generated URL is usable.
- For uniqueness-probe patterns, add a PostgreSQL race test that commits a conflicting row through an independent context after the probe, asserting the unique constraint is the final guard and the exception maps to `Conflict`.
- For interactive pages with event handlers, include a render-mode assertion or a focused Aspire/Playwright scenario; bUnit can invoke callbacks even when the deployed page would render as static SSR.
- Build culture-sensitive expected display strings (dates, numbers, currencies) with the same explicit culture the component uses. Do not hard-code an English rendering unless the product contract fixes that culture.
- bunit and NSubstitute are available in the unit and integration test projects for component/service tests; the browser suite does not use them.
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
    (`Npgsql.PostgresException 53300 "sorry, too many clients already"`). Conservative bounds how many
    tests start. Unit tests use per-test in-memory SQLite connections and can use Aggressive.
- Do not pass `null` or `null!` for required mock dependencies; supply `Substitute.For<T>()` (or a real implementation) and `Array.Empty<T>()` for empty collections. Reserve nulls for tests that intentionally exercise nullable behavior.

## Related

- `.agents/skills/nova-testing/` — harness internals, the write/run workflow, `references/blazor-component-tests.md` for bUnit and render-mode assertions, and `references/browser-suite.md` for the browser workflow suite.
- `.github/instructions/functional-core.instructions.md` — policy boundary and layered test coverage.
- `Nova.Unit.Tests/Data/TenancyTests.cs`, `Nova.Integration.Tests/Data/NovaAppHostFixture.cs`, `Nova.Browser.Tests/CampaignEvaluationBrowserTests.cs`.
