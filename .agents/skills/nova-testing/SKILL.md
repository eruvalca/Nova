---
name: nova-testing
description: >-
  Write or run Nova tests using the appropriate pure-policy, SQLite, bUnit, real HTTP/PostgreSQL,
  or Playwright harness on Microsoft.Testing.Platform. Use for test selection, lifecycle/retry
  races, async UI ownership, form recovery, and closing behavioral review findings. Feature
  implementation belongs to add-feature-slice or its focused UI/API/persistence skills.
---

# Nova Testing

Use this skill when writing or running Nova tests. Read the relevant reference before editing tests:

- [Unit SQLite tenancy harness](references/unit-sqlite-harness.md) for `Nova.Unit.Tests`, shared in-memory SQLite, `TenancyTestHarness`, `FakeCurrentUserProvider`, and `ActAs`.
- [Blazor component tests](references/blazor-component-tests.md) for bUnit + NSubstitute component
  rendering, `EventCallback` assertions, the required render-mode assertion, and persisted-state
  restore coverage.
- [Aspire integration harness](references/aspire-integration-harness.md) for `Nova.Integration.Tests`, real PostgreSQL 18 via Aspire AppHost, `NovaAppHostFixture`, HTTP e2e, and provider-specific checks.
- [Browser suite](references/browser-suite.md) for `Nova.Browser.Tests` — Playwright against the
  Aspire-hosted app, the browser fixture and seed, Blazor attachment/navigation pitfalls, and the
  accessibility regression conventions.
- [Aspire + Playwright validation](../aspire-playwright-validation/SKILL.md) for one-off manual
  browser acceptance passes; for committed regression coverage, add a `Nova.Browser.Tests`
  scenario instead.
- [Finding closure and independent review](references/review-and-finding-closure.md) when fixing
  review findings, checking sibling paths, or requesting a focused independent review. It also
  covers read-only retrieval of GitHub reviews with existing CLI/API tools.

## Choose the harness

| Test shape    | Project                  | Database                                    | Use for                                                                                                                        |
| ------------- | ------------------------ | ------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| Pure policy   | `Nova.Unit.Tests`        | None                                        | Deterministic decisions over constructed immutable facts; no harness, DI, mocks, or logger                                     |
| Service shell | `Nova.Unit.Tests`        | Shared in-memory SQLite (`EnsureCreated()`) | Query filters, interceptors, authorization, tenancy, effects, and OneOf state                                                  |
| HTTP boundary | `Nova.Integration.Tests` | Real app through the Aspire AppHost | Routing, middleware, authorization, binding, serialization, and Location follow behavior |
| Provider/race | `Nova.Integration.Tests` | Real PostgreSQL 18 via Aspire AppHost       | Migrations, constraints, advisory locks, transaction races, execution-strategy retries, ambiguous commits, and SQL translation |
| Browser flow  | `Nova.Browser.Tests`     | Real app via the Aspire AppHost + Playwright Chromium | Interactive UI flows crossing the server boundary: multi-user/role behavior, lifecycle conflicts, URL/history state, responsive layouts, keyboard/focus, contrast/touch targets |

Default new tests to `Nova.Unit.Tests`. Use integration tests for the real HTTP boundary or
provider behavior (type mappings, migrations, database constraints, advisory locks,
transaction races, execution-strategy retries, ambiguous commits, SQL translation, collation).
Add a browser test when the behavior is a real UI flow that bUnit cannot prove (interactive
attach, focus/keyboard, history/URL state, real HTTP/Identity, multi-user sessions).

## Run commands

All three test projects use xUnit v4 on Microsoft.Testing.Platform (MTP) with Shouldly assertions.
Use the explicit `--project` form and avoid bare csproj invocation, which has been observed to fail
MTP test discovery in this repo:

```powershell
dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj
dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj
dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj
dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*Name"
dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignParticipantHttpTests"
```

