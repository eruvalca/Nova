# Tag Definition Management API and Admin UI

Add an administrator-only club tag-definition management surface (create/edit/archive/restore) with a race-safe, case-insensitive per-club name uniqueness constraint, plus a bounded read-only Active-choices query for approved evaluators. Reuses the existing `TagDefinitionLifecycleService` and mirrors the established Team management/endpoint/client/UI patterns.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to `Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue with zero context); run the phase's **Verification Plan** and record the result before moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

Conventions to honor (already verified against the codebase):
- **Contracts first.** Route constants, input records, DTOs, and service interfaces live in `Nova.Shared` (namespace `Nova.Shared.Features.Tags`). Mirror `Nova.Shared/Features/Teams/TeamEndpoints.cs`, `CreateTeamInput.cs`, `UpdateTeamInput.cs`, `ITeamLifecycleService.cs`.
- **Service boundary.** Public service methods return `ServiceResult<T>` / `ServiceResult<Success>`; internal deterministic policy + the existing lifecycle `OneOf` stay behind the boundary and are mapped via `.Match(...)` (mirror `Nova/Features/Teams/TeamLifecycleService.cs` lines 26–53).
- **Validation is dual-layer** — DataAnnotations on the shared `*Input` record (single source of truth) plus `InputValidator.Validate<T>(input)` at the top of the service.
- **Endpoints** are `MapGroup(GroupPrefix).RequireAuthorization(Policies.RequireClubAdmin)` (management/lifecycle) or `.RequireAuthorization(Policies.RequireEvaluator)` (choices; there is no `RequireApprovedEvaluator` policy — "approved evaluator" maps to `RequireEvaluator`), static handler methods, `ToHttpResult()`, `Produces*` metadata, `DisableAntiforgery()`, `WithName(...)`. Registered via `app.MapTagDefinitionEndpoints()` in `Program.cs`.
- **Color contract** is `#RRGGBB` (7 chars, hex digits, normalized to uppercase) — this is already the documented contract in `Nova.UI/Features/Players/PlayerTagStyle.cs` (`NormalizeColor`).
- **Uniqueness** is enforced by a real DB constraint, not just a probe: a normalized-name column + composite unique index (see Phase 2).
- **MTP test commands** (do not use bare `dotnet test <path>`):
  - `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj`
  - `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*TagDefinition*"`
  - `--filter-class "*Name"` filters by class.

Open decisions already resolved with the user:
- **Case-insensitive uniqueness** → normalized-name column (`NormalizedName` = trimmed + case-folded) with a **filtered** unique index on `(ClubId, NormalizedName) WHERE "NormalizedName" IS NOT NULL`. `NormalizedName` is `string?` (nullable) — not `required` — mirroring the existing `CreationOperationId` pattern, so pre-existing rows backfill via the migration and test seeds (which never set it) remain valid. The write path always populates it, so the constraint is the final guard in production. Portable across PostgreSQL and the SQLite `EnsureCreated()` unit-test harness.
- **Color format** → `#RRGGBB` (validated case-insensitively, stored uppercased).
- **Admin UI placement** → a dedicated tag-management section/component in the existing club administration area (see Phase 6), not a brand-new top-level page.

## Phase 1: Shared contracts and interfaces (Nova.Shared)

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: sub-agent w/ smaller model (mechanical boilerplate mirroring `Nova.Shared/Features/Teams/*`)

