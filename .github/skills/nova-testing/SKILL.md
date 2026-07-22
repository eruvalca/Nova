---
name: nova-testing
description: >-
  Write and run Nova tests: pick the right harness (in-memory SQLite tenancy unit tests vs Aspire Postgres integration tests) and run them on Microsoft.Testing.Platform.
  USE FOR: write a unit test, add an integration test, run tests, dotnet test, which test project, tenancy test harness, NovaAppHostFixture, lifecycle race tests, migration verification, filter tests, MTP flags.
  DO NOT USE FOR: domain/persistence work (use add-domain-persistence), building full features (use add-feature-slice), or adding endpoints (use add-api-endpoint).
---

# Nova Testing

Use this skill when writing or running Nova tests. Read the relevant reference before editing tests:

- [Unit SQLite tenancy harness](references/unit-sqlite-harness.md) for `Nova.Unit.Tests`, shared in-memory SQLite, `TenancyTestHarness`, `FakeCurrentUserProvider`, and `ActAs`.
- [Aspire integration harness](references/aspire-integration-harness.md) for `Nova.Integration.Tests`, real PostgreSQL 18 via Aspire AppHost, `NovaAppHostFixture`, HTTP e2e, and provider-specific checks.

## Choose the harness

| Project                  | Speed                     | Database                                    | Use for                                                                                                  |
| ------------------------ | ------------------------- | ------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| `Nova.Unit.Tests`        | ~2s                       | Shared in-memory SQLite (`EnsureCreated()`) | Provider-agnostic logic: query-filter composition, interceptor branching, services, OneOf state                                  |
| `Nova.Integration.Tests` | ~20s+ (starts containers) | Real PostgreSQL 18 via the Aspire AppHost   | Postgres-only behavior: migrations, mappings, constraints, advisory locks, transaction races, filter SQL translation             |

Default new tests to `Nova.Unit.Tests`. Add an integration test only when the behavior depends
on the real provider (type mappings, migrations, database constraints, advisory locks,
transaction races, SQL translation, collation).

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
4. Use `TestContext.Current.CancellationToken` when an async API accepts a token.
5. For tenant data, set the simulated user before creating the context, seed through the admin context, then assert through the appropriate tenant/read/admin context.
6. If production behavior relies on `LifecycleMutationLock`, database constraints, or competing transactions, add a focused PostgreSQL integration test; SQLite cannot verify them.
7. Run the smallest targeted command with `dotnet test --project <project> --filter-class "*Name"`.
