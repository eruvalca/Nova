# Player Roster HTTP Failure Remediation

Correct the player-roster API's cross-club response from HTTP 500 to HTTP 403 while preserving Nova's dual-layer validation. Add focused HTTP conversion coverage and split the broad authorization integration test so each contract fails independently.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

Preserve unrelated user changes already present in the worktree. In particular, do not modify or revert the player-creation idempotency and retry work visible in `Nova/Entities/PlayerEntity.cs`, `Nova/Features/Players/PlayerManagementService.cs`, their migrations, tests, or related plan.

## Phase 1: Diagnose and Fix the Pre-Handler HTTP 500

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [x] Reproduce only `PlayerRosterApi_Enforces401And403_AndAllowsSameClubMembersAsync` and capture the cross-club response body plus correlated server diagnostics without committing temporary diagnostic assertions.
- [x] Confirm whether the request reaches `PlayerService.GetPlayerRosterAsync` by checking for its source-generated `Forbidden player-roster access attempt` warning or by using a narrowly scoped reversible probe.
- [x] Inspect the endpoint metadata generated for `[AsParameters] GetPlayerRosterInput` and rule out missing .NET 10 validation metadata as the cause.
- [x] Capture the framework exception with a temporary developer exception page and identify omitted non-nullable `Page` and `PageSize` query values as the `BadHttpRequestException` source.
- [x] Model `Page` and `PageSize` as nullable optional query inputs and apply the existing defaults authoritatively in `PlayerService` without disabling validation or weakening authorization.
- [x] Keep the endpoint's `RequireClubMember` policy and the service's route-club comparison: authenticated members of another club pass membership authorization and receive a service-generated forbidden response.

### Verification Plan

- Run `dotnet build Nova/Nova.csproj`; expect a successful build with no new warnings or errors.
- Run `dotnet test --project Nova.Unit.Tests --filter-class "*PlayerServiceTests"`; expect all roster service tests to pass, including the cross-club forbidden result.
- Run `dotnet test --project Nova.Integration.Tests --filter-method "*PlayerRosterApi_Enforces401And403_AndAllowsSameClubMembersAsync"`; expect the test to pass and the cross-club request to return HTTP 403 rather than HTTP 500.
- Inspect the generated validation artifacts or endpoint metadata after the build; expect `GetPlayerRosterInput` to have discoverable validation metadata without calling `DisableValidation()`.

### Phase Summary

The HTTP 500 was caused by minimal API `[AsParameters]` binding, not missing validation metadata. Non-nullable `Page` and `PageSize` properties were treated as required query parameters even though they had property initializers, so authenticated requests without those query values threw `BadHttpRequestException` before the handler. Making the query values nullable and coalescing them to the existing defaults in `PlayerService` restored 401/403/200 behavior while preserving endpoint and service validation. The isolated integration test, eight focused `PlayerServiceTests`, and `Nova.csproj` build passed; only existing NuGet vulnerability warnings remained.

## Phase 2: Add Focused ServiceResult-to-HTTP Coverage

Status: Complete

Suggested executor: sub-agent w/ smaller model

- [x] Identify the narrowest existing unit-test pattern capable of executing `ServiceProblem.ToHttpResult()` without starting the Aspire AppHost.
- [x] Add a focused test proving `ServiceProblem.Forbidden(...)` executes as HTTP 403 ProblemDetails and retains `ServiceProblemKind.Forbidden` semantics across the HTTP boundary.
- [x] Assert the response is RFC 7807-compatible and includes the W3C `traceId` extension when an `Activity` is active, matching repository observability requirements.
- [x] Keep adjacent coverage focused; existing `HttpResponseMessageExtensionsTests` already provide table-driven client-side mappings for the other problem kinds.

### Verification Plan

- Run `dotnet test --project Nova.Unit.Tests --filter-class "*ServiceResult*"`; expect all direct result-conversion tests to pass.
- If the final test class uses a different name, run the exact class with `dotnet test --project Nova.Unit.Tests --filter-class "*<ClassName>"`; expect HTTP 403 and trace-ID assertions to pass.
- Run `dotnet test --project Nova.Unit.Tests --filter-class "*HttpResponseMessageExtensionsTests"`; expect existing client-side ProblemDetails reconstruction tests to remain green.

