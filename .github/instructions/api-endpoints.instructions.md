# API Endpoint Patterns

This file documents the patterns and conventions used in Nova's HTTP endpoints for consuming service results, converting to ProblemDetails responses, and ensuring proper observability.

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
    return result.ToHttpResult(user => TypedResults.CreatedAtRoute("GetUser", new { userId = user.Id }, user));
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
return TypedResults.CreatedAtRoute("GetUser", new { userId = user.Id }, user);
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
        user => TypedResults.CreatedAtRoute("GetUser", new { userId = user.Id }, user));
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
