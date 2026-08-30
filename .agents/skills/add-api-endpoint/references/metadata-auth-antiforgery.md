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