Do not pass VSTest-only flags (`--nologo`, `--collect`, `--logger`); MTP rejects them.
Filter by class with `--filter-class "*Name"`.
The browser suite needs a one-time browser download per machine before its first run:
`Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1 install chromium`. Follow `AGENTS.md` for the
authoritative verification runner and PR/merge gate. Treat the proposed full CI job as effective
only after hosted-runner proof and required-check configuration are verified; keep local evidence
for suites not yet enforced. Browser tests locate
controls by role/label (see `references/browser-suite.md`); when a redesign changes markup, update
the locators — never weaken the assertion to make it pass.

All three projects run xUnit v4 `ParallelMode.All` via per-project
`TestAssemblyParallelization.cs`. Unit tests use the Aggressive algorithm at the CPU-thread default;
they have per-test in-memory SQLite connections and no shared collection fixture. Integration tests
use Conservative at the CPU-thread default, and browser tests use Conservative capped at 4 threads,
because both share the AppHost/database. Keep integration/browser data per-test unique and the
simulated user flow-local: direct `fixture.CurrentUser.X = ...` assignment is parallel-safe
(AsyncLocal-backed); use `fixture.UseUser(...)` only when restore-on-dispose semantics are needed.
Never introduce static mutable test state.

Broad test-generation workflows may create `.testagent/` as disposable local scratch state. The
directory is gitignored and must not be committed; keep durable evidence in the tests and the PR
validation summary.

## Checklist

1. Choose the smallest harness that proves the behavior, including real HTTP or browser boundaries
   when a direct service/component test cannot exercise them.
2. Follow existing sibling tests for arrangement and naming (`Subject_Outcome_Condition`).
3. Use Shouldly (`ShouldBe`, `Should.Throw<T>`) and `[Theory]`/`[InlineData]` for case matrices.
4. Test pure policies directly using the real policy and constructed values. Do not mock the policy
   or use the SQLite harness for deterministic logic; use `[Theory]` for tabular combinations.
   Assert the domain case by type (for example, `result.Value.ShouldBeOfType<CampaignMayClose>()`)
   rather than positional `IsTn`/`AsTn` checks.
5. Use `TestContext.Current.CancellationToken` when an async API accepts a token.
6. For tenant data, set the simulated user before creating the context, seed through the admin context, then assert through the appropriate tenant/read/admin context.
7. If production behavior relies on `LifecycleMutationLock`, database constraints, or competing transactions, add a focused PostgreSQL integration test; SQLite cannot verify them.
8. For retrying mutations, test both a failure before commit and a lost commit acknowledgement.
   Assert that fault injection ran, retries use fresh context state, and exactly one complete
   aggregate persisted.
9. For a probe-then-write uniqueness check, inject a conflicting write through an independent
   PostgreSQL context after the probe and assert the database violation maps to `Conflict`.
10. For `CreatedAtRoute`, assert `201`, exact `Location`, and a successful GET after following it.
11. For strict HTTP clients, test a populated valid body and table-driven malformed/invalid 2xx
    payloads, including nested nulls, invalid relationships, bounds, and portable ordering.
12. Exercise each endpoint and query-validation path independently, using the least-privileged
    permitted role and exact counts for lifecycle or tenancy exclusions.
13. When a query contract promises an exact asynchronous relational reader count or no N+1 reader
    queries, assert `ReaderExecutionCount` with `CountingCommandInterceptor`; context-factory
    invocations are not reader-command evidence. The interceptor does not observe synchronous,
    scalar, or non-query commands, so do not use it to claim an exact total SQL-command count.
14. For recoverable commands, contextual validation, or authorization-reactive async work, map the
    relevant transitions before implementation using
    [stateful-transitions.md](../add-feature-slice/references/stateful-transitions.md). Existing
    passing tests prove only the paths they execute. For a reported miss, follow
    [finding closure](references/review-and-finding-closure.md).
15. Run the smallest targeted command with `dotnet test --project <project> --filter-class "*Name"`.
    Repeat `--filter-class` for multiple classes; do not combine class names with `|`.
16. During implementation, run the smallest relevant tests. Before PR/merge, follow the all-suite
    gate in `AGENTS.md`; on intermediate pushes, include the suites affected by the change. Record
    test coverage separately from command execution and disclose incomplete verification.
