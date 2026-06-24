# Integration tests: Aspire AppHost fixture

Canonical files:

- `Nova.Integration.Tests\Data\NovaAppHostFixture.cs` defines `NovaAppHostFixture`, `NovaAppHostCollection`, live context factories, and `CreateNovaHttpClient`.
- `Nova.Integration.Tests\Data\PostgresTenancyTests.cs` shows provider-specific database tests for migrations, `timestamptz`, `DateOnly`, and query-filter SQL translation.
- `Nova.Integration.Tests\Http\ProfilePhotoHttpTests.cs` shows HTTP-layer e2e tests against the running app.
- `Nova.Integration.Tests\Http\IdentityHttpClientHelper.cs` shows HTTP auth bootstrap for tests.

## When to use integration tests

Default new tests to `Nova.Unit.Tests`. Add an integration test only when the behavior depends
on the real provider (type mappings, migrations, SQL translation, collation).

Use `Nova.Integration.Tests` for Postgres-only behavior: production migrations, `timestamptz`/`DateOnly` mappings, filter SQL translation.

SQLite is not Postgres: it won't catch `timestamptz` offset requirements, identity-column
semantics, case-sensitivity/collation, or SQL translation limits. If a query uses
provider-sensitive constructs, mirror one round-trip test in `Nova.Integration.Tests`.

## AppHost fixture internals

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

## HTTP-layer e2e

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

## Writing pattern

1. Mark the test class with `[Collection(NovaAppHostCollection.Name)]` and use primary-constructor injection for `NovaAppHostFixture`.
2. Seed through `fixture.CreateAdminContext()` and capture database-generated ids.
3. Set `fixture.CurrentUser` before creating filtered contexts.
4. Use `fixture.CreateTenantContext()`, `fixture.CreateReadContext()`, and `fixture.CreateAdminContext()` against the live PostgreSQL database.
5. For HTTP e2e, use `fixture.CreateNovaHttpClient(allowAutoRedirect: false)` and assert redirects/status codes directly.
6. Use unique emails/data per test; never rely on global counts.

Example pattern from `Nova.Integration.Tests\Data\PostgresTenancyTests.cs`:

```csharp
[Collection(NovaAppHostCollection.Name)]
public class PostgresTenancyTests(NovaAppHostFixture fixture)
```

```csharp
private void ActAs(long? userId, long? clubId, bool isClubAdmin = false)
{
    fixture.CurrentUser.UserId = userId;
    fixture.CurrentUser.ClubId = clubId;
    fixture.CurrentUser.IsClubAdmin = isClubAdmin;
}
```

## Conventions and gotchas

- One behavior per test; name `Subject_Outcome_Condition` style (e.g.
  `Interceptor_Throws_OnCrossTenantAdd`). Use Shouldly (`ShouldBe`, `Should.Throw<T>`),
  `[Theory]`/`[InlineData]` for case matrices.
- xUnit v3: fixtures implement `IAsyncLifetime` with `ValueTask`; test classes get fixtures via
  primary-constructor injection.
- Prefer `Xunit.TestContext.Current.CancellationToken` over `CancellationToken.None` in tests
  whenever the async API already accepts a `CancellationToken`. This keeps test cancellation tied
  to the xUnit runner and preserves cancellation behavior during test interruption. If no token-
  accepting overload or parameter exists, leave the call as-is (or omit the token argument) rather
  than forcing unrelated refactors. Example: `await context.SaveChangesAsync(Xunit.TestContext.Current.CancellationToken)`.
- bunit and NSubstitute are available in both projects for component/service tests.

## Run commands

Both test projects use xUnit v3 on Microsoft.Testing.Platform (MTP) with Shouldly assertions.
Run with `dotnet test --project <project>` — do NOT pass VSTest-only flags (`--nologo`,
`--collect`, `--logger`); MTP rejects them.

```powershell
dotnet test --project Nova.Integration.Tests
dotnet test --project Nova.Integration.Tests --filter-class "*Name"
```

Filter by class with `--filter-class "*Name"`.
