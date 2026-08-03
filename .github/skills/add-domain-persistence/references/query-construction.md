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
