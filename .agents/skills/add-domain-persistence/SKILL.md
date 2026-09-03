---
name: add-domain-persistence
description: >-
  Builds a Nova domain/persistence slice: deterministic domain policies, entities, EF configuration, tenant integrity, lifecycle/concurrency guards, retry-safe transactions, incremental migration, registration, and focused tests.
  USE FOR: add a domain policy/decision; add or change an entity, relationship, constraint, index, lifecycle state, domain service, optimistic concurrency, migration, tenant-owned persistence, advisory mutation lock, retrying transaction, ambiguous commit, idempotency key, provider-sensitive query, ILIKE escaping, bounded query ordering, keyset paging, append-only event log, polymorphic JSON payload, partial-failure projection.
  DO NOT USE FOR: extracting policy logic from an existing service without domain/persistence changes (use extract-functional-core), HTTP/WASM/UI feature slices (use add-feature-slice), a single endpoint (use add-api-endpoint), only writing/running tests (use nova-testing).
  INVOKES: nova-testing.
---

# Add Domain Persistence

Use this skill for server-side domain and persistence work that does not itself require HTTP,
WebAssembly, or Razor surfaces, or as the persistence phase of `add-feature-slice`.

Canonical examples:

- Participation integrity and concurrency: `PlayerCampaignAssignmentEntity`,
  `PlayerCampaignAssignmentEntityConfiguration`, `CampaignPlacementService`.
- Lifecycle and transaction races: `CampaignEntity`, `CampaignLifecycleService`,
  `LifecycleMutationLock`.
- Append-only activity events: `ActivityEventEntity`, `ActivityEventEntityConfiguration`,
  `ActivityEventWriter`, `ActivityEventPolicy`, `ClubActivityFeedPolicy`.
- Retry-safe and idempotent mutations: `TeamManagementService`, `TeamLifecycleService`,
  `TeamEntityConfiguration`, `TeamManagementRetryTests`, `TeamLifecycleRetryTests`.
- Multi-entity lock order and dynamic lock-set races: `TeamManagementService.UpdateTeamAsync`,
  `TeamPlayerGraduationYearRaceTests`.
- Provider-safe search and bounded ordering: `TeamRosterQueryService`, `TeamDetailQueryService`.
- Tenant-safe history: `CampaignTagApplicationEntity`, `NoteEntity`, and their configurations.

## Ordered checklist

1. **Define invariants first** — identify tenant ownership, lifecycle states, history requirements,
   authorization, uniqueness, delete behavior, and concurrency/race boundaries.
   When adding a lifecycle state, search the enum symbol repository-wide and explicitly classify
   the new state across mutation guards, capabilities, query filters, projections, endpoint/query
   binding, strict WASM validators, presentation ordering, and tests. Persisted numeric enum values
   are identity, not presentation order; use explicit allowlists and sort ranks unless numeric
   ordering or contiguity is contractual. When the state crosses HTTP/WASM, use `add-feature-slice`
   or `add-api-endpoint` for the affected boundary instead of stopping at persistence.
2. **Choose the decision boundary** — when rules form a non-trivial deterministic matrix, follow
   [functional-core-imperative-shell.md](references/functional-core-imperative-shell.md). Keep simple
   guards inline or on the entity. Logic-only policy work may skip every persistence-specific step.
3. **Model the entity, when needed** — add or update the entity under `Nova\Entities`; use a real `ClubId` FK plus
   `Club` navigation and `ITenantOwnedEntity` for club-scoped rows. Put shared enums in `Nova.Shared`.
4. **Configure persistence, when needed** — use one `IEntityTypeConfiguration<T>` under
   `Nova\Data\Configurations`; define keys, tenant-consistent composite FKs, indexes, check
   constraints, delete behavior, and concurrency tokens there.
5. **Implement domain operations** — use the correct DbContext factory, repeat service-layer
   authorization, preserve history, and enforce invariants transactionally. For lifecycle-sensitive
   writes, follow [retrying-mutations-and-locks.md](references/retrying-mutations-and-locks.md).
   For provider-sensitive search, paging, or bounded history queries — including keyset paging and
   partial-failure projections — follow [query-construction.md](references/query-construction.md).
   For an append-only event log with family-shaped snapshots, follow
   [append-only-activity-events.md](references/append-only-activity-events.md).
6. **Wire the application, when needed** — expose required `DbSet<T>` members and register each
   application-consumed service in `Nova\Program.cs`. Pure static policies require no DI registration.
7. **Add one incremental migration, when the model changed** — preserve the migration chain and generate against
   `NovaDbContext`:

   ```powershell
   dotnet ef migrations add <Name> --project Nova --context NovaDbContext
   ```

   Inspect `Up`, `Down`, and the model snapshot; document intentional destructive cleanup.
8. **Invoke `nova-testing`** — add direct policy tests and provider-agnostic service/invariant tests,
   tenancy visibility and
   cross-tenant-write coverage for every new tenant-owned entity, and PostgreSQL tests for migrations,
   constraints, mappings, advisory locks, or competing transactions.
9. **Verify model when persistence changed, then build**:

   ```powershell
   dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext
   dotnet build Nova.slnx
   ```

   Skip the EF command when entities/configuration are unchanged. Expect no pending model changes
   when applicable and a clean build.
