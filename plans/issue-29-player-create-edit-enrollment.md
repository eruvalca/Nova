# Player Create/Edit and Transactional Campaign Enrollment

Add admin-only player create/edit contracts, a transactional `PlayerManagementService` that atomically enrolls new players into every Active campaign, a club-scoped advisory lock to serialize concurrent player/campaign creation, an edit service that blocks graduation-year changes when they would invalidate active Assigned placements (returning structured blocker data), HTTP endpoints, a WASM client, and focused tests at all three layers.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to `Complete` and write its **Phase Summary**; run the phase's **Verification Plan** and record the result before moving on.

Key design decisions:
- Lock namespace: add `AcquireClubRosterLockAsync(clubId)` to `Nova/Features/Shared/LifecycleMutationLock.cs` using a distinct key space (e.g. `(long.MinValue / 4) + clubId`). Acquire this lock in both `CreateAsync` and in campaign-creation (when #9 lands) to prevent concurrent create+create races.
- `PlayerManagementService` uses `ServiceResult<PlayerDto>` (crosses the HTTP/WASM boundary), not native OneOf.
- Graduate-year edit: use a `PlayerGraduationYearPolicy` (feature-local pure policy) to classify placements as blocked, returning a `GraduationYearEditBlocked` result that carries `IReadOnlyList<GraduationYearBlockerItem>`.
- No migration expected (all entities and columns already exist).
- Lock acquisition order: campaign lock (if any) → player lock → team lock (as per shared convention, but we're using the club-roster lock for create; no campaign/player lock needed for edit since we acquire the player lock).

---

## Phase 1: Shared Contracts

Status: Not started

- [ ] Create `Nova.Shared/Players/` directory with the following files:
  - `CreatePlayerInput.cs` — `FirstName` [Required, NotWhitespace, MaxLength(100)], `LastName` [Required, NotWhitespace, MaxLength(100)], `DateOfBirth` [Required], `GraduationYear` [Required, Range(2000, 2100)], optional `Gender?`, optional `JerseyNumber?` [Range(0, 9999)]
  - `UpdatePlayerInput.cs` — `PlayerId` [Required, Range(1, long.MaxValue)], same profile fields as Create
  - `PlayerDto.cs` — readonly record with all profile fields plus `PlayerId`, `ClubId`, `LifecycleStatus`
  - `GraduationYearBlockerItem.cs` — readonly record: `PlayerId`, `PlayerCampaignAssignmentId`, `CampaignId`, `TeamId`, `TeamGraduationYear`
  - `IPlayerManagementService.cs` — interface with `CreateAsync(CreatePlayerInput, CancellationToken)` → `Task<ServiceResult<PlayerDto>>` and `UpdateAsync(UpdatePlayerInput, CancellationToken)` → `Task<ServiceResult<PlayerDto>>`
  - `PlayerEndpoints.cs` — route constants: `GroupPrefix = "/api/players"`, `Create = "/api/players"`, `CreateRelative = ""`, `UpdateTemplate = "/api/players/{playerId:long}"`, `UpdateRelative = "{playerId:long}"`, `UpdateUrl(long)` builder

### Verification Plan

- `dotnet build Nova.slnx` — expect zero errors after adding shared files

### Phase Summary

_(write when phase completes)_

---

## Phase 2: Club Roster Lock and Server Service

Status: Not started

- [ ] Add `AcquireClubRosterLockAsync(long clubId, CancellationToken)` to `Nova/Features/Shared/LifecycleMutationLock.cs` using key `(long.MinValue / 4) + clubId` to avoid collisions with the existing player/campaign/team lock namespaces
- [ ] Create `Nova/Features/Players/PlayerGraduationYearPolicy.cs`:
  - Pure static class; `Evaluate(int proposedGraduationYear, IReadOnlyList<AssignedPlacementFacts> placements)` where `AssignedPlacementFacts` holds `(PlayerCampaignAssignmentId, CampaignId, TeamId, TeamGraduationYear)`
  - Returns `OneOf<GraduationYearMayChange, GraduationYearEditBlocked>` where `GraduationYearEditBlocked` carries `IReadOnlyList<GraduationYearBlockerItem>`
  - Rule: a placement is blocked when `proposedGraduationYear < teamGraduationYear`
- [ ] Create `Nova/Features/Players/PlayerManagementService.cs` implementing `IPlayerManagementService`:
  - **Constructor**: `IDbContextFactory<NovaDbContext>`, `ICurrentUserProvider`, `ILogger<PlayerManagementService>`
  - **`CreateAsync`**:
    1. `InputValidator.Validate<CreatePlayerInput>` → return `ServiceProblem.Validation` on errors
    2. Authorize club admin; return `ServiceProblem.Forbidden` if not
    3. `await using var db = ...CreateDbContextAsync(...)`, `await using var tx = ...BeginTransactionAsync(...)`
    4. `await db.AcquireClubRosterLockAsync(clubId, cancellationToken)` — serializes with concurrent campaign creation
    5. Create `PlayerEntity` with `LifecycleStatus = Active`, all input fields, no manual `ClubId` (interceptor stamps it)
    6. `db.Players.Add(player)`; `await db.SaveChangesAsync(...)` to get the generated `PlayerId`
    7. Query all Active campaigns for the club: `db.Campaigns.Where(c => c.Status == CampaignStatus.Active).Select(c => c.CampaignId).ToListAsync(...)`
    8. For each campaign ID, add `PlayerCampaignAssignmentEntity { PlayerId = player.PlayerId, CampaignId = ..., ClubId = clubId, PlacementOutcome = Undecided }` — DO NOT set ClubId manually (interceptor)
    9. `await db.SaveChangesAsync(...)`, `await tx.CommitAsync(...)`
    10. Log success; return `PlayerDto` mapped from entity
    11. Catch `DbUpdateConcurrencyException` → return `ServiceProblem.Conflict`
  - **`UpdateAsync`**:
    1. `InputValidator.Validate<UpdatePlayerInput>` → validation error
    2. Authorize club admin
    3. `await using var db = ...`, `await using var tx = ...`, `await db.AcquirePlayerMutationLockAsync(playerId, ...)`
    4. Load player; cross-tenant check (`player.ClubId != clubId`) → `ServiceProblem.NotFound`
    5. Archived check (`player.LifecycleStatus != Active`) → `ServiceProblem.Conflict("Archived players cannot be edited...")`
    6. If `input.GraduationYear != player.GraduationYear`: load Assigned placements in Active campaigns projecting `AssignedPlacementFacts`; run `PlayerGraduationYearPolicy.Evaluate`; on `GraduationYearEditBlocked` → return `ServiceProblem.Conflict` with structured errors in `Errors` dict (`"blockers"` key with the serialized JSON or a flat representation)
    7. Apply all changed fields; `await db.SaveChangesAsync(...)`; `await tx.CommitAsync(...)`
    8. Return updated `PlayerDto`
    9. Catch `DbUpdateConcurrencyException` → conflict
- [ ] Register `PlayerManagementService` in `Nova/Program.cs`: `builder.Services.AddScoped<IPlayerManagementService, PlayerManagementService>()`

### Verification Plan

- `dotnet build Nova.slnx` — zero errors

### Phase Summary

_(write when phase completes)_

---

## Phase 3: HTTP Endpoints

Status: Not started

- [ ] Create `Nova/Features/Players/PlayerManagementEndpointRouteBuilderExtensions.cs`:
  - `MapPlayerManagementEndpoints()` extension method on `IEndpointRouteBuilder`
  - Group: `MapGroup(PlayerEndpoints.GroupPrefix).RequireAuthorization(Policies.RequireClubAdmin)`
  - `MapPost(PlayerEndpoints.CreateRelative, CreatePlayerHandler)` → 201 Created, `ProducesValidationProblem()`, 403, 409, 500, `DisableAntiforgery()`, `WithName("CreatePlayer")`
  - `MapPut(PlayerEndpoints.UpdateRelative, UpdatePlayerHandler)` → 200 OK, `ProducesValidationProblem()`, 403, 404, 409, 500, `DisableAntiforgery()`, `WithName("UpdatePlayer")`
  - Static handlers `CreatePlayerHandler` and `UpdatePlayerHandler` that call service and use `ToHttpResult`
  - `CreatePlayerHandler` returns `TypedResults.Created((string?)null, playerDto)` on success
- [ ] Register in `Nova/Program.cs`: `app.MapPlayerManagementEndpoints()`

### Verification Plan

- `dotnet build Nova.slnx` — zero errors

### Phase Summary

_(write when phase completes)_

---

## Phase 4: WASM Client

Status: Not started

- [ ] Create `Nova.Client/Services/HttpPlayerManagementService.cs` implementing `IPlayerManagementService`:
  - `CreateAsync` → `PostAsJsonAsync(PlayerEndpoints.Create, input, ...)` → deserialize `PlayerDto` on success; `ToServiceProblemAsync` on failure
  - `UpdateAsync` → `PutAsJsonAsync(PlayerEndpoints.UpdateUrl(input.PlayerId), input, ...)` → deserialize `PlayerDto` on success; `ToServiceProblemAsync` on failure
- [ ] Register in `Nova.Client/Program.cs`: `builder.Services.AddScoped<IPlayerManagementService, HttpPlayerManagementService>()`

### Verification Plan

- `dotnet build Nova.slnx` — zero errors

### Phase Summary

_(write when phase completes)_

---

## Phase 5: Tests

Status: Not started

Suggested executor: sub-agent (well-specified, mechanical test scaffolding)

- [ ] Create `Nova.Unit.Tests/Features/Players/CreatePlayerInputValidationTests.cs` (`[Theory]` matrix covering required, NotWhitespace, MaxLength, Range violations for all annotated fields)
- [ ] Create `Nova.Unit.Tests/Features/Players/UpdatePlayerInputValidationTests.cs` (same pattern for UpdatePlayerInput including PlayerId range)
- [ ] Create `Nova.Unit.Tests/Features/Players/PlayerGraduationYearPolicyTests.cs` (`[Theory]` matrix: proposed grad year < team grad year → blocked with correct blocker items; proposed year >= all teams → may change; partial block returns only affected placements)
- [ ] Create `Nova.Unit.Tests/Features/Players/PlayerManagementServiceTests.cs` (SQLite harness):
  - Create: succeeds for admin, enrolls exactly the Active campaigns (not Closed), forbidden for non-admin, validates input before accessing DB
  - Create: player + all enrollments visible under correct club, invisible to other club
  - Update: succeeds for admin, updates profile fields, forbidden for non-admin, archived player → conflict, cross-tenant → not found
  - Update: graduation-year change that violates eligibility → structured blocker returned, no write
  - Update: graduation-year change that remains eligible → succeeds
- [ ] Create `Nova.Integration.Tests/Http/PlayerManagementHttpTests.cs` (AppHost HTTP):
  - POST /api/players: admin creates player → 201, body is PlayerDto
  - POST /api/players: non-admin → 403
  - POST /api/players: invalid input → 422 validation problem
  - PUT /api/players/{id}: admin updates → 200
  - PUT /api/players/{id}: blocked graduation year → 409 with structured errors
  - PUT /api/players/{id}: other club's player → 404
- [ ] Create `Nova.Integration.Tests/Data/PlayerEnrollmentPostgresTests.cs` (real Postgres, concurrent transaction races):
  - Concurrent player creation for same club — both acquire roster lock sequentially, each gets exactly the campaigns active at that moment
  - Concurrent player-create + campaign-create for same club — roster lock ensures the player is enrolled if campaign creation commits first, or campaign-creation enrolls if player was already created (or tests that no gap exists)

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*CreatePlayerInput*"` — at least 1 test, all pass
- `dotnet test --project Nova.Unit.Tests --filter-class "*UpdatePlayerInput*"` — at least 1 test, all pass
- `dotnet test --project Nova.Unit.Tests --filter-class "*PlayerGraduationYearPolicy*"` — at least 1 test, all pass
- `dotnet test --project Nova.Unit.Tests --filter-class "*PlayerManagementService*"` — at least 1 test, all pass
- `dotnet test --project Nova.Integration.Tests --filter-class "*PlayerManagementHttp*"` — at least 1 test, all pass
- `dotnet test --project Nova.Integration.Tests --filter-class "*PlayerEnrollmentPostgres*"` — at least 1 test, all pass

### Phase Summary

_(write when phase completes)_

---

## Final Recap

_(write when all phases complete)_

## Deployment Plan

_(write when all phases complete)_
