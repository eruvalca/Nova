---
applyTo: "Nova/Features/**/*.cs,Nova.Shared/**/*Endpoints.cs,Nova.Client/Services/**/*.cs"
description: "HTTP endpoint rules: route constants, MapGroup, static handlers, ServiceResult-to-HTTP conversion, ProblemDetails/trace IDs, validation, authorization, antiforgery, and enum binding."
---

# API Endpoint Rules

> Declarative rules only. For the **step-by-step recipe and full code examples**, use the
> **`add-api-endpoint`** skill (`.github/skills/add-api-endpoint/`).

## Routes

- **All route strings must be constants in a static `*Endpoints` class in `Nova.Shared`** (one per
  feature folder, e.g. `Nova.Shared/Clubs/ClubEndpoints.cs`). Never write inline route literals in
  the mapping code or in WASM client services — server and client must consume the same constants.
- For routes with dynamic segments, expose a URL-builder static method rather than the raw template.
- Naming: `GroupPrefix` (full prefix for `MapGroup`), `{Verb}` (full absolute URL), `{Verb}Relative`
  (relative path/template used inside a group), `{Verb}Template` (full absolute template with tokens).

## Handlers and result conversion

- Organize related endpoints with `MapGroup` under a shared prefix and shared middleware.
- Use **static handler methods** declared in the same file as the mapping extension; inject
  dependencies as handler parameters.
- Convert service results with the `ToHttpResult` extensions in
  `Nova.Features.Shared.ServiceResultExtensions`. Prefer returning `IResult`; use `Results<T1, T2, …>`
  only when OpenAPI needs precise success-type information.
- ⚠️ `TypedResults.CreatedAtRoute<TValue>` takes the **value first**:
  `CreatedAtRoute(value, routeName, routeValues)`. Putting the route name first compiles but throws
  at runtime. Only use `CreatedAtRoute` when a matching GET route exists; otherwise return
  `TypedResults.Created((string?)null, value)`.

## ProblemDetails and trace IDs

- **Every `ProblemDetails` response must carry the W3C trace ID** (`Activity.Current?.TraceId`).
  `ToHttpResult` inserts it automatically; framework-generated 400s get it via the `AddProblemDetails`
  customization in `Program.cs`. This is required for log/trace correlation.
- `ServiceProblem.Validation(errors)` is converted to RFC 7807 `ValidationProblemDetails`; the WASM
  client reconstructs it with `HttpResponseMessageExtensions.ToServiceProblemAsync()`.

## Metadata, authorization, antiforgery

- Declare possible problem responses with `ProducesProblem`; use `WithName` for routes referenced in
  redirection/OpenAPI.
- Apply `RequireAuthorization` at the group or handler level as appropriate.
- Endpoints accepting JSON/multipart from the WASM client must call `DisableAntiforgery()` (the client
  cannot generate Razor CSRF tokens; `SameSite=Lax` on the Identity cookie provides CSRF protection).

## Validation at the endpoint layer

- Validation is **dual-layer** (endpoint + service); both are always required — see
  `.github/instructions/service-layer.instructions.md` → **Dual-Layer Validation**.
- In .NET 10, `builder.Services.AddValidation()` (global in `Program.cs`) makes parameter validation
  automatic and **opt-out**. Use `DisableValidation()` on endpoints where model binding does not apply
  (streaming/multipart).
- Annotate input records in `Nova.Shared` with DataAnnotations (see
  `.github/instructions/validation.instructions.md`). On body endpoints declare
  `.ProducesValidationProblem()` (not `.ProducesProblem(400)`).
- For inputs not expressible as DataAnnotations (file size, content-type, streaming), validate manually
  in the handler and return `ServiceProblem.Validation(...).ToHttpResult()`.

## Enum query parameters

- Minimal-API enum query binding is **case-sensitive**. Bind as `string?` and parse explicitly with
  `Enum.TryParse<T>(value, ignoreCase: true, out …)`, applying a default on failure.

## Related

- `.github/skills/add-api-endpoint/` — full endpoint recipe and examples.
- `Nova/Features/Shared/ServiceResultExtensions.cs` — `ToHttpResult` conversions.
- `.github/instructions/service-layer.instructions.md`, `.github/instructions/validation.instructions.md`.
- `Nova.Shared/Results/` — `ServiceProblem`, `ServiceResult`, `HttpResponseMessageExtensions`.
