# Fix Crest Regressions: Build Warnings, Integration Failures, and the ClubCrestManager Persistent-State Bug

Fix the 12 compiler warnings introduced by the club-crest work (PRs #142/#143) and the four
integration-test failures plus one browser-suite failure they shipped with (all documented as
pre-existing in `plans/club-crest-cropping-and-aspect-ratio.md`). The integration failures are a
mix of product bugs (unreachable handler validation for missing crest files) and wrong test
expectations (blob-name prefix, framework-400 trigger). The browser failure is a real app-level
race in `ClubCrestManager`'s `[PersistentState]` capture.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status
to `Complete` and write its **Phase Summary**; run the phase's **Verification Plan** and record the
result before moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

## Phase 1: Eliminate the 12 Compiler Warnings in Nova.Unit.Tests

Status: Not started

Suggested executor: sub-agent w/ smaller model (mechanical, well-specified edits)

- [ ] `Nova.Unit.Tests/Clubs/ClubCrestServiceTests.cs` — add `result.Problem.Errors.ShouldNotBeNull();`
      before the dereferences at lines 125 and 148 (CS8602), and replace
      `Response.FromValue(true, (Response?)null)` with `Substitute.For<Response<bool>>()` at lines
      285 and 498 (CS8625).
- [ ] `Nova.Unit.Tests/Clubs/ClubServiceTests.cs` — add the same `ShouldNotBeNull()` guard at lines
      221 and 237 (CS8602), and replace `Response.FromValue(true, (Response?)null)` with
      `Substitute.For<Response<bool>>()` at lines 284, 320, and 366 (CS8625).
- [ ] `Nova.Unit.Tests/Clubs/HttpClubCrestServiceTests.cs` — add
      `result.Problem.Errors.ShouldNotBeNull();` before line 60's `ShouldContainKey` (CS8604), and
      null-forgive the content-disposition chain at line 148:
      `multipart.Select(part => part.Headers.ContentDisposition!.Name!.Trim('"'))` (CS8619).
- [ ] `Nova.Unit.Tests/Clubs/HttpClubServicesTests.cs` — same null-forgiving Select fix at line 38
      (CS8619).

Conventions: the `Errors.ShouldNotBeNull()` guard is the repo pattern (see
`CampaignCreationServiceTests.cs`); `!` mirrors existing `handler.LastRequest!` usage.

### Verification Plan

- `dotnet build Nova.slnx -t:Rebuild` → **0 Warning(s), 0 Error(s)**
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` → all pass

### Phase Summary

_(write when phase completes)_

## Phase 2: Fix the 4 Integration Test Failures

Status: Not started

- [ ] **Product fix — missing crest file must produce the structured Validation problem**
      (`Nova/Features/Clubs/ClubEndpointRouteBuilderExtensions.cs`): binding a non-nullable
      `IFormFile crest` makes the framework 400 before the handler runs, so the handler's own
      `crest is null` branch (which returns `ServiceProblem.Validation("crest", ...)`) is
      unreachable over HTTP. Read the file from the form instead:
      - `CreateClubHandler`: drop the `IFormFile crest` parameter; add `HttpContext context`;
        `var crest = context.Request.Form.Files.GetFile("crest");`
      - `ChangeCrestHandler`: drop `IFormFile crest` (it already takes `HttpContext context`);
        read the same way.
      This makes `CreateClub_WithoutCrest_ReturnsValidationProblem` and
      `ChangeCrest_WithoutFile_ReturnsValidationProblem` receive Kind=Validation with a `crest`
      error key, matching the handler metadata (`.ProducesValidationProblem()`), the service unit
      tests, and the api-endpoints structured-problem convention. No unit tests invoke these
      handlers directly, so none need updating.
- [ ] **Test fix — blob prefix** (`Nova.Integration.Tests/Http/ClubCrestHttpTests.cs`,
      `CreateClub_WithCrest_PersistsRowAndServesVariants`, line 74): club-creation blobs are
      deliberately keyed `clubs/{userId}/{batchId}/` (stable across retried inserts that can get a
      different club id — see `ClubService.CreateClubAsync` lines 92-101). Capture the registered
      email in a variable and look up the user id from the admin context
      (`db.Users.SingleAsync(c => c.NormalizedEmail == email.ToUpperInvariant())`, the pattern
      already used by this file's `UpdateUserAsync`), then assert
      `crest.OriginalBlobName.ShouldStartWith($"clubs/{userId}/")`. Keep the suffix assertions.
- [ ] **Test fix — framework-400 trigger** (`Nova.Integration.Tests/Http/TraceCorrelationHttpTests.cs`,
      `MalformedJson_ReturnsTraceIdMatchingSentTraceparent`): `ClubEndpoints.Create` is now a
      multipart-form endpoint, so `application/json` returns 415, not 400. Send a malformed
      multipart body instead: `new StringContent("not a multipart body", Encoding.UTF8,
      "multipart/form-data")` (no boundary) — the form reader throws `BadHttpRequestException`
      (400) handled by `BadHttpRequestExceptionHandler`, preserving the framework-400 trace-id
      coverage. Rename the test to `MalformedForm_ReturnsTraceIdMatchingSentTraceparent` and update
      its doc comment.

### Verification Plan

- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*ClubCrestHttpTests"` → pass
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*TraceCorrelationHttpTests"` → pass
- Full suite: `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` → **372/372 pass** (requires the Aspire AppHost; suite starts its own fixture)

### Phase Summary

_(write when phase completes)_

## Phase 3: Fix the ClubCrestManager Persistent-State Race (Browser CC2)

Status: Not started

- [ ] **Product fix** (`Nova.UI/Features/Clubs/Components/ClubCrestManager.razor.cs`): `HasCrest` is
      supplied as `_summary?.HasCrest ?? false` by `ClubAdmin.razor`, and the island's first render
      can happen before the page summary loads, so `OnInitialized` captures `CrestPresent = false`
      and `HasCrestInitialized` blocks any recapture. Per the blazor-architecture rule ("copy to
      private component state only on first load **or when the incoming parameter value actually
      changes**"), add an `OnParametersSet` re-sync: when `HasCrest != CrestPresent` and the user
      has not locally mutated the crest since initialization, set `CrestPresent = HasCrest`. Track
      local mutation with a private `bool _crestMutatedLocally` field set to `true` on successful
      `SaveCrestAsync` and `ConfirmRemoveAsync` (mutations only occur after interactive attach, so
      a private field suffices — no persistence needed). Keep `OnInitialized`'s existing
      initial-capture guard unchanged.
- [ ] **Unit coverage** (`Nova.Unit.Tests/Features/Clubs/ClubCrestManagerComponentTests.cs`):
      - Parameter update false → true (no mutation) re-syncs the island from placeholder to crest.
      - After a successful save (or remove), a stale `HasCrest` parameter re-render does not revert
        `CrestPresent`.
      Use `cut.SetParametersAndRender(...)` following the file's existing patterns.
- [ ] **Browser verification** — run the crest scenarios; CC2
      (`ClubCrest_AdminReplacesAndRemoves_NavReflectsCrestPresence`) must pass its remove step now.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*ClubCrestManagerComponentTests"` → pass
- Browser (needs AppHost + one-time Chromium install): `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --filter-class "*ClubCrestBrowserTests"` → CC1, CC2, CC3 pass
- Playwright check: load `/Clubs/{id}/admin` for a seeded club with a crest, WASM-reload, and confirm the "Remove crest" button renders and the remove flow completes.

### Phase Summary

_(write when phase completes)_

## Phase 4: Full Validation

Status: Not started

- [ ] `dotnet build Nova.slnx -t:Rebuild` → 0 warnings, 0 errors
- [ ] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` → all pass
- [ ] `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` → 372/372 pass
- [ ] `dotnet format Nova.slnx --verify-no-changes` → clean
- [ ] `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` → full suite green
      (browser suite is local-only; run before merge)

### Phase Summary

_(write when phase completes)_

## Final Recap

_(write when all phases complete: summary of the entire piece of work)_

## Deployment Plan

_(write when all phases complete: step-by-step deployment instructions)_

### Root-Cause Appendix (for reference)

- **12 warnings**: nullability fallout from new crest test code (`ServiceProblem.Errors` is
  nullable; `Response.FromValue` rejects a null literal; form-part name projection is `string?[]`).
- **Missing-crest 400s**: minimal-API form binding for a required `IFormFile` fails with a
  framework-generated 400 (no `errors`) before the handler's explicit null check runs, so
  `ToServiceProblemAsync` maps Kind=BadRequest instead of Validation.
- **Blob prefix**: creation-time names are `clubs/{userId}/{operationId}` by design; the test
  asserted `clubs/{clubId}/`.
- **TraceCorrelation**: club creation changed from JSON to multipart; malformed JSON now 415s.
- **CC2**: `ClubCrestManager` captures `[PersistentState] CrestPresent` from a `HasCrest` parameter
  that is still `false` on the island's first render (page summary not yet loaded), and
  `HasCrestInitialized` prevents recapture. Documented at
  `plans/club-crest-cropping-and-aspect-ratio.md` lines 451-469.
