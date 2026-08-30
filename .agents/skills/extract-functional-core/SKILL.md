---
name: extract-functional-core
description: >-
  Extracts deterministic business decisions from an existing Nova service into a feature-local functional core while preserving imperative-shell behavior, freshness, concurrency, and effects.
  USE FOR: refactor existing decision-heavy service logic, extract a pure policy, functional core imperative shell, make business rules directly testable, remove database/mock setup from deterministic rule tests.
  DO NOT USE FOR: adding a new entity/schema/domain slice (use add-domain-persistence), building a full feature (use add-feature-slice), adding an endpoint (use add-api-endpoint), or only writing/running tests (use nova-testing).
  INVOKES: nova-testing.
---

# Extract Functional Core

Use this skill to refactor an existing Nova service whose deterministic business decisions are
obscured by authorization, EF, tenancy, locking, persistence, logging, or other effects.

Follow the detailed repository recipe in
`../add-domain-persistence/references/functional-core-imperative-shell.md` and the declarative rules
in `.github/instructions/functional-core.instructions.md`.

## Ordered checklist

1. **Capture the behavior contract** — run focused service and provider/race tests and record result
   variants, error keys/messages/order, authorization and tenant behavior, lock/transaction order,
   writes, history, concurrency, and logging facts.
2. **Confirm extraction triggers** — proceed only for a meaningful rule matrix, repeated/drifting
   invariant, frequently changing rule, or deterministic branch with disproportionate shell setup.
   Leave simple guards and database-native queries where they are.
3. **Draw the freshness boundary** — identify authorization, tenant queries, transactions, lifecycle
   locks, post-lock reloads, concurrency, persistence, logging, and effects that must remain in the
   service. Never widen a stale-state window to make a policy pure.
4. **Define compact facts and outcomes** — add feature-local immutable `*State`, `*Facts`, or
   `*Context` values and domain-named native `OneOf` outcomes. Use a source-generated named union
   only for reused or multi-case public/service contracts where the domain name improves the API.
   Do not pass tracked entities, add a mock-only interface, register the policy in DI, or create a
   generic core/rule-engine project.
5. **Extract one decision at a time** — project fresh facts in the shell, call an `internal static
   *Policy`, handle every outcome with `Match` or `Switch`, then apply effects in the shell. Do not
   branch with positional `IsTn`/`AsTn` members. Preserve behavior exactly; separate enhancements
   from the extraction.
6. **Invoke `nova-testing`** — add direct database-free policy tests with real values and no mocks,
   retain representative SQLite shell tests, and retain PostgreSQL tests for provider behavior,
   advisory locks, concurrency, and races.
7. **Run the pilot gate** — compare pre/post behavior, build, run `git diff --check`, and obtain a
   read-only review focused on behavior drift, TOCTOU, tenant/authorization leakage, effect leakage,
   and over-abstraction. Revise or revert an extraction that does not improve clarity and testability.

Canonical examples:

- `Nova\Features\Campaigns\CampaignClosurePolicy.cs`
- `Nova\Features\Campaigns\CampaignPlacementPolicy.cs`
- `Nova\Features\Account\AccountDeletionPolicy.cs`
