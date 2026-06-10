---
applyTo: "Nova.Unit.Tests/**,Nova.Integration.Tests/**"
description: "Test suite layout, how to run each project, the SQLite tenancy harness, and Aspire-based Postgres integration testing."
---

# Testing

Both test projects use xUnit v3 on Microsoft.Testing.Platform (MTP) with Shouldly assertions.
Run with `dotnet test --project <project>` — do NOT pass VSTest-only flags (`--nologo`,
`--collect`, `--logger`); MTP rejects them.

| Project                  | Speed                     | Database                                    | Use for                                                                                                  |
| ------------------------ | ------------------------- | ------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| `Nova.Unit.Tests`        | ~2s                       | Shared in-memory SQLite (`EnsureCreated()`) | Provider-agnostic logic: query-filter composition, interceptor branching, services, OneOf state          |
| `Nova.Integration.Tests` | ~20s+ (starts containers) | Real PostgreSQL 18 via the Aspire AppHost   | Postgres-only behavior: production migrations, `timestamptz`/`DateOnly` mappings, filter SQL translation |

Default new tests to `Nova.Unit.Tests`. Add an integration test only when the behavior depends
on the real provider (type mappings, migrations, SQL translation, collation).

## Unit tests: the SQLite tenancy harness

`TenancyTestHarness` (`Nova.Unit.Tests/Data/TenancyTests.cs`) opens one in-memory SQLite
connection shared by all three contexts, creates the schema with `EnsureCreated()` on the admin
context (production migrations are NOT exercised), and exposes:

- `CreateTenantContext()` / `CreateReadContext()` / `CreateAdminContext()` — mirroring production
  wiring (interceptor on tenant + admin; read context has no interceptor). Options attach
  `IdentityStoreServiceProvider.Instance` via `UseApplicationServiceProvider` so the Identity
  schema version (Version3, passkeys) matches the running app — keep this when adding harnesses.
- `CurrentUser` — a mutable `FakeCurrentUserProvider`. Tests call `ActAs(userId, clubId, isClubAdmin)`
  before creating a context. Filters are parameterized per context instance, so create the context
  AFTER setting the user.
- Seed data through `CreateAdminContext()` (bypasses tenant guarding) with explicit `ClubId` and
  `CreatedById`; the interceptor only auto-stamps `CreatedById` when a user id is set.

## Integration tests: Aspire AppHost fixture

`NovaAppHostFixture` (`Nova.Integration.Tests/Data/NovaAppHostFixture.cs`) is shared via the
`[Collection(NovaAppHostCollection.Name)]` collection fixture so the AppHost starts once per run.
It requires a running container runtime (Docker/Podman). What it does and why:

1. `DistributedApplicationTestingBuilder.CreateAsync<Projects.Nova_AppHost>()` boots the real
   AppHost model (Postgres 18 container + the Nova web app).
2. Strips `ContainerMountAnnotation` volumes so tests never reuse the developer's persistent
   data volume — every run starts with an empty database.
3. Waits for the `nova` resource to be healthy, then resolves the live connection string with
   `app.GetConnectionStringAsync("novadb")`.
4. Applies migrations itself via `CreateTenantContext().Database.MigrateAsync()`. Two pitfalls
   this avoids:
   - The testing builder does not run the app in `Development`, so the app's startup migration
     block in `Nova/Program.cs` is skipped.
   - Migrations are attributed `[DbContext(typeof(NovaDbContext))]` — `MigrateAsync` through
     `NovaAdminDbContext` silently finds ZERO migrations. Always migrate via `NovaDbContext`.
     Like the unit harness, its options attach `IdentityStoreServiceProvider.Instance` so the
     model matches the migrations (otherwise `MigrateAsync` throws `PendingModelChangesWarning`).

Test-isolation pattern: the database is shared across all tests in the collection, so each test
seeds its OWN clubs/users/players with database-generated ids (no hardcoded keys) and lets the
tenant query filters scope queries to that test's data. Never assert on global, unfiltered counts
in integration tests.

## Integration tests: HTTP-layer e2e

The fixture also exposes `CreateNovaHttpClient(allowAutoRedirect: false)` — an `HttpClient`
aimed at the running "nova" resource (https endpoint preferred, dev cert accepted) with a
per-client `CookieContainer` and redirect-following off so tests assert on status codes and
`Location` headers directly.

Auth bootstrap (`Nova.Integration.Tests/Http/IdentityHttpClientHelper.cs`): tests register a
real user over HTTP by GETting `/Account/Register`, scraping the hidden inputs from the Blazor
SSR form (antiforgery token + named-form handler field), and POSTing the form. The Identity
application cookie lands in the client's cookie container, authenticating subsequent API calls.
Use a unique email per test (shared database). Note the profile-photo gate: a freshly registered
user is redirected to `/Account/ProfilePhoto` on non-exempt paths until a photo is uploaded and
the `/Account/ProfilePhoto/Complete` cookie-refresh hop has run.

`Nova.Integration.Tests/Http/ProfilePhotoHttpTests.cs` covers route reachability (401-vs-404
distinguishes auth from routing regressions), the full register → upload → fetch → complete
flow, ProblemDetails bodies with `traceId`, ETag/304 caching, and owner-only access to
`size=original`.

Prefer `Xunit.TestContext.Current.CancellationToken` over `CancellationToken.None` in tests
whenever the async API already accepts a `CancellationToken`. This keeps test cancellation tied
to the xUnit runner and preserves cancellation behavior during test interruption. If no token-
accepting overload or parameter exists, leave the call as-is (or omit the token argument) rather
than forcing unrelated refactors. Example: `await context.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken)`.

## Conventions and gotchas

- One behavior per test; name `Subject_Outcome_Condition` style (e.g.
  `Interceptor_Throws_OnCrossTenantAdd`). Use Shouldly (`ShouldBe`, `Should.Throw<T>`),
  `[Theory]`/`[InlineData]` for case matrices.
- xUnit v3: fixtures implement `IAsyncLifetime` with `ValueTask`; test classes get fixtures via
  primary-constructor injection.
- When adding a tenant-owned entity (`ITenantOwnedEntity`), add unit filter coverage: visible to
  its club, invisible to another club, cross-tenant writes rejected. Bespoke-filtered entities
  (`ClubJoinRequestEntity`, `NovaUserEntity`, `NovaUserPhotoEntity`) need one test per visibility
  rule in the filter expression.
- SQLite is not Postgres: it won't catch `timestamptz` offset requirements, identity-column
  semantics, case-sensitivity/collation, or SQL translation limits. If a query uses
  provider-sensitive constructs, mirror one round-trip test in `Nova.Integration.Tests`.
- bunit and NSubstitute are available in both projects for component/service tests.
