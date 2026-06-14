---
applyTo: "Nova/Features/**/*.cs,Nova.Shared/**/*Endpoints.cs,Nova.Client/Services/**/*.cs"
description: "HTTP endpoint patterns: route constants, MapGroup organization, handler methods, ServiceResult-to-HTTP conversion, ProblemDetails/validation structure, authorization, antiforgery, and enum binding."
---

# API Endpoint Patterns

This file documents the patterns and conventions used in Nova's HTTP endpoints for consuming service results, converting to ProblemDetails responses, and ensuring proper observability.

## Route Constants

**All route strings must be defined as constants in a static `*Endpoints` class in `Nova.Shared`**, never as inline string literals in the endpoint mapping code. This ensures the server and the WASM client always agree on routes, and gives a single place to update a route.

### Structure

Each feature gets one `*Endpoints` class in the matching `Nova.Shared/{Feature}/` folder:

```
Nova.Shared/
  Clubs/
    ClubEndpoints.cs       ← GroupPrefix, per-route absolute constants, Relative siblings, URL builder methods
  Photos/
    PhotoEndpoints.cs
```

### Naming Conventions

| Constant | What it holds | Example value |
|---|---|---|
| `GroupPrefix` | Full absolute prefix passed to `MapGroup` | `"/api/clubs"` |
| `{Verb}` | Full absolute URL for simple routes | `"/api/clubs/search"` |
| `{Verb}Relative` | Relative path passed to `Map*` inside a group | `"search"` |
| `{Verb}Template` | Full absolute URL template with `{param}` tokens | `"/api/clubs/{clubId:long}/join-requests"` |
| `{Verb}Relative` | Relative template for parameterised routes inside a group | `"{clubId:long}/join-requests"` |

For routes with dynamic segments, add a URL-builder static method rather than exposing the template directly to callers:

```csharp
public static string CreateJoinRequestUrl(long clubId) =>
    $"/api/clubs/{clubId}/join-requests";
```

### Usage in Endpoint Mapping

Always reference the constants — never write inline route strings:

```csharp
// ❌ Don't do this
group.MapPost("{clubId:long}/join-requests", CreateJoinRequestHandler);
group.MapDelete("join-requests/{requestId:long}", CancelJoinRequestHandler);

// ✅ Do this
group.MapPost(ClubEndpoints.CreateJoinRequestRelative, CreateJoinRequestHandler);
group.MapDelete(ClubEndpoints.CancelJoinRequestRelative, CancelJoinRequestHandler);
```

### Usage in WASM Client Services

The same constants are consumed by client-side `HttpClient` calls, guaranteeing server and client always agree:

```csharp
// Nova.Client/Services/HttpClubService.cs
var response = await _httpClient.PostAsJsonAsync(ClubEndpoints.Create, input, cancellationToken);
var response = await _httpClient.GetAsync(ClubEndpoints.SearchUrl(query), cancellationToken);
```

## MapGroup Organization

Use `MapGroup` to organize related endpoints under a common prefix with shared middleware (authorization, validation, etc.):

```csharp
public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
{
    var group = endpoints
        .MapGroup("/api/users")
        .RequireAuthorization();

    group.MapPost("", CreateUserHandler)
        .Produces<UserDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .WithName("CreateUser");

    group.MapGet("{userId:long}", GetUserHandler)
        .Produces<UserDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("GetUser");

    return endpoints;
}
```

## Handler Methods and Dependency Injection

Use **static handler methods** declared in the same file as the mapping extension:

```csharp
private static async Task<IResult> CreateUserHandler(
    UserRegistrationInput input,
    IUserRegistrationService userService,
    CancellationToken cancellationToken)
{
    var result = await userService.RegisterAsync(input, cancellationToken);
    return result.ToHttpResult(user => TypedResults.CreatedAtRoute(user, "GetUser", new { userId = user.Id }));
}

private static async Task<IResult> GetUserHandler(
    long userId,
    IUserService userService,
    CancellationToken cancellationToken)
{
    var result = await userService.GetUserAsync(userId, cancellationToken);
    return result.ToHttpResult();
}
```

