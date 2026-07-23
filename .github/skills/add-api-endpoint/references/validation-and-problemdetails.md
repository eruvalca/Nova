# Validation and ProblemDetails

Canonical Nova examples:

- Mapping/handlers: `Nova\Features\Clubs\ClubEndpointRouteBuilderExtensions.cs`
- ToHttpResult: `Nova\Features\Shared\ServiceResultExtensions.cs`

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

## Optional `[AsParameters]` Query Properties

A property initializer does not make a non-nullable scalar optional during minimal-API
`[AsParameters]` query binding. The binder can reject an omitted value before the handler runs.
When omission is valid:

1. Make the query-record property nullable.
2. Keep DataAnnotations on the property so explicitly supplied values are validated.
3. Coalesce to the default in the service, which is the authoritative boundary for HTTP and direct
   callers.
4. Add HTTP tests for omission and for an invalid explicit value.

```csharp
public sealed record GetPlayerRosterInput
{
    [Range(1, 100)]
    public int? PageSize { get; init; }
}

var pageSize = input.PageSize ?? PlayerRosterDefaults.PageSize;
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
