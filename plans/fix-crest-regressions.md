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

Status: Complete

Suggested executor: sub-agent w/ smaller model (mechanical, well-specified edits)

- [x] `Nova.Unit.Tests/Clubs/ClubCrestServiceTests.cs` — add `result.Problem.Errors.ShouldNotBeNull();`
      before the dereferences at lines 125 and 148 (CS8602), and replace
      `Response.FromValue(true, (Response?)null)` with `Substitute.For<Response<bool>>()` at lines
      285 and 498 (CS8625).
- [x] `Nova.Unit.Tests/Clubs/ClubServiceTests.cs` — add the same `ShouldNotBeNull()` guard at lines
      221 and 237 (CS8602), and replace `Response.FromValue(true, (Response?)null)` with
      `Substitute.For<Response<bool>>()` at lines 284, 320, and 366 (CS8625).
- [x] `Nova.Unit.Tests/Clubs/HttpClubCrestServiceTests.cs` — add
      `result.Problem.Errors.ShouldNotBeNull();` before line 60's `ShouldContainKey` (CS8604), and
      null-forgive the content-disposition chain at line 148:
      `multipart.Select(part => part.Headers.ContentDisposition!.Name!.Trim('"'))` (CS8619).
- [x] `Nova.Unit.Tests/Clubs/HttpClubServicesTests.cs` — same null-forgiving Select fix at line 38
      (CS8619).

Conventions: the `Errors.ShouldNotBeNull()` guard is the repo pattern (see
`CampaignCreationServiceTests.cs`); `!` mirrors existing `handler.LastRequest!` usage.

### Verification Plan

- `dotnet build Nova.slnx -t:Rebuild` → **0 Warning(s), 0 Error(s)**
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` → all pass

### Phase Summary

Applied the 4 prescribed fixes across the 4 club-crest/HTTP test files: `Errors.ShouldNotBeNull()`
guards before nullable `ServiceProblem.Errors` dereferences (CS8602 ×4, CS8604 ×1), NSubstitute
`Substitute.For<Response<bool>>()` in place of `Response.FromValue(true, (Response?)null)` (CS8625
×5), and null-forgiving content-disposition name projection (CS8619 ×2). Verified with
`dotnet build Nova.slnx -t:Rebuild` → **0 Warning(s), 0 Error(s)**, and the full unit suite →
**1812/1812 pass** (before the Phase 3 additions). Committed as `428f32e`.

## Phase 2: Fix the 4 Integration Test Failures

Status: Complete

- [x] **Product fix — missing crest file must produce the structured Validation problem**
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
- [x] **Test fix — blob prefix** (`Nova.Integration.Tests/Http/ClubCrestHttpTests.cs`,
      `CreateClub_WithCrest_PersistsRowAndServesVariants`, line 74): club-creation blobs are
      deliberately keyed `clubs/{userId}/{batchId}/` (stable across retried inserts that can get a
      different club id — see `ClubService.CreateClubAsync` lines 92-101). Capture the registered
      email in a variable and look up the user id from the admin context
      (`db.Users.SingleAsync(c => c.NormalizedEmail == email.ToUpperInvariant())`, the pattern
      already used by this file's `UpdateUserAsync`), then assert
      `crest.OriginalBlobName.ShouldStartWith($"clubs/{userId}/")`. Keep the suffix assertions.
- [x] **Test fix — framework-400 trigger** (`Nova.Integration.Tests/Http/TraceCorrelationHttpTests.cs`,
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

Product fix in `ClubEndpointRouteBuilderExtensions.cs`: both `CreateClubHandler` and
`ChangeCrestHandler` now read the crest from `context.Request.Form.Files.GetFile("crest")`
instead of binding a non-nullable `IFormFile` parameter, so a missing crest file reaches the
handler's explicit null check and produces `ServiceProblem.Validation("crest", ...)` (Kind =
Validation) rather than a framework-generated 400 that `ToServiceProblemAsync` mapped to
Kind = BadRequest. (Review-remediation follow-up: `ChangeCrestHandler` was later refined to bind
a *nullable* `[FromForm] IFormFile? crest` — see the Review Remediation section — so the framework
pre-binds the form again, restoring the 415 for non-form content types, while a missing part still
reaches structured validation.) Test fixes: `ClubCrestHttpTests` captures the registered email
and looks up the user id from the admin context to assert the `clubs/{userId}/` blob prefix;
`TraceCorrelationHttpTests` sends a malformed multipart body (no boundary) instead of
`application/json` and was renamed `MalformedForm_ReturnsTraceIdMatchingSentTraceparent`.
Verified: `*ClubCrestHttpTests` → **13/13 pass**; `*TraceCorrelationHttpTests` → **3/3 pass**;
full integration suite → **372/372 pass**. Committed as `71ef90a`.

## Phase 3: Fix the ClubCrestManager Persistent-State Race (Browser CC2)

Status: Complete

- [x] **Product fix** (`Nova.UI/Features/Clubs/Components/ClubCrestManager.razor.cs`): `HasCrest` is
      supplied as `_summary?.HasCrest ?? false` by `ClubAdmin.razor`, and the island's first render
      can happen before the page summary loads, so `OnInitialized` captures `CrestPresent = false`
      and `HasCrestInitialized` blocks any recapture. Per the blazor-architecture rule ("copy to
      private component state only on first load **or when the incoming parameter value actually
      changes**"), add an `OnParametersSet` re-sync: when `HasCrest != CrestPresent` and the user
      has not locally mutated the crest since initialization, set `CrestPresent = HasCrest`. Track
      local mutation with a `[PersistentState] CrestMutatedLocally` property set to `true` on
      successful `SaveCrestAsync` and `ConfirmRemoveAsync` — persisted across circuit re-attach
      (like `HasCrestInitialized`/`CrestPresent`) so a re-attach after a local save with a
      still-loading host summary cannot revert local state. Keep `OnInitialized`'s existing
      initial-capture guard unchanged.
- [x] **Unit coverage** (`Nova.Unit.Tests/Features/Clubs/ClubCrestManagerComponentTests.cs`):
      - Parameter update false → true (no mutation) re-syncs the island from placeholder to crest.
      - After a successful save (or remove), a stale `HasCrest` parameter re-render does not revert
        `CrestPresent`.
      Use `cut.SetParametersAndRender(...)` following the file's existing patterns.
- [x] **Browser verification** — run the crest scenarios; CC2
      (`ClubCrest_AdminReplacesAndRemoves_NavReflectsCrestPresence`) must pass its remove step now.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*ClubCrestManagerComponentTests"` → pass
