# Handlers and Results

Canonical Nova examples:

- Mapping/handlers: `Nova\Features\Clubs\ClubEndpointRouteBuilderExtensions.cs`
- ToHttpResult: `Nova\Features\Shared\ServiceResultExtensions.cs`
- Created resource: `Nova\Features\Teams\TeamManagementEndpointRouteBuilderExtensions.cs`
- HTTP contract: `Nova.Integration.Tests\Http\TeamManagementHttpTests.cs`

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

Use a shared route-name constant for the target GET. Then add a real HTTP test that:

1. Asserts `201 Created`.
2. Deserializes the created DTO.
3. Asserts the exact `Location` generated from the shared URL builder.
4. Follows `Location` and asserts the canonical GET succeeds.

`TeamManagementHttpTests.CreateTeam_ReturnsCreatedWithLocationHeader_ForClubAdmin` is the canonical
test. Endpoint metadata tests cannot prove the route name and route values generate a usable URL.

## Related Files

- `Nova.Features.Shared.ServiceResultExtensions.cs` — Extension methods for converting ServiceResult to HTTP responses
- `.github/instructions/service-layer.instructions.md` — Service-layer result patterns and conventions
- `Nova.Shared/Results/` — ServiceProblem, ServiceResult, and HttpResponseMessageExtensions definitions
