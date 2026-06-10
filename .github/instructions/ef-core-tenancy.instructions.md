---
applyTo: "Nova/Data/**/*.cs,Nova/Entities/**/*.cs,Nova/Program.cs,Nova.Unit.Tests/**/*.cs"
description: "EF Core setup, club-based multi-tenancy, DbContext selection, entity/relationship rules, and migrations."
---

# EF Core & Tenancy

The tenant is a club (`ClubEntity`). Users belong to at most one club and must only see data
for their club. Full design history: `plans/dbcontext-tenancy-design.md`.

## DbContext selection — pick the right one

All three contexts derive from the abstract `ApplicationDbContext` (one shared model, one
migrations set) and are registered as **scoped** `AddDbContextFactory<T>` in `Nova/Program.cs`.

| Context | Use for | Behavior |
|---|---|---|
| `NovaDbContext` | Normal reads/writes for the signed-in user | Tenant query filters ON; `TenantSaveChangesInterceptor` ON |
| `NovaReadDbContext` | Read-only, larger or hot-path queries | Tenant filters ON; `NoTracking` + auto-detect-changes off; all `SaveChanges*` overloads throw |
| `NovaAdminDbContext` | Admin/maintenance UI, Identity stores, seeding, anonymous flows (login/registration) | Tenant filters BYPASSED; interceptor still stamps audit fields |

- Default to `NovaDbContext`. Use `NovaReadDbContext` when you know you won't write. Use
  `NovaAdminDbContext` only behind `Policies.RequireAdmin` or in infrastructure (Identity,
  seeding) — never in user-facing tenant flows.
- In Blazor components/services, inject `IDbContextFactory<T>` and `await factory.CreateDbContextAsync()`
  with `await using`; do not inject the context directly.
- Never call `IgnoreQueryFilters()` to "fix" a missing-data bug — switch to `NovaAdminDbContext`
  behind an admin policy instead, so the intent is auditable.

## Tenancy rules

- Every club-scoped entity implements `ITenantOwnedEntity` (`long ClubId`) and keeps a real
  `ClubId` FK + `Club` navigation. The generic filter loop in `ApplicationDbContext.OnModelCreating`
  picks it up automatically — adding the interface is all that's needed for filtering.
- Deliberately NOT tenant-owned: `ClubEntity` (globally visible so users can find clubs to join),
  `ClubJoinRequestEntity` (bespoke filter: requester sees own; ClubAdmin sees their club's),
  `NovaUserEntity` (bespoke filter: clubmates or self), `NovaUserPhotoEntity` (mirrors the user
  filter via `e.NovaUser.ClubId` — required dependents of a filtered principal must mirror the
  principal's filter or EF warns at startup).
- Query filters may only reference fields/properties on the context instance
  (`_bypassTenantFilter`, `_currentUser.ClubId`, `_currentUser.UserId`, `_currentUser.IsClubAdmin`)
  so EF parameterizes them per instance. Keep `ICurrentUserProvider` members flat primitives for
  this reason; `GetCurrentUserState()` (a OneOf union) exists for application/UI logic only.
- EF allows ONE query filter per entity (`HasQueryFilter` replaces). Bespoke filters live in
  `ApplicationDbContext` after the generic loop — never add filters in entity configurations.
- Do not set `ClubId` manually when creating entities via `NovaDbContext`;
  `TenantSaveChangesInterceptor` stamps it from the current user (and throws if the user has no
  club, or on any cross-tenant write). Under `NovaAdminDbContext` tenant guarding/stamping is
  skipped, so admin code MUST set `ClubId` explicitly. The interceptor always stamps audit
  fields (`CreatedAt`/`ModifiedAt` + `CreatedById`/`ModifiedById` — which are intentionally
  FK-less).
- Visibility belongs in query filters; ACTIONS (approve/reject/delete) belong in authorization
  policies (`Policies.RequireAdmin` / `RequireClubAdmin` / `RequireClubMember` in
  `Nova.Shared/Security/Policies.cs`).

## Current user & claims

- `ICurrentUserProvider` (`Nova/Data/Tenancy/`) resolves the user from `IHttpContextAccessor`
  first, then the Blazor `AuthenticationStateProvider`. `NullCurrentUserProvider` is for design
  time and tests.
- The club id travels as the `NovaClaimTypes.ClubId` claim, added by
  `NovaUserClaimsPrincipalFactory`. When a user's club membership changes, call
  `ClubMembershipClaimRefresher` (`RefreshCurrentUserAsync` for the acting user,
  `MarkUserClaimsStaleAsync` for another user) and `Match` on its
  `OneOf<Success, Error<string[]>>` result — do not ignore it.
- New users get `Roles.StandardUser` at registration (see `Register.razor` / `ExternalLogin.razor`).

## Entities, configurations, relationships

- Entities live in `Nova/Entities/`, one `IEntityTypeConfiguration<T>` per entity in
  `Nova/Data/Configurations/` (applied via `ApplyConfigurationsFromAssembly`). Put keys, FKs,
  delete behaviors, and indexes in configurations — not data annotations.
- Declare each relationship in ONE configuration only (the dependent's, by convention here);
  duplicate declarations across files drift and have caused bugs.
- Delete behavior conventions (see `plans/dbcontext-tenancy-design.md` for the full matrix):
  - Club-owned content cascades from `Club` (Postgres allows multiple cascade paths).
  - `Club → NovaUsers` is `SetNull` (users survive club deletion).
  - Optional "assignment" style FKs (e.g. `PlayerCampaignAssignment.Team`) are `SetNull`.
  - Audit columns (`CreatedById`/`ModifiedById`) never get FKs.
- Club deletion is NOT interceptor-guarded (Club isn't tenant-owned) — any club-delete feature
  must be gated by `Policies.RequireClubAdmin` or `RequireAdmin`.

## Migrations

- One migrations set under `Nova/Data/Migrations/`, generated against `NovaDbContext` via
  `NovaDbContextDesignTimeFactory` (which uses `NullCurrentUserProvider`).
- Migrations are attributed `[DbContext(typeof(NovaDbContext))]` — applying them via
  `Database.MigrateAsync()` on any other context (e.g. `NovaAdminDbContext`) silently finds
  ZERO migrations. Always migrate through `NovaDbContext`.
- The runtime sets `IdentityOptions.Stores.SchemaVersion = IdentitySchemaVersions.Version3`
  (adds the .NET 10 `AspNetUserPasskeys` table), and Identity reads that option from the
  **application service provider** while building the model. Any context built outside the host
  (design-time factory, test harnesses, scripts) MUST attach
  `.UseApplicationServiceProvider(IdentityStoreServiceProvider.Instance)` or its model will
  silently differ from the migrations — at runtime this surfaces as a
  `PendingModelChangesWarning` exception from `MigrateAsync`.
- After any model change: `dotnet ef migrations add <Name> --project Nova --context NovaDbContext`
  then verify with `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext`.
- The dev-startup block in `Nova/Program.cs` applies migrations (Development only) and seeds
  roles (all environments) via the execution strategy.

## Testing data access

See `.github/instructions/testing.instructions.md` for the full testing guide. In short:
provider-agnostic tenancy logic is tested with `TenancyTestHarness` (in-memory SQLite) in
`Nova.Unit.Tests/Data/TenancyTests.cs`; Postgres-specific behavior (production migrations,
`timestamptz`/`DateOnly` mappings, filter SQL translation) runs against the real AppHost in
`Nova.Integration.Tests/Data/PostgresTenancyTests.cs`. When adding a tenant-owned entity, add
filter coverage: visible to its club, invisible to another club, cross-tenant writes rejected.