- Browser (needs AppHost + one-time Chromium install): `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --filter-class "*ClubCrestBrowserTests"` → CC1, CC2, CC3 pass
- Playwright check: load `/Clubs/{id}/admin` for a seeded club with a crest, WASM-reload, and confirm the "Remove crest" button renders and the remove flow completes.

### Phase Summary

Added `CrestMutatedLocally` (`[PersistentState]`) to `ClubCrestManager.razor.cs`, set to `true` on
successful `SaveCrestAsync` and `ConfirmRemoveAsync`, plus an `OnParametersSet` override that
re-syncs `CrestPresent = HasCrest` when the incoming `HasCrest` parameter actually changes and the
user has not locally mutated the crest — per the blazor-architecture copy-to-private-state rule.
`OnInitialized`'s initial-capture guard is unchanged. Added 3 bUnit tests covering the
false→true parameter re-sync (placeholder → crest) and the stale-parameter-after-save / -remove
no-revert cases, plus (in the review-remediation follow-up) a 4th test asserting
`CrestMutatedLocally` carries `[PersistentState]` so the guard survives circuit re-attach. The
component suite went 10 → 14 tests. Note: the plan's `SetParametersAndRender` call is the bUnit
2.x `Render` extension (the API was renamed in bUnit 2.9.0), so the new tests use
`cut.Render(...)`, matching the file's existing `Render` usage. CC2 now passes through its remove
step. The stale "KNOWN PRE-EXISTING LIMITATION" comment in `ClubCrestBrowserTests.cs` was replaced
with a note describing the re-sync behavior. Verified: `*ClubCrestManagerComponentTests` →
**14/14 pass**; `*ClubCrestBrowserTests` → **CC1, CC2, CC3 all pass** (3/3). Committed as `a69906d`
with the review-remediation `[PersistentState]` upgrade in a later commit.

## Phase 4: Full Validation

Status: Complete

