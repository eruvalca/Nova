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
- `.github/instructions/csharp-conventions.instructions.md` for C# style, `.editorconfig`, and source-generated logging.
- `.github/instructions/ef-core-tenancy.instructions.md` for EF Core setup, club-based multi-tenancy, DbContext selection (`NovaDbContext`/`NovaReadDbContext`/`NovaAdminDbContext`), entity/relationship rules, and migrations.
- `.github/instructions/observability.instructions.md` for OpenTelemetry and correlation conventions: W3C/`Activity.Current` correlation, ServiceDefaults-owned wiring, Blazor tracing source inclusion, WASM HTTP trace propagation, and `ProblemDetails` trace IDs.
- `.github/instructions/testing.instructions.md` for the test suite: unit vs Aspire integration tests, the SQLite tenancy harness, the AppHost fixture, and how to run each project.
