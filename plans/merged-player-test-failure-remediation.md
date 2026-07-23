# Merged Player Test Failure Remediation

Resolve the integration-test regressions introduced by the recently merged player-management and roster-query sub-issues. Scope is limited to transaction execution-strategy compatibility, roster pagination binding defaults, the invalid-input HTTP expectation, and regression verification; NuGet vulnerability remediation and UI work are excluded.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.
The baseline reproduced on July 22, 2026 was:

- `dotnet test --project Nova.Unit.Tests`: 596 passed, 0 failed.
- `dotnet test --project Nova.Integration.Tests`: 70 passed, 6 failed.
- Four failures cascade from player creation returning HTTP 500 because
  `PlayerManagementService` starts user transactions outside Npgsql's retrying execution strategy.
- One roster failure returns HTTP 500 because omitted `Page` and `PageSize` query values are treated
  as required despite property initializers.
- One invalid-create test expects HTTP 422, while .NET 10 endpoint validation correctly rejects the
  structurally invalid body with HTTP 400 before the service executes.

## Phase 1: Repair Player Transaction Execution

Status: Complete <!-- Not started | In progress | Complete -->
Suggested executor: orchestrator

- [x] Refactor `PlayerManagementService.CreateAsync` so the entire transaction, club roster lock,
      player insert, active-campaign query, participation inserts, saves, and commit execute inside
      `db.Database.CreateExecutionStrategy().ExecuteAsync(...)`.
- [x] Refactor `PlayerManagementService.UpdateAsync` so the entire transaction, player mutation lock,
      tenant-safe lookup, lifecycle and graduation-year checks, save, and commit execute inside the same
      execution-strategy pattern.
- [x] Preserve current `ServiceResult` outcomes, logging, lock order, atomic enrollment semantics,
      cross-tenant behavior, and transaction rollback on every early problem result.
- [x] Ensure a retry creates a fresh `NovaDbContext` and transaction per execution attempt rather than
      reusing tracked state from a failed attempt.
- [x] Add or strengthen focused PostgreSQL integration coverage that reaches successful create and
      update paths under the configured Npgsql retrying execution strategy.

### Verification Plan

- Run `dotnet test --project Nova.Unit.Tests --filter-class "*PlayerManagementServiceTests"`; expect
  all player-management service tests to pass.
- Run `dotnet test --project Nova.Integration.Tests --filter-class "*PlayerManagementHttpTests"`;
  expect create, update, authorization, cross-tenant, and graduation-year blocker tests to pass with
  no execution-strategy exceptions or HTTP 500 responses.

Verification result:

- Passed on July 22, 2026: `dotnet test --project Nova.Unit.Tests --filter-class "*PlayerManagementServiceTests"` (15 passed).
- Passed on July 22, 2026: `dotnet test --project Nova.Integration.Tests --filter-class "*PlayerManagementHttpTests"` (7 passed).
- Passed on July 22, 2026: `dotnet test --project Nova.Integration.Tests --filter-class "*PlayerLifecycleRetryTests" --filter-class "*PlayerManagementRetryTests"` (3 passed).
- Passed on July 22, 2026 after idempotency hardening: `dotnet test --project Nova.Integration.Tests --filter-class "*PlayerManagementRetryTests"` (4 passed).
- Passed on July 22, 2026 after idempotency hardening: `dotnet test --project Nova.Integration.Tests --filter-class "*PlayerManagementRetryTests" --filter-class "*PlayerEnrollmentPostgresTests"` (6 passed).
- `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext` reported no pending model changes.
- `dotnet build Nova.slnx` succeeded.

### Phase Summary

`PlayerManagementService` now obtains an execution strategy from a setup context, then performs each
create/update attempt with a fresh `NovaDbContext` and transaction so Npgsql retries no longer reuse
tracked state from a failed attempt. The create flow still acquires the club-roster advisory lock
before inserting the player and active-campaign enrollments; the update flow still acquires the
player-mutation lock before the tenant-safe lookup, lifecycle checks, graduation-year blocker policy,
save, and commit. Added focused PostgreSQL retry tests for both successful create and update paths,
and aligned the invalid-create HTTP test with .NET 10 endpoint validation returning HTTP 400 before
the service runs. Player creation now also generates one stable creation-operation identifier before
the first execution attempt. The identifier is stored under a tenant-scoped unique constraint and
used by EF's execution-strategy verification callback to reconstruct a successful result when the
database committed but the commit acknowledgement was lost, preventing a retry from inserting a
duplicate player and enrollment set.

## Phase 2: Repair Roster Pagination Binding

Status: Not started
Suggested executor: orchestrator

- [ ] Change the roster endpoint contract so omitted `page` and `pageSize` query parameters bind
      successfully and resolve to `GetPlayerRosterInput.DefaultPage` and
      `GetPlayerRosterInput.DefaultPageSize`.
- [ ] Keep explicit pagination values subject to the existing DataAnnotations ranges and preserve
      service-layer validation as the authoritative non-HTTP boundary.
- [ ] Keep shared route constants and the WASM client URL builder compatible with requests that omit
      either or both pagination parameters.
- [ ] Add focused endpoint/integration assertions for omitted pagination defaults and invalid explicit
      pagination values so binding failures cannot regress into HTTP 500 responses.

## Phase 2: Repair Roster Pagination Binding

Status: Not started
Suggested executor: orchestrator

- [ ] Change the roster endpoint contract so omitted `page` and `pageSize` query parameters bind
      successfully and resolve to `GetPlayerRosterInput.DefaultPage` and
      `GetPlayerRosterInput.DefaultPageSize`.
- [ ] Keep explicit pagination values subject to the existing DataAnnotations ranges and preserve
      service-layer validation as the authoritative non-HTTP boundary.
- [ ] Keep shared route constants and the WASM client URL builder compatible with requests that omit
      either or both pagination parameters.
- [ ] Add focused endpoint/integration assertions for omitted pagination defaults and invalid explicit
      pagination values so binding failures cannot regress into HTTP 500 responses.ts and invalid explicit
  pagination values so binding failures cannot regress into HTTP 500 responses.
