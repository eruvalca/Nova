---
applyTo: "Nova/Features/**/*Service.cs,Nova/Features/**/*Policy.cs,Nova/Features/**/*Decision.cs,Nova/Entities/**/*.cs,Nova.Unit.Tests/**/*PolicyTests.cs"
description: "Functional-core rules for extracting deterministic business decisions while keeping authorization, EF, locking, persistence, and other effects in application services."
---

# Functional Core, Imperative Shell

Use this pattern selectively when it makes business decisions easier to understand and test. The
canonical example is `Nova/Features/Campaigns/CampaignClosurePolicy.cs`, composed by
`CampaignLifecycleService.CloseAsync`.

## When to extract

Consider a feature-local policy when at least one of these is true:

- a non-trivial matrix combines several business facts;
- the same invariant is repeated or likely to drift across branches;
- reaching a deterministic branch through the service requires substantial database, tenancy, or
  mock setup;
- a frequently changing rule can be expressed with explicit domain inputs and outcomes.

Do not extract a simple CRUD guard, one obvious comparison, a query best evaluated by the database,
or an abstraction that lacks domain vocabulary. Do not add a generic rule engine or a separate
`Core` project.

## Boundary

Follow an impure/pure/impure flow:

1. The service authorizes, validates tenant visibility, acquires transactions and lifecycle locks,
   reloads freshness-sensitive state, and projects compact immutable facts.
2. A deterministic policy evaluates those facts and returns an explicit domain outcome.
3. The service applies the approved effects, handles concurrency, persists, and logs.

The shell owns authorization, tenant filtering, efficient EF queries, transactions, mutation locks,
post-lock reloads, optimistic concurrency, persistence, logging, and effect execution. Never move
these responsibilities into a policy.

The core owns only decisions or calculations over explicit immutable values. It must not access EF,
DI, `IServiceProvider`, ambient users, clocks, randomness, configuration, logging, the network,
mutable static state, tracked entities, or navigation graphs. Resolve required values in the shell
and pass them explicitly.

## Freshness and performance

- Acquire required locks and read or reload lifecycle state before constructing the decision
  snapshot. Extraction must not introduce a time-of-check/time-of-use gap.
- Project only the facts the policy needs. Do not materialize large datasets solely to make code
  appear pure; aggregate or filter in the database when that is the correct execution location.
- Keep effect-producing values such as the current timestamp or a generated concurrency token in
  the shell unless the value itself is required to make the decision.

## Placement and results

- Keep a single-entity invariant on the entity when it naturally protects that entity's valid state.
- Use a feature-local `*Policy` for decisions over facts from multiple entities or contexts.
- Keep orchestration in the application `*Service`.
- Prefer `internal static` policies with immutable `*State`, `*Facts`, or `*Context` values and
  domain-named outcomes. Do not add DI registration or a mock-only interface for a pure policy.
- Return native `OneOf` outcomes from an internal policy. Map them to `ServiceResult` only at a
  cross-tier service boundary.
- Consume policy outcomes with exhaustive `Match` for value-producing branches or `Switch` for
  side-effect-only branches. Do not use positional `IsTn`/`AsTn` checks in the shell.
- Use a source-generated named OneOf union when a result shape is reused, forms a public or service
  contract with several cases, or benefits from a domain name. Do not generate a wrapper for every
  small, single-use policy result.

## Testing

- Test pure policies directly in `Nova.Unit.Tests` with constructed values, no database harness,
  DI, mocks, logger, or service setup. Use `[Theory]` matrices when combinations are tabular.
- Keep representative SQLite service tests for authorization, tenancy, projection, no-write
  rejection, and effect application.
- Keep PostgreSQL integration tests for migrations, provider translation, advisory locks,
  constraints, concurrency, and transaction races.
- Preserve behavior during extraction: result variants, messages, ordering, persistence effects,
  lock order, and logging facts. Make intentional behavior changes separately.

## Related

- `.github/instructions/service-layer.instructions.md`
- `.github/instructions/testing.instructions.md`
- `.github/instructions/validation.instructions.md`
- `.github/skills/extract-functional-core/`
- `.github/skills/add-domain-persistence/references/functional-core-imperative-shell.md`