## ServiceResult to HTTP Conversion

Use the `ToHttpResult` extension methods in `Nova.Features.Shared.ServiceResultExtensions` to convert ServiceResult to typed HTTP responses:

```csharp
// Success with default OK response
return result.ToHttpResult();

// Success with custom response transformation
return result.ToHttpResult(userDto => TypedResults.Created($"/api/users/{userDto.Id}", userDto));

// Problem is automatically converted to appropriate status code + ProblemDetails
```

The extension automatically:
1. Maps ServiceProblemKind to HTTP status code (404, 403, 409, 400, 500)
2. Converts Validation problems to RFC 7807 ValidationProblemDetails with structured errors
3. **Inserts the W3C trace ID** from `Activity.Current?.TraceId` into the extensions dictionary

## Results Type Union (Optional)

For endpoints that can emit multiple success types, use `Results<T1, T2, ...>`:

```csharp
private static async Task<Results<Ok<UserDto>, NotFound>> GetUserHandler(
    long userId,
    IUserService userService,
    CancellationToken cancellationToken)
{
    var result = await userService.GetUserAsync(userId, cancellationToken);
    return result.Match(
        user => TypedResults.Ok(user),
        problem => problem.Kind == ServiceProblemKind.NotFound
            ? (Results<Ok<UserDto>, NotFound>)TypedResults.NotFound()
            : throw new InvalidOperationException());
}
```

For simplicity, prefer returning `IResult` unless OpenAPI documentation requires precise type information.

## ProducesProblem Metadata

Always declare the possible problem responses with `ProducesProblem`:

```csharp
group.MapPost("", CreateUserHandler)
    .Produces<UserDto>(StatusCodes.Status201Created)
    .ProducesProblem(StatusCodes.Status400BadRequest)  // Validation
    .ProducesProblem(StatusCodes.Status409Conflict)    // Email conflict
    .ProducesProblem(StatusCodes.Status403Forbidden)   // User not authorized
    .ProducesProblem(StatusCodes.Status500InternalServerError)  // Unexpected error
    .WithName("CreateUser");
```

## Antiforgery Handling

Minimal API endpoints that accept JSON or multipart data from the WebAssembly client must disable the Razor antiforgery check (the client cannot generate CSRF tokens). Rely on `SameSite=Lax` on the Identity cookie for CSRF protection:

```csharp
group.MapPost(PhotoEndpoints.UploadRelative, UploadHandler)
    .DisableAntiforgery()  // WASM client posts without Razor token
    .RequireAuthorization();
```

## Authorization

Use `RequireAuthorization` at the group or handler level:

```csharp
// Authorize entire group
var group = endpoints
    .MapGroup("/api/users")
    .RequireAuthorization();  // All handlers in group require authorization

// Or individual handler
group.MapPost("", CreateUserHandler)
    .RequireAuthorization(policyName: "AdminOnly");

// Some handlers may not require authorization
group.MapPost("register", RegisterHandler);  // No authorization
```

## Validation Problem Details Structure

When a service returns `ServiceProblem.Validation(errors)`, the `ToHttpResult` extension converts it to RFC 7807 ValidationProblemDetails:

```json
{
  "type": "https://tools.ietf.org/html/rfc7807",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": null,
  "errors": {
    "email": [
      "Email is required.",
      "Email must be a valid email address."
    ],
    "password": [
      "Password must be at least 8 characters long."
    ]
  },
  "extensions": {
    "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
  }
}
```

The client deserializes this using `HttpResponseMessageExtensions.ToServiceProblemAsync()`, which reconstructs the `ServiceProblem` with the `Errors` dictionary intact.

## Trace ID Guarantee

