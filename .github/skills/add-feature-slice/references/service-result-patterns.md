# Service Result Patterns Recipe

Use this reference while implementing the shared interface and server service for a feature slice.
Canonical examples: `Nova.Shared\Clubs\IClubService.cs`, `Nova.Shared\Clubs\ClubDto.cs`, and
`Nova\Features\Clubs\ClubService.cs`.

## Dual-Layer Validation

Validation must occur at **both** the endpoint layer and the service layer:

- **Endpoint layer** (DataAnnotations + `AddValidation()`): rejects structurally invalid HTTP requests (null, empty, length violations) before the handler runs. Fast, no service allocation needed.
- **Service layer** (`InputValidator.Validate<T>(input)` in the service method): re-runs the same DataAnnotations declared on the input record — enforcing whitespace-only rejection (via `[NotWhitespace]`), length constraints, and required fields — plus any business rules, as the authoritative boundary. See `.github/instructions/validation.instructions.md` for the full pattern.

**Why both layers are required:** Server-side Blazor (SSR) pages can inject and call server services directly via DI without going through HTTP endpoints at all. Endpoint-level validation only runs for HTTP requests; it never fires when a service is called from an SSR page, a background job, or a test. The service is the authoritative validation boundary regardless of the call path:

| Caller | Endpoint validation runs? | Service validation runs? |
|---|---|---|
| WASM client → HTTP endpoint → service | ✅ | ✅ |
| SSR page → service directly | ❌ | ✅ |
| Background job → service directly | ❌ | ✅ |
| Integration test → service directly | ❌ | ✅ |

See the `add-api-endpoint` skill (`references/validation-and-problemdetails.md`) for the endpoint-side rules.
See `.github/instructions/validation.instructions.md` for the shared `InputValidator.Validate<T>` helper and the `[NotWhitespace]` attribute that implement this pattern.

## ServiceProblem and ServiceResult Types

The service layer uses **ServiceProblem** and **ServiceResult<T>** to represent operations that cross service boundaries (HTTP endpoints, WebAssembly client calls, etc.). These types are defined in `Nova.Shared.Results`:

- **ServiceProblem**: A readonly record struct that represents a known problem from a service operation, with a `Kind` enum, optional `Detail` string, and optional structured validation `Errors` dictionary. Maps directly to HTTP status codes and RFC 7807 ProblemDetails.
- **ServiceResult<T>**: A OneOf union type representing either success (`T` value) or failure (`ServiceProblem`). Always used when a service boundary (HTTP, WASM client) is crossed.
- **ServiceProblemKind**: Enum defining problem categories (NotFound, Forbidden, Conflict, BadRequest, Validation, ServerError).

## OneOf Preference Rule

**Default to native OneOf types** (Success, Error<T>, NotFound, Conflict) in service operations that do not cross boundaries. Use ServiceResult only when:

1. The operation is called from HTTP endpoints, requiring translation to ProblemDetails.
2. The operation is called from a WebAssembly client, requiring client-side problem deserialization.
3. The operation is part of a cross-tier contract (like IProfilePhotoService).

Example:
- ✅ **ClubMembershipClaimRefresher** (internal service, single tier) → native OneOf
- ✅ **IProfilePhotoService** (boundary-crossing interface) → ServiceResult

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
var errors = new Dictionary<string, string[]>
{
    ["email"] = ["Email is required.", "Email must be valid."],
    ["password"] = ["Password must be at least 8 characters."]
};
return ServiceProblem.Validation(errors, detail: "Validation failed.");

// Single-field shorthand for Validation
return ServiceProblem.Validation("email", "Email is required.");

// ServerError (HTTP 500) — use for unexpected failures
return ServiceProblem.ServerError("An unexpected error occurred.");
```

## Validation Problem Structure

When returning validation errors, use a structured dictionary mapping field names to string arrays of error messages. The ServiceResult extension methods translate this into RFC 7807 ValidationProblemDetails on HTTP responses:

```csharp
public async Task<ServiceResult<UserRegistration>> RegisterAsync(UserInput input, ...)
{
    // Validation rules live on UserInput as DataAnnotations; InputValidator runs them and
    // returns the Dictionary<string, string[]> shape that ServiceProblem.Validation expects.
    var errors = InputValidator.Validate(input);
    if (errors.Count > 0)
        return ServiceProblem.Validation(errors, "Please correct the validation errors.");

    // ... continue with registration
}
```

## Trace ID Guarantee

All ServiceProblem instances converted to HTTP responses **must carry the W3C trace ID** from `Activity.Current?.TraceId` for observability correlation. The `ServiceResultExtensions.ToHttpResult` methods automatically insert the trace ID into the `extensions` dictionary of ProblemDetails responses.

Example:
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "Invalid email format.",
  "extensions": {
    "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
  }
}
```

## Service Implementation Example

```csharp
public sealed partial class UserRegistrationService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<UserRegistrationService> logger) : IUserRegistrationService
{
    public async Task<ServiceResult<UserRegistration>> RegisterAsync(
        UserRegistrationInput input,
        CancellationToken cancellationToken = default)
    {
        // Validate input against the DataAnnotations declared on UserRegistrationInput.
        var validationErrors = InputValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            return ServiceProblem.Validation(validationErrors);
        }

        // Check for conflicts (e.g., email already registered)
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var exists = await context.Users.AnyAsync(u => u.Email == input.Email, cancellationToken);
        if (exists)
        {
            return ServiceProblem.Conflict("Email address is already registered.");
        }

        // Create the user
        var user = new UserEntity { Email = input.Email, Name = input.Name };
        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);

        LogUserRegistered(user.Id);
        return new UserRegistration(user.Id, user.Email);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} registered.")]
    private partial void LogUserRegistered(long userId);
}
```

## Logging and Error Context

- Log **warnings** for expected but noteworthy failures (validation errors, conflicts).
- Log **errors** for unexpected failures (database exceptions, network failures).
- Always include relevant context (user ID, resource ID, operation name) in logs.
- Do **not** log sensitive data (passwords, personal information).

Example:
```csharp
catch (DbUpdateException ex)
{
    LogRegistrationFailed(ex, userId);
    return ServiceProblem.ServerError("The registration could not be saved. Please try again.");
}

[LoggerMessage(Level = LogLevel.Error, Message = "User {UserId} registration failed due to database error.")]
private partial void LogRegistrationFailed(Exception exception, long userId);
```

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

