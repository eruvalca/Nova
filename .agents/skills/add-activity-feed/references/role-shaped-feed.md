# Role-shaped feed

Canonical Nova examples:

- `Nova\Features\Activity\ClubActivityFeedPolicy.cs`
- `Nova\Features\Activity\ClubActivityQueryService.cs`
- `Nova.Shared\Features\Activity\ClubActivityContexts.cs` (the `MembershipContext.ApprovedByActorName`
  member/admin shape split)

## Role visibility filtering

Persist a stored visibility flag on the event row (`ActivityEventEntity.IsAdminOnly`), computed by a
pure kind → visibility rule (`ActivityEventPolicy.IsAdminOnly`). Apply the filter as
`isAdmin || !row.IsAdminOnly` so administrators see everything and members see only the non-admin
subset. Push the filter into SQL on PostgreSQL; on SQLite materialize the club's rows and apply the
same predicate in memory.

## Actor redaction

The same event can project to different shapes per viewer. A `MemberJoined` event carries the
approving administrator snapshot in its payload; members see a subject-led shape ("Sam Doe joined the
club") while administrators see the approval action ("Jordan Lee approved Sam Doe's membership").
Project the row, then for the member view strip the actor fields (`ActorUserId`, `ActorDisplayName`,
and the payload's `ApprovedByActorName`) rather than selecting a different row.

## Projection-cursor interaction

Role visibility filtering happens *before* paging (`isAdmin || !row.IsAdminOnly`), so member pages are
never padded with hidden rows. Projection runs *after* paging and only skips malformed or
kind/context-mismatched payloads — not administrator-only rows. The keyset cursor is computed from the
final raw page row *before* projection, which may be malformed and absent from the returned DTOs, so
it marks the **page boundary**, not the oldest returned DTO, and a skipped row does not strand the
following pages. See
[query-construction.md](../../add-domain-persistence/references/query-construction.md) for the keyset
predicate and `Take(PageSize + 1)` hasMore probe.

## Attention badge counts

Badge counts are derived live from current state (pending join requests, campaigns needing placement),
not from the event log. Keep each region's count/status independent and failure-aware: an unavailable
region reports `AttentionRegionStatus.Unavailable`, never a misleading zero, and never hides another
region. See **Partial-failure projections** in
[query-construction.md](../../add-domain-persistence/references/query-construction.md).
