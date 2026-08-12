# Sub Tag Definition Management API and Admin UI

Provide administrator-only club tag-definition management while allowing approved evaluators to consume active definitions. This work spans the shared contracts, server services/endpoints, WASM client registration, database uniqueness migration, and a small club-admin UI surface in the existing administration area.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to `Complete` and write its Phase Summary, then run the Verification Plan and record the result before moving on. When all phases are done, fill in Final Recap and Deployment Plan.

## Phase 1: Shared contracts, validation, and persistence guardrails

Status: Complete

- [x] Inspect the existing `PlayerTagEntity` lifecycle, `TagDefinitionLifecycleService`, club admin authorization, and route patterns to align the new management API with the current app conventions.
- [x] Add shared tag-definition input/query contracts and service interfaces in `Nova.Shared` for create, edit, and bounded management queries.
- [x] Add or update `PlayerTagEntity`/configuration/migration so names are trimmed, case-insensitive unique per club, and race-safe under concurrent create/edit attempts.
- [x] Add the management service shell and validation logic for create/edit/archive/restore using the club-admin lifecycle rules and shared `InputValidator` pattern.

### Verification Plan

- `dotnet build Nova.slnx`
- `dotnet test --project Nova.Unit.Tests --filter-class "*TagDefinition*"`
- `dotnet test --project Nova.Integration.Tests --filter-class "*TagDefinition*"`

### Phase Summary

Added `ITagDefinitionService` plus create/edit/archive/restore input and query contracts in `Nova.Shared`, an admin-gated `TagDefinitionManagementService` following the `ServiceResult`/`ServiceProblem` conventions, and a normalized-uniqueness migration (trimmed, case-insensitive `Name` unique per `ClubId`). Duplicate prevention and lifecycle gating are enforced through the database uniqueness constraint with a retrying execution strategy for race safety.

## Phase 2: Server endpoints and WASM client wiring

Status: Complete

- [x] Add the `MapGroup` endpoints for active/archived queries and admin mutations, with route constants and URL builders shared across server and client.
- [x] Implement service boundary mapping to `ServiceResult` and convert to `ProblemDetails` with trace IDs, auth metadata, and antiforgery handling on writes.
- [x] Register the new endpoints and services in `Nova/Program.cs` and add matching WASM client methods/DI registration.
- [x] Verify the client contract maps success payloads and validation errors without dropping data or masking server-provided problems.

### Verification Plan

- `dotnet build Nova.slnx`
- `dotnet test --project Nova.Unit.Tests --filter-class "*TagDefinition*"`
- `dotnet test --project Nova.Integration.Tests --filter-class "*TagDefinition*"`

### Phase Summary

Added the `/api/clubs/{clubId}/tag-definitions` `MapGroup` with active/archived queries and admin-only mutation endpoints, shared route constants/URL builders, `ToHttpResult` conversion with `ProblemDetails` + trace IDs, antiforgery on writes, and authorization metadata. Registered services and endpoints in `Nova/Program.cs` and added matching `HttpTagDefinitionService` WASM client methods with DI registration, verified by `HttpTagDefinitionServiceTests` and `TagDefinitionEndpointTests`.

## Phase 3: Admin UI and behavior validation

Status: Complete

- [x] Add/adjust the club administration pages and dialog components to show Active/Archived views, create/edit forms, color input validation, archive/restore confirmation, and role-correct command visibility.
- [x] Ensure approved evaluators can read active definitions but cannot see or trigger management actions.
- [x] Add focused component or HTTP tests for duplicate prevention, lifecycle gating, and admin-only visibility behavior.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*TagDefinition*"`
- `dotnet test --project Nova.Integration.Tests --filter-class "*TagDefinition*"`
- Playwright/browser pass for admin plus evaluator visibility paths when the UI behavior is ready.

### Phase Summary

Added `TagDefinitionManagementPanel` (admin-only create/edit/archive/restore with color validation and confirmation dialogs) hosted in `ClubAdmin.razor`, and a read-only `ActiveTagDefinitionsPanel` hosted in `ClubDetail.razor` so approved evaluators see active definitions without any management controls. Panels use interactive render modes and `[PersistentState]` to avoid duplicate fetches. `TagDefinitionComponentsTests` covers render-mode hosting, persisted-state no-fetch, admin mutation flows (default and error paths), forbidden navigation for non-admins, and member read-only degradation to an empty state on error.

## Final Recap

Built a complete tag-definition management vertical slice: normalized-uniqueness persistence (trimmed, case-insensitive unique name per club with race-safe retry), `ServiceResult`-based service layer with lifecycle gating, REST endpoints with `ProblemDetails`/trace IDs/antiforgery/auth, a WASM client, and a role-correct admin UI. Issue #66 is fully addressed by this PR.

## Deployment Plan

1. Merge the PR; the incremental EF Core migration (`..._AddNormalizedTagDefinitionName`) applies automatically on deploy via the existing migration strategy.
2. No environment configuration or new secrets are required.
3. Verify post-deploy: an admin can create/edit/archive/restore tag definitions in a club; an approved evaluator sees active definitions on the club detail page with no management controls.
4. Existing tag definitions are backfilled to normalized names by the migration.
