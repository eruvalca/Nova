# Service-Layer Result Patterns

This file documents the patterns and conventions used in Nova's service layer for result handling, error representation, and boundary-crossing operations.

## Dual-Layer Validation

Validation must occur at **both** the endpoint layer and the service layer:

- **Endpoint layer** (DataAnnotations + `AddValidation()`): rejects structurally invalid HTTP requests (null, empty, length violations) before the handler runs. Fast, no service allocation needed.
- **Service layer** (explicit checks in the service method): enforces whitespace-only rejection, business rules, and length constraints as defense in depth.

**Why both layers are required:** Server-side Blazor (SSR) pages can inject and call server services directly via DI without going through HTTP endpoints at all. Endpoint-level validation only runs for HTTP requests; it never fires when a service is called from an SSR page, a background job, or a test. The service is the authoritative validation boundary regardless of the call path:

| Caller | Endpoint validation runs? | Service validation runs? |
|---|---|---|
| WASM client → HTTP endpoint → service | ✅ | ✅ |
| SSR page → service directly | ❌ | ✅ |
| Background job → service directly | ❌ | ✅ |
| Integration test → service directly | ❌ | ✅ |

See `.github/instructions/api-endpoints.instructions.md` → **Input Validation at the Endpoint Layer** for the endpoint-side rules.

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
    var errors = new Dictionary<string, string[]>();
    
    if (string.IsNullOrWhiteSpace(input.Email))
        errors.TryAdd("email", []);
    errors["email"]?.Append("Email is required.");
    
    if (!IsValidEmail(input.Email))
        errors.TryAdd("email", []);
    errors["email"]?.Append("Email format is invalid.");
    
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
        // Validate input (can be validation errors or business-rule rejections)
        var validationErrors = ValidateInput(input);
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

    private static Dictionary<string, string[]> ValidateInput(UserRegistrationInput input)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(input.Email))
            errors["email"] = ["Email is required."];

        if (string.IsNullOrWhiteSpace(input.Name))
            errors["name"] = ["Name is required."];

        return errors;
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

## Related Files

- `Nova.Shared/Results/` — ServiceProblem, ServiceResult, ServiceProblemKind, and HttpResponseMessageExtensions
- `Nova/Features/Shared/ServiceResultExtensions.cs` — Extension methods for converting ServiceResult to HTTP responses
- `.github/instructions/api-endpoints.instructions.md` — HTTP endpoint patterns for consuming ServiceResult
