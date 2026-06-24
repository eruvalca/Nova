# Handlers and Results

Canonical Nova examples:

- Mapping/handlers: `Nova\Features\Clubs\ClubEndpointRouteBuilderExtensions.cs`
- ToHttpResult: `Nova\Features\Shared\ServiceResultExtensions.cs`

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
