---
name: nova-testing
description: >-
  Write and run Nova tests: pick the right harness (in-memory SQLite tenancy unit tests vs Aspire Postgres integration tests) and run them on Microsoft.Testing.Platform.
  USE FOR: write a unit test, add an integration test, run tests, dotnet test, which test project, tenancy test harness, NovaAppHostFixture, lifecycle race tests, execution-strategy retry tests, transient fault injection, ambiguous commit verification, migration verification, filter tests, MTP flags.
  DO NOT USE FOR: domain/persistence work (use add-domain-persistence), building full features (use add-feature-slice), or adding endpoints (use add-api-endpoint).
---

# Nova Testing

Use this skill when writing or running Nova tests. Read the relevant reference before editing tests:

- [Unit SQLite tenancy harness](references/unit-sqlite-harness.md) for `Nova.Unit.Tests`, shared in-memory SQLite, `TenancyTestHarness`, `FakeCurrentUserProvider`, and `ActAs`.
- [Aspire integration harness](references/aspire-integration-harness.md) for `Nova.Integration.Tests`, real PostgreSQL 18 via Aspire AppHost, `NovaAppHostFixture`, HTTP e2e, and provider-specific checks.
- [Aspire + Playwright validation](../aspire-playwright-validation/SKILL.md) for live browser acceptance flows that must run against the Aspire-hosted app.

## Choose the harness

| Test shape | Project | Database | Use for |
| --- | --- | --- | --- |
| Pure policy | `Nova.Unit.Tests` | None | Deterministic decisions over constructed immutable facts; no harness, DI, mocks, or logger |
| Service shell | `Nova.Unit.Tests` | Shared in-memory SQLite (`EnsureCreated()`) | Query filters, interceptors, authorization, tenancy, effects, and OneOf state |
| Provider/race | `Nova.Integration.Tests` | Real PostgreSQL 18 via Aspire AppHost | Migrations, constraints, advisory locks, transaction races, execution-strategy retries, ambiguous commits, and SQL translation |

Default new tests to `Nova.Unit.Tests`. Add an integration test only when the behavior depends
on the real provider (type mappings, migrations, database constraints, advisory locks,
transaction races, execution-strategy retries, ambiguous commits, SQL translation, collation).

## Run commands

Both test projects use xUnit v3 on Microsoft.Testing.Platform (MTP) with Shouldly assertions.
Run with `dotnet test --project <project>` — do NOT pass VSTest-only flags (`--nologo`,
`--collect`, `--logger`); MTP rejects them.

```powershell
dotnet test --project Nova.Unit.Tests
dotnet test --project Nova.Integration.Tests
dotnet test --project Nova.Unit.Tests --filter-class "*Name"
```

Filter by class with `--filter-class "*Name"`.

## Checklist

1. Pick `Nova.Unit.Tests` unless the behavior is provider-specific.
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
9. Run the smallest targeted command with `dotnet test --project <project> --filter-class "*Name"`.
   Repeat `--filter-class` for multiple classes; do not combine class names with `|`.
