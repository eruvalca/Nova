# Map BadHttpRequestException (Malformed JSON) to 400 ProblemDetails in the API Exception Pipeline

Fixes https://github.com/eruvalca/Nova/issues/91: malformed JSON request bodies currently surface as
**500** instead of **400** because the API exception pipeline (parameterless
`UseExceptionHandler()` in the `/api` branch of `Nova/Program.cs`) does not preserve
`BadHttpRequestException.StatusCode`. We add a foundation-level `IExceptionHandler` that maps
`BadHttpRequestException` to a ProblemDetails response carrying the exception's own status code,
update the pinned integration test, add foundation-wide coverage, and sweep `.ProducesProblem(400)`
metadata onto every JSON-body endpoint.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to
`Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue
with zero context); run the phase's **Verification Plan** and record the result before moving on.
When all phases are done, fill in **Final Recap** and **Deployment Plan**.

Confirmed scope decisions (from the issue owner):
- Mechanism: a **custom `IExceptionHandler`** registered via `AddExceptionHandler<T>()` (NOT
  `ExceptionHandlerOptions.StatusCodeSelector`, NOT an inline `UseExceptionHandler` lambda).
- Status code: **preserve the exception's `StatusCode`** (400 for malformed JSON today; also correct
  for future framework cases such as 413 request-body-too-large). Do not hardcode 400.
- OpenAPI metadata: **in scope** — ensure every JSON-body endpoint has a non-conflicting
  ProblemDetails response contract; retain `.ProducesValidationProblem()` instead of adding a
  duplicate `.ProducesProblem(400)` entry.

Design decisions made during planning (revisit if challenged):
- **Detail content**: `ProblemDetails.Detail = exception.Message` (framework-authored, cause-specific,
  e.g. "Failed to read parameter … from the request body as JSON"). Alternative considered and
  rejected: a stable generic message that hides the cause.
- **Logging**: the handler does NOT log. `ExceptionHandlerMiddlewareImpl` suppresses
  unhandled-exception diagnostics for exceptions handled by an `IExceptionHandler` service, so client
  errors stop producing server-side "unhandled exception" noise (the same false-alert class the issue
  calls out for the WASM client). Correlation stays available via the `traceId` extension the
  framework's `DefaultProblemDetailsWriter` always writes.
- **Scope**: API routes only. The handler is registered globally in DI but only the `/api`
  `UseWhen` branch uses the middleware that consults `IExceptionHandler` services
  (`ExceptionHandlerMiddlewareImpl`); non-API routes keep `UseExceptionHandler("/Error")` /
  default behavior.
- **WASM client**: no change. `HttpResponseMessageExtensions.ToServiceProblemAsync` already maps
  `400` → `ServiceProblem.BadRequest` (covered by
  `Nova.Unit.Tests/Results/HttpResponseMessageExtensionsTests.cs`), so client classification fixes
  itself once the server returns 400.
- **Profile photo upload** (`POST /api/photos`, multipart): excluded from the metadata sweep
  (no JSON body binding; `ProfilePhotoValidator` owns its errors). It still benefits from the
  runtime handler if a framework `BadHttpRequestException` occurs.

### How the framework behaves (verified against .NET 10 aspnetcore source)

- `app.UseExceptionHandler()` (no args) in the `/api` branch builds
  `ExceptionHandlerMiddlewareImpl`, which first runs all DI-registered `IExceptionHandler`
  services, then falls back to `IProblemDetailsService` with
  `ProblemDetails.Status = context.Response.StatusCode` (500 by default — nothing applies
  `IStatusCodeException`).
- `DefaultProblemDetailsWriter` fills `Title`/`Type` from the status code
  (`ProblemDetailsDefaults.Apply`), always writes the `traceId` extension
  (`Activity.Current?.Id ?? httpContext.TraceIdentifier`), and emits
  `Content-Type: application/problem+json`. It does NOT fill `Detail` — the handler must set it.

## Phase 1: Foundation handler + pipeline wiring

Status: Complete

Suggested executor: orchestrator (core design; keep the context here)

- [x] Create `Nova/Features/Shared/BadHttpRequestExceptionHandler.cs`:
      `internal sealed class BadHttpRequestExceptionHandler : IExceptionHandler` with
      constructor-injected `IProblemDetailsService`.
  - `TryHandleAsync(HttpContext, Exception, CancellationToken)`:
    - Return `false` when the exception is not `BadHttpRequestException` (let the existing
      ProblemDetails fallback keep current 500 behavior for everything else).
    - Otherwise set `httpContext.Response.StatusCode = badRequest.StatusCode` and write via
      `problemDetailsService.TryWriteAsync(new ProblemDetailsContext
      { HttpContext = httpContext, Exception = exception, ProblemDetails = { Status = badRequest.StatusCode, Detail = badRequest.Message } })`.
    - Return the `TryWriteAsync` result (true = handled; false lets the middleware fall through).
  - Full XML docs (`<summary>`, `<param>`, `<returns>`) per csharp-conventions.
  - No logging per the decision above.
- [x] In `Nova/Program.cs`, add `builder.Services.AddExceptionHandler<BadHttpRequestExceptionHandler>();`
      next to the existing `AddProblemDetails` block (before `var app = builder.Build();`).
- [x] Confirm no changes are needed to the `UseWhen` `/api` branch in `Program.cs`
      (the parameterless `UseExceptionHandler()` already consults registered `IExceptionHandler`s).

### Verification Plan

- `dotnet build Nova.slnx` succeeds.
- `dotnet format Nova.slnx --verify-no-changes --verbosity diagnostic` passes (run
  `dotnet format Nova.slnx` first if it fails).

### Phase Summary

Added the foundation `BadHttpRequestExceptionHandler`, registered it with DI, and left the
parameterless `/api` `UseExceptionHandler()` branch unchanged so it discovers the handler. The
handler preserves the framework status code and message while delegating correlated
ProblemDetails writing to `IProblemDetailsService`. `dotnet build Nova.slnx --no-restore` passed;
the changed-file format check passed (the full-solution format check is noted in Phase 6).

## Phase 2: Repurpose the pinned integration test

Status: Complete

Suggested executor: orchestrator (touches the test that documents the debt; behavior-sensitive)

- [x] In `Nova.Integration.Tests/Http/CampaignPlacementHttpTests.cs`:
  - Rename `CampaignPlacementUpdate_ReturnsServerError_ForUnparseableJsonBody` to
    `CampaignPlacementUpdate_ReturnsBadRequest_ForUnparseableJsonBody`.
  - Replace the debt-documenting XML doc comment (which references issue #91) with a
    behavior-accurate comment: unparseable JSON is rejected during body binding, before the
    handler runs, and the API exception pipeline maps it to 400 ProblemDetails.
  - Change the assertion from `HttpStatusCode.InternalServerError` to `HttpStatusCode.BadRequest`.
  - Extend the test to assert the ProblemDetails contract: content type
    `application/problem+json`, `status` = 400, `title` = "Bad Request", non-empty `detail`,
    and a non-empty `traceId` extension. Reuse the file's existing helper style
    (`ReadErrorsAsync` / the traceId-reading helper around line 259) or add a small
    `ReadProblemDetailsAsync`-style helper if one is not already present.

### Verification Plan

- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignPlacementHttpTests"` passes (requires the Aspire `NovaAppHostFixture`; PostgreSQL 18 comes up automatically).
- Re-run the single test by name first (`--filter "…ReturnsBadRequest_ForUnparseableJsonBody"`) to iterate quickly.

