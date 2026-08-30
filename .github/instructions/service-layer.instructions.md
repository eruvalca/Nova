---
applyTo: "Nova/Features/**/*Service.cs,Nova.Shared/**/I*Service.cs,Nova.Shared/Results/**/*.cs,Nova.Client/Services/**/*.cs"
description: "Service-layer rules: validation, ServiceResult, retry-safe transactions, lifecycle locking, trace IDs, and logging."
---

# Service-Layer Rules

> Declarative rules only. For the **step-by-step recipe and full code examples** (ServiceProblem
> factories, service implementation, validation wiring), use the **`add-feature-slice`** skill
> (`.agents/skills/add-feature-slice/`).

## Dual-Layer Validation

Validate at **both** layers. The **endpoint** (DataAnnotations + `AddValidation()`) fast-rejects
structurally invalid HTTP requests before the handler runs; the **service**
(`InputValidator.Validate<T>(input)`) re-runs the same attributes plus business rules and is the
authoritative boundary for every caller. SSR pages, background jobs, and direct callers bypass
endpoint validation, so both layers read the **same attributes**. See
`.github/instructions/validation.instructions.md`.

## ServiceProblem / ServiceResult types

Defined in `Nova.Shared.Results`:

- **ServiceProblem** — readonly record struct for a known failure: a `Kind`, optional `Detail`, and
  optional structured `Errors` dictionary. Maps to HTTP status + RFC 7807 ProblemDetails.
- **ServiceResult<T>** — OneOf union of success (`T`) or failure (`ServiceProblem`). Use whenever a
  service boundary is crossed.
- **ServiceProblemKind** — `NotFound`, `Forbidden`, `Conflict`, `BadRequest`, `Validation`, `ServerError`.

Construct problems via the `ServiceProblem` factory methods (`NotFound`, `Forbidden`, `Conflict`,
`BadRequest`, `Validation`, `ServerError`). Validation errors use a `Dictionary<string, string[]>`
(field → messages).

## OneOf preference rule

See `.github/instructions/csharp-conventions.instructions.md` → **Discriminated Unions** for the full OneOf vs ServiceResult boundary rule. In service operations: prefer native OneOf within a tier; use `ServiceResult<T>` only at HTTP endpoints, WASM boundaries, or cross-tier contracts.

## Trace ID guarantee

All `ServiceProblem` instances converted to HTTP **must carry the W3C trace ID**
(`Activity.Current?.TraceId`); `ServiceResultExtensions.ToHttpResult` inserts it automatically.

## Composition root

- Register every application-consumed server service in `Nova/Program.cs` in the same change that adds
  it. Unit tests that construct a service directly do not verify DI registration.
- Use scoped lifetime for services that depend on scoped user, authorization, or DbContext-factory
  state; map boundary-crossing interfaces to their implementation explicitly.

## Lifecycle-sensitive mutations

- When a mutation depends on campaign, player, team, or tag lifecycle state, start a transaction,
  acquire the matching `LifecycleMutationLock`, then read the lifecycle entity—or reload it if
  already tracked—and re-check the guard before writing.
- Every writer of a shared invariant must follow the global entity-type lock order:
  campaign → player → team → tag. When acquiring multiple locks of the same type, sort their
  identifiers ascending before locking. A writer may take a subsequence of the global order, but it
  must never reverse that order.
- When the required lock set comes from mutable relationships, compute the candidates, acquire locks in global order, reload guarded state, and detect relationships that appeared outside the locked set. Fail with a retryable conflict rather than evaluating an invariant against an unlocked row. (`TeamManagementService.UpdateTeamAsync` is the canonical example.)
- The lock is intentionally a no-op under SQLite. Add a PostgreSQL integration test for lifecycle
  races such as close-versus-write or archive-versus-placement.

## Retrying execution strategies

- With a retrying database provider, run the entire explicit transaction inside
  `CreateExecutionStrategy().ExecuteAsync`. Create and dispose a fresh `DbContext` and transaction
  for every attempt; never reuse tracked state after a transient failure.
- For inserts whose commit acknowledgement can be lost, generate a stable operation ID before the
  first attempt, enforce tenant-scoped uniqueness in the database, and use `verifySucceeded` to
  reconstruct the committed result instead of replaying a non-idempotent mutation.
- When persisted state could have been produced by an earlier request, track whether the current attempt reached `CommitAsync` and only treat that state as proof for an attempt that did commit.
- Verify retry behavior with focused PostgreSQL integration tests; the SQLite harness cannot model
  provider execution strategies or ambiguous commits.

## Functional core boundary

Consider a feature-local pure policy when a service contains a non-trivial deterministic rule matrix. The service remains the imperative shell: validate, authorize, query tenant-safe facts, acquire locks, reload freshness-sensitive state, call the policy once, then apply effects, persist, handle concurrency, and log. Do not introduce a policy for simple guards or move EF, authorization, locking, persistence, or logging into the policy. See `.github/instructions/functional-core.instructions.md`.

## Logging

Follow source-generated `[LoggerMessage]` conventions from `.github/instructions/csharp-conventions.instructions.md`. In services: log `Warning` for expected-but-noteworthy failures (validation errors, conflicts), `Error` for unexpected exceptions (database/network). Always include user id, resource id, and operation in context. Never log sensitive data.

## Related

- `.agents/skills/add-feature-slice/` — full service + input recipe and examples.
- `Nova.Shared/Results/` — `ServiceProblem`, `ServiceResult`, `ServiceProblemKind`, `HttpResponseMessageExtensions`.
- `Nova/Features/Shared/ServiceResultExtensions.cs`.
- `.github/instructions/api-endpoints.instructions.md`, `.github/instructions/validation.instructions.md`.
- `.github/instructions/functional-core.instructions.md`.
