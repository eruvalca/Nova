# Service Result Patterns Recipe

Use this reference while implementing the shared interface and server service for a feature slice.
Canonical examples: `Nova.Shared\Features\Clubs\IClubService.cs`, `Nova.Shared\Features\Clubs\ClubDto.cs`, and
`Nova\Features\Clubs\ClubService.cs`.

Validation must occur at both the endpoint layer and the service layer. See
`.github/instructions/service-layer.instructions.md` → **Dual-Layer Validation** for the full rationale and
caller/service table. See `add-api-endpoint/references/validation-and-problemdetails.md` for
the endpoint-side rules.

`ServiceResult<T>`, `ServiceProblem`, and `ServiceProblemKind` are defined in `Nova.Shared/Results/`.

## OneOf Preference Rule

Follow `.github/instructions/csharp-conventions.instructions.md` → **Discriminated Unions**.
This cross-tier recipe uses `ServiceResult<T>` for shared HTTP/WASM contracts; internal operations and
pure policies use native OneOf outcomes.

### Composing a pure domain policy

When a service contains a non-trivial deterministic rule matrix, keep it as the imperative shell and
compose a feature-local policy. The policy returns native `OneOf`; the service maps that result into
its boundary contract:

```csharp
var decision = CampaignClosurePolicy.Evaluate(assignmentStates);
return await decision.Match(
    ApplyClosureAsync,
    RejectClosureAsync);
```

Prefer exhaustive `Match` for value-producing branches and `Switch` for side-effect-only branches;
do not branch on positional `IsTn`/`AsTn` members. Use a source-generated named OneOf union when a
multi-case service contract is reused or benefits from a domain name.

Do not return `ServiceResult` from an internal policy. Do not move authorization, tenant-safe EF
queries, transactions, lifecycle locks, concurrency, persistence, or logging out of the service.
Follow `.agents/skills/add-domain-persistence/references/functional-core-imperative-shell.md`.

## ServiceProblem Construction

Use the factory methods on ServiceProblem for type-safe creation:

```csharp
// NotFound (HTTP 404)
return ServiceProblem.NotFound("Resource not found.");

// Forbidden (HTTP 403) — use when user is authenticated but not authorized
return ServiceProblem.Forbidden("You do not have permission.");

// Conflict (HTTP 409) — use for state-conflict errors
return ServiceProblem.Conflict("The resource has been modified.");

// BadRequest (HTTP 400) — use for single-message semantic rejections
return ServiceProblem.BadRequest("Invalid operation state.");

// Validation (HTTP 400) — use for structured field errors
return ServiceProblem.Validation(
    new Dictionary<string, string[]> { ["email"] = ["Email is required."] },
    detail: "Validation failed.");

// Single-field shorthand for Validation
return ServiceProblem.Validation("email", "Email is required.");

// ServerError (HTTP 500) — use for unexpected failures
return ServiceProblem.ServerError("An unexpected error occurred.");
```

## Validation Problem Structure

Run `InputValidator.Validate(input)` at the top of the service method and short-circuit on failure:

```csharp
var errors = InputValidator.Validate(input);
if (errors.Count > 0)
    return ServiceProblem.Validation(errors, "Please correct the validation errors.");
```

`ToHttpResult` converts `ServiceProblem.Validation` to RFC 7807 `ValidationProblemDetails` (HTTP 400)
and automatically inserts the W3C `traceId` from `Activity.Current?.TraceId` into the extensions dictionary.

## Nova Clubs implementation pattern

Use the Clubs slice as the concrete implementation model:

```csharp
public interface IClubService
{
    Task<ServiceResult<ClubDto>> CreateClubAsync(CreateClubInput input, CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<ClubDto>>> SearchClubsAsync(string? query, CancellationToken cancellationToken = default);
}
```

```csharp
public sealed partial class ClubService(
    IDbContextFactory<NovaAdminDbContext> adminDbContextFactory,
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    UserManager<NovaUserEntity> userManager,
    ICurrentUserProvider currentUserProvider,
    ILogger<ClubService> logger) : IClubService
{
    public async Task<ServiceResult<ClubDto>> CreateClubAsync(CreateClubInput input, CancellationToken cancellationToken = default)
    {
        // Validate input against the DataAnnotations declared on CreateClubInput.
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        // Check if current user already belongs to a club
        if (currentUserProvider.ClubId.HasValue)
        {
            return ServiceProblem.Conflict("You already belong to a club.");
        }

        // Get current user ID
        if (currentUserProvider.UserId is not long userId)
        {
            return ServiceProblem.Forbidden("You must be signed in to create a club.");
        }

        // ... persistence and error handling
    }
}
```