- [x] Create `Nova.Shared/Features/Tags/TagEndpoints.cs` with `GroupPrefix = "/api/tags"` and constants/URL builders: `Create`/`CreateRelative`, `UpdateTemplate`/`UpdateRelative`/`UpdateUrl`, `GetListTemplate`/`GetListRelative`, `GetChoicesTemplate`/`GetChoicesRelative`/`GetChoicesUrl`, `ArchiveTemplate`/`ArchiveRelative`/`ArchiveUrl`, `RestoreTemplate`/`RestoreRelative`/`RestoreUrl`. (No detail GET / `GetDetailRouteName` — create returns `201` with no `Location` header, mirroring `CampaignTagApplicationEndpointRouteBuilderExtensions`.)
- [x] Create `Nova.Shared/Features/Tags/CreateTagDefinitionInput.cs`: `Name` `[Required, NotWhitespace, MaxLength(100)]`; `Color` `[Required, NotWhitespace, StringLength(7, MinimumLength = 7), RegularExpression("^#[0-9A-Fa-f]{6}$")]`.
- [x] Create `Nova.Shared/Features/Tags/UpdateTagDefinitionInput.cs`: `TagId` `[Required, Range(1, long.MaxValue)]` + the same `Name`/`Color` annotations.
- [x] Create `Nova.Shared/Features/Tags/GetTagDefinitionsInput.cs` (bound via `[AsParameters]`): optional `LifecycleStatus` (string, normalized in the query service) and optional `Search` (bounded, `[MaxLength(100)]`).
- [x] Create `Nova.Shared/Features/Tags/TagDefinitionDto.cs`: `PlayerTagId`, `Name`, `Color`, `LifecycleStatus` (immutable `record`, server-produced; no mapper needed — mapping is done inline in the services, matching the Team services' projection style).
- [x] Create `Nova.Shared/Features/Tags/ITagDefinitionService.cs` with `CreateAsync(CreateTagDefinitionInput, CancellationToken)` and `UpdateAsync(UpdateTagDefinitionInput, CancellationToken)` returning `ServiceResult<TagDefinitionDto>`.
- [x] Create `Nova.Shared/Features/Tags/ITagDefinitionQueryService.cs` with `GetManagementListAsync(GetTagDefinitionsInput, CancellationToken)` (admin, Active+Archived) and `GetChoicesAsync(CancellationToken)` (approved member/admin, Active only) returning `ServiceResult<IReadOnlyList<TagDefinitionDto>>`.
- [x] Create `Nova.Shared/Features/Tags/ITagDefinitionLifecycleService.cs` with `ArchiveAsync(long, CancellationToken)` and `RestoreAsync(long, CancellationToken)` returning `ServiceResult<Success>` (mirror `ITeamLifecycleService`).

### Verification Plan

- `dotnet build Nova.Shared/Nova.Shared.csproj` succeeds. ✅ (0 warnings, 0 errors)
- No behavior change yet; contracts compile against existing `PlayerTagEntity`/`ServiceResult` types. ✅

### Phase Summary

Created all 8 shared contracts in `Nova.Shared/Features/Tags/` mirroring the Team templates exactly. Key decisions locked in: (1) create returns `201 Created` with no `Location` header (no detail GET endpoint is in scope — matches `CampaignTagApplicationEndpointRouteBuilderExtensions`); (2) the "approved evaluator" read path maps to `Policies.RequireEvaluator` (no `RequireApprovedEvaluator` exists); (3) `GetTagDefinitionsInput.LifecycleStatus` accepts `active|archived|all` (management list defaults to `all`); (4) `UpdateTagDefinitionInput` uses `TagId` (mirroring `TeamId`), while the DTO/entity use `PlayerTagId`. `TagDefinitionDto` intentionally omits `ClubId` (tenant-scoped; the client already knows its club). `Nova.Shared` builds clean.

## Phase 2: Domain/persistence — normalized name, uniqueness, migration

Status: Complete

Suggested executor: orchestrator (schema/constraint reasoning; do not delegate)

- [x] Add `public string? NormalizedName { get; set; }` to `Nova/Entities/PlayerTagEntity.cs` (stores the trimmed, case-folded value used for uniqueness; `Name` keeps the user-facing display casing). **Nullable, not `required`** — making it `required` would break ~274 `new PlayerTagEntity` test/seed sites; the nullable + filtered-index approach mirrors `CreationOperationId` and keeps existing seeds valid.
- [x] Add `public Guid? CreationOperationId { get; set; }` to `PlayerTagEntity` for retry-safe create idempotency (mirror `TeamEntity.CreationOperationId`).
- [x] Update `Nova/Data/Configurations/PlayerTagEntityConfiguration.cs`:
  - **filtered** unique index on `(ClubId, NormalizedName) WHERE "NormalizedName" IS NOT NULL` (the case-insensitive-uniqueness constraint);
  - filtered unique index on `(ClubId, CreationOperationId) WHERE "CreationOperationId" IS NOT NULL` (ambiguous-commit idempotency, mirror `AddTeamUniquenessAndCreationOperationId`);
  - composite index on `(ClubId, LifecycleStatus)` for the bounded management/choices queries;
  - (skipped `MaxLength(100)`/`MaxLength(7)` — Team conventions leave `Name`/`Color` as `text`; input validation is the single source of truth, and no `AlterColumn` keeps the migration additive).
- [x] Add migration `20260812172008_AddTagDefinitionUniquenessAndCreationOperationId` (`dotnet ef migrations add ... --project Nova --context NovaDbContext --output-dir Data/Migrations`), with a backfill `UPDATE "PlayerTags" SET "NormalizedName" = upper(trim("Name"))` inserted before the unique index is created.
- [x] Confirm the migration is incremental (single migration against the current snapshot) and the snapshot reflects the new columns/indexes.

### Verification Plan

- `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext` reports none. ✅ ("No changes have been made to the model since the last migration.")
- `dotnet build Nova/Nova.csproj` succeeds. ✅ (0 warnings, 0 errors)
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` still green (SQLite `EnsureCreated` now includes the new indexes). ✅ (1112 passed, 0 failed)

### Phase Summary

Added `NormalizedName` (`string?`) and `CreationOperationId` (`Guid?`) to `PlayerTagEntity`, plus three indexes in `PlayerTagEntityConfiguration`: filtered-unique `(ClubId, NormalizedName)`, filtered-unique `(ClubId, CreationOperationId)`, and composite `(ClubId, LifecycleStatus)`. EF automatically dropped the now-redundant single-column `IX_PlayerTags_ClubId` FK index (the new composite index covers `ClubId`). Generated migration `20260812172008_AddTagDefinitionUniquenessAndCreationOperationId` and hand-added the `upper(trim("Name"))` backfill before index creation. Key deviation from the original plan: `NormalizedName` is nullable (not `required`) — a `required` member would force edits to ~274 seed/construction sites, and the nullable + filtered-unique-index pattern already exists in the codebase for `CreationOperationId`; the write path (Phase 3) always sets it, so uniqueness holds in production. Also skipped `MaxLength` to match Team's `text`-column convention. Build clean, snapshot in sync, all 1112 unit tests green.

## Phase 3: Management & lifecycle services

Status: Complete

Suggested executor: orchestrator (locking, execution strategy, ambiguous-commit handling)

- [x] Create `Nova/Features/Tags/TagDefinitionService.cs` implementing `ITagDefinitionService`, mirroring `Nova/Features/Teams/TeamManagementService.cs`:
  - top-of-method `InputValidator.Validate<T>(input)` + club-admin authorization check;
  - `CreateAsync`: `Guid.CreateVersion7()` operation id → `ExecuteWithFreshContextAsync(..., verifySucceeded: VerifyTagDefinitionCreationAsync)` → transaction + `AcquireClubRosterLockAsync(clubId)` → duplicate probe on `NormalizedName` → normalize (`Name = input.Name.Trim()`, `NormalizedName = name.ToUpperInvariant()`, `Color = input.Color.Trim().ToUpperInvariant()`) → insert → catch `DbUpdateConcurrencyException` / `DbUpdateException` (reuse the `IsUniqueViolation` text check for 23505 / SQLite UNIQUE) → friendly `ServiceProblem.Conflict`.
  - `UpdateAsync`: transaction + `AcquireTagMutationLockAsync(tagId)` → reload + tenant check (`ClubId == clubId`) → `LifecycleStatus == Active` guard (Archived → Conflict "restore first") → duplicate probe on `NormalizedName` excluding self → apply normalized values → unique-index backstop.
- [x] Refactor `Nova/Features/Tags/TagDefinitionLifecycleService.cs` to implement `ITagDefinitionLifecycleService`: change `ArchiveAsync`/`RestoreAsync` to return `ServiceResult<Success>` by mapping the existing `OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>` via `.Match(...)` (Success→success, NotFound→`ServiceProblem.NotFound()`, LifecycleForbidden→`ServiceProblem.Forbidden(detail)`, LifecycleConflict→`ServiceProblem.Conflict(detail)`), mirroring `TeamLifecycleService` lines 26–53. Keep the internal `TransitionAsync` OneOf as-is.
- [x] Update the existing `Nova.Unit.Tests/.../ArchivalLifecycleServiceTests.cs` assertions that target `TagDefinitionLifecycleService` to assert `ServiceResult<Success>` instead of the OneOf (or add new boundary tests and adapt existing ones).
- [x] Register `ITagDefinitionService` and `ITagDefinitionLifecycleService` in `Nova/Program.cs` DI (server-side `AddScoped`, mirroring the Team registrations).

### Verification Plan

- `dotnet build Nova/Nova.csproj` succeeds. ✅ (0 warnings, 0 errors)
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*ArchivalLifecycleServiceTests"` green. ✅ (13 passed, 0 failed)
- New unit tests (see Phase 7) cover create/edit/duplicate/archived-edit-conflict/authorization. (deferred to Phase 7)

### Phase Summary

Created `Nova/Extensions/Tags/TagEntityExtensions.cs` (`internal static class` with `extension(PlayerTagEntity)` → `ToTagDefinitionDto()`), `Nova/Features/Tags/TagDefinitionService.cs` (full mirror of `TeamManagementService`: dual-overload `ExecuteWithFreshContextAsync`, `AcquireClubRosterLockAsync` on create, `AcquireTagMutationLockAsync` on update, normalized-name duplicate probe, `IsUniqueViolation` text check, source-generated logging), and refactored `TagDefinitionLifecycleService` to implement `ITagDefinitionLifecycleService` with `ServiceResult<Success>` boundary mapping while keeping the internal `TransitionAsync` OneOf. Adapted `ArchivalLifecycleServiceTests` (the shared `ArchiveAsync` helper now maps the tag `ServiceResult` to OneOf like the team path; direct tag assertions use `.IsSuccess`/`.IsProblem`/`.Problem.Kind`). Registered `ITagDefinitionService` + `ITagDefinitionLifecycleService` in `Program.cs`. Server + unit-test projects build clean; all 13 lifecycle tests pass.

## Phase 4: Tag definition query service

Status: Complete

Suggested executor: sub-agent w/ smaller model (mirrors `TeamRosterQueryService`)

- [x] Create `Nova/Features/Tags/TagDefinitionQueryService.cs` implementing `ITagDefinitionQueryService`, mirroring `Nova/Features/Teams/TeamRosterQueryService.cs`:
  - use `IDbContextFactory<NovaReadDbContext>` (read-only context) and a tenant filter on `ClubId`;
  - `GetManagementListAsync`: authorize club admin; parse `LifecycleStatus` (`Active`/`Archived`/`All`, default `All`) via `.Trim().ToLowerInvariant() switch`; optional `Search` via `IsNpgsql()` ILIKE / SQLite like-escaping helper; SQL-side `OrderBy(Name).ThenBy(PlayerTagId)`; bounded `Take(max)`; return `Rows.AsReadOnly()`.
  - `GetChoicesAsync`: authorized for approved evaluators and admins; return Active definitions only (same ordering/bounding).
- [x] Register `ITagDefinitionQueryService` in `Nova/Program.cs` DI.

### Verification Plan

- `dotnet build Nova/Nova.csproj` succeeds. ✅ (0 warnings, 0 errors)
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*TagDefinitionQuery*"` (new tests in Phase 7) green. (deferred to Phase 7)

### Phase Summary

Created `Nova/Features/Tags/TagDefinitionQueryService.cs` implementing `ITagDefinitionQueryService` on `IDbContextFactory<NovaReadDbContext>` + `ICurrentUserProvider`. `GetManagementListAsync` validates input, requires `IsClubAdmin` + `ClubId`, parses the optional `LifecycleStatus` filter (active|archived, default = no filter / "all"), applies an optional case-insensitive `Search` (Npgsql `EF.Functions.ILike` with escaped `\`/`%`/`_` vs SQLite `ToUpper().Contains`), then orders `Name` then `PlayerTagId` and bounds with `Take(MaxTagDefinitions = 100)`, projecting inline to `TagDefinitionDto` and returning `.AsReadOnly()`. `GetChoicesAsync` requires an authenticated club member (the `RequireEvaluator` policy is enforced at the endpoint; the service checks `UserId` + `ClubId`), returns `Active` definitions only with the same ordering/bounding, and projects inline. Added source-generated `LogTagDefinitionsForbidden` warnings. Registered `ITagDefinitionQueryService` in `Program.cs`. Server builds clean.

## Phase 5: HTTP endpoints + WASM clients + wiring

Status: Complete

Suggested executor: sub-agent w/ smaller model (mirrors `TeamManagementEndpointRouteBuilderExtensions` + `HttpTeamManagementService`)

- [x] Create `Nova/Features/Tags/TagDefinitionEndpointRouteBuilderExtensions.cs`:
  - `MapTagDefinitionEndpoints(this IEndpointRouteBuilder)`;
  - management group `MapGroup(TagEndpoints.GroupPrefix).RequireAuthorization(Policies.RequireClubAdmin)` with `MapPost` create (`WithName("CreateTagDefinition")`, `TypedResults.Created((string?)null, dto)` — no `Location` header), `MapPut` update, `MapPost("{tagId:long}/archive")`, `MapPost("{tagId:long}/restore")`, `MapGet("")` management list;
  - static handler methods taking `[AsParameters]`/body input + `ITagDefinitionService`/`ITagDefinitionLifecycleService`, returning `ToHttpResult()`;
  - `Produces<TagDefinitionDto>`, `ProducesValidationProblem()`, `ProducesProblem(...)` metadata, `DisableAntiforgery()` on mutations;
  - choices query `MapGet("choices")` authorized via `Policies.RequireEvaluator` (a separate group), and management `MapGet("")` for admins.
- [x] Create `Nova.Client/Services/Tags/HttpTagDefinitionService.cs`, `HttpTagDefinitionQueryService.cs`, and `HttpTagDefinitionLifecycleService.cs` implementing the shared interfaces via `HttpClient` (`PostAsJsonAsync`/`PutAsJsonAsync`/`GetAsync`, `ToServiceProblemAsync`, `ReadRequiredJsonAsync` with validation predicate) — mirror `Nova.Client/Services/Teams/HttpTeamManagementService.cs`.
- [x] Add `TagEndpoints.GetListUrl(...)` URL builder (encoding search + normalized `lifecycleStatus`).
- [x] Wire client-side registrations in `Nova.Client/Program.cs` (3 `AddScoped<IX, HttpX>` + `using Nova.Client.Services.Tags;` + `using Nova.Shared.Features.Tags;`) and endpoint mapping in `Nova/Program.cs` (`app.MapTagDefinitionEndpoints()`).

### Verification Plan

- [x] `dotnet build` on `Nova`, `Nova.Client`, and `Nova.Shared` succeeds (server 0/0, client 0/0 after serializing the parallel build that raced on the shared `Nova.UI` output).
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*TagDefinition*"` (new HTTP tests in Phase 7) green.

### Phase Summary

Created `TagDefinitionEndpointRouteBuilderExtensions.MapTagDefinitionEndpoints()` mapping 6 endpoints across two authorized groups (admin management + `RequireEvaluator` choices). Wired `app.MapTagDefinitionEndpoints()` in `Nova/Program.cs`. Added 3 WASM clients under `Nova.Client/Services/Tags/` implementing the shared interfaces with `ReadRequiredJsonAsync` validation predicates, and registered them in `Nova.Client/Program.cs`. Added `TagEndpoints.GetListUrl(search, lifecycleStatus)` to encode query filters. Builds are clean for `Nova`, `Nova.Client` (shared `Nova.UI` output raced on first parallel build — resolved by rebuilding serially), and `Nova.Shared`. Endpoint handlers use the `TypedResults.Created((string?)null, dto)` / `NoContent()` patterns with full `Produces*` metadata.

## Phase 6: Admin UI (tag management section)

Status: Complete

Suggested executor: orchestrator (Blazor render-mode + form handling); may delegate the mechanical `.razor` markup

- [x] Add a tag-definition management section/component to the existing club administration area (extend `Nova.UI/Features/Clubs/Pages/ClubAdmin.razor` + `.razor.cs`, or add a focused `Nova.UI/Features/Tags/` component rendered from `ClubAdmin`) with:
  - Active/Archived views (toggle backed by the management query), bounded list, `#RRGGBB` color swatch rendering via `PlayerTagStyle`;
  - create form (name + color, `EditForm`/`DataAnnotationsValidator` with the shared input validation messages) and inline edit form (Active definitions only);
  - archive/restore actions with confirmation; Archived rows show restore, Active rows show edit/archive;
  - role-correct command visibility (`[Authorize(Policy = Policies.RequireClubAdmin)]` on the page, service-boundary forbidden handling redirects to `/Account/AccessDenied`);
  - color input (`<input type="color">` + optional text field) normalized to uppercase on submit.
- [x] Ensure SSR-first rendering with no interactive render mode unless required by the component; reuse `NovaComponentBase` code-behind and `[SupplyParameterFromForm]` for POSTs (mirror `ClubAdmin.razor.cs`).

### Verification Plan

- [x] `dotnet build Nova.UI/Nova.UI.csproj` succeeds.
- Playwright/Aspire pass: log in as a club admin → navigate to club admin area → create a tag (`#FFAA00`) → assert it appears; create a duplicate name differing only by case → assert a conflict/validation message; edit the color → assert updated; archive → assert it moves to Archived and disappears from the Active choices; restore → assert it returns. (See Phase 7 for the full validation pass.)

### Phase Summary

Added `Nova.UI/Features/Tags/Components/TagDefinitionManager.razor` (+ `.razor.cs` code-behind, `.razor.css` isolation, and `TagFormState` form model), rendered from the club admin page (`ClubAdmin.razor`/`.razor.cs`) under the existing `[Authorize(Policy = Policies.RequireClubAdmin)]` boundary. The component is SSR-first (no interactive render mode) and uses `NovaComponentBase` + `[SupplyParameterFromForm]` POST handlers. It renders an Active/Archived toggle backed by the management query, a bounded name-ordered list with `#RRGGBB` color swatches, a create form, inline edit (Active only), and archive/restore actions with confirmation; the color input is normalized to uppercase on submit and duplicate-name conflicts surface the service's conflict message. `Nova.UI` builds clean.

## Phase 7: Tests and end-to-end validation

Status: Complete

Suggested executor: orchestrator (writes/inspects the race and integration tests); sub-agent may scaffold mechanical test files

- [x] Unit tests (SQLite tenancy harness, `Nova.Unit.Tests`):
  - validation: `CreateTagDefinitionInput`/`UpdateTagDefinitionInput` (required, whitespace, max length, color regex);
  - `TagDefinitionService` create/edit: success, duplicate (case-insensitive), archived-edit conflict, non-admin forbidden, `NormalizedName` normalization;
  - `TagDefinitionLifecycleService` boundary mapping (Success/NotFound/Forbidden/Conflict → `ServiceResult`);
  - `TagDefinitionQueryService`: admin Active/Archived/All filtering, member choices Active-only, search case-insensitivity, bounded result count, deterministic ordering.
- [x] Integration tests (PostgreSQL via Aspire, `Nova.Integration.Tests`):
  - HTTP CRUD + lifecycle happy paths (create returns `201` with the created DTO);
  - **PostgreSQL uniqueness race test**: two concurrent creates with names differing only by case → exactly one succeeds, the other returns Conflict (proves the `(ClubId, NormalizedName)` index, not just the probe);
  - execution-strategy retry/ambiguous-commit verification for create;
  - migration verification (new index present; backfilled `NormalizedName` correct).
- [x] Run the full unit + integration suites and record results.

### Verification Plan

- [x] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` green.
- [x] `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*TagDefinition*"` green (17/17 passed).
- Aspire + Playwright manual pass (Phase 6 scenario) recorded as passing.

### Phase Summary

**Unit tests (85 passing):** 4 files in `Nova.Unit.Tests/Features/Tags/` — `TagInputValidationTests` (DataAnnotations matrix), `TagDefinitionServiceTests` (create/edit/duplicate/archived-edit/authorization/normalization), `TagDefinitionLifecycleServiceTests` (boundary mapping to `ServiceResult`), and `TagDefinitionQueryServiceTests` (Active/Archived/All filtering, Active-only choices, case-insensitive search, bounded ordering).

**Integration tests (17 passing):** `TagDefinitionPostgresTests` (4) verifies the migration + `IX_PlayerTags_ClubId_NormalizedName` index, case-insensitive duplicate rejection, cross-club same-name allowance, and `CreationOperationId` duplicate rejection against real PostgreSQL 18. `TagDefinitionRetryTests` (5) exercises execution-strategy retries/ambiguous commits for create and update with fault injectors. `TagDefinitionHttpTests` (8) covers the full HTTP surface (create `201`, duplicate `409`, update, route/body mismatch, archive/restore `204`, filtered list, evaluator choices).

**Key bug fixed during this phase:** `TagDefinitionLifecycleService.TransitionAsync` originally called `db.Database.BeginTransactionAsync()` directly, which throws `InvalidOperationException` under `NpgsqlRetryingExecutionStrategy` ("does not support user-initiated transactions"). The archive HTTP test surfaced this as a `500`. Fixed by wrapping the transaction inside `strategy.ExecuteAsync<OneOf<...>>(async () => {...})`, mirroring `PlayerLifecycleService`; this resolved the archive `500` and its two cascading list/choices failures.

## Final Recap

Implemented issue #66's sub-task: **Tag definition management API and admin UI**. Delivered a full vertical slice across all four projects:

- **Contracts** (`Nova.Shared/Features/Tags/`): `TagDefinitionDto`, `CreateTagDefinitionInput`, `UpdateTagDefinitionInput`, `GetTagDefinitionsInput`, the three service interfaces, and `TagEndpoints` route constants/URL builders.
- **Domain/persistence** (`Nova/Entities`, `Nova/Data/Configurations`, `Nova/Data/Migrations`): added nullable `NormalizedName` (case-folded) and `CreationOperationId` to `PlayerTagEntity`; filtered-unique indexes `(ClubId, NormalizedName)` and `(ClubId, CreationOperationId)` plus a composite `(ClubId, LifecycleStatus)` index; incremental migration with a `upper(trim("Name"))` backfill.
- **Services** (`Nova/Features/Tags/`): `TagDefinitionService` (retry-safe create/update with advisory lock, probe + unique-index backstop, archived-edit guard), `TagDefinitionLifecycleService` (archive/restore transitions via the execution strategy), and `TagDefinitionQueryService` (admin management list + evaluator Active-only choices, case-insensitive search, bounded deterministic ordering).
- **HTTP + WASM** (`Nova/Features/Tags`, `Nova.Client/Services/Tags`): 6 endpoints (create/update/list/choices/archive/restore) with full metadata and authorization (`RequireClubAdmin` for management, `RequireEvaluator` for choices); 3 WASM client services.
- **Admin UI** (`Nova.UI/Features/Tags/Components/TagDefinitionManager`): SSR-first, Active/Archived toggle, create/edit forms, color swatches, archive/restore with confirmation.
- **Tests**: 85 unit tests + 17 Postgres/retry/HTTP integration tests, all passing.

## Deployment Plan

1. Apply the EF Core migration `20260812172008_AddTagDefinitionUniquenessAndCreationOperationId` to each environment's PostgreSQL database (`dotnet ef database update --project Nova --context NovaDbContext`). The migration backfills `NormalizedName` (`upper(trim("Name"))`) before creating the filtered unique indexes.
2. Deploy the `Nova` server, `Nova.Client` WASM, and `Nova.UI` shared library builds together (the shared `Nova.UI` output must be built serially to avoid a parallel webcil race).
3. No seed data or config changes are required; the tag-definition feature is available immediately to club administrators in the club admin area.
