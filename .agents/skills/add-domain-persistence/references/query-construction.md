# Provider-safe query construction

Canonical Nova examples:

- `Nova\Features\Teams\TeamRosterQueryService.cs`
- `Nova\Features\Teams\TeamDetailQueryService.cs`
- `Nova.Unit.Tests\Features\Teams\TeamRosterQueryServiceTests.cs`
- `Nova.Unit.Tests\Features\Teams\TeamDetailQueryServiceTests.cs`
- `Nova.Integration.Tests\Http\TeamRosterHttpTests.cs`

## Literal PostgreSQL contains search

When user input is intended as a literal substring, do not pass it directly into an `ILIKE` pattern.
Escape in this order:

1. Backslash, because it is the escape character.
2. `%`, because it matches any sequence.
3. `_`, because it matches one character.

Pass the same explicit escape character to `EF.Functions.ILike`. Keep a clearly isolated SQLite
fallback, such as normalized `Contains`, when the provider cannot translate the production shape.
Tests must prove literal `%`, literal `_`, and backslash behavior.

## Bounded SQL and in-memory ordering

Apply filtering, deterministic ordering, and `Take`/`Skip` in SQL. If the materialized bounded rows
must be ordered again in memory, repeat the exact SQL keys, directions, null behavior, and stable
tie-breakers. A different in-memory order cannot recover rows excluded by the SQL bound and can
silently change which items appear active or first.

Prefer preserving the database-returned order after materialization. `TeamDetailQueryService` is the
canonical pattern: campaign-active flag, campaign start date, campaign ID, player display name, and
player ID are applied before `Take`, and the materialized rows are projected without a second sort
that could use different collation semantics.

## Count and page consistency

`CountAsync` followed by a bounded projection is two reads. Under normal read-committed behavior,
concurrent changes can make the total briefly lag the rows. Choose and document one contract:

- Prefer an eventually consistent total for ordinary list/setup reads; validate the bound and
  non-negative total without assuming `TotalCount >= returned rows`.
- Use a provider-compatible snapshot only when an atomic count/page pair is a product requirement,
  not merely to satisfy defensive client validation.

## Keyset paging for unbounded feeds

For a newest-first feed with no fixed upper bound (an append-only event log, a history page), prefer
**keyset paging** over `Skip`/`Take`. `Skip` drifts when rows are inserted or removed between pages
(skipping or duplicating items); a keyset predicate over a stable, monotonic ordering key stays
deterministic. `ClubActivityQueryService` and `ClubActivityFeedPolicy` are the canonical pattern:

1. Order by `(OccurredAt DESC, Id DESC)` and probe `PageSize + 1` rows to detect whether another page
   exists.
2. The cursor is the final raw page row's `(OccurredAt, Id)` — the page boundary, computed *before*
   projection — not the oldest returned DTO or the newest row, because projection can skip malformed or
   kind/context-mismatched rows (role visibility filtering happens before paging).
3. The continuation predicate is
   `OccurredAt < cursor.OccurredAt || (OccurredAt == cursor.OccurredAt && Id < cursor.Id)`.
4. Emit `hasMore` from the `PageSize + 1` probe and a `NextCursor` only when more rows follow.

### DateTimeOffset provider caveats

- Npgsql binds only offset-zero `DateTimeOffset` values to `timestamptz`; normalize cursor/timestamp
  values to UTC (`ToUniversalTime()`) before binding.
- SQLite cannot translate `ORDER BY` over `DateTimeOffset` columns. Keep SQL-side ordering on
  PostgreSQL; on SQLite, materialize the club-scoped set and re-apply the exact same keys, directions,
  and null semantics in memory so projection and cursor rules stay identical across providers. The
  SQLite fallback is unbounded — do not `Take` before the in-memory order, or newer rows can be
  omitted.

## Partial-failure projections

When a read projection combines independent regions (attention badge counts, summary tiles), keep each
region's result/error state separate. A transient failure in one region must report an explicit
unavailable status for that region — never a misleading zero — and must not hide or zero the other
region. `ClubAttentionQueryService` is the canonical pattern:

- Load each region in its own context scope, catch per region, re-throw on cancellation, and return a
  region status enum (`Loaded`/`Unavailable`) plus the count only when loaded.
- When a count and a resolution target must agree (count + newest campaign), project both under one
  repeatable-read snapshot transaction so a concurrent change cannot make them disagree.
