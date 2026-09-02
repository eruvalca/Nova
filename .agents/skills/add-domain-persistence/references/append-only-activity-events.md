# Append-only activity events

Canonical Nova examples:

- `Nova\Entities\ActivityEventEntity.cs`
- `Nova\Data\Configurations\ActivityEventEntityConfiguration.cs`
- `Nova\Features\Activity\ActivityEventWriter.cs`
- `Nova\Features\Activity\ActivityEventPolicy.cs`
- `Nova\Features\Activity\ClubActivityFeedPolicy.cs`
- `Nova\Features\Activity\ClubActivityQueryService.cs`
- `Nova.Integration.Tests\Data\ActivityEventPostgresTests.cs`

## The durable event log

The club activity feed is an append-only, tenant-owned event log. Each row records a `kind`, actor and
subject display-name **snapshots**, a stored visibility flag, and a family-shaped structured payload.
Application code never updates or deletes rows: `TenantSaveChangesInterceptor` throws on any
modify/delete in every context (including admin contexts), so a correction is a new event, not an
edit. The interceptor is not a database-level guarantee — it does not run for EF bulk
`ExecuteUpdate`/`ExecuteDelete`, which bypass the guard and can erase history, so those APIs must never
target `ActivityEventEntity`. The one intended removal path is database cascade delete when the owning
club is deleted.

## Writing events

Write only through `ActivityEventWriter` (internal static) by passing the caller's open
`ApplicationDbContext`, so the event commits atomically with the owning mutation. Do not open a second
context to append an event. On execution-strategy retries, re-run the whole mutation on a fresh
context and the event is re-added naturally.

## Snapshots, not navigations

Display names and every payload name are snapshots, so the feed stays readable after an actor leaves,
a subject is renamed, or a Draft/team is removed. `CampaignId` and `ActorUserId` are deliberate loose
snapshot keys with **no FK**.

## Family-shaped polymorphic payloads

One abstract `*Context` base (`ClubActivityContext`) carries several family-specific payloads. Mark the
base with `[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]` and one
`[JsonDerivedType(typeof(…), "…")]` per derived type. Serialize through the base type
(`JsonSerializer.Serialize(value, typeof(Base))`) so the discriminator is emitted; deserialize through
the base with `PropertyNameCaseInsensitive = true` (payloads are written camelCase). Treat a
missing/unknown discriminator or malformed payload as skip-not-crash: catch `JsonException` and
`NotSupportedException` and omit the row instead of failing the page.

## Adding a new event kind

A new lifecycle/placement/join/membership/role transition means:

1. Add the enum member to `ActivityEventKind` (no schema migration needed).
2. Add the kind → family and admin-only mappings to `ActivityEventPolicy`. Kinds are grouped into
   families (`ActivityEventFamily`); `FamilyFor` and `ContextMatchesKind` key off the family, not the
   kind.
3. Call it from the owning mutation on the caller's open context, reusing the family's existing
   `Append*` method and `*Context` record.

Only when the new kind belongs to a **new** family, additionally add:

- A `*Context` record registered on the base via `[JsonDerivedType]`.
- An `Append*` method to `ActivityEventWriter` that validates the family and serializes the payload.

## Tenant carve-out

One carve-out to the tenant guard: a club-less user may write their own
`JoinRequestSubmitted`/`JoinRequestCancelled` event for the club they are requesting to join (the row
carries the explicit target `ClubId`). This is the only club-less write path for a tenant-owned row;
do not generalize it.
