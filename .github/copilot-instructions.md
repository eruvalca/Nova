# Repository Overview

- This solution is a .NET 10 Blazor web app.
- The server/host project is `Nova/Nova.csproj`.
- The Blazor WebAssembly project for interactive components is `Nova.Client/Nova.Client.csproj`.
- The shared UI library is `Nova.UI/Nova.UI.csproj`.
- The shared models, interfaces, endpoints, results, validation, and utilities project is `Nova.Shared/Nova.Shared.csproj`.
- Aspire instrumentation is configured in `Nova.AppHost/Nova.AppHost.csproj` and `Nova.ServiceDefaults/Nova.ServiceDefaults.csproj`.

## Targeted Instructions

Detailed repo conventions live in targeted instruction files so they only load when relevant:
If a targeted instruction file is referenced but not available in context, state which file is missing and ask the user to provide it before proceeding with the affected code area.

- `.github/instructions/blazor-architecture.instructions.md` for Blazor architecture: component/page placement (`Nova.UI` first), SSR-first render-mode rules, feature folder organization, code-behind/CSS-isolation conventions, and service vs `DbContext`/`HttpContext` usage from components.
- `.github/instructions/csharp-conventions.instructions.md` for C# style, `.editorconfig`, OneOf/ServiceResult conventions, and source-generated logging.
- `.github/instructions/ef-core-tenancy.instructions.md` for EF Core setup, club-based multi-tenancy, DbContext selection (`NovaDbContext`/`NovaReadDbContext`/`NovaAdminDbContext`), entity/relationship rules, and migrations.
- `.github/instructions/observability.instructions.md` for OpenTelemetry and correlation conventions: W3C/`Activity.Current` correlation, ServiceDefaults-owned wiring, Blazor tracing source inclusion, WASM HTTP trace propagation, and `ProblemDetails` trace IDs.
- `.github/instructions/testing.instructions.md` for the test suite: unit vs Aspire integration tests, the SQLite tenancy harness, the AppHost fixture, and how to run each project.
- `.github/instructions/service-layer.instructions.md` for service-layer patterns: ServiceProblem/ServiceResult types, OneOf preference, validation, DI registration, lifecycle-mutation locking, trace IDs, and logging.
- `.github/instructions/api-endpoints.instructions.md` for HTTP endpoint patterns: MapGroup organization, handler methods, ServiceResult conversion, ProblemDetails structure, authorization, and enum binding.

## Skills

The instruction files above hold the always-on *rules*. The step-by-step *recipes* (and full code
examples) live in model-invoked Agent Skills under `.github/skills/`, loaded on demand by intent:

- `add-api-endpoint` — add/modify a minimal-API endpoint (route constants, handlers, `ToHttpResult`, ProblemDetails, antiforgery, auth, enum binding).
- `add-domain-persistence` — add/modify domain entities, EF configuration, tenancy, lifecycle/concurrency invariants, incremental migrations, and provider-focused tests; invokes `nova-testing`.
- `add-feature-slice` — orchestrate a full vertical slice end to end (input record + validation → service → endpoint → WASM client → tests); invokes `add-domain-persistence` when needed, `add-api-endpoint`, and `nova-testing`.
- `nova-testing` — pick the harness (SQLite tenancy unit tests vs Aspire Postgres integration tests), write a test, and run it on Microsoft.Testing.Platform.