- [x] `dotnet build Nova.slnx -t:Rebuild` → 0 warnings, 0 errors
- [x] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` → all pass
- [x] `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` → 372/372 pass
- [x] `dotnet format Nova.slnx --verify-no-changes` → clean
- [ ] `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` → full suite green
      (browser suite is local-only; run before merge) — **79/83 pass; 4 pre-existing failures
      unrelated to this change** (see Phase Summary)

### Phase Summary

Full validation results on branch `eruvalca-fix-crest-tests`:
- `dotnet build Nova.slnx -t:Rebuild` → **0 Warning(s), 0 Error(s)**.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` → **1815/1815 pass** (1812 prior +
  3 new `ClubCrestManagerComponentTests`).
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` → **372/372 pass**.
- `dotnet format Nova.slnx --verify-no-changes` → clean (exit 0).
- Browser: `--filter-class "*ClubCrestBrowserTests"` → **CC1, CC2, CC3 all pass (3/3)**. Full
  browser suite → **79/83 pass, 4 fail**: `BootstrapThemeBrowserTests.
  Theme_PrimaryButtonAndFocusRing_AreKelpTeal_WithNoBootstrapBlue`, `NavbarBrowserTests.
  Navbar_Authenticated_ShowsIconFirstItemsWithBootstrapIcons`, `NavbarBrowserTests.
  Navbar_Desktop_StacksIconAboveLabel`, and `NavbarBrowserTests.
  Navbar_Mobile_ExpandedMenu_KeepsInlineRowAndOutlineGlyph`.
  **These 4 failures are pre-existing and unrelated**: they reproduce identically on pristine
  `main` (5112f5d) in a clean worktree (`D:\repos\Nova`), and none of this change's 3 commits touch
  the theme CSS, navbar markup, or those test files. The BootstrapTheme failure is a focus-ring
  `box-shadow` assertion (`rgba(0,0,0,0) 0px...)` on `#campaigns-view-filter`); the Navbar failures
  are element-not-found assertions. Both trace to the theme/navbar work in #132/#134/#139/#141 on
  `main`, not to the crest regressions. The original club-crest plan
  (`plans/club-crest-cropping-and-aspect-ratio.md`) likewise documented that the repository runs
  its browser suite locally and had pre-existing environment-sensitive failures. CI (`.github/
  workflows/ci.yml`) runs build + unit tests only.

## Final Recap

The club-crest regressions introduced by PRs #142/#143 are fixed end to end:

1. **12 compiler warnings eliminated** — `Errors.ShouldNotBeNull()` guards (CS8602/CS8604),
   `Substitute.For<Response<bool>>()` substitutes (CS8625), and null-forgiving content-disposition
   projections (CS8619) across the 4 club-crest unit-test files. Build is now 0 warnings / 0 errors.
2. **Product bug fixed**: `ClubEndpoints.Create` / `ClubEndpoints.ChangeCrest` now read the crest
   file from `HttpContext.Request.Form.Files.GetFile("crest")` instead of binding a non-nullable
   `IFormFile`, so a missing crest produces the handler's structured `ServiceProblem.Validation`
   (Kind = Validation with a `crest` error key) rather than a framework 400 that mapped to
   Kind = BadRequest.
3. **2 test expectations fixed**: blob prefix `clubs/{userId}/` via an admin-context user lookup
   (creation blobs are keyed by userId for retry stability), and the framework-400 trace-correlation
   trigger now sends a malformed multipart body (club creation is a multipart endpoint; JSON returns
   415).
4. **ClubCrestManager persistent-state race fixed** (browser CC2): `OnParametersSet` re-syncs
   `CrestPresent` from a changed `HasCrest` parameter unless the user mutated the crest locally
   (`CrestMutatedLocally`, now `[PersistentState]` so it survives circuit re-attach), with 3 new
   bUnit tests. CC2's remove step now passes.

Validation: build 0/0; unit 1816/1816; integration 373/373; format clean; browser CC1-CC3 pass.
The full browser suite is 79/83 with 4 failures that reproduce identically on pristine `main`
(theme/navbar, not crest-related) — flagged in Phase 4 for an owner on the theme/navbar side.
5 commits on `eruvalca-fix-crest-tests`: `428f32e` (warnings), `71ef90a` (product + integration
fixes), `a69906d` (ClubCrestManager race), plus the plan update commit and the review-remediation
commits.

## Review Remediation (PR #144, Review ID 5013800548)

Two findings were raised on the PR; both were addressed with the fixes below (see the threaded
replies on the PR for the per-finding commit references).

