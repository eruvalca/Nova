---
name: add-activity-feed
description: >-
  Builds a durable append-only activity event log plus its role-shaped feed and attention badge
  projections in Nova: ActivityEventEntity and ActivityEventWriter, family-shaped polymorphic
  payloads, role visibility filtering, keyset paging, and per-region attention counts.
  USE FOR: add an activity/attention foundation, add a new activity event kind, add a role-shaped
  feed or badge count, durable event log, keyset paging, polymorphic activity payload, attention
  projection with partial failures.
  DO NOT USE FOR: a generic entity/relationship (use add-domain-persistence), a single endpoint on an
  existing service (use add-api-endpoint), a generic bounded read-only query API (use
  add-feature-slice), or only writing/running tests (use nova-testing).
  INVOKES: add-domain-persistence (append-only event entity), add-api-endpoint (feed/attention
  endpoints and polymorphic DTOs), add-blazor-ui (UI step), nova-testing (test step).
---

# Add Activity Feed

Use this skill when building or extending Nova's durable activity foundation: an append-only,
tenant-owned event log that surfaces as a role-shaped, keyset-paged feed, plus administrator attention
badge counts derived from live state.

Canonical examples:

- Write boundary: `Nova\Features\Activity\ActivityEventWriter.cs`
- Kind → family → visibility rules: `Nova\Features\Activity\ActivityEventPolicy.cs`
- Role-shaped projection + keyset: `Nova\Features\Activity\ClubActivityFeedPolicy.cs`
- Read service: `Nova\Features\Activity\ClubActivityQueryService.cs`
- Badge counts: `Nova\Features\Attention\ClubAttentionQueryService.cs`
- Polymorphic contexts: `Nova.Shared\Features\Activity\ClubActivityContexts.cs`
- DTOs/cursor: `Nova.Shared\Features\Activity\ClubActivityContracts.cs`,
  `Nova.Shared\Features\Activity\ClubActivityCursor.cs`
- WASM client: `Nova.Client\Services\Activity\HttpClubActivityQueryService.cs`

## Ordered checklist

1. **Durable event entity** — invoke `add-domain-persistence` and follow
   [append-only-activity-events.md](../add-domain-persistence/references/append-only-activity-events.md):
   model `ActivityEventEntity` (append-only, snapshots not navigations, family-shaped `PayloadJson`),
   add the interceptor guard, and add the incremental migration. Adding a new event kind requires no
   migration.
2. **Write boundary** — add the `Append*` method to `ActivityEventWriter` and the kind → family /
   admin-only mapping to `ActivityEventPolicy`. Emit the event from the owning mutation on its open
   context so it commits atomically.
3. **Shared contracts** — add the family `*Context` record (with `[JsonDerivedType]`), the cursor, the
   item/result DTOs, and the input record in `Nova.Shared\Features\Activity\`. Use
   `IValidatableObject` for the cursor's both-or-neither rule (see
   [add-api-endpoint validation](../add-api-endpoint/references/validation-and-problemdetails.md)).
4. **Feed query service** — implement `IClubActivityQueryService` against `NovaReadDbContext`, then
   project role-shaped pages through `ClubActivityFeedPolicy`; follow
   [role-shaped-feed.md](references/role-shaped-feed.md) for visibility filtering and actor redaction,
   and [query-construction.md](../add-domain-persistence/references/query-construction.md) for keyset
   paging.
5. **Attention projection** — for badge counts, keep each region's status separate and failure-aware
   per [query-construction.md](../add-domain-persistence/references/query-construction.md)
   **Partial-failure projections**.
6. **HTTP endpoints** — invoke `add-api-endpoint`: map `GET /api/activity` and the attention endpoint,
   use `[AsParameters] GetClubActivityInput`, `Produces<ClubActivityResult>`, and
   `RequireAuthorization(Policies.RequireClubMember)` (administrator-only for attention).
7. **WASM client** — add `HttpClubActivityQueryService` / `HttpClubAttentionQueryService` following the
   `add-feature-slice` [wasm-client.md](../add-feature-slice/references/wasm-client.md) recipe.
8. **UI** — invoke `add-blazor-ui` to render the feed and badge counts (load-more with the cursor,
   role-aware copy).
9. **Tests** — invoke `nova-testing`: direct policy tests for kind/visibility/transition rules and the
   role-shaped projection, provider-agnostic service tests, PostgreSQL tests for append-only integrity
   and keyset continuation, and HTTP tests for cursor binding and role visibility.

## Required references

- [role-shaped-feed.md](references/role-shaped-feed.md) — role visibility filtering, actor redaction,
  and the projection-cursor interaction.
- [../add-domain-persistence/references/append-only-activity-events.md](../add-domain-persistence/references/append-only-activity-events.md) —
  the durable event log, snapshots, polymorphic payloads, and adding a new kind.
- [../add-domain-persistence/references/query-construction.md](../add-domain-persistence/references/query-construction.md) —
  keyset paging and partial-failure projections.
- [../add-api-endpoint/references/validation-and-problemdetails.md](../add-api-endpoint/references/validation-and-problemdetails.md) —
  polymorphic response payloads and `IValidatableObject` cursor validation.