### Phase Summary

Renamed the placement malformed-JSON test to the 400 behavior, replaced the issue-91 debt
comment, and asserted the `application/problem+json` response, 400 status/title/detail, and
trace ID. The `*CampaignPlacementHttpTests` filter passed with 26 tests.

## Phase 3: Foundation-wide integration coverage

Status: Complete

Suggested executor: orchestrator (mirrors existing seeding patterns; needs judgment)

- [x] Add one malformed-JSON test on a different feature's body endpoint to prove the mapping is
      foundation-level, not placement-specific: in `Nova.Integration.Tests/Http/CampaignCreationHttpTests.cs`
      add `CampaignCreate_ReturnsBadRequest_ForUnparseableJsonBody` — register/setup a club admin the
      same way the file's existing create tests do, then POST `{ not json` to
      `CampaignEndpoints.CreateUrl()` and assert 400 + ProblemDetails shape (mirror Phase 2's
      assertions; the class already has a private `AssertProblemDetailsAsync` helper — reuse or
      generalize it).
- [x] Re-run the Phase 1 grep sweep to confirm no other test anywhere still pins 500 for malformed
      JSON bodies (only `CampaignPlacementHttpTests` pinned it as of planning; the `{ not json`
      usage in `Nova.Unit.Tests/Campaigns/HttpCampaignPlacementServiceTests.cs` is client-side
      success-body validation and is unaffected).

