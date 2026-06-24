---
applyTo: "Nova.Unit.Tests/**,Nova.Integration.Tests/**"
description: "Testing rules: which project to use, run commands and MTP flag constraints, and core test conventions."
---

# Testing Rules

> Declarative rules only. For the **harness internals and step-by-step workflow** (SQLite
> `TenancyTestHarness`, Aspire `NovaAppHostFixture`, HTTP e2e bootstrap), use the **`nova-testing`**
> skill (`.github/skills/nova-testing/`).

## Which project

Both projects use **xUnit v3 on Microsoft.Testing.Platform (MTP)** with **Shouldly** assertions.

| Project | Speed | Database | Use for |
| --- | --- | --- | --- |
| `Nova.Unit.Tests` | ~2s | Shared in-memory SQLite (`EnsureCreated()`) | Provider-agnostic logic: query-filter composition, interceptor branching, services, OneOf state |
| `Nova.Integration.Tests` | ~20s+ (starts containers) | Real PostgreSQL 18 via the Aspire AppHost | Postgres-only behavior: production migrations, `timestamptz`/`DateOnly` mappings, filter SQL translation |

**Default new tests to `Nova.Unit.Tests`.** Add an integration test only when behavior depends on the
real provider (type mappings, migrations, SQL translation, collation). SQLite will not catch
`timestamptz` offsets, identity-column semantics, collation, or SQL-translation limits — mirror one
round-trip test in `Nova.Integration.Tests` for provider-sensitive queries.

## Run commands

- Run with `dotnet test --project <project>`.
- **Do NOT pass VSTest-only flags** (`--nologo`, `--collect`, `--logger`) — MTP rejects them.
- Filter by class with `--filter-class "*Name"`.

## Conventions

- One behavior per test; name `Subject_Outcome_Condition` (e.g. `Interceptor_Throws_OnCrossTenantAdd`).
  Use Shouldly (`ShouldBe`, `Should.Throw<T>`) and `[Theory]`/`[InlineData]` for case matrices.
- Prefer `Xunit.TestContext.Current.CancellationToken` over `CancellationToken.None` whenever the async
  API accepts a token; otherwise leave the call as-is rather than forcing refactors.
- xUnit v3: fixtures implement `IAsyncLifetime` with `ValueTask`; test classes receive fixtures via
  primary-constructor injection.
- When adding a tenant-owned entity (`ITenantOwnedEntity`), add unit filter coverage: visible to its
  club, invisible to another club, cross-tenant writes rejected. Bespoke-filtered entities
  (`ClubJoinRequestEntity`, `NovaUserEntity`, `NovaUserPhotoEntity`) need one test per visibility rule.
- Never assert on global, unfiltered counts in integration tests (the database is shared across the
  collection — each test seeds its own data with database-generated ids).
- bunit and NSubstitute are available in both projects for component/service tests.

## Related

- `.github/skills/nova-testing/` — harness internals and the write/run workflow.
- `Nova.Unit.Tests/Data/TenancyTests.cs`, `Nova.Integration.Tests/Data/NovaAppHostFixture.cs`.
