# Retrying mutations, idempotency, and advisory locks

Canonical Nova examples:

- `Nova\Features\Seasons\SeasonCommandService.cs`
- `Nova\Features\Campaigns\CampaignCreationService.cs`
- `Nova\Features\Campaigns\CampaignLifecycleService.Opening.cs`
- `Nova\Features\Teams\TeamManagementService.cs`
- `Nova\Features\Teams\TeamLifecycleService.cs`
- `Nova\Data\Configurations\TeamEntityConfiguration.cs`
- `Nova.Integration.Tests\Data\SeasonFoundationPostgresTests.cs`
- `Nova.Integration.Tests\Data\CampaignLifecyclePostgresTests.cs`
- `Nova.Integration.Tests\Data\CampaignLifecycleRetryTests.cs`
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

For lifecycle transitions, persisted target state is sufficient commit proof only when it uniquely
identifies the logical operation and cannot be overwritten before verification. A commit-attempt
tracker prevents state from an earlier request from proving success: reset it at the start of every
attempt, mark it immediately before `CommitAsync`, and reject state-based verification when the
attempt never reached commit. `TeamLifecycleService` is the canonical pattern for stable target
state.

When another operation can replace the target state before `verifySucceeded` runs, persist an
immutable operation receipt instead; a mutable security or concurrency stamp is not proof. Generate
one stable operation ID before the first attempt, add a uniquely constrained receipt through the
same context and transaction as every domain effect, and verify that receipt by operation ID through
a fresh context. Check aggregate and club deletion as well as later updates: cascading receipt
deletion can erase proof between commit and verification. Nova's FK-less receipt ownership rules
are in `.github/instructions/ef-core-tenancy.instructions.md`; do not copy an older receipt's FK
configuration without checking this boundary. `CampaignPlacementService` and
`CampaignPlacementRetryTests` demonstrate recovery after both a later save and club deletion.

Tenant-local pruning alone cannot reach receipts of deleted clubs. Provide an age-based cleanup
path reachable from operations in any tenant (or a background worker), with a `CreatedAt`-leading
index. The existing `ClubMembershipMutationReceipts.PruneExpiredAsync` handles membership and
placement receipts globally; it removes only expired receipt evidence through an admin context.
Receipts are commit proof, not history or effective-state inputs, and are not rewritten by later saves.

When the aggregate itself can retain immutable opening evidence, store the receipt on the aggregate
and reconstruct the original result from those persisted fields. Do not verify by recounting mutable
dependents that may have changed after the operation. `CampaignLifecycleService.OpenAsync` is the
canonical example.

For idempotent deletion without a separate receipt table, an append-only event may be the durable
tombstone when it snapshots the tenant, aggregate id, and required result evidence. Append it in the
same transaction as deletion, serialize competitors on the aggregate lock, and verify an ambiguous
commit only when both the tenant-scoped tombstone exists and the aggregate is absent.
`CampaignLifecycleService.DeleteDraftAsync` is the canonical example.

## Multi-entity advisory locks

Every writer of the same invariant must use the global entity-type order:
club-season → club-roster → campaign → player → team → tag. Acquire multiple locks of the same type
by ascending ID. Writers may take a subsequence, but never reverse it. Campaign creation is the
canonical club-season-then-club-roster example.

Club-membership writers use a separate shared order: user-membership locks by ascending user id,
then the club-membership lock, then any join-request lock. Club creation, join approval, explicit
member lifecycle mutations, and account deletion all participate because each can change
`NovaUserEntity.ClubId` or the `ClubAdmin` role. Re-read the actor, target, membership, roles, and
guard counts only after the required locks are held.

The canonical global order for the team/player eligibility invariant is campaign, players ascending,
then team. `TeamManagementService.UpdateTeamAsync` takes the players-then-team subsequence: it
computes the placed-player IDs, locks them in order, locks and reloads the team, reloads placement
facts, and returns a retryable conflict if a placement appeared for a player outside the locked set.

Add PostgreSQL tests that assert:

- The emitted lock order.
- Competing writers cannot jointly violate the invariant.
- A related row appearing outside the computed lock set reaches the fail-safe conflict.

Prove actual contention rather than starting operations back-to-back: hold or intercept the target
advisory lock, start every competitor, use a count-aware provider observation to prove the expected
number of distinct waiters reached the blocked lock acquisition, and only then release the gate.
Repeated existence checks can observe the same waiter and are not count evidence. A test that can
pass through sequential execution does not validate the lock.

SQLite cannot validate advisory-lock serialization.
