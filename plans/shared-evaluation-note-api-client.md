# Shared Evaluation Note API and Client

Expose the completed `EvaluationNoteService` add/edit/delete foundation (issue #15) through
authorized HTTP endpoints and a typed WebAssembly client, so a participant drawer (issue #70) can
add, edit, and delete evaluation notes and refresh participant detail (issue #68). Reuses
`AddEvaluationNoteInput` / `EditEvaluationNoteInput` unchanged and the existing
`EvaluationNoteService` (no second service, no entity/migration work). Adds no notes-list endpoint —
notes stay readable only through the existing participant-detail payload.

Scope (confirmed with user):

- **Routes** (flat "evaluation-notes" group under `/api/campaigns`, `RequireClubMember` at group level):
  - `POST /api/campaigns/evaluation-notes` — body `AddEvaluationNoteInput` → `201 Created` + body
    `{ "noteId": N }` (no Location; no canonical GET for a note — the drawer refreshes participant detail).
  - `PUT /api/campaigns/evaluation-notes/{noteId:long}` — route param is authoritative; body
    `EditEvaluationNoteInput` supplies content (handler passes `input with { NoteId = noteId }`) →
    `204 NoContent`.
  - `DELETE /api/campaigns/evaluation-notes/{noteId:long}` — route param matching `DeleteAsync(long)` →
    `204 NoContent`.
- **Shared contracts** (Nova.Shared): add `EvaluationNoteMutationSuccess(long NoteId)` (mirrors
  `CampaignTagApplicationMutationSuccess`); extend `CampaignEndpoints` with add/edit/delete constants +
  URL builder; add `ICampaignEvaluationNoteService` boundary interface
  (`Task<ServiceResult<EvaluationNoteMutationSuccess>> AddAsync(AddEvaluationNoteInput, CancellationToken)`,
  `Task<ServiceResult<Success>> EditAsync(EditEvaluationNoteInput, CancellationToken)`,
  `Task<ServiceResult<Success>> DeleteAsync(long noteId, CancellationToken)`); add `DateTimeOffset? ModifiedAt`
  to `CampaignParticipantNoteDto` so edits expose the modified timestamp (issue #68 detail refresh).
- **Server**: change `EvaluationNoteService.AddAsync`'s first OneOf variant from `Success` to
  `EvaluationNoteMutationSuccess` (returns `new EvaluationNoteMutationSuccess(note.NoteId)`; line 95 —
  the user-confirmed small return-shape change needed to surface the 201 NoteId); implement
  `ICampaignEvaluationNoteService` on the existing service via **explicit interface implementations**
  (public native-OneOf methods stay; boundary methods call them and map with `.Match<ServiceResult<...>>`);
  new `EvaluationNoteEndpointRouteBuilderExtensions`; DI registration mirroring the
  `ICampaignTagApplicationService` pattern; `app.MapCampaignEvaluationNoteEndpoints()` call.
- **WASM client**: `HttpCampaignEvaluationNoteService` (PostAsJsonAsync for add reading the 201 body;
  PutAsJsonAsync for edit; DeleteAsync for delete, 204 → `Success`); DI registration in `Nova.Client/Program.cs`.
- **Tests**: WASM client unit tests (`FakeHttpMessageHandler`), update the 5 `CampaignParticipantNoteDto`
  test constructions for the new `ModifiedAt` argument, and HTTP boundary integration tests.
- **Out of scope**: entities, migrations, replacement service, participant-detail queries, drawer UI,
  append-only history, rich text, concurrency token, notes-list endpoint.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to
`Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue
with zero context); run the phase's **Verification Plan** and record the result before moving on. When
all phases are done, fill in **Final Recap** and **Deployment Plan**.

## Phase 1: Shared contracts, ModifiedAt DTO, server endpoint, and boundary mapping

Status: Complete

Suggested executor: orchestrator

- [x] Add `Nova.Shared\Features\Campaigns\EvaluationNoteContracts.cs` in namespace
      `Nova.Shared.Features.Campaigns` (mirror `CampaignTagApplicationContracts.cs`):
      `public readonly record struct EvaluationNoteMutationSuccess(long NoteId);` with doc comments.
- [x] Extend `Nova.Shared\Features\Campaigns\CampaignEndpoints.cs` with (follow the existing
      doc-comment style used by the tag-application constants):
  - `AddEvaluationNote` = `$"{GroupPrefix}/evaluation-notes"`,
    `AddEvaluationNoteRelative` = `"evaluation-notes"`,
    `AddEvaluationNoteRouteName` = `"AddEvaluationNote"`.
  - `EditEvaluationNote` = `$"{GroupPrefix}/evaluation-notes/{{noteId:long}}"`,
    `EditEvaluationNoteRelative` = `"evaluation-notes/{noteId:long}"`,
    `EditEvaluationNoteRouteName` = `"EditEvaluationNote"`.
  - `DeleteEvaluationNote` = `$"{GroupPrefix}/evaluation-notes/{{noteId:long}}"`,
    `DeleteEvaluationNoteRelative` = `"evaluation-notes/{noteId:long}"`,
    `DeleteEvaluationNoteRouteName` = `"DeleteEvaluationNote"`.
  - `public static string EditEvaluationNoteUrl(long noteId)` and
    `public static string DeleteEvaluationNoteUrl(long noteId)` returning
    `$"{GroupPrefix}/evaluation-notes/{noteId}"`.
- [x] Add `Nova.Shared\Features\Campaigns\ICampaignEvaluationNoteService.cs`:
  - `using Nova.Shared.Results;` and `using OneOf.Types;`.
  - `Task<ServiceResult<EvaluationNoteMutationSuccess>> AddAsync(AddEvaluationNoteInput input, CancellationToken cancellationToken = default)`.
  - `Task<ServiceResult<Success>> EditAsync(EditEvaluationNoteInput input, CancellationToken cancellationToken = default)`.
  - `Task<ServiceResult<Success>> DeleteAsync(long noteId, CancellationToken cancellationToken = default)`.
- [x] Add `DateTimeOffset? ModifiedAt` to `CampaignParticipantNoteDto` in
      `Nova.Shared\Features\Campaigns\CampaignParticipantContracts.cs` (record currently lines 91–97):
      insert the new parameter after `CreatedAt` (so it reads `..., DateTimeOffset CreatedAt,
      DateTimeOffset? ModifiedAt, bool CanEdit, bool CanDelete`) plus a `<param>` doc comment
      ("When the note was last edited, if it has been edited.").
- [x] In `Nova\Features\Campaigns\CampaignParticipantQueryService.cs`:
  - Add `DateTimeOffset? ModifiedAt` to the `ParticipantNoteProjection` record (line 508) after `CreatedAt`.
  - Add `note.ModifiedAt` as the 5th constructor argument in **both** projection Select sites
    (lines 254–258 Npgsql branch and 261–265 non-Npgsql branch).
  - Add `note.ModifiedAt` after `note.CreatedAt` in the `CampaignParticipantNoteDto` construction
    (lines 332–338).
- [x] In `Nova\Features\Campaigns\EvaluationNoteService.cs`:
  - Change `AddAsync`'s return type first variant from `Success` to `EvaluationNoteMutationSuccess`
    (line 36): `OneOf<EvaluationNoteMutationSuccess, Error<IReadOnlyDictionary<string, string[]>>, NotFound, LifecycleForbidden, LifecycleConflict>`.
  - Change line 95 `return new Success();` to `return new EvaluationNoteMutationSuccess(note.NoteId);`.
  - Add `using Nova.Shared.Results;` (OneOf.Types already present; keep it — Edit/Delete still return `Success`).
  - Change class declaration to `public sealed partial class EvaluationNoteService(...) : ICampaignEvaluationNoteService`.
  - Add three **explicit** interface implementations (public methods share the same names, so explicit
    implementation is required):
    ```csharp
    async Task<ServiceResult<EvaluationNoteMutationSuccess>> ICampaignEvaluationNoteService.AddAsync(
        AddEvaluationNoteInput input, CancellationToken cancellationToken)
    {
        var outcome = await AddAsync(input, cancellationToken);
        return outcome.Match<ServiceResult<EvaluationNoteMutationSuccess>>(
            success => success,
            validation => ServiceProblem.Validation(validation.Value),
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail));
    }

    async Task<ServiceResult<Success>> ICampaignEvaluationNoteService.EditAsync(
        EditEvaluationNoteInput input, CancellationToken cancellationToken)
    {
        var outcome = await EditAsync(input, cancellationToken);
        return outcome.Match<ServiceResult<Success>>(
            success => success,
            validation => ServiceProblem.Validation(validation.Value),
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail));
    }

    async Task<ServiceResult<Success>> ICampaignEvaluationNoteService.DeleteAsync(
        long noteId, CancellationToken cancellationToken)
    {
        var outcome = await DeleteAsync(noteId, cancellationToken);
        return outcome.Match<ServiceResult<Success>>(
            success => success,
            validation => ServiceProblem.Validation(validation.Value),
            _ => ServiceProblem.NotFound(),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail));
    }
    ```
    `LifecycleForbidden`/`LifecycleConflict` are record structs with a `Detail` property
    (`Nova\Features\Shared\LifecycleMutationResults.cs`), so `forbidden.Detail` / `conflict.Detail`
    compile directly.
- [x] Add `Nova\Features\Campaigns\EvaluationNoteEndpointRouteBuilderExtensions.cs` (model on
      `CampaignTagApplicationEndpointRouteBuilderExtensions.cs`):
  - `internal static class` with `extension(IEndpointRouteBuilder endpoints)` exposing
    `public IEndpointRouteBuilder MapCampaignEvaluationNoteEndpoints()`.
  - Group: `.MapGroup(CampaignEndpoints.GroupPrefix).RequireAuthorization(Policies.RequireClubMember)`.
  - `group.MapPost(CampaignEndpoints.AddEvaluationNoteRelative, AddEvaluationNoteHandler)`
    with `.Produces<EvaluationNoteMutationSuccess>(StatusCodes.Status201Created)`,
    `.ProducesValidationProblem()`, `.ProducesProblem(401)`, `.ProducesProblem(403)`,
    `.ProducesProblem(404)`, `.ProducesProblem(409)`, `.ProducesProblem(500)`,
    `.DisableAntiforgery()`, `.WithName(CampaignEndpoints.AddEvaluationNoteRouteName)`.
  - `group.MapPut(CampaignEndpoints.EditEvaluationNoteRelative, EditEvaluationNoteHandler)`
    with `.Produces(StatusCodes.Status204NoContent)`, `.ProducesValidationProblem()`,
    `.ProducesProblem(401/403/404/409/500)`, `.DisableAntiforgery()`,
    `.WithName(CampaignEndpoints.EditEvaluationNoteRouteName)`.
  - `group.MapDelete(CampaignEndpoints.DeleteEvaluationNoteRelative, DeleteEvaluationNoteHandler)`
    with `.Produces(StatusCodes.Status204NoContent)`, `.ProducesProblem(401/403/404/409/500)`,
    `.DisableAntiforgery()`, `.WithName(CampaignEndpoints.DeleteEvaluationNoteRouteName)`.
  - Handlers:
    ```csharp
    private static async Task<IResult> AddEvaluationNoteHandler(
        AddEvaluationNoteInput input,
        ICampaignEvaluationNoteService service,
        CancellationToken cancellationToken)
    {
        var result = await service.AddAsync(input, cancellationToken);
        return result.ToHttpResult(success => TypedResults.Created((string?)null, success));
    }

    private static async Task<IResult> EditEvaluationNoteHandler(
        long noteId,
        EditEvaluationNoteInput input,
        ICampaignEvaluationNoteService service,
        CancellationToken cancellationToken)
    {
        var result = await service.EditAsync(input with { NoteId = noteId }, cancellationToken);
        return result.ToHttpResult(_ => TypedResults.NoContent());
    }

    private static async Task<IResult> DeleteEvaluationNoteHandler(
        long noteId,
        ICampaignEvaluationNoteService service,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(noteId, cancellationToken);
        return result.ToHttpResult(_ => TypedResults.NoContent());
    }
    ```
- [x] Wire up `Nova\Program.cs`:
  - After line 107 (`builder.Services.AddScoped<EvaluationNoteService>();`) add
    `builder.Services.AddScoped<ICampaignEvaluationNoteService>(services => services.GetRequiredService<EvaluationNoteService>());`
    (mirror the tag-application lines 109–110 pattern).
  - After line 253 (`app.MapCampaignTagApplicationEndpoints();`) add
    `app.MapCampaignEvaluationNoteEndpoints();`.
- [x] Update `Nova.Unit.Tests\Features\Campaigns\EvaluationNoteServiceTests.cs`:
  - The `AddAsync` success assertions (`result.IsT0.ShouldBeTrue(); // Success` at line 71) still compile
    because T0 stays the first variant; strengthen them with `result.AsT0.NoteId.ShouldBeGreaterThan(0)`.
  - No other assertions reference the success type by name (grep confirmed only `IsT*` checks).

### Verification Plan

- `dotnet build Nova.slnx` — builds clean (no warnings treated as errors). ✅ Verified: build succeeded (0 errors; only pre-existing NU1903 vulnerability warnings for Microsoft.OpenApi 2.0.0 / SQLitePCLRaw.lib.e_sqlite3 2.1.11).
- `dotnet format Nova.slnx --verify-no-changes --verbosity diagnostic` — no formatting diffs. ✅ Verified: exit 0.
- `dotnet test --project Nova.Unit.Tests --filter-class "*EvaluationNoteServiceTests*"` — foundation
  tests still pass after the `AddAsync` return-shape change. ✅ Verified: 16/16 passed.
- `dotnet test --project Nova.Unit.Tests --filter-class "*HttpCampaignParticipantQueryServiceTests*"` —
  server-side DTO tests unaffected in this phase (client test updates happen in Phase 2); build coverage
  is the main signal here. ✅ Verified: 8/8 passed (also updated here to keep the build green).

### Phase Summary

Phase 1 (server foundation) is **complete and verified**. The shared boundary contracts now exist in
`Nova.Shared\Features\Campaigns\`: `EvaluationNoteContracts.cs` (`EvaluationNoteMutationSuccess(long NoteId)`),
`ICampaignEvaluationNoteService.cs` (AddAsync→`ServiceResult<EvaluationNoteMutationSuccess>`, EditAsync and
DeleteAsync→`ServiceResult<Success>`), and the `CampaignEndpoints.cs` add/edit/delete route constants +
`EditEvaluationNoteUrl(long)`/`DeleteEvaluationNoteUrl(long)` builders. `CampaignParticipantNoteDto` now has
`DateTimeOffset? ModifiedAt` (after `CreatedAt`), and `CampaignParticipantQueryService.cs` projects it through
`ParticipantNoteProjection` in both Npgsql/non-Npgsql branches.

`EvaluationNoteService` implements `ICampaignEvaluationNoteService` via three explicit interface
implementations that call the public native-OneOf methods and map via `.Match<ServiceResult<...>>` to
`ServiceProblem.Validation/NotFound/Forbidden/Conflict` (the same pattern as `CampaignTagApplicationService`).
`AddAsync`'s first OneOf variant changed `Success`→`EvaluationNoteMutationSuccess`, returning
`new EvaluationNoteMutationSuccess(note.NoteId)`; T0 stays first so existing `IsT0` assertions still compile.

`EvaluationNoteEndpointRouteBuilderExtensions.cs` maps three endpoints under
`/api/campaigns/evaluation-notes` with `RequireClubMember` at group level: POST (201 + mutation success body,
no Location header), PUT (204), DELETE (204), each with `.ProducesValidationProblem()`, `.ProducesProblem(401/
403/404/409/500)`, `.DisableAntiforgery()`, and `.WithName(...)`. `Program.cs` registers
`ICampaignEvaluationNoteService` → the existing `EvaluationNoteService` scoped instance and calls
`app.MapCampaignEvaluationNoteEndpoints()`.

Tests updated: `EvaluationNoteServiceTests.Add_Succeeds_ForClubMember` now asserts
`result.AsT0.NoteId.ShouldBeGreaterThan(0)`; all 5 `CampaignParticipantNoteDto` constructions in
`HttpCampaignParticipantQueryServiceTests.cs` pass `null` for the new `ModifiedAt`.

Nothing is committed yet; Phase 1 is staged for a commit. Phase 2 (WASM client `HttpCampaignEvaluationNoteService`
+ `HttpCampaignEvaluationNoteServiceTests`) is next.

## Phase 2: WASM client and client unit tests

Status: Complete

Suggested executor: orchestrator

- [x] Add `Nova.Client\Services\Campaigns\HttpCampaignEvaluationNoteService.cs` (model on
      `HttpCampaignTagApplicationService.cs`):
  - `public sealed class HttpCampaignEvaluationNoteService(HttpClient http) : ICampaignEvaluationNoteService`.
  - `AddAsync`: `PostAsJsonAsync(CampaignEndpoints.AddEvaluationNote, input, ct)` → non-success →
    `await response.ToServiceProblemAsync(ct)`; success →
    `ReadRequiredJsonAsync<EvaluationNoteMutationSuccess>("The server returned an invalid evaluation note response.", result => result.NoteId > 0, ct)`.
  - `EditAsync`: `PutAsJsonAsync(CampaignEndpoints.EditEvaluationNoteUrl(input.NoteId), input, ct)` →
    non-success → `ToServiceProblemAsync(ct)`; success → `new Success()`.
  - `DeleteAsync`: `DeleteAsync(CampaignEndpoints.DeleteEvaluationNoteUrl(noteId), ct)` → non-success →
    `ToServiceProblemAsync(ct)`; success → `new Success()`.
- [x] Register in `Nova.Client\Program.cs` after line 52:
      `builder.Services.AddScoped<ICampaignEvaluationNoteService, HttpCampaignEvaluationNoteService>();`.
- [x] Add `Nova.Unit.Tests\Campaigns\HttpCampaignEvaluationNoteServiceTests.cs` using the existing
      `FakeHttpMessageHandler` pattern (see `HttpCampaignTagApplicationServiceTests`):
  - Add: POST to `CampaignEndpoints.AddEvaluationNote`, 201 with `{"noteId": 7}` → success `NoteId == 7`;
    POST 400/403/404/409 with a ProblemDetails body → `ServiceProblem.Validation/Forbidden/NotFound/Conflict`.
  - Edit: PUT to `CampaignEndpoints.EditEvaluationNoteUrl(7)`, 204 → `Success`; PUT 409 → Conflict.
  - Delete: DELETE to `CampaignEndpoints.DeleteEvaluationNoteUrl(7)`, 204 → `Success`; DELETE 404 → NotFound.
  - Use `TestContext.Current.CancellationToken` and assert the exact route constant used per request.
- [x] Update `Nova.Unit.Tests\Campaigns\HttpCampaignParticipantQueryServiceTests.cs` — add the `ModifiedAt`
      argument to all 5 `CampaignParticipantNoteDto` constructions (lines 85, 123, 163–164, 204–205);
      use `null` where the scenario does not exercise ModifiedAt, and a non-null value where the test
      verifies editing.

### Verification Plan

- `dotnet build Nova.slnx` — builds clean.
- `dotnet test --project Nova.Unit.Tests --filter-class "*HttpCampaignEvaluationNoteServiceTests*"` —
  all client tests pass.
- `dotnet test --project Nova.Unit.Tests --filter-class "*HttpCampaignParticipantQueryServiceTests*"` —
  DTO-shape updates compile and pass.
- `dotnet format Nova.slnx --verify-no-changes --verbosity diagnostic` — no formatting diffs.

### Phase Summary

WASM client `HttpCampaignEvaluationNoteService` added at
`Nova.Client\Services\Campaigns\HttpCampaignEvaluationNoteService.cs`, modeled on
`HttpCampaignTagApplicationService`: `AddAsync` POSTs to `CampaignEndpoints.AddEvaluationNote` and
reads the 201 body via `ReadRequiredJsonAsync<EvaluationNoteMutationSuccess>(..., result => result.NoteId > 0, ct)`;
`EditAsync` PUTs to `CampaignEndpoints.EditEvaluationNoteUrl(input.NoteId)`; `DeleteAsync` DELETEs to
`CampaignEndpoints.DeleteEvaluationNoteUrl(noteId)`. Non-success responses flow through
`ToServiceProblemAsync`. Registered in `Nova.Client\Program.cs` with
`AddScoped<ICampaignEvaluationNoteService, HttpCampaignEvaluationNoteService>()`.

11 client unit tests added at `Nova.Unit.Tests\Campaigns\HttpCampaignEvaluationNoteServiceTests.cs`
(FakeHttpMessageHandler pattern). The validation test uses a 400 body with an `errors` dictionary —
matching the real `ProducesValidationProblem` shape — so `ToServiceProblemAsync` maps it to
`ServiceProblemKind.Validation` (a plain 400 body maps to BadRequest; see
`HttpResponseMessageExtensionsTests`). `HttpCampaignParticipantQueryServiceTests` update (5 `null`
`ModifiedAt` args) was completed during Phase 1.

Verification results: `dotnet test --filter-class "*HttpCampaignEvaluationNoteServiceTests"` → 11/11
passed; `--filter-class "*HttpCampaignParticipantQueryServiceTests"` → 8/8 passed;
`dotnet format Nova.slnx --verify-no-changes` → clean. Build: 0 errors (8 pre-existing NU1903
vulnerability warnings, not from this work).

## Phase 3: HTTP boundary integration tests

Status: Complete

Suggested executor: sub-agent with smaller model (well-specified after Phases 1–2 land)

- [x] Add `Nova.Integration.Tests\Http\EvaluationNoteHttpTests.cs` (model on
      `CampaignTagApplicationHttpTests.cs`):
  - Seed an Active campaign participation and a Closed campaign participation in separate clubs.
  - **Add**: 201 + body `{ "noteId": N }` with N > 0; note visible in a subsequent participant-detail
    GET with author display name; duplicate-tenant isolation (cross-club assignment id → 404).
  - **Add validation**: blank content → 400 ValidationProblemDetails.
  - **Add lifecycle**: Closed campaign participation → 409 Conflict.
  - **Edit**: author edits content → 204; participant detail then shows updated content with
    `CreatedById`/`AuthorDisplayName` unchanged and `ModifiedAt` set (non-null, ≥ `CreatedAt`).
  - **Edit authorization**: non-author non-admin → 403; cross-tenant note id → 404; Closed campaign → 409.
  - **Delete**: author deletes → 204; participant detail no longer shows the note; non-author non-admin → 403;
    cross-tenant → 404; Closed campaign → 409.
  - **Anonymous / not-a-member**: 401 for anonymous requests on all three verbs; 403 for a user with no club.
  - **Refresh flow** (issue #68): POST add → GET participant detail → assert the added note appears
    (mirror of the tag-application "mutation reflected in detail" test).
- [x] **Fix `EvaluationNoteService` retry-strategy transaction bug** (found while running Phase 3
      tests): the service opened `BeginTransactionAsync` directly, which throws under
      `NpgsqlRetryingExecutionStrategy` (`InvalidOperationException: The configured execution strategy
      'NpgsqlRetryingExecutionStrategy' does not support user-initiated transactions`). Extract each
      mutation body into a private `AddNoteAsync`/`EditNoteAsync`/`DeleteNoteAsync` method that opens
      the transaction on a caller-provided fresh context, and add a private
      `ExecuteWithFreshContextAsync<TResult>` helper mirroring `TeamManagementService` (strategy from a
      factory context, fresh context per attempt). The `AcquireCampaignMutationLockAsync` + reload +
      closed-409 checks stay inside the transaction.

### Verification Plan

- `dotnet build Nova.slnx` — builds clean. ✅ 0 errors (8 pre-existing NU1903 warnings).
- `dotnet test --project Nova.Integration.Tests --filter-class "*EvaluationNoteHttpTests"` — all
  boundary tests pass. ✅ **15/15 passed** (35s). **Docker caveat**: these are Aspire/Postgres
  integration tests; if the Docker daemon is unhealthy on the runner, they will not start
  (environment issue, not code) — Docker 29.6.2 confirmed available for this run. Note: use the
  leading-wildcard-only filter (no trailing `*`; a trailing `*` → exit 5 "Zero tests ran").
- `dotnet test --project Nova.Unit.Tests` — full unit suite still green. ✅ **1110/1110 passed** (11s).
- `dotnet build Nova.slnx` and `dotnet format Nova.slnx --verify-no-changes --verbosity diagnostic` — clean.
  ✅ 0 errors; ✅ 0 of 483 files formatted.

### Phase Summary

`Nova.Integration.Tests\Http\EvaluationNoteHttpTests.cs` (15 tests) exercises the full HTTP boundary
for the note API, modeled on `CampaignTagApplicationHttpTests.cs`: add (201 + `{ "noteId": N }`,
detail refresh, cross-tenant 404, blank-content 400, closed-campaign 409), edit (204, `ModifiedAt`
set ≥ `CreatedAt` with author unchanged, non-author 403, cross-tenant 404, closed 409), delete (204,
detail no longer shows note, non-author 403, cross-tenant 404, closed 409), plus anonymous 401 /
no-club 403 on all three verbs.

**Retry-strategy fix**: the initial run had 12 of 15 tests failing with HTTP 500. Server logs showed
`NpgsqlRetryingExecutionStrategy` throwing `InvalidOperationException` on `BeginTransactionAsync`.
`EvaluationNoteService` was refactored to run each transaction inside
`CreateExecutionStrategy().ExecuteAsync` with a fresh `NovaDbContext` per attempt (new private
`ExecuteWithFreshContextAsync<TResult>` helper mirroring `TeamManagementService`; mutation bodies
moved to private `AddNoteAsync`/`EditNoteAsync`/`DeleteNoteAsync` taking the fresh context). The
advisory mutation lock + campaign reload + closed-409 guard stay inside the transaction. Validation
and membership checks remain outside the strategy. After the fix all 15 integration tests pass; the
full 1110-test unit suite, solution build, and `dotnet format` are clean. No entity/migration changes
were needed (per plan scope). Phase 3 committed alongside the fix.

## Final Recap

Issue #71 is complete: the existing `EvaluationNoteService` add/edit/delete foundation (issue #15) is
now exposed through authorized HTTP endpoints and a typed WebAssembly client, ready for the issue #70
participant drawer.

- **Phase 1** (commit `537af4e`): shared contracts (`EvaluationNoteMutationSuccess`,
  `ICampaignEvaluationNoteService`), route constants + URL builders in `CampaignEndpoints`,
  `DateTimeOffset? ModifiedAt` on `CampaignParticipantNoteDto`, the
  `EvaluationNoteEndpointRouteBuilderExtensions` (POST 201 / PUT 204 / DELETE 204 under
  `/api/campaigns/evaluation-notes`, `RequireClubMember` group, `DisableAntiforgery()`), explicit
  interface implementations on `EvaluationNoteService`, and DI + `MapCampaignEvaluationNoteEndpoints()`
  in `Nova/Program.cs`.
- **Phase 2** (commit `f581dc1`): `HttpCampaignEvaluationNoteService` WASM client (PostAsJsonAsync add
  reading the 201 body, PutAsJsonAsync edit, DeleteAsync delete), DI registration in
  `Nova.Client/Program.cs`, 11 client unit tests, and `HttpCampaignParticipantQueryServiceTests` DTO
  updates for the new `ModifiedAt`.
- **Phase 3** (this commit): 15 HTTP boundary integration tests in
  `Nova.Integration.Tests/Http/EvaluationNoteHttpTests.cs`, plus a retry-strategy transaction fix in
  `EvaluationNoteService` (transactions now run inside `CreateExecutionStrategy().ExecuteAsync` with
  fresh contexts per attempt, per `service-layer.instructions.md`). The fix was required because the
  direct `BeginTransactionAsync` calls threw under `NpgsqlRetryingExecutionStrategy` on PostgreSQL.

Verification: integration tests 15/15, unit tests 1110/1110, solution build 0 errors, `dotnet format`
clean. Out-of-scope items held per plan: entities/migrations, notes-list endpoint (notes stay readable
only through participant detail), drawer UI, append-only history, rich text, concurrency token.

## Deployment Plan

No schema, migration, or infrastructure changes are required; this is code-only.

1. Merge the Phase 1/2/3 commits for issue #71 into `main`.
2. CI runs the existing suite: `dotnet build Nova.slnx`, `dotnet test --project Nova.Unit.Tests`
   (SQLite harness), and `dotnet test --project Nova.Integration.Tests` (Aspire/Postgres; requires a
   healthy Docker daemon on the runner).
3. Deploy the `Nova` (server) and `Nova.Client` (WASM) apps together — the WASM client calls the new
   `/api/campaigns/evaluation-notes` endpoints at runtime, so the server must ship first (or
   atomically with the client). No configuration changes.
4. Post-deploy smoke check: as an approved club member, POST a note to an Active campaign
   participation and confirm `201` + `{ "noteId": N }`; PUT a content edit and confirm `204` and that
   participant detail shows updated `Content` and non-null `ModifiedAt`; DELETE and confirm `204` and
   that the note disappears from participant detail. Anonymous requests must return `401`.