### Verification Plan

- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignCreationHttpTests"` passes.
- The new test fails (with 500) if the Phase 1 handler is disabled — spot-check that coupling once during development.

### Phase Summary

Added campaign-creation malformed-JSON coverage to prove the foundation handler is not
placement-specific, including the same ProblemDetails contract assertions. The
`*CampaignCreationHttpTests` filter passed with 7 tests, and the stale malformed-JSON 500 sweep
found no remaining server-side pin (the unit client success-body fixture is intentionally
unrelated).

## Phase 4: Handler unit tests

Status: Complete

Suggested executor: sub-agent w/ smaller model (mechanical, well-specified test scaffolding)

- [x] Create `Nova.Unit.Tests/Http/BadRequestExceptionHandlerTests.cs` (new `Http` folder if
      absent). xUnit v3 + Shouldly + NSubstitute (per testing.instructions) with a real
      `DefaultHttpContext`.
  - `BadRequestExceptionHandler_ReturnsFalse_ForNonBadHttpRequestException`: e.g.
    `InvalidOperationException` → returns `false` and never calls `IProblemDetailsService`.
  - `BadRequestExceptionHandler_WritesProblemDetails_PreservingStatusCode`: `new
    BadHttpRequestException("payload", 400)` → returns `true`, and the captured
    `ProblemDetailsContext` has `ProblemDetails.Status == 400`, `Detail == "payload"`, and
    `HttpContext.Response.StatusCode == 400`. Capture the context with
    `Arg.Do<ProblemDetailsContext>(…)` on a `Substitute.For<IProblemDetailsService>()`.
  - `BadRequestExceptionHandler_WritesProblemDetails_PreservingNon400StatusCode`: same with 413
    (documents that the status is preserved, not hardcoded).
  - Use `TestContext.Current.CancellationToken` for the `TryHandleAsync` token.
