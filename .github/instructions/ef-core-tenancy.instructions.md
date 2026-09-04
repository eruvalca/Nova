---
applyTo: "Nova/Data/**/*.cs,Nova/Entities/**/*.cs,Nova/Features/**/*Service*.cs,Nova/Program.cs,Nova.Unit.Tests/**/*.cs"
description: "EF Core setup, club-based multi-tenancy, tenant-safe query construction, provider behavior, entity/relationship rules, and migrations."
---

# EF Core & Tenancy

The tenant is a club (`ClubEntity`). Users belong to at most one club and must only see data
for their club.

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

- Every club-scoped entity implements `ITenantOwnedEntity` (`long ClubId`). Ordinary club-owned
  entities keep a real `ClubId` FK + `Club` navigation. Immutable operation receipts that prove an
  ambiguous commit are the deliberate exception: they retain a `ClubId` snapshot without an FK so
  verification survives later deletion of the club aggregate. Those FK-less receipts must have a
  global age-based cleanup path and a `CreatedAt`-leading index so rows for deleted clubs do not
  accumulate indefinitely. The generic filter loop in
  `ApplicationDbContext.OnModelCreating` picks both shapes up automatically — adding the interface
  is all that's needed for filtering.
- Deliberately NOT tenant-owned: `ClubEntity` (globally visible so users can find clubs to join),
  `ClubJoinRequestEntity` (bespoke filter: requester sees own; ClubAdmin sees their club's),
  `NovaUserEntity` (bespoke filter: clubmates or self), `NovaUserPhotoEntity` (mirrors the user
  filter via `e.NovaUser.ClubId` — required dependents of a filtered principal must mirror the
  principal's filter or EF warns at startup).
- A principal query filter does not protect rows queried directly from a dependent `DbSet`. When
  role or lifecycle visibility hides the principal, mirror that boundary on every dependent that
  can disclose it, and test principal, dependent, and `NovaAdminDbContext` bypass reads.
- Query filters may only reference fields/properties on the context instance (`_bypassTenantFilter`, `_currentUser.ClubId`, `_currentUser.UserId`, `_currentUser.IsClubAdmin`) so EF parameterizes them per instance. Keep `ICurrentUserProvider` members flat primitives; `GetCurrentUserState()` (a OneOf union) is for application/UI logic only.
- EF allows ONE query filter per entity (`HasQueryFilter` replaces). Bespoke filters live in
  `ApplicationDbContext` after the generic loop — never add filters in entity configurations.
- Do not set `ClubId` manually when creating entities via `NovaDbContext`; `TenantSaveChangesInterceptor` stamps it from the current user (throws if the user has no club or on cross-tenant write). Under `NovaAdminDbContext` stamping is skipped — admin code MUST set `ClubId` explicitly. The interceptor always stamps `CreatedAt`/`ModifiedAt` + `CreatedById`/`ModifiedById` (intentionally FK-less). The one exception to manual `ClubId` stamping is the club-less join-request activity write — see **Append-only activity events**.
- Visibility belongs in query filters; ACTIONS (approve/reject/delete) belong in authorization
  policies (`Policies.RequireAdmin` / `RequireClubAdmin` / `RequireClubMember` in
  `Nova.Shared/Security/Policies.cs`).
- Scope tenant queries from the authenticated tenant context, not from a caller-supplied route or
  query value. A route tenant id may be compared for non-disclosing authorization behavior, but it
  must not become the authoritative LINQ predicate.

## Current user & claims

- `ICurrentUserProvider` (`Nova/Data/Tenancy/`) resolves the user from `IHttpContextAccessor`
  first, then the Blazor `AuthenticationStateProvider`. `NullCurrentUserProvider` is for design
  time and tests.
- The club id travels as the `NovaClaimTypes.ClubId` claim, added by
  `NovaUserClaimsPrincipalFactory`. A membership mutation owns exactly one claims-invalidation path:
  either the direct-EF transactional path below, or `ClubMembershipClaimRefresher`
  (`RefreshCurrentUserAsync` for the acting user or `MarkUserClaimsStaleAsync` for another) when
  Identity owns the stamp update. `Match` the helper's `OneOf<Success, Error<string[]>>`; do not
  ignore it or combine it with an already-transactional stamp write.
- When membership or Identity roles change through EF so they can share a domain transaction,
  rotate both `SecurityStamp` and `ConcurrencyStamp` through that same context. For the acting
  user's own change, load them after commit from a fresh `NovaAdminDbContext` with `AsNoTracking`
  and call the refresh-only `RefreshCurrentUserSignInAsync`; do not refresh from a possibly stale
  entity tracked by the scoped `UserManager`. A remote user's transactional `SecurityStamp` is the
  invalidation marker—do not call the stamp-mutating `MarkUserClaimsStaleAsync` after commit. A
  remote stamp mismatch invalidates the active authentication state at revalidation; it does not
  rebuild the principal, so the user must authenticate again to receive updated claims.
- New users get `Roles.StandardUser` at registration (see `Register.razor` / `ExternalLogin.razor`).

## Entities, configurations, relationships

- Entities live in `Nova/Entities/`, one `IEntityTypeConfiguration<T>` per entity in
  `Nova/Data/Configurations/` (applied via `ApplyConfigurationsFromAssembly`). Put keys, FKs,
  delete behaviors, and indexes in configurations — not data annotations.
- Declare each relationship in ONE configuration only (the dependent's, by convention here);
  duplicate declarations across files drift and have caused bugs.
- Delete behavior: club-owned content cascades from `Club`; `Club → NovaUsers` is `SetNull` (users survive club deletion); optional assignment FKs (e.g. `PlayerCampaignAssignment.Team`) are `SetNull`; audit columns never get FKs.
- Club deletion is NOT interceptor-guarded (Club isn't tenant-owned) — any club-delete feature
  must be gated by `Policies.RequireClubAdmin` or `RequireAdmin`.

## Append-only activity events

- The activity feed is a durable, club-scoped event log (`ActivityEventEntity`). Rows are
  **append-only**: `TenantSaveChangesInterceptor` throws on any modify/delete in every context,
  including admin contexts. Never update or delete history; a correction is a new event, not an edit.
- Write events only through `ActivityEventWriter` (internal static) by passing the caller's open
  `ApplicationDbContext`, so the event commits atomically with the owning mutation. Do not open a
  second context to append an event. On execution-strategy retries, re-run the whole mutation on a
  fresh context and the event is re-added naturally.
- Display names (`ActorDisplayName` and every payload name) are **snapshots**, not navigations, so
  the feed stays readable after an actor leaves, a subject is renamed, or a Draft/team is removed.
  `CampaignId` and `ActorUserId` are deliberate loose snapshot keys with **no FK**.
- Every mutation that changes member- or admin-visible state emits an event through this boundary.
  Adding a new lifecycle/placement/join/membership/role transition means adding it to
  `ActivityEventPolicy` (kind → family, admin-only) plus a matching `*Context` type with a
  `[JsonDerivedType]` discriminator.
- One carve-out to the tenant guard: a club-less user may write their own
  `JoinRequestSubmitted`/`JoinRequestCancelled` event for the club they are requesting to join (the
  row carries the explicit target `ClubId`). This is the only club-less write path for a
  tenant-owned row; do not generalize it.

## Query construction and provider behavior

- Keep filtering, deterministic ordering, `Skip`, and `Take` in SQL before materialization; do not load an entire tenant result set to sort or page in memory. For provider-incompatible behavior, isolate a fallback and retain SQL-side execution for PostgreSQL.
- Avoid materializing identifier lists and feeding them back through large `Contains`/`IN`
  predicates when a mapped navigation can express the relationship directly. Navigation predicates
  keep SQL bounded and avoid parameter-list growth for long histories.
- Use provider helpers such as `db.Database.IsNpgsql()`/`IsSqlite()` rather than comparing
  `ProviderName` strings. For PostgreSQL case-insensitive contains search, prefer
  `EF.Functions.ILike`; use a clearly isolated provider-compatible fallback for SQLite tests.
- Treat user-supplied `LIKE`/`ILIKE` search text as a literal substring unless wildcard syntax is an explicit product feature. Escape the escape character first, then `%` and `_`, and pass the same explicit escape character to the PostgreSQL `ILike` overload.
- If results are ordered in SQL before `Take`/`Skip` and then ordered again after materialization, both orderings must use the same keys, directions, null semantics, and deterministic tie-breakers — otherwise the bounded SQL slice and the displayed order describe different result sets. Prefer preserving the database-returned order after materialization.
- A total count and its bounded rows are separate statements, not an atomic snapshot. Decide explicitly: document and tolerate eventual consistency, or use a provider-compatible snapshot only when correctness requires it. Clients must not enforce count relationships the contract cannot guarantee.
- Validation and normalization must have one explicit behavior. If `[Range]` rejects a page size,
  do not also clamp it after validation; either reject or cap, and make the contract, service, and
  tests agree.
- SQLite cannot translate `ORDER BY` over `DateTimeOffset` columns, and Npgsql binds only
  offset-zero `DateTimeOffset` values to `timestamptz`. Normalize `DateTimeOffset` cursor/timestamp
  values to UTC (`ToUniversalTime`) before binding, and where a `DateTimeOffset` ordering is required,
  keep SQL-side ordering on PostgreSQL and fall back to materialize-then-order in memory on SQLite
  with identical keys, directions, and null semantics.
- For unbounded newest-first feeds, prefer keyset paging over `Skip`/`Take`: `Skip` drifts when rows
  are inserted or removed between pages, while a keyset predicate over the stable ordering key
  `(OccurredAt, Id)` stays deterministic. The keyset cursor marks the page boundary, not the newest
  row, when projection can skip rows.

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
- After any model change, use the `add-domain-persistence` skill to add an incremental migration, then verify with `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext`.
- `Nova/Program.cs` applies migrations at startup in Development only and seeds roles in all environments through the execution strategy.
- **No backwards compatibility, no data preservation.** There is no production data; local dev databases are disposable (Aspire provisions them fresh). Migrations do not need to be data-preserving or backwards-compatible: prefer the simplest schema for the current model, and never add legacy-tolerant columns, tables, or query paths for data that does not exist.

## Testing data access

See `.github/instructions/testing.instructions.md` for the required tenancy filter-coverage tests.
