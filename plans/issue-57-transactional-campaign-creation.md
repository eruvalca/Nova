# Transactional Campaign and Inline Season Creation

Add the ClubAdmin-only command slice for creating an Active campaign in an existing or inline-created season, atomically enrolling every Active player, and exposing the operation through shared contracts, HTTP, and WebAssembly. The slice includes retry-safe caller idempotency, tenant-safe uniqueness and date rules, rollback guarantees, and PostgreSQL race coverage; Razor UI and campaign query/list work remain out of scope.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

Confirmed product decisions:

- `CreateCampaignInput` carries a caller-supplied `Guid OperationId`.
- Exactly one season choice is required: an existing season ID or inline season data.
- Inline season and campaign end dates cannot precede their respective start dates.
- Campaign dates must fit within the selected season. A finite season requires a finite campaign planned end date.
- Inline season names use the existing case-sensitive, club-scoped unique constraint.
- Campaign names are case-sensitive and unique within a season for the current club.
- A cross-tenant existing season ID is non-disclosing `NotFound`.
- Success returns campaign and season IDs and metadata, Active status, whether the season was created inline, and enrolled-player count.
- The endpoint and service require ClubAdmin authorization. The JSON endpoint follows Nova's WASM convention by disabling antiforgery token validation while retaining SameSite cookie protection.
- Razor UI, setup/preview queries, list queries, metadata correction, dashboard work, and campaign-team persistence are out of scope.

## Phase 1: Shared Contracts and Persistence Model

Status: Complete

Suggested executor: orchestrator

- [x] Add explicit-property campaign and inline-season input records with DataAnnotations and cross-field validation for exactly one season choice and intrinsic date ordering.
- [x] Add a creation result DTO, boundary-crossing service interface, and shared campaign route constants.
- [x] Add campaign and season creation-operation IDs with club-scoped filtered unique indexes.
- [x] Add a club/season/campaign-name unique index and preserve the existing case-sensitive database semantics.
- [x] Generate one incremental EF Core migration and inspect its `Up`, `Down`, designer, and model snapshot changes.
- [x] Add focused input-validation and persistence-model tests, including tenant-scoped operation-ID and campaign-name uniqueness.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*CreateCampaignInput*"` — all campaign creation input validation tests pass.
- `dotnet test --project Nova.Integration.Tests --filter-class "*CampaignCreationPersistence*"` — PostgreSQL enforces tenant-scoped idempotency and campaign-name uniqueness.
- `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext` — reports no pending model changes.

### Phase Summary

Added validated shared request/result contracts, caller operation IDs, tenant-scoped filtered
idempotency indexes, campaign-name uniqueness, and a tenant-consistent composite campaign-to-season
foreign key. Migration `AddCampaignCreationIdempotency` and its snapshot are model-current. Seven
input-validation tests and PostgreSQL uniqueness tests pass.

## Phase 2: Transactional Campaign Creation Service

Status: Complete

Suggested executor: orchestrator

- [x] Implement `CampaignCreationService` with authoritative validation and ClubAdmin authorization.
- [x] Resolve an existing season through tenant filters or create the inline season inside the mutation transaction.
- [x] Validate campaign dates against the freshly loaded or inline season and return structured validation failures without writes.
- [x] Execute the complete mutation inside EF Core's retrying execution strategy with a fresh `NovaDbContext` per attempt.
- [x] Acquire the shared club-roster transaction lock before resolving/creating the season, snapshotting Active players, and writing the campaign and participations.
- [x] Persist the caller operation ID and reconstruct the original successful result during ambiguous-commit verification.
- [x] Map uniqueness conflicts consistently, ensure Archived players are excluded, and use source-generated structured logging.
- [x] Register the server implementation in `Nova/Program.cs`.
- [x] Add SQLite service tests for validation, authorization, existing/inline season paths, cross-tenant `NotFound`, Active status, roster filtering, duplicate conflicts, idempotent result reconstruction, and rollback.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*CampaignCreationService*"` — all provider-agnostic service tests pass and assert no writes on rejected operations.

### Phase Summary

Implemented a retry-safe transactional service that validates before effects, authorizes ClubAdmin,
uses a fresh context per execution/verification attempt, holds the shared roster advisory lock, and
commits inline season, campaign, and Active-player assignments atomically. Repeated and ambiguous
operations reconstruct the original creation-time status and enrollment count. Twelve focused service
tests pass.