- [x] Follow naming convention `Subject_Outcome_Condition`; add XML doc comments.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` passes (full unit suite, cheap).
- `dotnet format Nova.slnx --verify-no-changes --verbosity diagnostic` passes.

### Phase Summary

Added unit coverage for non-handler exceptions plus preserved 400 and 413 status codes and
ProblemDetails detail/status behavior. The full unit suite passed with 1,472 tests, and the
changed-file format check passed.

## Phase 5: OpenAPI metadata verification for JSON-body endpoints

Status: Complete

Suggested executor: sub-agent w/ smaller model (mechanical sweep; give it this exact list and the
instruction to verify existing metadata first)

- [x] For each endpoint below, confirm the mapping's current metadata. Body endpoints retain their
      single `.ProducesValidationProblem()` declaration; do not add `.ProducesProblem(400)`
      alongside it because both describe the same 400 response and create conflicting OpenAPI
      metadata. Existing standalone 400 metadata on endpoints without validation metadata remains
      unchanged.
  - [x] `Nova/Features/Clubs/ClubEndpointRouteBuilderExtensions.cs`: `CreateClub`, `AssignClubAdmin`
  - [x] `Nova/Features/Players/PlayerManagementEndpointRouteBuilderExtensions.cs`: `CreatePlayer`, `UpdatePlayer`
  - [x] `Nova/Features/Teams/TeamManagementEndpointRouteBuilderExtensions.cs`: `CreateTeam`, `UpdateTeam`
  - [x] `Nova/Features/Tags/TagDefinitionEndpointRouteBuilderExtensions.cs`: `CreateTagDefinition`, `UpdateTagDefinition`
  - [x] `Nova/Features/Campaigns/CampaignCreationEndpointRouteBuilderExtensions.cs`: `CreateCampaign`
  - [x] `Nova/Features/Campaigns/CampaignMetadataEndpointRouteBuilderExtensions.cs`: `UpdateCampaignMetadata`, `UpdateSeasonMetadata`
  - [x] `Nova/Features/Campaigns/CampaignPlacementEndpointRouteBuilderExtensions.cs`: `UpdateCampaignPlacement`
  - [x] `Nova/Features/Campaigns/CampaignTagApplicationEndpointRouteBuilderExtensions.cs`: `ApplyCampaignTagApplication`
  - [x] `Nova/Features/Campaigns/EvaluationNoteEndpointRouteBuilderExtensions.cs`: `AddEvaluationNote`, `EditEvaluationNote`
- [x] Excluded by design (verified during planning): multipart `ProfilePhoto` upload; all
      route-only mutation handlers (team/player/tag lifecycle archive-restore, join-request
      create/cancel/approve/reject, `RemoveCampaignTagApplication`, `DeleteEvaluationNote`).
- [x] Sanity-grep afterwards for any `MapPost`/`MapPut` with a body-bound input record that the
      list missed (`grep -n "Input input\|Input body" Nova/Features`).

### Verification Plan

- `dotnet build Nova.slnx` succeeds.
- (Optional, heavier) Start the Aspire AppHost and GET `/openapi` in Development to confirm the
  400 responses appear and no duplicate-response generation errors surface. Nice-to-have; the
  build + integration suites are the required gates.

### Phase Summary

Verified the planned JSON-body endpoints and removed duplicate `ProducesProblem(400)` metadata from
all mappings that already declare `ProducesValidationProblem()`. Standalone 400 metadata on
endpoints without validation metadata, the multipart exclusion, and the route-only exclusions
remain unchanged. The endpoint sweep found no additional body-bound mutation endpoint outside the
approved list.

## Phase 6: Full validation

Status: Complete

Suggested executor: orchestrator (final gate; needs the full picture)

- [x] `dotnet build Nova.slnx` (validated as `dotnet build Nova.slnx --no-restore`; passed).
- [x] `dotnet format Nova.slnx --verify-no-changes --verbosity diagnostic` (the changed-file
      verification passed; the full-solution check still reports pre-existing `CHARSET` findings
      in unrelated files).
- [x] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` (1,472 passed).
- [x] `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignPlacementHttpTests"` (26 passed).
- [x] `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignCreationHttpTests"` (7 passed).
- [x] Full integration suite locally: `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj`
      (265 passed).
      (HTTP-boundary change — per testing.instructions, do not rely on unit tests alone;
      CI only runs build + unit tests).
- [x] No browser-suite change is required (no UI surface; the WASM client cannot produce
      malformed JSON through normal flows, and its 400 classification is already unit-tested).

### Verification Plan

- Build, unit, targeted integration, and full integration validation passed. Targeted format
  validation passed; the full-solution format command continues to report only pre-existing
  `CHARSET` findings in unrelated files. No browser validation was required because this is an
  API exception-pipeline change with no UI surface.

### Phase Summary

Completed the full validation gate: solution build, changed-file format verification, 1,472
unit tests, 26 placement integration tests, 7 creation integration tests, and 265 full
integration tests passed. CI also passed its build and unit-test checks on the implementation
commit. The full-solution format check retains unrelated pre-existing `CHARSET` findings.

## Final Recap

Implemented issue #91 end to end. The API now maps framework-generated
`BadHttpRequestException` instances to correlated ProblemDetails while preserving the exception
status code and detail, malformed JSON is covered by placement and creation integration tests,
the handler has 400/413 unit coverage, and planned JSON-body endpoints avoid conflicting duplicate
400 OpenAPI metadata. No client or browser changes were needed.

## Deployment Plan

1. Merge PR #94 into `main` (linked to issue #91).
2. Deploy the API normally; no database migration, configuration, or client asset change is
   required.
3. Smoke-test one malformed JSON request against a JSON-body `/api` endpoint and confirm a
   `400 application/problem+json` response with `status`, `detail`, and `traceId`.
4. Verify the generated `/openapi` document has no duplicate 400 response entries for the
   updated JSON-body endpoints.
5. Monitor normal API error telemetry after deployment; handled bad-request exceptions should no
   longer appear as unhandled 500 diagnostics.
