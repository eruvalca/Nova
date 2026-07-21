# Issue #19 Archival Lifecycle

Implement the persistence and reusable server-side lifecycle foundation from GitHub issue #19 for players, teams, and tag definitions. Scope includes consistent archive metadata, administrator-only transitions, placement-integrity guards, one incremental migration, and focused tests; HTTP endpoints, clients, and UI are excluded.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

## Confirmed Decisions

- Implement issue #19 only in this worktree; issue #14 is the completed prerequisite.
- Add one shared `LifecycleStatus` enum with `Active` and `Archived` values to `PlayerEntity`, `TeamEntity`, and `PlayerTagEntity`.
- Add nullable `ArchivedAt` and FK-less `ArchivedById` to each entity, with PostgreSQL/EF check constraints requiring both fields null for Active and both populated for Archived.
- Existing rows are initialized as Active by the migration; no historical records are deleted.
- Add internal, DI-registered `PlayerLifecycleService`, `TeamLifecycleService`, and `TagDefinitionLifecycleService` operations using `NovaDbContext`, `ICurrentUserProvider`, native OneOf results, tenant filters, and source-generated logging.
- Lifecycle transitions are idempotence-safe conflicts: archiving an archived record or restoring an active record is rejected rather than silently succeeding.
- PostgreSQL transaction-scoped advisory locks serialize lifecycle-sensitive player/team mutations with placement writes; lifecycle status is also an EF concurrency token so stale transitions cannot overwrite provenance.
- Until issue #20 adds explicit campaign lifecycle state, an Active campaign means the current `!Campaign.IsComplete` model (`EndDate` is null).
- Player archive rejects any Undecided participation in an Active campaign and never changes participation outcomes.
- Team archive rejects any placement referencing that team in an Active campaign. Team graduation-year changes reject values that would make any Active-campaign assigned player ineligible.
- Extend `CampaignPlacementService` to reject archived players and archived teams before changing placement state.
- Archived records remain tenant-queryable for historical use. Future-enrollment filtering and tag-application guards remain responsibilities of downstream workflows identified by issue #19.
- Do not add endpoints, shared service interfaces, WebAssembly clients, UI-specific DTOs, Razor UI, or campaign lifecycle state.

## Phase 1: Add Lifecycle Persistence

Status: Complete

- [x] Add the shared lifecycle enum and lifecycle/archive properties to players, teams, and tag definitions.
- [x] Configure lifecycle status values and archive metadata consistency constraints for all three entities.
- [x] Confirm archived rows remain visible through existing tenant query filters.

### Verification Plan

- `dotnet build Nova.slnx` reports 0 errors.
- Focused model/tenancy tests verify lifecycle defaults, constraint metadata, tenant visibility, and cross-tenant write guards.

### Phase Summary

Added the shared `LifecycleStatus` enum and `ArchivableEntity` base with status, timestamp, and FK-less actor provenance. Players, teams, and tag definitions now share that persistence representation, and each table has a constraint that accepts only a fully empty Active archive tuple or fully populated Archived tuple. Lifecycle status is configured as an EF concurrency token, and archived rows intentionally remain visible to existing tenant filters. The solution build completed with 0 errors and the baseline 12 package-vulnerability warnings.

## Phase 2: Add Lifecycle Domain Services

Status: Complete

- [x] Add administrator-only, tenant-safe player archive and restore operations.
- [x] Reject player archive while an Active-campaign participation remains Undecided, preserving all outcomes.
- [x] Add administrator-only, tenant-safe team archive, restore, and graduation-year update operations.
- [x] Reject team archive or graduation-year changes that would invalidate Active-campaign placements.
- [x] Add administrator-only, tenant-safe tag-definition archive and restore operations.
- [x] Register all lifecycle services and use native OneOf results plus source-generated logging.
- [x] Add focused unit tests for success, transition conflicts, authorization, tenant isolation, blockers, audit provenance, restore behavior, and history preservation.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*LifecycleServiceTests"` discovers at least one test and all discovered tests pass.

