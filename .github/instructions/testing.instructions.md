---
applyTo: "Nova.Unit.Tests/**,Nova.Integration.Tests/**"
description: "Testing rules: project and harness selection, HTTP/UI boundary coverage, MTP commands, and core test conventions."
---

# Testing Rules

> Declarative rules only. For the **harness internals and step-by-step workflow** (SQLite
> `TenancyTestHarness`, Aspire `NovaAppHostFixture`, HTTP e2e bootstrap), use the **`nova-testing`**
> skill (`.github/skills/nova-testing/`).

## Which project

Both projects use **xUnit v3 on Microsoft.Testing.Platform (MTP)** with **Shouldly** assertions.

| Test shape | Project | Database | Use for |
| --- | --- | --- | --- |
| Pure policy | `Nova.Unit.Tests` | None | Deterministic business decisions over constructed immutable facts; no harness, DI, mocks, or logger |
| Service shell | `Nova.Unit.Tests` | Shared in-memory SQLite (`EnsureCreated()`) | Query-filter composition, interceptor branching, authorization, tenancy, effects, OneOf state |
| Provider/race | `Nova.Integration.Tests` | Real PostgreSQL 18 via the Aspire AppHost | Production migrations, mappings, constraints, advisory locks, transaction races, execution-strategy retries, ambiguous commits, filter SQL translation |

**Default new tests to `Nova.Unit.Tests`.** Add an integration test only when behavior depends on the
real provider (type mappings, migrations, constraints, advisory locks, transaction races,
execution-strategy retries, ambiguous commits, SQL translation, collation). SQLite will not catch
`timestamptz` offsets, identity-column semantics, collation, advisory-lock behavior, provider retry
semantics, or SQL-translation limits.

## Run commands

- Run with `dotnet test --project <project>`.
- **Do NOT pass VSTest-only flags** (`--nologo`, `--collect`, `--logger`) — MTP rejects them.
- Filter by class with `--filter-class "*Name"`.

## Aspire + Playwright validation (manual browser pass)

Use only when a ticket explicitly requires browser-level behavior validation (interactive auth, UI mutation controls, or flow-level UX that unit/integration tests cannot prove). For the procedure, use the **`aspire-playwright-validation`** skill.

Rules: never guess the frontend URL (always read it from `aspire describe --format Json`); keep the pass focused and scenario-based (admin happy path + read-only role checks); clean temporary browser artifacts from repo paths afterward.

## Conventions

- One behavior per test; name `Subject_Outcome_Condition` (e.g. `Interceptor_Throws_OnCrossTenantAdd`).
  Use Shouldly (`ShouldBe`, `Should.Throw<T>`) and `[Theory]`/`[InlineData]` for case matrices.
- Test pure policies directly with real policy types and constructed values. Do not use a database
  harness, DI, mocks, or substitute policy implementations; use `[Theory]` for tabular rule matrices.
- Prefer `Xunit.TestContext.Current.CancellationToken` over `CancellationToken.None` whenever the async
  API accepts a token; otherwise leave the call as-is rather than forcing refactors.
- xUnit v3: fixtures implement `IAsyncLifetime` with `ValueTask`; test classes receive fixtures via
  primary-constructor injection.
- When adding a tenant-owned entity (`ITenantOwnedEntity`), add unit filter coverage: visible to its
  club, invisible to another club, cross-tenant writes rejected. Bespoke-filtered entities
  (`ClubJoinRequestEntity`, `NovaUserEntity`, `NovaUserPhotoEntity`) need one test per visibility rule.
- Never assert on global, unfiltered counts in integration tests (the database is shared across the
  collection — each test seeds its own data with database-generated ids).
- For every new HTTP endpoint, add boundary coverage for route registration, auth policy behavior, success serialization, and each declared ProblemDetails shape that cannot be proven by service or client unit tests. Keep provider-specific assertions separate.
- Exercise every route independently; prove the least-privileged role (a creator or admin does not establish ordinary-member access). Test independent query-validation paths separately.
- For clients validating success bodies: cover a populated payload, explicit nested nulls, malformed JSON, invalid ID/date/count relationships, shared-bound violations, and incorrect ordering. Use exact expected counts when proving lifecycle or tenant exclusion.
- For `CreatedAtRoute`, assert `201 Created`, the exact `Location`, and a successful GET after
  following it. Route metadata alone cannot prove the generated URL is usable.
- For uniqueness-probe patterns, add a PostgreSQL race test that commits a conflicting row through an independent context after the probe, asserting the unique constraint is the final guard and the exception maps to `Conflict`.
- For interactive pages with event handlers, include a render-mode assertion or a focused Aspire/Playwright scenario; bUnit can invoke callbacks even when the deployed page would render as static SSR.
- Build culture-sensitive expected display strings (dates, numbers, currencies) with the same explicit culture the component uses. Do not hard-code an English rendering unless the product contract fixes that culture.
- bunit and NSubstitute are available in both projects for component/service tests.
- Do not pass `null` or `null!` for required mock dependencies; supply `Substitute.For<T>()` (or a real implementation) and `Array.Empty<T>()` for empty collections. Reserve nulls for tests that intentionally exercise nullable behavior.

## Related

- `.github/skills/nova-testing/` — harness internals, the write/run workflow, and `references/blazor-component-tests.md` for bUnit and render-mode assertions.
- `.github/instructions/functional-core.instructions.md` — policy boundary and layered test coverage.
- `Nova.Unit.Tests/Data/TenancyTests.cs`, `Nova.Integration.Tests/Data/NovaAppHostFixture.cs`.
