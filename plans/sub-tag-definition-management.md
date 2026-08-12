# Sub Tag Definition Management API and Admin UI

Provide administrator-only club tag-definition management while allowing approved evaluators to consume active definitions. This work spans the shared contracts, server services/endpoints, WASM client registration, database uniqueness migration, and a small club-admin UI surface in the existing administration area.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to `Complete` and write its Phase Summary, then run the Verification Plan and record the result before moving on. When all phases are done, fill in Final Recap and Deployment Plan.

## Phase 1: Shared contracts, validation, and persistence guardrails

Status: Not started

- [ ] Inspect the existing `PlayerTagEntity` lifecycle, `TagDefinitionLifecycleService`, club admin authorization, and route patterns to align the new management API with the current app conventions.
- [ ] Add shared tag-definition input/query contracts and service interfaces in `Nova.Shared` for create, edit, and bounded management queries.
- [ ] Add or update `PlayerTagEntity`/configuration/migration so names are trimmed, case-insensitive unique per club, and race-safe under concurrent create/edit attempts.
- [ ] Add the management service shell and validation logic for create/edit/archive/restore using the club-admin lifecycle rules and shared `InputValidator` pattern.

### Verification Plan

- `dotnet build Nova.slnx`
- `dotnet test --project Nova.Unit.Tests --filter-class "*TagDefinition*"`
- `dotnet test --project Nova.Integration.Tests --filter-class "*TagDefinition*"`

### Phase Summary

_(write when phase completes)_

## Phase 2: Server endpoints and WASM client wiring

Status: Not started

- [ ] Add the `MapGroup` endpoints for active/archived queries and admin mutations, with route constants and URL builders shared across server and client.
- [ ] Implement service boundary mapping to `ServiceResult` and convert to `ProblemDetails` with trace IDs, auth metadata, and antiforgery handling on writes.
- [ ] Register the new endpoints and services in `Nova/Program.cs` and add matching WASM client methods/DI registration.
- [ ] Verify the client contract maps success payloads and validation errors without dropping data or masking server-provided problems.

### Verification Plan

- `dotnet build Nova.slnx`
- `dotnet test --project Nova.Unit.Tests --filter-class "*TagDefinition*"`
- `dotnet test --project Nova.Integration.Tests --filter-class "*TagDefinition*"`

### Phase Summary

_(write when phase completes)_

## Phase 3: Admin UI and behavior validation

Status: Not started

- [ ] Add/adjust the club administration pages and dialog components to show Active/Archived views, create/edit forms, color input validation, archive/restore confirmation, and role-correct command visibility.
- [ ] Ensure approved evaluators can read active definitions but cannot see or trigger management actions.
- [ ] Add focused component or HTTP tests for duplicate prevention, lifecycle gating, and admin-only visibility behavior.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*TagDefinition*"`
- `dotnet test --project Nova.Integration.Tests --filter-class "*TagDefinition*"`
- Playwright/browser pass for admin plus evaluator visibility paths when the UI behavior is ready.

### Phase Summary

_(write when phase completes)_

## Final Recap

_(write when all phases complete: summary of the entire piece of work)_

## Deployment Plan

_(write when all phases complete: step-by-step deployment instructions)_
