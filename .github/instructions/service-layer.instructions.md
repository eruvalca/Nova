---
applyTo: "Nova/Features/**/*Service.cs,Nova.Shared/**/I*Service.cs,Nova.Shared/Results/**/*.cs,Nova.Client/Services/**/*.cs"
description: "Service-layer rules: dual-layer validation, ServiceProblem/ServiceResult types, OneOf preference, trace ID guarantee, and logging conventions."
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