**Every ProblemDetails response must carry the W3C trace ID**. The `ToHttpResult` extension methods automatically insert `Activity.Current?.TraceId` into the response extensions. This is critical for correlating API errors back to server logs.

Example with explicit logging:
```csharp
private static async Task<IResult> GetUserHandler(
    long userId,
    ILogger<UserHandler> logger,
    IUserService userService,
    CancellationToken cancellationToken)
{
    try
    {
        var result = await userService.GetUserAsync(userId, cancellationToken);
        return result.ToHttpResult();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to get user {UserId}", userId);
        return ServiceProblem.ServerError("An unexpected error occurred.").ToHttpResult();
    }
}
```

## Endpoint Naming and OpenAPI

Always use `WithName` for route names used in redirection and OpenAPI:

```csharp
group.MapPost("", CreateUserHandler)
    .WithName("CreateUser");

group.MapGet("{userId:long}", GetUserHandler)
    .WithName("GetUser");
```

Then use the named route in redirection:
```csharp
return TypedResults.CreatedAtRoute(user, "GetUser", new { userId = user.Id });
```

### CreatedAtRoute Parameter Order

⚠️ The generic `TypedResults.CreatedAtRoute<TValue>` takes the **value first**: `CreatedAtRoute(value, routeName, routeValues)`. Passing the route name first compiles silently (the route name binds as `TValue`) but throws `InvalidOperationException: No route matches the supplied values` at runtime:

```csharp
// ❌ Compiles, but routeName=null and the DTO becomes routeValues — fails at runtime
return TypedResults.CreatedAtRoute("GetUser", new { userId = user.Id }, user);

// ✅ Value first, then route name, then route values
return TypedResults.CreatedAtRoute(user, "GetUser", new { userId = user.Id });
```

Only use `CreatedAtRoute` when a matching GET route actually exists. If the resource has no canonical GET endpoint, return `TypedResults.Created((string?)null, value)` (201 without a Location header) instead of pointing Location at the POST route.

## Input Validation at the Endpoint Layer

Validation happens at **two layers**: the endpoint layer (fast rejection before the service runs for HTTP callers) and the service layer (authoritative validation for all callers including SSR pages). Both are always required — see `.github/instructions/service-layer.instructions.md` → **Dual-Layer Validation** for the full reasoning.

### .NET 10 Automatic Validation

In .NET 10, `builder.Services.AddValidation()` (registered globally in `Program.cs`) activates automatic parameter validation for **all** minimal API endpoints. There is no per-group or per-endpoint opt-in call — validation is automatic and **opt-out**. Use `DisableValidation()` on the rare endpoints that should skip it.

### DataAnnotations on Input Records

Annotate all input records in `Nova.Shared` with appropriate DataAnnotations. These drive both runtime enforcement and OpenAPI documentation:

```csharp
// Nova.Shared/Clubs/CreateClubInput.cs
using Nova.Shared.Validation;

public sealed record CreateClubInput
{
    [Required, NotWhitespace, MaxLength(200)]
    public required string Name { get; init; }

    [Required, NotWhitespace, MaxLength(100)]
    public required string City { get; init; }

    [Required, NotWhitespace, MaxLength(100)]
    public required string State { get; init; }
}
```

Pair `[Required]` with `[NotWhitespace]` (defined in `Nova.Shared/Validation/NotWhitespaceAttribute.cs`) on every string field that must contain non-blank text — `[Required]` alone treats `"   "` as valid. Use explicit init-only properties rather than positional constructor parameters so attributes land on the properties where `Validator.TryValidateObject` can reflect on them. The same attributes are re-run at the service layer via `InputValidator.Validate<T>`; see `.github/instructions/validation.instructions.md`.

When a request body fails DataAnnotations validation the framework returns an RFC 7807 `HttpValidationProblemDetails` (HTTP 400) **before the handler is invoked**. The `AddProblemDetails` customization in `Program.cs` injects the W3C `traceId` into all problem responses, including framework-generated ones.

