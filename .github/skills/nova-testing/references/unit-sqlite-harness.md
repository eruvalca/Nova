# Unit tests: the SQLite tenancy harness

Canonical files:

- `Nova.Unit.Tests\Data\TenancyTests.cs` defines `TenancyTestHarness`, `FakeCurrentUserProvider`, and tenancy filter/interceptor examples.
- `Nova.Unit.Tests\Clubs\ClubJoinRequestServiceTests.cs` shows service tests that inject harness contexts through EF factories.
- `Nova.Unit.Tests\Features\Photos\ProfilePhotoValidatorTests.cs` shows focused provider-agnostic validation tests.

## When to use unit tests

Default new tests to `Nova.Unit.Tests`. Add an integration test only when the behavior depends
on the real provider (type mappings, migrations, SQL translation, collation).

Use `Nova.Unit.Tests` for provider-agnostic logic: query-filter composition, interceptor branching, services, OneOf state.

## Harness internals

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

## Writing pattern

1. Create a harness in the test class and dispose it.
2. Seed cross-tenant data through `CreateAdminContext()`.
3. Set `CurrentUser` directly or through an `ActAs` helper before creating the context under test.
4. Use `CreateTenantContext()` for filtered writes/queries, `CreateReadContext()` for read-only filtered no-tracking queries, and `CreateAdminContext()` to bypass filters for setup or verification.
5. Assert with Shouldly.

Example pattern from `Nova.Unit.Tests\Data\TenancyTests.cs`:

```csharp
private void ActAs(long? userId, long? clubId, bool isClubAdmin = false)
{
    _harness.CurrentUser.UserId = userId;
    _harness.CurrentUser.ClubId = clubId;
    _harness.CurrentUser.IsClubAdmin = isClubAdmin;
}
```

```csharp
[Fact]
public void TenantContext_ReturnsOnlyCurrentClubsRows()
{
    ActAs(ClubAMember1Id, ClubAId);
    using var context = _harness.CreateTenantContext();

    var players = context.Players.ToList();

    players.Count.ShouldBe(2);
    players.ShouldAllBe(p => p.ClubId == ClubAId);
}
```

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
dotnet test --project Nova.Unit.Tests
dotnet test --project Nova.Unit.Tests --filter-class "*Name"
```

Filter by class with `--filter-class "*Name"`.
