# Functional Core, Imperative Shell Recipe

Use this recipe for a deterministic domain decision inside a server feature. The canonical Nova
example is `CampaignClosurePolicy`, called by `CampaignLifecycleService.CloseAsync`.

## 1. Confirm extraction pays for itself

Extract when rules form a meaningful matrix, repeat, change frequently, risk drifting, or require
substantial service/database setup to test. Keep a simple guard or one comparison inline. Do not
introduce a generic rules framework, a new class-library project, or an interface solely for mocks.

## 2. Draw the shell/core boundary

List the effectful responsibilities that remain in the service:

- input validation and authorization;
- tenant-safe EF queries and compact projections;
- transactions, lifecycle mutation locks, and post-lock reloads;
- optimistic concurrency, time/random value resolution, persistence, and logging.

Define the smallest immutable `*State`, `*Facts`, or `*Context` value that contains only facts needed
by the decision. Never pass a `DbContext`, service, tracked entity, or navigation graph.

For freshness-sensitive decisions, use this order:

1. begin the transaction;
2. acquire locks in the shared campaign → player → team → tag order;
3. read or reload current state;
4. project the decision snapshot;
5. evaluate once;
6. apply approved effects and persist.

## 3. Implement the policy

Place an `internal static *Policy` beside its feature service. Give outcomes domain names and return
native `OneOf`; do not return `ServiceResult` from the policy. Keep evaluation deterministic and
free of EF, DI, ambient users, clocks, randomness, configuration, logging, I/O, and mutable static
state.

```csharp
var assignmentStates = await db.PlayerCampaignAssignments
    .Where(assignment => assignment.CampaignId == campaignId)
    .Select(assignment => new CampaignAssignmentClosureState(/* compact facts */))
    .ToListAsync(cancellationToken);

var decision = CampaignClosurePolicy.Evaluate(assignmentStates);
return await decision.Match(
    ApplyClosureAsync,
    RejectClosureAsync);
```

Use `Match` when each case produces a value and `Switch` when every case only performs side effects.
Give handlers domain names; do not branch with positional `IsTn`/`AsTn` members. Use a
source-generated named OneOf union for reused or multi-case public/service contracts, not for every
small two-case policy.

Preserve result variants, blocker keys/messages, ordering, and effect semantics during extraction.
Make any intended behavior enhancement as a separate change.

## 4. Test in layers

1. Add direct `Nova.Unit.Tests` policy tests using constructed values and no harness, mocks, DI, or
   logger. Use `[Theory]` for a tabular combination matrix.
2. Retain representative SQLite service tests for authorization, tenancy, projections, rejected
   no-write behavior, and successful effects.
3. Retain or add PostgreSQL integration tests for provider behavior, advisory locks, constraints,
   concurrency, and transaction races.

Expose internal policies to `Nova.Unit.Tests` with `InternalsVisibleTo` only when needed; do not make
production policy types public merely for tests.

## 5. Verify the extraction

- Compare pre/post result shapes, messages, lock/transaction order, writes, history, and logging facts.
- Run the smallest policy and service filters together, repeating `--filter-class` for each class.
- Run focused PostgreSQL tests when lifecycle locks, concurrency, or provider behavior are involved.
- Build the solution and run `git diff --check`.
