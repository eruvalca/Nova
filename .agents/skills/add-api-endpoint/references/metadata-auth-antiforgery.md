# Metadata, Authorization, and Antiforgery

Canonical Nova example:

- Mapping/handlers: `Nova\Features\Clubs\ClubEndpointRouteBuilderExtensions.cs`

## ProducesProblem Metadata

Always declare the possible problem responses with `ProducesProblem`:

```csharp
group.MapPost("", CreateUserHandler)
    .Produces<UserDto>(StatusCodes.Status201Created)
    .ProducesProblem(StatusCodes.Status400BadRequest)  // Validation
    .ProducesProblem(StatusCodes.Status409Conflict)    // Email conflict
    .ProducesProblem(StatusCodes.Status401Unauthorized) // Anonymous caller
    .ProducesProblem(StatusCodes.Status403Forbidden)    // Authenticated but not authorized
    .ProducesProblem(StatusCodes.Status500InternalServerError)  // Unexpected error
    .WithName("CreateUser");
```

Authorization middleware returns `401` for an anonymous caller and `403` when an authenticated
caller fails the selected policy; advertise each status that the policy can reach. For a form-bound
multipart endpoint, also declare `415` because model binding can reject a non-form content type before
the handler runs. Declare `413` when request-size middleware is configured for the route. Exercise
these framework-generated paths through real HTTP tests so metadata, middleware, and trace-correlated
ProblemDetails stay aligned.

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