### ProducesValidationProblem for OpenAPI Metadata

Declare `.ProducesValidationProblem()` (not `.ProducesProblem(400)`) on endpoints that accept a body input. This emits the correct `ValidationProblemDetails` schema in OpenAPI, distinguishing structured field errors from generic 400s:

```csharp
group.MapPost(ClubEndpoints.CreateRelative, CreateClubHandler)
    .Produces<ClubDto>(StatusCodes.Status201Created)
    .ProducesValidationProblem()          // Framework automatic validation (DataAnnotations)
    .ProducesProblem(StatusCodes.Status409Conflict)
    .ProducesProblem(StatusCodes.Status500InternalServerError)
    .DisableAntiforgery()
    .WithName("CreateClub");
```

### Disabling Validation

Use `DisableValidation()` to opt specific endpoints out of automatic parameter validation (e.g., streaming or multipart endpoints where model binding does not apply):

```csharp
group.MapPost(PhotoEndpoints.UploadRelative, UploadHandler)
    .DisableValidation()     // IFormFile — validation handled manually in the handler
    .DisableAntiforgery()
    .RequireAuthorization();
```

### Manual Endpoint Validation for Non-Model Inputs

For inputs that cannot be expressed as DataAnnotations (file size, content-type, streaming), validate manually in the handler and return `ServiceProblem.Validation().ToHttpResult()` directly:

```csharp
private static async Task<IResult> UploadHandler(IFormFile file, ...)
{
    if (file.Length is 0 or > ProfilePhotoConstraints.MaxBytes)
    {
        return ServiceProblem.Validation("file",
            $"The photo must be between 1 byte and {ProfilePhotoConstraints.MaxBytes / (1024 * 1024)} MB.")
            .ToHttpResult();
    }
    // ... delegate to service
}
```

## Enum Query Parameter Binding

Query parameter enum binding in minimal APIs is **case-sensitive**. Always use explicit parsing with `ignoreCase: true`:

```csharp
// ❌ Don't do this; "small" won't match ProfilePhotoSize.Small
private static async Task<IResult> GetPhotoHandler(
    [FromQuery] ProfilePhotoSize size,
    ...)

// ✅ Do this instead
private static async Task<IResult> GetPhotoHandler(
    [FromQuery] string? size,
    ...)
{
    if (!Enum.TryParse<ProfilePhotoSize>(size, ignoreCase: true, out var photoSize))
    {
        photoSize = ProfilePhotoSize.Medium;  // Default
    }
    // ... use photoSize
}
```

## Complete Endpoint Example

```csharp
/// <summary>
/// Maps the user management endpoints.
/// </summary>
public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
{
    var group = endpoints
        .MapGroup("/api/users")
        .RequireAuthorization();

    group.MapPost("", CreateUserHandler)
        .Produces<UserDto>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithName("CreateUser");

    group.MapGet("{userId:long}", GetUserHandler)
        .Produces<UserDto>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .WithName("GetUser");

    return endpoints;
}

private static async Task<IResult> CreateUserHandler(
    UserRegistrationInput input,
    IUserRegistrationService userService,
    CancellationToken cancellationToken)
{
    var result = await userService.RegisterAsync(input, cancellationToken);
    return result.ToHttpResult(
        user => TypedResults.CreatedAtRoute(user, "GetUser", new { userId = user.Id }));
}

private static async Task<IResult> GetUserHandler(
    long userId,
    IUserService userService,
    CancellationToken cancellationToken)
{
    var result = await userService.GetUserAsync(userId, cancellationToken);
    return result.ToHttpResult();
}
```

## Related Files

- `Nova.Features.Shared.ServiceResultExtensions.cs` — Extension methods for converting ServiceResult to HTTP responses
- `.github/instructions/service-layer.instructions.md` — Service-layer result patterns and conventions
- `Nova.Shared/Results/` — ServiceProblem, ServiceResult, and HttpResponseMessageExtensions definitions
