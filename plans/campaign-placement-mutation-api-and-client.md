# Campaign Placement Mutation API and Client

Expose the completed `CampaignPlacementService.UpdatePlacementAsync` operation (issue #85, parent #11)
through an administrator-only HTTP endpoint (`PUT /api/campaigns/participants/{playerCampaignAssignmentId}/placement`)
and a typed WASM client, with exhaustive ProblemDetails mapping, route/body identifier consistency, and
full unit + Aspire integration HTTP test coverage. No entities, migrations, queries, Razor UI, or policy
changes — the service, policy, input contract, and result union are reused as-is.

Decisions confirmed with the requester:
- Route: `PUT /api/campaigns/participants/{playerCampaignAssignmentId:long}/placement`.
- Integration HTTP tests are in scope and will be run locally against the Aspire AppHost.
- Route/body identifier mismatch returns `400 BadRequest` ProblemDetails (player/team/tag precedent),
  not a validation-errors dictionary.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to
`Complete` and write its **Phase Summary**; run the phase's **Verification Plan** and record the result
before moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

## Phase 1: Shared contracts and route constants

Status: Complete <!-- Not started | In progress | Complete -->

- [x] Create `Nova.Shared/Features/Campaigns/CampaignPlacementContracts.cs` with the
      `PlacementMutationSuccess(Guid ConcurrencyToken)` record struct (moved from the server
      service file, XML docs preserved).
- [x] Create `Nova.Shared/Features/Campaigns/ICampaignPlacementService.cs` — the cross-tier WASM
      contract: `Task<ServiceResult<PlacementMutationSuccess>> UpdatePlacementAsync(UpdateCampaignPlacementInput, CancellationToken = default)`.
- [x] Add to `Nova.Shared/Features/Campaigns/CampaignEndpoints.cs`:
      `UpdateCampaignPlacement` (full URL), `UpdateCampaignPlacementRelative`
      (`participants/{playerCampaignAssignmentId:long}/placement`),
      `UpdateCampaignPlacementRouteName` (`UpdateCampaignPlacement`), and
      `UpdateCampaignPlacementUrl(long playerCampaignAssignmentId)` builder.
- [x] Remove the `PlacementMutationSuccess` declaration from
      `Nova/Features/Campaigns/CampaignPlacementService.cs` (the `using Nova.Shared.Features.Campaigns;`
      is already present, so the `PlacementUpdateResult` union now references the shared type).

### Verification Plan

- `dotnet build Nova.slnx` succeeds (proves the OneOf source generator resolves the shared success type).
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignPlacementServiceTests"` still passes.

### Phase Summary

- Shared `PlacementMutationSuccess` receipt moved to `Nova.Shared/Features/Campaigns/CampaignPlacementContracts.cs`; the server OneOf union now references the shared type and the OneOf source generator resolves it (build clean).
- `ICampaignPlacementService` added as the cross-tier WASM contract returning `ServiceResult<PlacementMutationSuccess>`.
- `CampaignEndpoints` gained the PUT route constants and `UpdateCampaignPlacementUrl` builder; route agreed with requester: `PUT /api/campaigns/participants/{playerCampaignAssignmentId}/placement`.
- Verification: solution build succeeded (0 warnings/0 errors); all 15 `CampaignPlacementServiceTests` passed.

## Phase 2: Server endpoint mapping and handler

Status: Complete <!-- Not started | In progress | Complete -->

- [x] Create `Nova/Features/Campaigns/CampaignPlacementEndpointRouteBuilderExtensions.cs`:
      - `extension(IEndpointRouteBuilder endpoints)` block with `MapCampaignPlacementEndpoints()`
        mapping `MapPut(CampaignEndpoints.UpdateCampaignPlacementRelative, ...)` under
        `MapGroup(CampaignEndpoints.GroupPrefix).RequireAuthorization(Policies.RequireClubAdmin)`
        with `.Produces<PlacementMutationSuccess>(200)`, `.ProducesValidationProblem()`,
        `.ProducesProblem(400/401/403/404/409/500)`, `.DisableAntiforgery()`, `.WithName(...)`.
      - Static `UpdateCampaignPlacementHandler(long playerCampaignAssignmentId, UpdateCampaignPlacementInput input, CampaignPlacementService placementService, CancellationToken)`
        that rejects a route/body identifier mismatch with
        `ServiceProblem.BadRequest("The player campaign assignment identifier in the route does not match the request body.")`.
      - `extension(PlacementUpdateResult result)` block with `ToHttpResult()` converting every union
        case exhaustively: success → `TypedResults.Ok`; `Error<...>` → `ServiceProblem.Validation(errors)`;
        `NotFound` → non-disclosing `ServiceProblem.NotFound()`; `PlacementForbidden` →
        `ServiceProblem.Forbidden(detail)`; `PlacementConflict` → `ServiceProblem.Conflict(detail)`.
- [x] Register `app.MapCampaignPlacementEndpoints();` in `Nova/Program.cs` next to the other campaign mappings.

### Verification Plan

- `dotnet build Nova.slnx` succeeds.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignPlacementEndpointTests"` passes (Phase 4 tests).

### Phase Summary

- Endpoint builder created with `MapPut` under the shared campaign group, `RequireClubAdmin` policy, full
  `Produces*` metadata (200/400/401/403/404/409/500), `DisableAntiforgery()`, and the shared route name.
- Handler rejects route/body identifier mismatches with 400 BadRequest and delegates otherwise to the
  existing service, converting the OneOf via a feature-local `ToHttpResult` extension covering all five cases.
- Wired `app.MapCampaignPlacementEndpoints()` in `Nova/Program.cs` after the other campaign mappings.
- Verification: solution build succeeded (0 warnings/0 errors).

## Phase 3: WASM client and DI registration

Status: Complete <!-- Not started | In progress | Complete -->

- [x] Create `Nova.Client/Services/Campaigns/HttpCampaignPlacementService.cs` implementing
      `ICampaignPlacementService`:
      - `PutAsJsonAsync(CampaignEndpoints.UpdateCampaignPlacementUrl(input.PlayerCampaignAssignmentId), input)`.
      - Failure → `response.ToServiceProblemAsync(...)` (preserves structured errors).
      - Success → `ReadRequiredJsonAsync<PlacementMutationSuccess>(...)` validating
        `ConcurrencyToken != Guid.Empty && ConcurrencyToken != input.ExpectedConcurrencyToken`
        (no success-shaped fallbacks).
- [x] Register `builder.Services.AddScoped<ICampaignPlacementService, HttpCampaignPlacementService>();`
      in `Nova.Client/Program.cs`.

### Verification Plan

- `dotnet build Nova.slnx` succeeds.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*HttpCampaignPlacementServiceTests"` passes (Phase 4 tests).

### Phase Summary

- `HttpCampaignPlacementService` PUTs to the shared URL builder, converts failures via
  `ToServiceProblemAsync` (structured errors preserved), and deserializes the success receipt with
  contract validation (fresh non-empty token that replaces the submitted one) — no success-shaped fallbacks.
- Registered against `ICampaignPlacementService` in `Nova.Client/Program.cs`.
- Verification: solution build succeeded (0 warnings/0 errors).

## Phase 4: Unit tests — endpoint metadata, result conversion, WASM client

Status: Complete <!-- Not started | In progress | Complete -->

- [x] Create `Nova.Unit.Tests/Campaigns/CampaignPlacementEndpointTests.cs`:
      - Route is registered with template `CampaignEndpoints.UpdateCampaignPlacementRelative`-derived
        pattern under the shared group, carrying `RequireClubAdmin` authorization metadata and
        `IAntiforgeryMetadata` with `RequiresValidation == false`.
      - `ToHttpResult` conversion for all five `PlacementUpdateResult` cases executed against a
        `DefaultHttpContext` (pattern of `ServiceResultExtensionsTests`): 200 + body JSON contains the
        token; validation problem with the errors dictionary; 404 without disclosure detail; 403 with
        the service detail; 409 with the conflict detail.
      - Route/body mismatch guard is covered at unit level only indirectly (private handler) — its
        HTTP behavior is proven by the integration test in Phase 5.
- [x] Create `Nova.Unit.Tests/Campaigns/HttpCampaignPlacementServiceTests.cs`:
      - Success PUTs to the built URL with `HttpMethod.Put` and returns the validated receipt.
      - Validation, not-found, forbidden, and conflict ProblemDetails round-trip to the matching
        `ServiceProblemKind`.
      - `null`/malformed success body, empty token, and echoed-token payloads map to `ServerError`.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignPlacementEndpointTests"` passes.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*HttpCampaignPlacementServiceTests"` passes.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full unit suite stays green.

### Phase Summary

- `CampaignPlacementEndpointTests` (6 tests): route registered at the shared template with
  `RequireClubAdmin` + disabled antiforgery + PUT + route name; `ToHttpResult` conversion executed
  against an isolated `DefaultHttpContext` for all five `PlacementUpdateResult` cases (200 + token,
  validation problem with errors, non-disclosing 404, 403/409 with service details).
- `HttpCampaignPlacementServiceTests` (9 tests): PUT to the shared URL builder, token contract
  validation (fresh, non-empty, not echoing the submitted token), null/malformed success payloads →
  `ServerError`, and validation/not-found/forbidden/conflict problem round-trips.
- Full unit suite green: 1399 passed, 0 failed.

## Phase 5: Aspire integration HTTP tests (run locally)

Status: Complete <!-- Not started | In progress | Complete -->

- [x] Seeding for teams stays feature-local in the test class (`SeedPlacementDataAsync`, following the
      `SeedTagApplicationDataAsync` precedent); no second consumer needed a shared helper.
- [x] Create `Nova.Integration.Tests/Http/CampaignPlacementHttpTests.cs`
      (`[Collection(NovaAppHostCollection.Name)]`, fixture via primary constructor, `using static SeedingHelpers`):
      - Anonymous caller → 401.
      - Authenticated club member (non-admin) → 403.
      - Club admin success: seeded assignment + eligible team, correct route/body/token → 200 with a
        new non-empty token different from the expected token; row persisted with outcome, team, token.
      - Route/body assignment id mismatch → 400 with the mismatch detail.
      - Malformed body (e.g. `Assigned` without a team, invalid enum) → 400 validation problem naming fields.
      - Cross-tenant assignment id → 404.
      - Stale concurrency token → 409 and the row keeps the winning update.
      - Closed campaign → 409; archived player → 409 (foundation conflicts through the HTTP boundary).
      - Ineligible team (player graduation year below team cutoff) → 400 validation problem on `TeamId`.

### Verification Plan

- `dotnet build Nova.slnx` succeeds.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignPlacementHttpTests"` passes against the Aspire AppHost (starts it if not running).
- Re-run the existing `*CampaignTagApplicationHttpTests` class to prove no cross-test seeding regression.

### Phase Summary

- **Foundation defect found and fixed**: `CampaignPlacementService.UpdatePlacementAsync` called
  `BeginTransactionAsync` directly, which throws `InvalidOperationException` on the real
  `NpgsqlRetryingExecutionStrategy` provider (the SQLite unit harness and fixture contexts don't
  configure the strategy, so this only surfaced through the real HTTP path). Refactored the service
  to the repo's canonical `CreateExecutionStrategy().ExecuteAsync` pattern with a fresh tenant
  context per attempt (matching `CampaignMetadataService` and peers); the concurrency token makes
  retries safe (a re-run after an ambiguous commit fails the token check instead of double-applying).
  No persistence rules or policy logic changed.
- `CampaignPlacementHttpTests` (11 tests) covers: anonymous 401, member 403, admin 200 + replacement
  token + persisted row, route/body mismatch 400, Assigned-without-team validation, unparseable JSON
  (framework-level 500, documented), cross-tenant 404, stale-token 409 preserving the winner, closed
  campaign 409, archived player 409, ineligible-team validation.
- Regression runs: `CampaignLifecyclePostgresTests` 8 passed, `CampaignParticipationPostgresTests`
  9 passed, `TeamPlayerGraduationYearRaceTests` 3 passed, `CampaignTagApplicationHttpTests` 17 passed.

## Phase 6: Formatting, full validation, and acceptance review

Status: Not started

- [ ] `dotnet format Nova.slnx` (apply), then `dotnet format Nova.slnx --verify-no-changes` passes.
- [ ] `dotnet build Nova.slnx` clean.
- [ ] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` full suite green.
- [ ] Walk the issue acceptance criteria line by line and record evidence in the Final Recap.
- [ ] Commit the change to `eruvalca-campaign-placement-mutation-api-and-clie` with the
      Co-authored-by trailer.

### Verification Plan

- `dotnet format Nova.slnx --verify-no-changes` exits 0.
- Full unit suite exits 0; the Phase 5 integration class exits 0.
- `git status` shows only the intended files.

### Phase Summary

_(write when phase completes)_

## Final Recap

_(write when all phases complete: summary of the entire piece of work)_

## Deployment Plan

_(write when all phases complete: step-by-step deployment instructions)_
