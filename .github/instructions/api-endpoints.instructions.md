---
applyTo: "Nova/Features/**/*.cs,Nova.Shared/Features/**/*Endpoints.cs,Nova.Shared/Features/**/*Input.cs,Nova.Client/Services/**/*.cs"
description: "HTTP endpoint and WASM client rules: routes, handlers, contract fidelity, ProblemDetails, validation, metadata, authorization, antiforgery, and query binding."
---

# API Endpoint Rules

> Declarative rules only. For the **step-by-step recipe and full code examples**, use the
> **`add-api-endpoint`** skill (`.github/skills/add-api-endpoint/`).

## Routes

- Before adding a route, search existing service mutations, route constants, server mappings, WASM
  clients, UI callers, and HTTP tests. Do not add a specialized endpoint when an existing command
  already owns the same mutation and invariant unless a distinct external contract is intentional.
  Every endpoint needs an intended caller or an explicit external-consumer justification.
- **All route strings must be constants in a static `*Endpoints` class in `Nova.Shared`** (one per
  feature folder, e.g. `Nova.Shared/Features/Clubs/ClubEndpoints.cs`). Never write inline route literals in
  the mapping code or in WASM client services — server and client must consume the same constants.
- For routes with dynamic segments, expose a URL-builder static method rather than the raw template.
- Compose URL builders from the feature's existing route constants (especially `GroupPrefix`) instead
  of repeating prefixes. A builder must emit only query values accepted by the corresponding input
  contract; share allowed values/bounds or normalize and omit invalid optional values so the client
  cannot generate a URL that endpoint validation will reject.
- Naming: `GroupPrefix` (full prefix for `MapGroup`), `{Verb}` (full absolute URL), `{Verb}Relative`
  (relative path/template used inside a group), `{Verb}Template` (full absolute template with tokens).

## Handlers and result conversion

- Organize related endpoints with `MapGroup` under a shared prefix and shared middleware.
- Use **static handler methods** declared in the same file as the mapping extension; inject
  dependencies as handler parameters.
- Convert service results with the `ToHttpResult` extensions in
  `Nova.Features.Shared.ServiceResultExtensions`. Prefer returning `IResult`; use `Results<T1, T2, …>`
  only when OpenAPI needs precise success-type information.
- Keep endpoint metadata aligned with every status the handler can return (route/body mismatch 400s, conflicts, not-found, 500s). Client or service unit tests do not prove route registration, middleware, metadata, and status mapping agree.
- ⚠️ `TypedResults.CreatedAtRoute<TValue>` takes the **value first**:
  `CreatedAtRoute(value, routeName, routeValues)`. Putting the route name first compiles but throws
  at runtime. Only use `CreatedAtRoute` when a matching GET route exists; otherwise return
  `TypedResults.Created((string?)null, value)`.
- A `CreatedAtRoute` contract is not complete until a real HTTP test asserts `201 Created`, validates the `Location` header, follows it, and confirms the GET resolves the resource. Use a shared route-name constant so the GET mapping and create handler cannot drift.

## Endpoint removal

Remove dead endpoints end to end in one change: route constants/builders, input/DTO/interface members, server mapping and handler, WASM client method, DI registration, UI callers, metadata, and tests. Search for every removed symbol after editing.

## ProblemDetails and trace IDs

- **Every `ProblemDetails` response must carry the W3C trace ID** (`Activity.Current?.TraceId`).
  `ToHttpResult` inserts it automatically; framework-generated 400s get it via the `AddProblemDetails`
  customization in `Program.cs`. This is required for log/trace correlation.
- `ServiceProblem.Validation(errors)` is converted to RFC 7807 `ValidationProblemDetails`; the WASM
  client reconstructs it with `HttpResponseMessageExtensions.ToServiceProblemAsync()`.
- Preserve structured `ServiceProblem.Errors` for every problem kind that uses them, not only
  `Validation`. Add the matching factory overload, HTTP serialization, WASM deserialization, and
  round-trip tests together; never hand-construct a problem to bypass a missing shared factory.
- Treat both 400 and 422 responses containing an `errors` payload as validation failures in WASM
  clients. Do not misclassify .NET 10 automatic-validation 422 responses as server errors.

## Metadata, authorization, antiforgery

- Declare possible problem responses with `ProducesProblem`; use `WithName` for routes referenced in
  redirection/OpenAPI.
- Apply the narrowest required authorization policy at the group or handler level. If an operation is
  administrator-only, middleware must require the administrator policy even when the service repeats
  the check as defense in depth; authentication-only metadata is insufficient.
- Endpoints accepting JSON/multipart from the WASM client must call `DisableAntiforgery()` (the client
  cannot generate Razor CSRF tokens; `SameSite=Lax` on the Identity cookie provides CSRF protection).

## Validation at the endpoint layer

- Validation is **dual-layer** (endpoint + service); both are always required — see `.github/instructions/service-layer.instructions.md` → **Dual-Layer Validation**.
- `builder.Services.AddValidation()` (global in `Program.cs`) makes parameter validation automatic and opt-out. Use `DisableValidation()` on endpoints where model binding does not apply (streaming/multipart).
- Annotate input records in `Nova.Shared` with DataAnnotations (see `.github/instructions/validation.instructions.md`). On body endpoints declare `.ProducesValidationProblem()` (not `.ProducesProblem(400)`).
- For inputs not expressible as DataAnnotations (file size, content-type, streaming), validate manually in the handler and return `ServiceProblem.Validation(...).ToHttpResult()`.

## Optional `[AsParameters]` query properties

- A property initializer does not make a non-nullable scalar optional during minimal-API
  `[AsParameters]` query binding. If omission is valid, make the property nullable, retain
  DataAnnotations for explicitly supplied values, and coalesce to the default in the service.
- Add HTTP coverage for both an omitted optional property and an invalid explicit value so binding
  and validation behavior are exercised before the handler boundary.

## Enum query parameters

- Minimal-API enum query binding is **case-sensitive**. Bind as `string?` and parse explicitly with
  `Enum.TryParse<T>(value, ignoreCase: true, out …)`, applying a default on failure.

## WASM success payloads

- A success response with a required body must deserialize to that body. Treat an empty, `null`, malformed, or unexpected success payload as `ServiceProblem.ServerError`, never as an empty collection or default DTO that hides a contract defect.

## Related

- `.github/skills/add-api-endpoint/` — full endpoint recipe and examples.
- `Nova/Features/Shared/ServiceResultExtensions.cs` — `ToHttpResult` conversions.
- `.github/instructions/service-layer.instructions.md`, `.github/instructions/validation.instructions.md`.
- `Nova.Shared/Results/` — `ServiceProblem`, `ServiceResult`, `HttpResponseMessageExtensions`.
