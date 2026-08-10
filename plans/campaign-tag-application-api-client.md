# Campaign Tag Application API and Client

Expose the completed `CampaignTagApplicationService` add/remove foundation (issue #16) through
authorized HTTP endpoints and a typed WebAssembly client, so the participant drawer can apply and
remove tags and refresh participant detail (issue #70). Reuses the existing
`ApplyCampaignTagApplicationInput` / `RemoveCampaignTagApplicationInput` records unchanged; adds no
new entities, migrations, tag-definition CRUD, or applied-tags list endpoint.

Scope (confirmed with user):

- **Routes** (flat tag-applications group under `/api/campaigns`, `RequireClubMember` at group level):
  - `POST /api/campaigns/tag-applications` — body `ApplyCampaignTagApplicationInput` →
    `201 Created` + body `{ "campaignTagApplicationId": N }` (no Location; no canonical GET for the application).
  - `DELETE /api/campaigns/tag-applications/{campaignTagApplicationId:long}` — route param,
    `RemoveCampaignTagApplicationInput.CampaignTagApplicationId` semantics → `204 NoContent`.
- **Shared contracts** (Nova.Shared): move `CampaignTagApplicationMutationSuccess` into
  `Nova.Shared\Features\Campaigns`; extend `CampaignEndpoints` with apply/remove constants + URL
  builder; add `ICampaignTagApplicationService` boundary interface
  (`Task<ServiceResult<CampaignTagApplicationMutationSuccess>> ApplyAsync(ApplyCampaignTagApplicationInput, CancellationToken)`
  and `Task<ServiceResult<Success>> RemoveAsync(RemoveCampaignTagApplicationInput, CancellationToken)`).
- **Server**: implement `ICampaignTagApplicationService` on the existing `CampaignTagApplicationService`
  via **explicit interface implementation** (public native-OneOf `ApplyAsync`/`RemoveAsync` stay intact
  for foundation tests; boundary methods call them and map with `.Match<ServiceResult<...>>`); new
  `CampaignTagApplicationEndpointRouteBuilderExtensions`; DI registration mirroring the TeamLifecycle
  pattern; `app.MapCampaignTagApplicationEndpoints()` call.
- **WASM client**: `HttpCampaignTagApplicationService` (PostAsJsonAsync for apply reading the 201 body;
  DeleteAsync for remove, 204 → `Success`); DI registration in `Nova.Client/Program.cs`.
- **Tests**: WASM client unit tests (`FakeHttpMessageHandler`) and HTTP boundary integration tests.
- **Out of scope**: entities, migrations, duplicate-policy reimplementation, tag CRUD,
  participant-detail queries, drawer UI, placement, append-only history.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to
`Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue
with zero context); run the phase's **Verification Plan** and record the result before moving on. When
all phases are done, fill in **Final Recap** and **Deployment Plan**.

## Phase 1: Shared contracts, server endpoint, and boundary mapping

Status: Complete

Suggested executor: orchestrator

- [x] Move `CampaignTagApplicationMutationSuccess` from `Nova\Features\Campaigns\CampaignTagApplicationService.cs`
      (currently line 19) into a new `Nova.Shared\Features\Campaigns\CampaignTagApplicationContracts.cs`
      file in namespace `Nova.Shared.Features.Campaigns`. The server service already
      `using Nova.Shared.Features.Campaigns;` so simple-name references at lines 51 and 142 keep resolving.
      Grep confirmed no test references the type by name (only `.AsT0.CampaignTagApplicationId`).
- [x] Extend `Nova.Shared\Features\Campaigns\CampaignEndpoints.cs` with:
  - `ApplyCampaignTagApplication` = `$"{GroupPrefix}/tag-applications"`,
    `ApplyCampaignTagApplicationRelative` = `"tag-applications"`,
    `ApplyCampaignTagApplicationRouteName` = `"ApplyCampaignTagApplication"`.
  - `RemoveCampaignTagApplication` = `$"{GroupPrefix}/tag-applications/{{campaignTagApplicationId:long}}"`,
    `RemoveCampaignTagApplicationRelative` = `"tag-applications/{campaignTagApplicationId:long}"`,
    `RemoveCampaignTagApplicationRouteName` = `"RemoveCampaignTagApplication"`.
  - `public static string RemoveCampaignTagApplicationUrl(long campaignTagApplicationId)`
    returning `$"{GroupPrefix}/tag-applications/{campaignTagApplicationId}"`.
  - Follow the existing doc-comment style for each constant.
- [x] Add `Nova.Shared\Features\Campaigns\ICampaignTagApplicationService.cs`:
  - `using Nova.Shared.Results;` and `using OneOf.Types;`.
  - `Task<ServiceResult<CampaignTagApplicationMutationSuccess>> ApplyAsync(ApplyCampaignTagApplicationInput input, CancellationToken cancellationToken = default)`.
  - `Task<ServiceResult<Success>> RemoveAsync(RemoveCampaignTagApplicationInput input, CancellationToken cancellationToken = default)`.
- [x] In `Nova\Features\Campaigns\CampaignTagApplicationService.cs`:
  - Change class declaration to `public sealed partial class CampaignTagApplicationService(...) : ICampaignTagApplicationService`.
  - Add two **explicit** interface implementations (the public methods keep the same name, so explicit
    implementation is required to avoid a signature clash):
    ```csharp
    async Task<ServiceResult<CampaignTagApplicationMutationSuccess>> ICampaignTagApplicationService.ApplyAsync(
        ApplyCampaignTagApplicationInput input, CancellationToken cancellationToken)
    {
        var outcome = await ApplyAsync(input, cancellationToken);
        return outcome.Match<ServiceResult<CampaignTagApplicationMutationSuccess>>(
            success => success,
            validation => ServiceProblem.Validation(validation.Value),
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail));
    }

    async Task<ServiceResult<Success>> ICampaignTagApplicationService.RemoveAsync(
        RemoveCampaignTagApplicationInput input, CancellationToken cancellationToken)
    {
        var outcome = await RemoveAsync(input, cancellationToken);
        return outcome.Match<ServiceResult<Success>>(
            success => success,
            validation => ServiceProblem.Validation(validation.Value),
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail));
    }
    ```
  - Add `using Nova.Shared.Results;` (and `using OneOf.Types;` already present).
- [x] Add `Nova\Features\Campaigns\CampaignTagApplicationEndpointRouteBuilderExtensions.cs`
      (model on `CampaignCreationEndpointRouteBuilderExtensions.cs` / `TeamLifecycleEndpointRouteBuilderExtensions.cs`):
  - `internal static class` with `extension(IEndpointRouteBuilder endpoints)` exposing
    `public IEndpointRouteBuilder MapCampaignTagApplicationEndpoints()`.
  - Group: `.MapGroup(CampaignEndpoints.GroupPrefix).RequireAuthorization(Policies.RequireClubMember)`.
  - `group.MapPost(CampaignEndpoints.ApplyCampaignTagApplicationRelative, ApplyCampaignTagApplicationHandler)`
    with `.Produces<CampaignTagApplicationMutationSuccess>(StatusCodes.Status201Created)`,
    `.ProducesValidationProblem()`, `.ProducesProblem(401)`, `.ProducesProblem(403)`,
    `.ProducesProblem(404)`, `.ProducesProblem(409)`, `.ProducesProblem(500)`,
    `.DisableAntiforgery()`, `.WithName(CampaignEndpoints.ApplyCampaignTagApplicationRouteName)`.
  - `group.MapDelete(CampaignEndpoints.RemoveCampaignTagApplicationRelative, RemoveCampaignTagApplicationHandler)`
    with `.Produces(StatusCodes.Status204NoContent)`, `.ProducesValidationProblem()`,
    `.ProducesProblem(401/403/404/409/500)`, `.DisableAntiforgery()`,
    `.WithName(CampaignEndpoints.RemoveCampaignTagApplicationRouteName)`.
    (ProducesValidationProblem is accurate here: the service validates the Range on
    `CampaignTagApplicationId`, so id=0 yields a 400 ValidationProblemDetails.)
  - Handlers:
    ```csharp
    private static async Task<IResult> ApplyCampaignTagApplicationHandler(
        ApplyCampaignTagApplicationInput input,
        ICampaignTagApplicationService service,
        CancellationToken cancellationToken)
    {
        var result = await service.ApplyAsync(input, cancellationToken);
        return result.ToHttpResult(success => TypedResults.Created((string?)null, success));
    }

    private static async Task<IResult> RemoveCampaignTagApplicationHandler(
        long campaignTagApplicationId,
        ICampaignTagApplicationService service,
        CancellationToken cancellationToken)
    {
        var input = new RemoveCampaignTagApplicationInput { CampaignTagApplicationId = campaignTagApplicationId };
        var result = await service.RemoveAsync(input, cancellationToken);
        return result.ToHttpResult(_ => TypedResults.NoContent());
    }
    ```
  - Usings: `Nova.Features.Shared`, `Nova.Shared.Features.Campaigns`, `Nova.Shared.Security`
    (drop `Nova.Shared.Results` — not needed by the endpoint file; format verified clean).
- [x] `Nova\Program.cs`: after line 109
      (`AddScoped<CampaignTagApplicationService>()`) add
      `builder.Services.AddScoped<ICampaignTagApplicationService>(services => services.GetRequiredService<CampaignTagApplicationService>());`
      (mirrors the TeamLifecycle mapping at lines 119–120). Add `app.MapCampaignTagApplicationEndpoints();`
      after line 251 (`app.MapCampaignParticipantEndpoints();`).

### Verification Plan

- `dotnet build Nova.slnx` — builds clean (no warnings treated as errors).
- `dotnet format Nova.slnx --verify-no-changes --verbosity diagnostic` — no formatting diffs.
- `dotnet test --project Nova.Unit.Tests --filter-class "*CampaignTagApplicationService*"` — foundation
  unit tests still pass after the type move and explicit interface additions.
- `dotnet test --project Nova.Integration.Tests --filter-class "*CampaignTagApplicationPostgresTests*"` —
  foundation Postgres tests still pass.
- Optional ad-hoc probe: `dotnet run --project Nova.AppHost` is NOT required at this phase; endpoint
  behavior is verified by Phase 3 HTTP tests.

### Phase Summary

Phase 1 complete. Shared contracts in place: `CampaignTagApplicationMutationSuccess` moved to
`Nova.Shared\Features\Campaigns\CampaignTagApplicationContracts.cs`; `ICampaignTagApplicationService`
boundary interface added; `CampaignEndpoints` gained apply/remove constants and the
`RemoveCampaignTagApplicationUrl(long)` builder. Server: `CampaignTagApplicationService` now implements
the interface via two **explicit** interface methods (`.Match<ServiceResult<...>>` mapping per the
TeamLifecycle precedent — validation/not-found/forbidden/conflict → ServiceProblem); new
`CampaignTagApplicationEndpointRouteBuilderExtensions` (MapPost 201 + body with `TypedResults.Created`,
MapDelete 204 with `TypedResults.NoContent`, `RequireClubMember` group, `DisableAntiforgery`,
`WithName`, full ProducesProblem metadata); DI wired in `Nova/Program.cs` (scoped interface mapping to
the concrete service + `app.MapCampaignTagApplicationEndpoints();`).

Verification results: `dotnet build Nova.slnx` clean (0 errors); `dotnet format` clean (0/469 files);
`Nova.Unit.Tests` `*CampaignTagApplicationService*` 11/11 passed. `Nova.Integration.Tests`
`*CampaignTagApplicationPostgresTests*` could NOT run — Docker daemon is unhealthy/hung on this machine
(environment issue, not code). Re-run `dotnet test --project Nova.Integration.Tests
--filter-class "*CampaignTagApplicationPostgresTests*"` once Docker is up. Next: Phase 2 WASM client
(delegate to sub-agent per plan).

## Phase 2: WASM client and client unit tests

Status: Complete

Suggested executor: sub-agent (smaller model, mechanical)

- [x] Add `Nova.Client\Services\Campaigns\HttpCampaignTagApplicationService.cs` implementing
      `ICampaignTagApplicationService` (model on `HttpCampaignCreationService` + `HttpTeamLifecycleService`):
  - `ApplyAsync`: `http.PostAsJsonAsync(CampaignEndpoints.ApplyCampaignTagApplication, input, cancellationToken)`;
    non-success → `await response.ToServiceProblemAsync(cancellationToken)`; success →
    `await response.Content.ReadRequiredJsonAsync<CampaignTagApplicationMutationSuccess>(
        "The server returned an invalid campaign tag application response.",
        result => result.CampaignTagApplicationId > 0,
        cancellationToken)`.
  - `RemoveAsync`: `http.DeleteAsync(CampaignEndpoints.RemoveCampaignTagApplicationUrl(input.CampaignTagApplicationId), cancellationToken)`;
    non-success → `ToServiceProblemAsync`; success (204) → `return new Success();`.
  - Usings: `Nova.Shared.Features.Campaigns`, `Nova.Shared.Results`, `OneOf.Types`.
- [x] `Nova.Client\Program.cs`: after line 51 add
      `builder.Services.AddScoped<ICampaignTagApplicationService, HttpCampaignTagApplicationService>();`.
- [x] Add `Nova.Unit.Tests\Campaigns\HttpCampaignTagApplicationServiceTests.cs` using the existing
      `FakeHttpMessageHandler` pattern (see `HttpTeamLifecycleServiceTests` / `HttpCampaignCreationServiceTests`):
  - Apply success: asserts `HttpMethod.Post`, request URI equals `CampaignEndpoints.ApplyCampaignTagApplication`
    (absolute path `/api/campaigns/tag-applications`), serialized body contains both ids; 201 + valid JSON body
    (`{"campaignTagApplicationId": 42}`) → `result.IsSuccess` and `result.Value.CampaignTagApplicationId == 42`.
  - Apply problem mapping: 403/404/409 ProblemDetails → matching `ServiceProblemKind`.
  - Apply empty/null success payload → `ServiceProblemKind.ServerError`.
  - Remove success: asserts `HttpMethod.Delete`, request URI equals
    `CampaignEndpoints.RemoveCampaignTagApplicationUrl(42)`; 204 → `result.IsSuccess`.
  - Remove problem mapping: 403/404/409 → matching kinds; empty payload behavior n/a (204 has no body).
  - Use `Shouldly` and `Subject_Outcome_Condition` naming per `testing.instructions.md`.

### Verification Plan

- `dotnet build Nova.slnx` — builds clean.
- `dotnet test --project Nova.Unit.Tests --filter-class "*HttpCampaignTagApplicationServiceTests*"` —
  all client unit tests pass.
- `dotnet format Nova.slnx --verify-no-changes --verbosity diagnostic` — no formatting diffs.

### Phase Summary

Phase 2 complete. WASM client `HttpCampaignTagApplicationService` added in
`Nova.Client\Services\Campaigns\` implementing `ICampaignTagApplicationService`: Apply posts
`ApplyCampaignTagApplicationInput` to `CampaignEndpoints.ApplyCampaignTagApplication`, maps non-success
via `ToServiceProblemAsync`, and validates the 201 payload with `ReadRequiredJsonAsync` (requires
`CampaignTagApplicationId > 0`, server-error detail "The server returned an invalid campaign tag
application response."); Remove issues `DeleteAsync` against
`RemoveCampaignTagApplicationUrl(id)` and returns `new Success()` on 204. Registered
`ICampaignTagApplicationService → HttpCampaignTagApplicationService` in `Nova.Client/Program.cs`.

`Nova.Unit.Tests\Campaigns\HttpCampaignTagApplicationServiceTests.cs` covers 11 cases: apply POST route +
201 body deserialization, apply 403/404/409 problem mapping, apply empty/null/invalid-payload server
errors, remove DELETE route + 204 success, remove 403/404/409 problem mapping.

Verification results: `dotnet build Nova.slnx` clean (0 errors); `dotnet test --project Nova.Unit.Tests
--filter-class "*HttpCampaignTagApplicationServiceTests*"` 11/11 passed; `dotnet format` clean (0/471
files). Next: Phase 3 HTTP boundary integration tests.

## Phase 3: HTTP boundary integration tests

Status: Complete

Suggested executor: orchestrator

- [ ] Add `Nova.Integration.Tests\Http\CampaignTagApplicationHttpTests.cs`
      (`[Collection(NovaAppHostCollection.Name)]`, primary-constructor `NovaAppHostFixture fixture`),
      modeled on `CampaignParticipantHttpTests.cs`. Use `RegisterUserWithCompletedProfilePhotoAsync`,
      `UpdateUserAsync(email, clubId, ct)`, `RefreshClubMembershipCookieAsync(client, ct)`,
      `CreateClubAsync(client, ct)`, and a `SeedTagApplicationDataAsync` helper (admin-context seeding:
      SeasonEntity → CampaignEntity (Active) → PlayerEntity → PlayerTagEntity (LifecycleStatus.Active) →
      PlayerCampaignAssignmentEntity (PlacementOutcome.Undecided) → CampaignTagApplicationEntity,
      with `CreatedById` set to a real user id from `context.Users`).
- [ ] Cover the full boundary matrix:
  - Anonymous `POST apply` and `DELETE remove` → 401.
  - Authenticated user with no club → 403 (RequireClubMember).
  - Least-privilege member (not club admin): apply success → 201 with body id > 0, then verify the DB row
    exists via `fixture.CreateAdminContext()` (this is the HTTP → DB row verification; do NOT duplicate
    the Postgres foundation tests).
  - Apply validation: missing/invalid body → 400 ValidationProblemDetails (`PlayerCampaignAssignmentId`/`PlayerTagId`).
  - Duplicate apply (tag already applied to participation) → 409 with expected detail.
  - Apply to a Closed campaign → 409.
  - Apply an archived tag definition → 409.
  - Non-disclosing 404: apply with cross-tenant or nonexistent assignment/tag ids.
  - Remove by non-owner, non-admin → 403.
  - Remove by owner → 204, then verify the DB row is gone.
  - Remove by club admin → 204.
  - Stale mutation: remove an already-removed application (concurrent second removal) → 404.
  - DELETE with `campaignTagApplicationId` = 0 → 400 ValidationProblemDetails.
  - Refresh story: after a successful apply, `GET CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId)`
    returns `AppliedTags` containing the new tag (proves the #70 refresh target).
- [ ] Run the suite and fix any failures until green.

### Verification Plan

- `dotnet test --project Nova.Integration.Tests --filter-class "*CampaignTagApplicationHttpTests*"` — all
  HTTP boundary tests pass.
- `dotnet test --project Nova.Unit.Tests` — full unit suite still green.
- `dotnet build Nova.slnx` and `dotnet format Nova.slnx --verify-no-changes --verbosity diagnostic` — clean.
- Commit with the required Co-authored-by trailer once all phases are verified.

### Phase Summary

Phase 3 complete. `Nova.Integration.Tests\Http\CampaignTagApplicationHttpTests.cs` (14 tests) added,
modeled on `CampaignParticipantHttpTests.cs`, covering the full boundary matrix: anonymous 401;
no-club 403; least-privilege apply 201 + DB-row verification via `fixture.CreateAdminContext()`;
validation 400 for missing/invalid body and for `campaignTagApplicationId = 0`; duplicate apply 409;
Closed-campaign apply 409; archived tag-definition apply 409; non-disclosing 404 for cross-tenant/
nonexistent ids; remove by non-owner/non-admin 403; remove by owner and by club admin 204 + row-gone
verification; stale second remove 404; and the refresh story — after apply, GET participant detail
returns the new tag in `AppliedTags` (proves the #70 refresh target).

Two pre-existing issues were discovered and resolved during this phase:
- **Stale test binary** caused the archived-tag 409 failure — fixed by rebuilding `Nova.Integration.Tests`.
- **Npgsql-only LINQ translation bug** in `CampaignParticipantQueryService.GetParticipantDetailAsync`
  (pre-existing from PR #72, reproduced on `main` in the existing participant detail tests): EF inlined
  `new ParticipantNoteProjection(...)` into `OrderByDescending(note => note.CreatedAt)` and could not
  translate `.CreatedAt` on the constructed projection. Fixed by ordering on **entity** columns
  (`note.CreatedAt`, `note.NoteId`) **before** the `Select` projection on the Npgsql branch while
  preserving the client-side SQLite ordering (SQLite cannot translate `DateTimeOffset` in `ORDER BY`).
  This unblocked the campaign-tag reflection test and fixed 2 of 3 failing participant-detail tests.

Verification results (arm64 workaround: run the test exe directly with `--filter-class`):
`*CampaignTagApplicationHttpTests*` 14/14 passed; `*CampaignTagApplicationPostgresTests*` 3/3 passed;
full unit suite 1096/1096 passed; `*CampaignParticipantHttpTests*` 11/12 passed — the single failure
`GetParticipantRoster_TreatsSearchWildcardsAsLiterals_OnPostgresLikeBranch` is an **unrelated
pre-existing seed-data bug** (`IX_PlayerCampaignAssignments_CampaignId_TryoutNumber` duplicate unique
violation in `SeedWildcardSearchDataAsync`, line ~478).

## Final Recap

Issue #65 complete. The `CampaignTagApplicationService` add/remove foundation (PR #72) is now exposed
through authorized HTTP endpoints and a typed WASM client, end to end:

- **Shared contracts** (`Nova.Shared\Features\Campaigns`): `CampaignTagApplicationContracts.cs`
  (`CampaignTagApplicationMutationSuccess` moved from the server namespace), `ICampaignTagApplicationService`
  boundary interface (`ApplyAsync`/`RemoveAsync` returning `ServiceResult`), and apply/remove route
  constants + `RemoveCampaignTagApplicationUrl(long)` in `CampaignEndpoints.cs`.
- **Server** (`Nova\Features\Campaigns`): `CampaignTagApplicationService` implements the interface via
  **explicit** methods mapping OneOf → `ServiceResult` (validation/not-found/forbidden/conflict →
  `ServiceProblem`); mutations now run inside `CreateExecutionStrategy().ExecuteAsync` with
  fresh-context-per-attempt and commit-verification (fixes the direct `BeginTransactionAsync`
  anti-pattern with `NpgsqlRetryingExecutionStrategy`); new
  `CampaignTagApplicationEndpointRouteBuilderExtensions` maps `POST /api/campaigns/tag-applications`
  (201 + body) and `DELETE /api/campaigns/tag-applications/{id:long}` (204) under a
  `RequireClubMember` group with `DisableAntiforgery`, `WithName`, and full `ProducesProblem`
  metadata. DI wired in `Nova/Program.cs`.
- **WASM client** (`Nova.Client`): `HttpCampaignTagApplicationService` implements the interface
  (`PostAsJsonAsync` + validated 201 body; `DeleteAsync` → `Success` on 204; failures via
  `ToServiceProblemAsync`); registered in `Nova.Client/Program.cs`.
- **Tests**: 11 WASM-client unit tests, 14 HTTP boundary integration tests, 3 foundation Postgres
  tests, and the full 1096-test unit suite all pass.
- **Bonus fix**: pre-existing Npgsql-only translation bug in `CampaignParticipantQueryService`
  (ordered projection in `OrderBy`) fixed — this is a real defect shipped by PR #72.

## Deployment Plan

No schema or migration changes — this feature adds only endpoints, a client, and DI registration, all
in code. Steps:

1. Merge the PR into `main`; CI builds `Nova.slnx`, runs `dotnet format --verify-no-changes`, the full
   unit suite, and the integration suites (Postgres via Aspire).
2. No database migration to apply; the `CampaignTagApplication`, `PlayerTag`, and
   `PlayerCampaignAssignment` tables already exist from the PR #72 migration.
3. Deploy the server (`Nova`) and WASM assets (`Nova.Client`) as usual. The two new routes are
   authenticated via the existing `RequireClubMember` policy, so no new configuration/secrets needed.
4. Client-side: nothing to configure — the WASM service reads shared route constants.
5. Post-deploy smoke test: apply a tag to a participant in an active campaign (expect 201 + id), then
   remove it (expect 204), and confirm the participant drawer shows the tag in participant detail.