### Phase Summary

Added and registered the three focused lifecycle services using tenant-filtered `NovaDbContext`, current-club administrator checks, shared native-OneOf forbidden/conflict variants, and source-generated logging. Player archival preserves outcomes and blocks only unresolved Active-campaign participation. Team archival blocks active placements, and graduation-year changes block newly ineligible active placements. Restore clears archive provenance. Transaction-scoped PostgreSQL advisory locks serialize player/team lifecycle checks with placement writes. The focused lifecycle filter discovered and passed 14 tests.

## Phase 3: Guard Placement and Add Migration Coverage

Status: Complete

- [x] Reject new placement mutations for archived players and archived teams.
- [x] Generate one named incremental migration through `NovaDbContext`.
- [x] Verify the migration initializes existing rows as Active and adds all lifecycle columns and consistency constraints.
- [x] Add PostgreSQL tests for clean migration application and all three lifecycle consistency constraints.
- [x] Extend tenancy/history tests for archived-record visibility and cross-tenant lifecycle protection.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*CampaignPlacementServiceTests"` discovers tests and passes.
- `dotnet test --project Nova.Unit.Tests --filter-class "*TenancyTests"` discovers tests and passes.
- `dotnet test --project Nova.Integration.Tests --filter-class "*ArchivalLifecycle*"` discovers at least one test and all discovered tests pass.
- `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext` reports no pending model changes.

### Phase Summary

Extended `CampaignPlacementService` to return conflicts for archived players and teams and to share the lifecycle mutation locks. Generated `AddArchivalLifecycle`, which adds nullable archive provenance and non-null status columns defaulting existing rows to Active before adding all three constraints. PostgreSQL tests cover clean migration application, partial/Active provenance rejection on every table, undefined status rejection, and stale lifecycle transitions. Focused placement, tenancy, and archival PostgreSQL filters passed 14/14, 23/23, and 9/9 tests respectively; EF reports no pending model changes.

## Phase 4: Final Verification

Status: Complete

- [x] Run the complete unit test project.
- [x] Run the complete integration test project.
- [x] Build the solution.
- [x] Review the final diff against issue #19 acceptance criteria and confirm no endpoint/client/UI or campaign-lifecycle scope was introduced.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests` passes.
- `dotnet test --project Nova.Integration.Tests` passes.
- `dotnet build Nova.slnx` reports 0 errors.
- `git diff --check` reports no whitespace errors.

### Phase Summary

The complete unit project passed 428/428 tests and the complete Aspire integration project passed 44/44 tests. The final solution build completed with 0 errors and the same 12 package-vulnerability warnings present at baseline. `git diff --check` passed. A read-only review identified cross-table placement/lifecycle races; the implementation was hardened with shared PostgreSQL advisory transaction locks and lifecycle-status concurrency checks, then a second review reported no significant issues. The diff contains no endpoints, clients, UI, or campaign lifecycle state.

## Final Recap

Implemented issue #19 end to end. Players, teams, and tag definitions now share constrained Active/Archived persistence with FK-less provenance, optimistic lifecycle concurrency, administrator-only tenant-safe archive/restore operations, and historical visibility. Player and team transition guards preserve active-campaign integrity, team cutoff changes cannot invalidate active placements, and placement mutations reject archived targets. Added the incremental migration plus focused SQLite and PostgreSQL coverage, with all repository tests passing.

## Deployment Plan

1. Apply `AddArchivalLifecycle` through `NovaDbContext` using the normal deployment migration process; existing player, team, and tag-definition rows are initialized as Active.
2. Deploy the updated Nova server and shared assembly together so the lifecycle enum, EF model, services, and placement guards remain version-aligned.
3. No endpoint, client, UI, feature-flag, or manual data-backfill rollout is required for this foundation-only change.
