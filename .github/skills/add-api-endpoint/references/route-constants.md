# Route Constants and MapGroup Organization

Canonical Nova examples:

- Routes: `Nova.Shared\Clubs\ClubEndpoints.cs`
- Mapping/handlers: `Nova\Features\Clubs\ClubEndpointRouteBuilderExtensions.cs`
- WASM client: `Nova.Client\Services\HttpClubService.cs`

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