### Finding 1 (High/Possible): `ChangeCrestHandler` no-form-params regression — non-form content type now yields 500 instead of 415

The original fix removed the `[FromForm] IFormFile crest` parameter entirely, so the framework
stopped pre-binding the form (`RequestDelegateFactory.TryReadFormAsync`) for this endpoint; a
non-form content type (e.g. `application/json` POST) then threw `InvalidOperationException`
(`FormFeature`: "Incorrect Content-Type") at the direct `context.Request.Form` read, which
bypassed `BadHttpRequestExceptionHandler` and became a 500 via the generic exception handler.

**Fix** (`Nova/Features/Clubs/ClubEndpointRouteBuilderExtensions.cs`): bind the file as a
*nullable* form parameter and drop the `HttpContext` read:

```csharp
private static async Task<IResult> ChangeCrestHandler(
    long clubId,
    [FromForm] IFormFile? crest,
    HttpContext context,   // still needed for RefreshAdminCookieAsync
    ...)
{
    if (crest is null || crest.Length is 0 or > ProfilePhotoConstraints.MaxBytes)
    { ... ServiceProblem.Validation("crest", message).ToHttpResult(); }
    ...
}
```

This restores framework pre-binding (415 for non-form content types, 400 for bodyless requests)
while a missing `crest` part binds `null` and still reaches the structured validation problem.
`CreateClubHandler` is unaffected: its `name`/`city`/`state` `[FromForm]` parameters keep the
framework pre-binding there (per the review).

**Regression coverage**: added `ChangeCrest_WithJsonContentType_ReturnsUnsupportedMediaType`
(`Nova.Integration.Tests/Http/ClubCrestHttpTests.cs`) — asserts `415
UnsupportedMediaType` for a JSON POST to the change-crest endpoint (would have been 500 before).

### Finding 2 (Low/Possible): `_crestMutatedLocally` is not `[PersistentState]`

The private `bool _crestMutatedLocally` field reset to `false` when the circuit re-attached after
a local save/remove, so a re-attach with a still-loading host summary (stale `HasCrest == false`)
could revert `CrestPresent` back to `false` via `OnParametersSet`.

**Fix** (`Nova.UI/Features/Clubs/Components/ClubCrestManager.razor.cs`): promote the field to a
`[PersistentState]` public property (`CrestMutatedLocally`), matching the existing
`HasCrestInitialized`/`CrestPresent` pattern in the same file. Note: `PersistentStateAttribute`
targets properties only (`[AttributeUsage(AttributeTargets.Property)]`), so the correct
equivalent of the review's field-level suggestion is a property.

**Regression coverage**: added `CrestMutatedLocally_IsPersistentState_ToSurviveCircuitReattach`
(`Nova.Unit.Tests/Features/Clubs/ClubCrestManagerComponentTests.cs`) — reflection-based assertion
that the property carries `[PersistentState]`.

### Verification (after remediation)

- `dotnet build Nova.slnx -t:Rebuild` → **0 Warning(s), 0 Error(s)**.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` → **1816/1816 pass**.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` → **373/373 pass**.
- `dotnet format Nova.slnx --verify-no-changes` → clean (exit 0).
- Browser: `--filter-class "*ClubCrestBrowserTests"` → **CC1, CC2, CC3 all pass (3/3)**.

## Deployment Plan

1. Merge PR against `main` (base `main`, branch `eruvalca-fix-crest-tests`) — no manual DB or
   configuration steps are needed; the change is code-only (HTTP handler, Blazor component, tests,
   plan doc).
2. CI gates (build + unit tests on `ubuntu-latest`) must be green on the merged commit — they cover
   the solution build (0 warnings/errors) and the full 1815-test unit suite.
3. Local-only checks to re-run after merge (browser/integration suites require the Aspire AppHost,
   Docker Postgres + Azurite, and a one-time `playwright.ps1 install chromium`):
   - `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` → 372/372.
   - `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --filter-class "*ClubCrestBrowserTests"` → CC1/CC2/CC3 pass.
   - Full browser suite before any theme/navbar release: the 4 known pre-existing failures
     (BootstrapTheme focus ring + 3 Navbar element-not-found) should be triaged separately
     (pre-existing on `main`, not introduced by this change).
4. No rollout order or data migration required; the endpoints and component behave correctly for
   both existing clubs (with and without crests) and new creation flows.

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
