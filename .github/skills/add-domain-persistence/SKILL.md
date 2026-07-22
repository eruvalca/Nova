---
name: add-domain-persistence
description: >-
  Builds a Nova domain/persistence slice: entities, EF configuration, tenant integrity, lifecycle/concurrency guards, incremental migration, registration, and focused tests.
  USE FOR: add or change an entity, relationship, constraint, index, lifecycle state, domain service, optimistic concurrency, migration, tenant-owned persistence, advisory mutation lock.
  DO NOT USE FOR: HTTP/WASM/UI feature slices (use add-feature-slice), a single endpoint (use add-api-endpoint), only writing/running tests (use nova-testing).
  INVOKES: nova-testing.
---

# Add Domain Persistence

Use this skill for server-side domain and persistence work that does not itself require HTTP,
WebAssembly, or Razor surfaces, or as the persistence phase of `add-feature-slice`.

Canonical examples:

- Participation integrity and concurrency: `PlayerCampaignAssignmentEntity`,
  `PlayerCampaignAssignmentEntityConfiguration`, `CampaignPlacementService`.
- Lifecycle and transaction races: `CampaignEntity`, `CampaignLifecycleEventEntity`,
  `CampaignLifecycleService`, `LifecycleMutationLock`.
- Tenant-safe history: `CampaignTagApplicationEntity`, `NoteEntity`, and their configurations.

## Ordered checklist

1. **Define invariants first** — identify tenant ownership, lifecycle states, history requirements,
   authorization, uniqueness, delete behavior, and concurrency/race boundaries.
2. **Model the entity** — add or update the entity under `Nova\Entities`; use a real `ClubId` FK plus
   `Club` navigation and `ITenantOwnedEntity` for club-scoped rows. Put shared enums in `Nova.Shared`.
3. **Configure persistence** — use one `IEntityTypeConfiguration<T>` under
   `Nova\Data\Configurations`; define keys, tenant-consistent composite FKs, indexes, check
   constraints, delete behavior, and concurrency tokens there.
4. **Implement domain operations** — use the correct DbContext factory, repeat service-layer
   authorization, preserve history, and enforce invariants transactionally. For lifecycle-sensitive
   writes, use `LifecycleMutationLock`, read or reload state after locking, and preserve lock order:
   campaign → player → team → tag.
5. **Wire the application** — expose required `DbSet<T>` members and register each application-consumed
   service in `Nova\Program.cs`.
6. **Add one incremental migration** — preserve the migration chain and generate against
   `NovaDbContext`:

   ```powershell
   dotnet ef migrations add <Name> --project Nova --context NovaDbContext
   ```

   Inspect `Up`, `Down`, and the model snapshot; document intentional destructive cleanup.
7. **Invoke `nova-testing`** — add provider-agnostic service/invariant tests, tenancy visibility and
   cross-tenant-write coverage for every new tenant-owned entity, and PostgreSQL tests for migrations,
   constraints, mappings, advisory locks, or competing transactions.
8. **Verify model and build**:

   ```powershell
   dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext
   dotnet build Nova.slnx
   ```

   Expect no pending model changes and a clean build.
