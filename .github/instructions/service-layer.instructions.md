---
applyTo: "Nova/Features/**/*Service.cs,Nova.Shared/**/I*Service.cs,Nova.Shared/Results/**/*.cs,Nova.Client/Services/**/*.cs"
description: "Service-layer rules: validation, ServiceResult, retry-safe transactions, lifecycle locking, trace IDs, and logging."
---

# Service-Layer Rules

> Declarative rules only. For the **step-by-step recipe and full code examples** (ServiceProblem
> factories, service implementation, validation wiring), use the **`add-feature-slice`** skill
> (`.github/skills/add-feature-slice/`).

## Dual-Layer Validation

Validation must occur at **both** layers, because the service is the authoritative boundary for every
call path:

- **Endpoint layer** (DataAnnotations + `AddValidation()`): fast rejection of structurally invalid
  HTTP requests before the handler runs.
- **Service layer** (`InputValidator.Validate<T>(input)`): re-runs the same DataAnnotations plus
  business rules — authoritative for all callers.

| Caller | Endpoint validation runs? | Service validation runs? |
|---|---|---|
| WASM client → HTTP endpoint → service | ✅ | ✅ |
| SSR page → service directly | ❌ | ✅ |
| Background job → service directly | ❌ | ✅ |
| Integration test → service directly | ❌ | ✅ |

Both layers read the **same attributes**, so calling `InputValidator.Validate<T>` in the service
guarantees identical rules on every path. See `.github/instructions/validation.instructions.md`.

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

**Default to native OneOf types** (Success, Error<T>, NotFound, Conflict) for operations that do not
cross boundaries. Use **ServiceResult** only when the operation:

1. is called from HTTP endpoints (needs ProblemDetails translation),
2. is called from a WebAssembly client (needs client-side problem deserialization), or
3. is part of a cross-tier contract.

Examples: `ClubMembershipClaimRefresher` (single tier) → native OneOf; `IProfilePhotoService`
(boundary-crossing) → ServiceResult.

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
  already tracked—and re-check the guard before writing. For multiple locks, preserve the shared
  order: campaign → player → team → tag.
- The lock is intentionally a no-op under SQLite. Add a PostgreSQL integration test for lifecycle
  races such as close-versus-write or archive-versus-placement.

## Retrying execution strategies

- With a retrying database provider, run the entire explicit transaction inside
  `CreateExecutionStrategy().ExecuteAsync`. Create and dispose a fresh `DbContext` and transaction
  for every attempt; never reuse tracked state after a transient failure.
- For inserts whose commit acknowledgement can be lost, generate a stable operation ID before the
  first attempt, enforce tenant-scoped uniqueness in the database, and use `verifySucceeded` to
  reconstruct the committed result instead of replaying a non-idempotent mutation.
- Verify retry behavior with focused PostgreSQL integration tests; the SQLite harness cannot model
  provider execution strategies or ambiguous commits.

## Functional core boundary

- When a service contains a non-trivial deterministic rule matrix, consider a feature-local pure
  policy after applying the extraction triggers in
  `.github/instructions/functional-core.instructions.md`.
- Keep the service as the imperative shell: validate and authorize, query tenant-safe facts, acquire
  transactions and lifecycle locks, reload freshness-sensitive state, call the policy once, then
  apply effects, persist, handle concurrency, and log.
- Do not introduce a policy for simple guards or move EF, authorization, locking, persistence, or
  logging into the policy.

## Logging

- Log **warnings** for expected-but-noteworthy failures (validation errors, conflicts).
- Log **errors** for unexpected failures (database/network exceptions).
- Always include relevant context (user id, resource id, operation). Use source-generated
  `[LoggerMessage]` methods.
- Never log sensitive data (passwords, personal information).

## Related

- `.github/skills/add-feature-slice/` — full service + input recipe and examples.
- `Nova.Shared/Results/` — `ServiceProblem`, `ServiceResult`, `ServiceProblemKind`, `HttpResponseMessageExtensions`.
- `Nova/Features/Shared/ServiceResultExtensions.cs`.
- `.github/instructions/api-endpoints.instructions.md`, `.github/instructions/validation.instructions.md`.
- `.github/instructions/functional-core.instructions.md`.