### Phase Summary

Added `ServiceResultExtensionsTests` using `DefaultHttpContext` to execute a forbidden result, assert HTTP 403, RFC 7807 detail/status, and the W3C trace ID, then parse the response through the real client extension and assert `ServiceProblemKind.Forbidden`. The focused server-result test and all 12 existing `HttpResponseMessageExtensionsTests` passed.

## Phase 3: Split and Harden the Authorization Integration Tests

Status: Complete

Suggested executor: sub-agent w/ smaller model

- [x] Replace `PlayerRosterApi_Enforces401And403_AndAllowsSameClubMembersAsync` with three independently discoverable tests for anonymous HTTP 401, cross-club HTTP 403 plus forbidden ProblemDetails, and same-club HTTP 200 plus roster pagination defaults.
- [x] Extract only the shared player-roster arrangement needed to avoid duplicating lengthy identity and club setup; keep test ownership and cleanup consistent with `ClubDetailAdminHttpTests` and the shared Aspire fixture.
- [x] Ensure each test uses `TestContext.Current.CancellationToken`, unique seeded identities, and tenant-safe assertions rather than global database counts.
- [x] Preserve the endpoint contract assertions: anonymous requests are rejected by authorization middleware, cross-club members receive `ServiceProblemKind.Forbidden`, and same-club members receive page 1 with page size 20.
- [x] Keep the incidental PostgreSQL health-check connection warning outside this remediation because it did not recur as a focused or full-suite failure after the endpoint fix.

### Verification Plan

- Run each new method independently with `dotnet test --project Nova.Integration.Tests --filter-method "*<MethodName>"`; expect one passing test per authorization state.
- Run `dotnet test --project Nova.Integration.Tests --filter-class "*ClubDetailAdminHttpTests"`; expect all class tests to pass with no HTTP 500 in the player-roster scenarios.
- Run `dotnet test --project Nova.Unit.Tests`; expect the full unit suite to pass.
- Run `dotnet test --project Nova.Integration.Tests`; expect the full integration suite to pass. If an unrelated existing failure occurs, record its exact test and diagnostic without changing unrelated code.
- Run `git diff --check`; expect no whitespace errors.

### Phase Summary

Split the combined test into independent anonymous, cross-club, and same-club methods. Added focused helpers for creating a club admin and registering an existing-club member, while retaining unique identities, cancellation tokens, and tenant-safe response assertions. All three roster methods passed together, the full 10-test `ClubDetailAdminHttpTests` class passed, and the complete integration suite passed 82/82.

## Final Recap

Fixed the player-roster API's omitted-pagination failure by modeling `Page` and `PageSize` as optional nullable query values and applying existing defaults in `PlayerService`. Added direct server-to-client forbidden ProblemDetails coverage with W3C trace correlation, and split the roster authorization integration test into independently diagnosable 401, 403, and 200 contracts. Verification passed: server build, 597/597 unit tests, and 82/82 Aspire/PostgreSQL integration tests. Existing NuGet vulnerability warnings for `AngleSharp`, `Microsoft.OpenApi`, and `SQLitePCLRaw.lib.e_sqlite3` were unchanged and are outside this remediation.

## Deployment Plan

No schema, migration, configuration, or infrastructure changes are required.

1. Build and publish the Nova server through the repository's existing deployment pipeline.
2. Deploy the server and matching client/shared assemblies together because `GetPlayerRosterInput` changed nullability.
3. Smoke-test `GET /api/clubs/{clubId}/players/roster` without pagination query parameters: expect 401 for anonymous, 403 ProblemDetails for a member of another club, and 200 with page 1/page size 20 for a same-club member.
4. Monitor error telemetry for `BadHttpRequestException` on the roster route and confirm no new HTTP 500 responses appear.