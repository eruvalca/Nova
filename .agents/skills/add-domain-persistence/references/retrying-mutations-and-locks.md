# Retrying mutations, idempotency, and advisory locks

Canonical Nova examples:

- `Nova\Features\Seasons\SeasonCommandService.cs`
- `Nova\Features\Campaigns\CampaignCreationService.cs`
- `Nova\Features\Teams\TeamManagementService.cs`
- `Nova\Features\Teams\TeamLifecycleService.cs`
- `Nova\Data\Configurations\TeamEntityConfiguration.cs`
- `Nova.Integration.Tests\Data\SeasonFoundationPostgresTests.cs`
- `Nova.Integration.Tests\Data\TeamManagementRetryTests.cs`
- `Nova.Integration.Tests\Data\TeamLifecycleRetryTests.cs`
- `Nova.Integration.Tests\Data\TeamPlayerGraduationYearRaceTests.cs`

## Retrying explicit transactions

When the provider enables a retrying execution strategy, put the complete explicit transaction
inside `CreateExecutionStrategy().ExecuteAsync`. Create and dispose a fresh `DbContext` and
transaction for every attempt; never replay with tracked state from the failed attempt.

Test both provider failure modes:

1. A transient failure before commit rolls the attempt back and retries with fresh state.
2. A lost commit acknowledgement verifies persisted success instead of replaying a non-idempotent
   mutation.

## Idempotent create

`TeamManagementService.CreateAsync` is the canonical pattern:

1. Generate one stable `Guid.CreateVersion7()` operation ID before the first attempt.
2. Store it on the inserted entity.
3. Add a tenant-scoped filtered unique index such as
   `(ClubId, CreationOperationId) WHERE CreationOperationId IS NOT NULL`.
4. Keep the natural business-key unique constraint as the final domain-integrity guard.
5. In `verifySucceeded`, query by tenant and operation ID using a fresh context and reconstruct the
   successful result.

For lifecycle transitions or other mutations without an operation ID, persisted target state may
have come from an earlier request. Follow `TeamLifecycleService`: reset a commit-attempt tracker at
the start of each attempt, mark it immediately before `CommitAsync`, and only let
`verifySucceeded` treat target state as proof when that attempt reached commit.

## Multi-entity advisory locks

Every writer of the same invariant must use the global entity-type order:
club-season → club-roster → campaign → player → team → tag. Acquire multiple locks of the same type
by ascending ID. Writers may take a subsequence, but never reverse it. Campaign creation is the
canonical club-season-then-club-roster example.

The canonical global order for the team/player eligibility invariant is campaign, players ascending,
then team. `TeamManagementService.UpdateTeamAsync` takes the players-then-team subsequence: it
computes the placed-player IDs, locks them in order, locks and reloads the team, reloads placement
facts, and returns a retryable conflict if a placement appeared for a player outside the locked set.

Add PostgreSQL tests that assert:

- The emitted lock order.
- Competing writers cannot jointly violate the invariant.
- A related row appearing outside the computed lock set reaches the fail-safe conflict.

SQLite cannot validate advisory-lock serialization.