## Phase 3: HTTP Endpoint and WebAssembly Client

Status: Complete

Suggested executor: orchestrator

- [x] Map a ClubAdmin-only campaign creation endpoint using shared route constants, static handlers, automatic endpoint validation, complete ProblemDetails metadata, and the repository's WASM antiforgery convention.
- [x] Register the endpoint mapping in `Nova/Program.cs`.
- [x] Implement and register the typed WebAssembly `ICampaignCreationService` HTTP client with strict required success-payload handling.
- [x] Add endpoint tests for route registration, anonymous/role policy behavior, validation, success serialization, `NotFound`, and `Conflict` ProblemDetails.
- [x] Add HTTP client tests for success and problem responses, including malformed or empty success payloads.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*CampaignCreationEndpoint*|*HttpCampaignCreationService*"` — endpoint and client contract tests pass.

### Phase Summary

Mapped `POST /api/campaigns` with ClubAdmin authorization, automatic validation, complete response
metadata, antiforgery handling for the WASM JSON boundary, and `ServiceResult` conversion. The typed
client shares the route contract and rejects empty, null, or malformed success payloads. Six live HTTP
tests and four client tests pass.

## Phase 4: PostgreSQL Retry, Rollback, and Race Coverage

Status: Complete

Suggested executor: orchestrator

- [x] Add a transient pre-commit failure test proving retries use a fresh context and leave one complete aggregate.
- [x] Add an ambiguous-commit test proving verification returns the original campaign/season/result without duplicate rows.
- [x] Add a forced-failure rollback test proving inline season, campaign, and participations all roll back together.
- [x] Add concurrent campaign/player creation coverage proving the shared roster lock leaves every Active player enrolled exactly once regardless of lock winner.
- [x] Assert Archived players are excluded and no campaign-team join is created.

### Verification Plan

- `dotnet test --project Nova.Integration.Tests --filter-class "*CampaignCreation*"` — all PostgreSQL constraint, retry, rollback, and race tests pass.

### Phase Summary

Added real PostgreSQL coverage for operation/name constraints, transient retry with fresh contexts,
ambiguous commit verification, second-save rollback, and the campaign/player roster-lock race. The
service creates only undecided player assignments for Active players and does not create campaign-team
state. All six PostgreSQL campaign tests pass.

## Phase 5: Final Validation

Status: Complete

Suggested executor: orchestrator

- [x] Run the complete unit and integration test projects.
- [x] Build the solution and confirm the EF model matches the migration.
- [x] Review the final diff for contract, metadata, DI, tenancy, transaction, logging, and documentation completeness.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests` — all unit tests pass.
- `dotnet test --project Nova.Integration.Tests` — all integration tests pass.
- `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext` — reports no pending model changes.
- `dotnet build Nova.slnx` — build succeeds with no new warnings.

### Phase Summary

The complete unit suite passes with 772 tests and the complete Aspire/PostgreSQL integration suite
passes with 126 tests. EF reports no pending model changes, the solution builds successfully, and
`git diff --check` reports no whitespace errors. Existing NuGet vulnerability and EF tool-version
warnings remain unchanged by this feature.

## Final Recap

Issue #57 now has an end-to-end campaign creation command slice. Club administrators can create an
Active campaign against a tenant-visible season or atomically create an inline season, enroll the
current Active roster, safely retry with a caller operation ID, and receive complete campaign/season
metadata through the server or WebAssembly client. Tenant boundaries, date containment, uniqueness,
rollback, ambiguous commits, and player/campaign races are covered at the appropriate SQLite, HTTP,
and PostgreSQL layers. Razor UI and campaign query/list work remain intentionally out of scope.

## Deployment Plan

1. Deploy the application build and apply migration `AddCampaignCreationIdempotency` before enabling
   campaign creation callers.
2. Deploy server and WebAssembly assets together so both sides use the shared `/api/campaigns`
   contract.
3. Monitor campaign creation conflict, validation, and success logs by operation ID during rollout.
4. Roll back the application before running the migration `Down`; the down migration removes
   idempotency metadata and restores the prior single-column season foreign key.
