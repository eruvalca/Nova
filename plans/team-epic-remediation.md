# Team Epic (#8) Remediation

Address ten review findings against the merged persistent-team-management epic (#8, commits
`0aa3180..457418d`): retry-unsafe transactions, a tenant-safety regression in `IsClubAdmin`,
missing Postgres coverage, duplicated/inconsistent domain rules, dead API surface, missing
uniqueness/idempotency, an ordering bug, three red unit tests, and minor polish. All work lands
as a single PR on branch `eruvalca-team-epic-remediation`.

## Context for Future Agents

**Repo:** `eruvalca/Nova`, .NET 10 Blazor + Aspire. Server `Nova/`, WASM `Nova.Client/`, shared UI
`Nova.UI/`, contracts `Nova.Shared/`, tests `Nova.Unit.Tests/` (SQLite + bUnit) and
`Nova.Integration.Tests/` (Aspire + Postgres via `NovaAppHostFixture`).

**Test commands** (Microsoft.Testing.Platform — do NOT pass `--nologo`, `--collect`, `--logger`):

- `dotnet test --project Nova.Unit.Tests\Nova.Unit.Tests.csproj`
- `dotnet test --project Nova.Integration.Tests\Nova.Integration.Tests.csproj`
- Filter with `--filter-class "*TeamManagementServiceTests"`.

**Known baseline at plan time:** unit tests = 729 total, 726 passed, **3 failed** (the
`PlayerComponentsTests.PlayerDetail_*` trio in Phase G). Any other failure is a new regression.

**Decisions already made with the user — do not relitigate:**

1. Team `GraduationYear` is a **minimum** eligible player graduation year. A placement blocks a
   change when `Player.GraduationYear < newTeamGraduationYear`. Current code is correct.
2. Global `Roles.Admin` is **not** a club operator. Everything narrows to `ClubAdmin` only.
3. Teams read access standardizes on `Policies.RequireClubMember` (matches Players).
4. `PUT /api/teams/{id}/graduation-year` is removed entirely; `UpdateAsync` covers cutoffs.
5. Blockers ship in ProblemDetails **extensions** everywhere, never as an indexed validation-errors
   dictionary.
6. Team names are unique per `(ClubId, Name, GraduationYear)`.
7. Database is pre-production; a plain additive migration is fine, no backfill.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to
`Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue
with zero context); run the phase's **Verification Plan** and record the result before moving on.
When all phases are done, fill in **Final Recap** and **Deployment Plan**.

Relevant repo instructions load automatically by path, but read them before editing:
`.github/instructions/service-layer.instructions.md`, `ef-core-tenancy.instructions.md`,
`functional-core.instructions.md`, `api-endpoints.instructions.md`, `testing.instructions.md`,
`blazor-architecture.instructions.md`. Skills `add-domain-persistence`, `nova-testing`, and
`aspire-playwright-validation` cover the recipes.

## Phase A: Re-verify every finding before changing code

Status: Complete

Suggested executor: orchestrator

Prove each finding with a concrete artifact so no phase is built on a misreading. Record the
evidence in the Phase Summary.

- [x] Capture the unit-test baseline and confirm exactly 3 failures, all `PlayerComponentsTests.PlayerDetail_*`.
- [x] Confirm `builder.EnrichNpgsqlDbContext<NovaDbContext>()` (`Nova/Program.cs:134`) yields a retrying execution strategy at runtime — assert `db.Database.CreateExecutionStrategy()` is `NpgsqlRetryingExecutionStrategy` in a scratch integration test or via the existing `NovaAppHostFixture`.
- [x] Prove finding #1 is a real runtime failure, not theory: add a temporary integration test that calls `TeamLifecycleService.ArchiveAsync` through a retrying factory and observe the `InvalidOperationException` about user-initiated transactions. Delete or convert the scratch test afterwards.
- [x] Confirm `Policies.RequireClubAdmin` already admitted `Roles.Admin` before the epic (`Nova/Extensions/Security/AuthorizationBuilderExtensions.cs:27`) and list every test that asserts that behavior (`Nova.Unit.Tests/Security/EvaluatorAuthorizationPolicyTests.cs` lines ~92-97).
- [x] Confirm `RequireEvaluator` and `RequireClubMember` are currently identical policy definitions.
- [x] Confirm no caller anywhere invokes `UpdateGraduationYearAsync` outside its own tests (`Nova.UI`, `Nova.Client`, `Nova/`).
- [x] Confirm `TeamEntity`/`TeamEntityConfiguration` have no unique index and no `CreationOperationId`.
- [x] Confirm the `TeamDetailQueryService` ordering mismatch by writing a failing test first — **carried into Phase E**, which started with exactly this failing test and completed there.
- [x] Record which findings, if any, turned out to be wrong, and adjust later phases accordingly.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests\Nova.Unit.Tests.csproj` → expect `total: 729, failed: 3`, all three named `PlayerDetail_*`.
- `docker info` → expect a running daemon, confirming Aspire integration tests can execute.
- `dotnet test --project Nova.Integration.Tests\Nova.Integration.Tests.csproj --filter-class "*TeamLifecycleHttpTests"` → expect pass (current coverage is anonymous-401 only).
- Scratch archive-through-retrying-factory test → expect it to FAIL with `InvalidOperationException` mentioning the execution strategy; this is the proof for Phase C.

### Phase Summary

**Complete.** Every finding verified; none were wrong. Evidence:

- **Unit baseline confirmed:** `total: 729, failed: 3` — `PlayerDetail_UsesPlayersFallback_WhenReturnUrlIsExternal`, `PlayerDetail_PreservesSafeRelativeReturnUrl`, `PlayerDetail_UsesFallbackTagColor_WhenTraitColorIsInvalid`, all failing with `Unable to resolve service for type 'IPlayerManagementService'`.
- **Docker 29.6.2 running**, Aspire integration tests execute locally (~40s per filtered run after container startup).
- **Finding #1 proven at runtime.** Instead of a throwaway scratch test, the real Phase G deliverable `Nova.Integration.Tests/Data/TeamLifecycleRetryTests.cs` was written first. Both tests fail today with:
  `System.InvalidOperationException : The configured execution strategy 'NpgsqlRetryingExecutionStrategy' does not support user-initiated transactions. Use the execution strategy returned by 'DbContext.Database.CreateExecutionStrategy()' ...`
  thrown from `TeamLifecycleService.TransitionAsync`. This file stays in the tree and must go green in Phase C — do not delete it.
- **Finding #4 confirmed:** `AuthorizationBuilderExtensions.cs:27` already read `RequireRole(Roles.ClubAdmin, Roles.Admin)` before the epic, so the epic's `IsClubAdmin` widening aligned the provider with a policy that was itself too permissive. Tests asserting the old behavior: `EvaluatorAuthorizationPolicyTests.cs:92,95` and `CurrentUserStateTests.cs:67`. Production `Roles.Admin` call sites needing narrowing: `Teams.razor.cs:219`, `TeamDetail.razor.cs:178`, `Players.razor.cs:242`, `PlayerDetail.razor.cs:126`, `CurrentUserProvider.cs:50`.
- **Finding #7 confirmed and downgraded:** `RequireEvaluator` and `RequireClubMember` are both bound to the same `ConfigureClubMemberPolicy` local function, so they are behaviorally identical today — a latent naming divergence, not a live hole.
- **Finding #6 confirmed:** `UpdateGraduationYearAsync` has zero production callers. All hits are the definition chain (`ITeamLifecycleService`, `TeamLifecycleService`, `HttpTeamLifecycleService`, endpoint mapping, `TeamEndpoints` constants) plus tests.
- **Finding #8 confirmed:** `TeamEntity` has no `CreationOperationId` and `TeamEntityConfiguration` declares no `HasIndex` at all — not even on `ClubId`. Contrast `PlayerEntityConfiguration.cs:24-27`, which has `HasIndex(ClubId)` plus a filtered unique index on `(ClubId, CreationOperationId)`. Phase D should also add the plain `ClubId` index for parity.
- **Reusable harness found:** `Nova.Integration.Tests/Data/ExecutionStrategyRetryTestSupport.cs` already provides `RetryingTenantDbContextFactory` (retry enabled, `maxRetryCount: 1`, counts created contexts), `FailFirstSaveChangesInterceptor`, and `FailFirstCommittedTransactionInterceptor` for the ambiguous-commit case. Phase G reuses all three; nothing new needs building.

## Phase B: Restore ClubAdmin-only authorization

Status: Complete

Suggested executor: orchestrator

Global `Admin` must not act as a club operator. Today the policy admits it, the provider (post-epic)
admits it, and the UI admits it. Narrow all three consistently.

- [x] `Nova/Extensions/Security/AuthorizationBuilderExtensions.cs`: change `RequireClubAdmin` to `policy.RequireRole(Roles.ClubAdmin)` only.
- [x] `Nova/Data/Tenancy/CurrentUserProvider.cs`: delete `IsClubAdminRole` and restore `IsInRole(Roles.ClubAdmin)` for both `IsClubAdmin` and `GetCurrentUserState`.
- [x] Revert the doc-comment changes on `ICurrentUserProvider.IsClubAdmin` and `ClubMember.IsClubAdmin` (`Nova.Shared/Security/CurrentUserState.cs`) to describe ClubAdmin only.
- [x] `Nova.UI/Features/Teams/Pages/Teams.razor.cs:219` and `TeamDetail.razor.cs:178`: `_canManageTeams = principal.IsInRole(Roles.ClubAdmin);`
- [x] `Nova.UI/Features/Players/Pages/Players.razor.cs:242` and `PlayerDetail.razor.cs:126`: same narrowing for `_canManagePlayers`.
- [x] Standardize teams read: change `TeamRosterEndpointRouteBuilderExtensions.cs:29` to `Policies.RequireClubMember`, and both team pages' `@attribute [Authorize(Policy = ...)]` to `Policies.RequireClubMember`. `TeamDetailEndpointRouteBuilderExtensions.cs` already uses it.
- [x] Update `Nova.Unit.Tests/Security/CurrentUserStateTests.cs` (drop the `Roles.Admin, true` case, expect `false`).
- [x] Update `Nova.Unit.Tests/Security/EvaluatorAuthorizationPolicyTests.cs` lines ~92-97 so `RequireClubAdmin` + `Roles.Admin` now expects denial.
- [x] Update `Nova.Unit.Tests/Teams/TeamComponentsTests.cs:675` and any bUnit test that grants `Roles.Admin` expecting management controls.
- [x] Add a unit test asserting a principal holding only `Roles.Admin` (with a ClubId claim) is denied `RequireClubAdmin` and gets `IsClubAdmin == false`.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests\Nova.Unit.Tests.csproj --filter-class "*CurrentUserStateTests"` → all pass.
- `dotnet test --project Nova.Unit.Tests\Nova.Unit.Tests.csproj --filter-class "*EvaluatorAuthorizationPolicyTests"` → all pass, including the new Admin-denied case.
- `dotnet test --project Nova.Unit.Tests\Nova.Unit.Tests.csproj --filter-class "*TeamComponentsTests"` → all pass.
- `grep -rn "Roles.Admin" Nova Nova.UI Nova.Client` → the only remaining production hits are `Policies.RequireAdmin` and `StartupDatabaseInitializer` role seeding.

### Phase Summary

Global `Roles.Admin` is no longer a club operator anywhere in the stack.

Production changes:

- `AuthorizationBuilderExtensions.cs:27` — `RequireClubAdmin` is now `RequireRole(Roles.ClubAdmin)` only.
- `CurrentUserProvider.cs` — `IsClubAdminRole` returns `principal?.IsInRole(Roles.ClubAdmin) ?? false`; the doc comment records *why* (`Admin` is a platform role with no club tenancy, and `IsClubAdmin` feeds EF tenant filters).
- Doc comments reverted on `ICurrentUserProvider.IsClubAdmin` and `CurrentUserState.ClubMember.IsClubAdmin`.
- UI capability gates narrowed to `IsInRole(Roles.ClubAdmin)` in `Teams.razor.cs:219`, `TeamDetail.razor.cs:178`, `Players.razor.cs:242`, `PlayerDetail.razor.cs:126`.
- Teams read auth standardized on `Policies.RequireClubMember`: `TeamRosterEndpointRouteBuilderExtensions.cs:29`, `Teams.razor:3`, `TeamDetail.razor:3`. `RequireEvaluator` now has zero consumers outside its own policy test — left registered intentionally (it is the forward-looking evaluator seam and is currently identical to `RequireClubMember`).

Test changes:

- `CurrentUserStateTests.cs` — `Roles.Admin` case flipped to `false`; added a `Roles.StandardUser, false` case.
- `EvaluatorAuthorizationPolicyTests.cs` — `RequireClubAdmin` + `Roles.Admin` flipped to denial, plus a new `hasClub: true` denial row and a dedicated `ClubAdminPolicy_DeniesGlobalAdmin` theory.
- `TeamComponentsTests.cs` — `Teams_ShowsMutationControls_ForAdmin` inverted and renamed to `Teams_HidesMutationControls_ForGlobalAdminWithoutClubAdmin`.

Verification result: `dotnet test --project Nova.Unit.Tests\Nova.Unit.Tests.csproj` → 733 total, 730 passed, 3 failed. The 3 failures are the known pre-existing `PlayerComponentsTests.PlayerDetail_*` DI failures from the Players epic (baseline was 729/726/3); test count rose by 4 from the added theory rows. No new regressions. `grep "Roles.Admin"` across `Nova`, `Nova.UI`, `Nova.Client` returns only `Policies.RequireAdmin`, the explanatory doc comment, and `StartupDatabaseInitializer` role seeding — exactly as the verification plan predicted.

## Phase C: Make team mutations retry-safe

Status: Complete

Suggested executor: orchestrator

Three code paths call `db.Database.BeginTransactionAsync` outside an execution strategy, which throws
under `NpgsqlRetryingExecutionStrategy`. Mirror `PlayerLifecycleService` /
`PlayerManagementService`: a fresh `DbContext` **and** fresh transaction per attempt, with no tracked
state reused across attempts.

- [x] `Nova/Features/Teams/TeamLifecycleService.cs` → wrap `TransitionAsync` (used by both `ArchiveAsync` and `RestoreAsync`) in `CreateExecutionStrategy().ExecuteAsync`, creating the context inside the delegate.
- [x] `Nova/Features/Teams/TeamManagementService.cs` → wrap `UpdateAsync` the same way.
- [x] `TeamLifecycleService.UpdateGraduationYearAsync` is deleted in Phase D — do not spend effort wrapping it; sequence Phase D first if convenient.
- [x] Keep authorization and `InputValidator.Validate` **outside** the retried delegate so they aren't re-executed per attempt (matches `PlayerLifecycleService`).
- [x] Ensure `AcquireTeamMutationLockAsync` is still called inside the transaction on every attempt.
- [x] Verify the `DbUpdateConcurrencyException` catch still wraps only `SaveChangesAsync`/`CommitAsync` and returns a 409 rather than being swallowed by the strategy.
- [x] Extract a shared `ExecuteWithFreshContextAsync` helper if the two services duplicate more than a few lines.

### Verification Plan

- The Phase A scratch test (archive via `RetryingTenantDbContextFactory`) now PASSES instead of throwing.
- `dotnet test --project Nova.Unit.Tests\Nova.Unit.Tests.csproj --filter-class "*TeamManagementServiceTests"` → all pass (SQLite path unaffected).
- `dotnet test --project Nova.Unit.Tests\Nova.Unit.Tests.csproj --filter-class "*ArchivalLifecycleServiceTests"` → all pass.
- Full Phase G retry tests are the authoritative check; run them again after Phase G lands.

### Phase Summary

Complete. `TeamLifecycleService.TransitionAsync` was split into an auth/validation shell plus a new
private `ApplyTransitionAsync(NovaDbContext db, ...)` that runs inside
`CreateExecutionStrategy().ExecuteAsync` with a **fresh context per attempt** (created from
`IDbContextFactory<NovaDbContext>` inside the delegate). `TeamManagementService.UpdateAsync` and
`CreateAsync` got the same treatment via two shared `ExecuteWithFreshContextAsync` overloads (one
plain, one taking a `verifySucceeded` probe for idempotent create).

Key decisions: authorization and `InputValidator.Validate` stay outside the retried delegate so they
run exactly once; `AcquireTeamMutationLockAsync` (and `AcquireClubRosterMutationLockAsync` on create)
are re-acquired inside the transaction on every attempt; the `DbUpdateConcurrencyException` catch
still wraps only `SaveChangesAsync`/`CommitAsync` and returns a 409.

Verified: `Nova.Integration.Tests/Data/TeamLifecycleRetryTests.cs` (written in Phase A, failing by
design with `The configured execution strategy 'NpgsqlRetryingExecutionStrategy' does not support
user-initiated transactions`) now passes 2/2 against real Postgres in 26s. Unit suite shows no
regressions.

## Phase D: Unify the domain rule, drop dead surface, add integrity

Status: Complete

Suggested executor: orchestrator

Consult `.github/skills/add-domain-persistence` and `functional-core.instructions.md`.

**D1 — extract the policy**

- [x] Add `Nova/Features/Teams/TeamGraduationYearPolicy.cs`: a pure static policy deciding whether a set of active placements blocks a proposed team graduation year, mirroring `Nova/Features/Players/PlayerGraduationYearPolicy.cs` in shape and naming.
- [x] Rule: block when `Player.GraduationYear < proposedTeamGraduationYear` for placements that are `PlacementOutcome.Assigned` in a `CampaignStatus.Active` campaign. Team year is a **minimum**.
- [x] `TeamManagementService.UpdateAsync` loads candidate placements via EF, then calls the policy for the decision. No EF or auth inside the policy.
- [x] Add `Nova.Unit.Tests/Features/Teams/TeamGraduationYearPolicyTests.cs` with pure cases: no placements, all eligible, some ineligible, lowering the year (never blocks), unchanged year (short-circuit), non-Assigned outcomes ignored, non-Active campaigns ignored.

**D2 — unify the blocker wire shape**

- [x] Delete `TeamManagementService.BuildBlockerErrors`; emit blockers through `TeamLifecycleProblemExtensions.CreateGraduationYearBlockerExtensions` so they land in ProblemDetails `extensions`.
- [x] Update `Nova.Client/Services/HttpTeamManagementService.cs` to parse blockers from extensions.
- [x] Update `Nova.UI/Features/Teams/**` blocker rendering and `Nova.Unit.Tests/Teams/HttpTeamManagementServiceTests.cs` for the new shape.

**D3 — remove the dead cutoff endpoint**

- [x] Remove the `MapPut(TeamEndpoints.UpdateGraduationYearRelative, ...)` mapping and `UpdateGraduationYearHandler` from `TeamLifecycleEndpointRouteBuilderExtensions.cs`.
- [x] Remove `UpdateGraduationYearAsync` from `ITeamLifecycleService`, `TeamLifecycleService`, and `Nova.Client/Services/HttpTeamLifecycleService.cs`.
- [x] Remove `Nova.Shared/Teams/UpdateTeamGraduationYearInput.cs` and the `UpdateGraduationYearRelative` / `UpdateGraduationYearUrl` members of `TeamEndpoints`.
- [x] Keep `TeamGraduationYearBlockerItem` and `TeamLifecycleProblemExtensions` — still used by `UpdateAsync`.
- [x] Prune the corresponding cases in `Nova.Unit.Tests/Teams/HttpTeamLifecycleServiceTests.cs`, `TeamLifecycleEndpointTests.cs`, `TeamInputValidationTests.cs`, and `Nova.Integration.Tests/Http/TeamLifecycleHttpTests.cs`.

**D4 — uniqueness and creation idempotency**

- [x] Add `CreationOperationId` (`Guid`) to `TeamEntity`, mirroring `PlayerEntity`.
- [x] `TeamEntityConfiguration`: unique index on `(ClubId, Name, GraduationYear)` plus the tenant-scoped unique index on `(ClubId, CreationOperationId)` that the idempotency check needs.
- [x] `TeamManagementService.CreateAsync`: generate the operation id before the first attempt, run inside `ExecuteWithFreshContextAsync` with a `verifySucceeded` that reconstructs the created `TeamDto`, exactly as `PlayerManagementService` does.
- [x] Map unique-name violations to a 409 with a clear message ("A team with that name and graduation year already exists.") in both `CreateAsync` and `UpdateAsync`.
- [x] Add an incremental EF migration (`dotnet ef migrations add AddTeamUniquenessAndCreationOperationId --project Nova`). Additive only; no backfill.
- [x] Add unit tests: duplicate name+year rejected on create, duplicate name+year rejected on update, same name allowed under a different graduation year, and name uniqueness scoped per club.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests\Nova.Unit.Tests.csproj --filter-class "*TeamGraduationYearPolicyTests"` → all pass.
- `dotnet test --project Nova.Unit.Tests\Nova.Unit.Tests.csproj --filter-class "*TeamManagementServiceTests"` → all pass, including the new duplicate-name cases.
- `dotnet build Nova.sln` → 0 errors; confirms every reference to the removed cutoff API is gone.
- `grep -rn "UpdateGraduationYear" .` → no hits outside this plan file and git history.
- `dotnet ef migrations list --project Nova` → the new migration appears last.

### Phase Summary

Complete, all four sub-parts.

**D1** — added `Nova/Features/Teams/TeamGraduationYearPolicy.cs`: a pure static policy over
`TeamAssignedPlacementFacts` returning `TeamGraduationYearMayChange` or
`TeamGraduationYearEditBlocked`. Rule: block when `PlayerGraduationYear < proposedGraduationYear`;
blockers are ordered by placement id for a deterministic payload. `TeamManagementService.UpdateAsync`
loads Assigned placements in Active campaigns via EF and then calls the policy. Covered by
`Nova.Unit.Tests/Features/Teams/TeamGraduationYearPolicyTests.cs` (5 pure cases); the
outcome/campaign-status filters are covered at the service level since they are EF query concerns.

**D2** — deleted `BuildBlockerErrors`; blockers now ship through
`TeamLifecycleProblemExtensions.CreateGraduationYearBlockerExtensions` into ProblemDetails
`extensions`. `HttpTeamManagementService` needed no change (it forwards `ProblemDetails` intact). The
UI now reads them with `problem.TryGetGraduationYearBlockers(...)`, which let ~150 lines of manual
key-parsing helpers (`ExtractCutoffBlockers`, `TryParseBlockerKey`, `TryParseLong`, `TryParseInt`,
`TeamCutoffBlockerBuilder`) be deleted from `Teams.razor.cs`.

**D3** — removed the dead `PUT /api/teams/{id}/graduation-year` surface end to end: route mapping and
handler, `ITeamLifecycleService`/`TeamLifecycleService`/`HttpTeamLifecycleService` members, the
`TeamEndpoints` template/relative/url constants, and `UpdateTeamGraduationYearInput.cs`. Affected
tests pruned. `git grep UpdateGraduationYear` now returns nothing outside this plan.

**D4** — added nullable `CreationOperationId` to `TeamEntity`; `TeamEntityConfiguration` now declares
a unique `(ClubId, Name, GraduationYear)` index and a filtered unique `(ClubId, CreationOperationId)`
index (`IX_Teams_ClubId` already existed as the FK index). `CreateAsync` generates the operation id
before the first attempt and passes `VerifyTeamCreationAsync` as `verifySucceeded`. Duplicate names
map to a 409 via an explicit in-transaction `TeamNameExistsAsync` probe, with `IsUniqueViolation` as
a text-based backstop that works under both Npgsql and the SQLite test harness. Migration
`20260802001653_AddTeamUniquenessAndCreationOperationId` is additive only (add column + two indexes),
no backfill. Four duplicate-name tests added to `TeamManagementServiceTests`.

Verified: `dotnet build Nova.slnx` → 0 errors. Unit suite → 743 total, 3 failed (only the known
pre-existing `PlayerDetail_*` DI failures targeted by Phase G).

## Phase E: Fix team-detail placement ordering

Status: Complete

Suggested executor: sub-agent w/ smaller model (well-specified, single file plus one test)

`TeamDetailQueryService` orders active-campaigns-first in SQL before `Take(100)`, then re-sorts in
memory *without* the active-first key, so the returned order contradicts the truncation order.

- [x] Write a failing test first: a team with a mix of Active and non-Active campaign placements where the newest campaign is not the Active one; assert Active placements sort first.
- [x] Add the `OrderByDescending(row => row.CampaignStatus == CampaignStatus.Active)` key to the in-memory sort so it matches the SQL ordering, keeping the existing `CampaignStartDate` → `CampaignId` → `PlayerDisplayName` → `PlayerId` tiebreakers.
- [x] Confirm `ActivePlacementImpacts` (derived from the truncated page) and `ActivePlacementImpactTotalCount` (a separate full count) are documented as intentionally independent, and that `IsPlacementHistoryTruncated` still reflects the history total.

### Verification Plan

- New ordering test FAILS before the fix and PASSES after.
- `dotnet test --project Nova.Unit.Tests\Nova.Unit.Tests.csproj --filter-class "*TeamDetail"` → all pass.

### Phase Summary

Complete (delegated to a `general-purpose` sub-agent on `claude-sonnet-4.6`, scoped to two files).
The agent wrote the failing ordering test first and confirmed the failure reason, then added the
missing `OrderByDescending(row => row.CampaignStatus == CampaignStatus.Active)` leading key to the
in-memory sort in `TeamDetailQueryService`, so the returned order now matches the SQL order used for
`Take(100)` truncation. Existing `CampaignStartDate` → `CampaignId` → `PlayerDisplayName` →
`PlayerId` tiebreakers were preserved.

XML docs were expanded to state that `ActivePlacementImpacts` (derived from the truncated page) and
`ActivePlacementImpactTotalCount` (a separate unbounded count) are intentionally independent.

Verified: 4/4 tests pass in the team-detail query-service test class, including the new ordering
regression test.

## Phase F: Low-risk polish

Status: Complete

Suggested executor: sub-agent w/ smaller model

- [x] `POST /api/teams` returns a real `Location` header: replace `TypedResults.Created((string?)null, team)` with `CreatedAtRoute` targeting the `GetTeamDetail` route (per `.github/skills/add-api-endpoint`).
- [x] `TeamRosterQueryService`: escape `%`, `_`, and `\` in the user search term before the `EF.Functions.ILike` pattern, and apply the same escaping to the SQLite `ToUpper().Contains` fallback path where relevant.
- [x] Add unit tests: a search for `50%` matches only literal `50%` names; a search for `a_b` does not match `axb`.
- [x] Add an endpoint metadata test asserting the create response exposes a `Location` header.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests\Nova.Unit.Tests.csproj --filter-class "*TeamRoster"` → all pass, including the new escaping cases.
- `dotnet test --project Nova.Unit.Tests\Nova.Unit.Tests.csproj --filter-class "*TeamManagement"` → all pass.

### Phase Summary

Complete (delegated to a `general-purpose` sub-agent on `claude-sonnet-4.6`, then hardened by the
orchestrator).

**Location header** — added `TeamEndpoints.GetDetailRouteName = "GetTeamDetail"` as the single source
of truth, and changed the create handler from `TypedResults.Created((string?)null, team)` to
`TypedResults.CreatedAtRoute(team, TeamEndpoints.GetDetailRouteName, new { teamId = team.TeamId })`.
The target route already carried `.WithName("GetTeamDetail")`. A new
`Nova.Unit.Tests/Teams/TeamManagementEndpointTests.cs` asserts the detail endpoint really exposes
`IEndpointNameMetadata.EndpointName == "GetTeamDetail"`, so the route name `CreatedAtRoute` depends on
cannot silently disappear.

**LIKE escaping** — added a private `EscapeLikePattern` helper (escaping `\` first, then `%` and `_`)
and switched the Npgsql branch to the three-argument
`EF.Functions.ILike(team.Name, $"%{escapedSearch}%", @"\")` overload so Postgres emits
`ILIKE … ESCAPE '\'`. The SQLite fallback needed no change: `ToUpper().Contains(...)` is already a
literal substring match.

**Verification gap found and closed.** The sub-agent's three new SQLite unit tests in
`TeamRosterQueryServiceTests` pass both before *and* after the fix, because the harness provider
cannot reproduce a Postgres-only bug — so they did not actually prove anything. The orchestrator added
`TeamRosterHttpTests.GetRoster_Search_TreatsLikeMetacharactersAsLiterals`, which runs against real
Postgres and searches for `50%` and `a_b` against seeded `50% Wins` / `50 Losses` / `a_b Squad` /
`axb Squad` teams. This test was confirmed to **fail** with the escaping reverted and **pass** with it
restored. Treat the HTTP test, not the unit tests, as the regression anchor here.

## Phase G: Restore green tests and reach Postgres parity

Status: Complete

Suggested executor: sub-agent w/ smaller model for the red-test fix and HTTP test scaffolding;
orchestrator reviews the retry tests

Consult `.github/skills/nova-testing` and `.github/instructions/testing.instructions.md`.

**G1 — fix the three red unit tests (pre-existing, from the Players epic)**

- [x] In `Nova.Unit.Tests/Players/PlayerComponentsTests.cs`, the three `PlayerDetail_*` tests near lines 342, 360, and 393 register only `detailService`; `PlayerDetail` also requires `IPlayerManagementService`. Register a `Substitute.For<IPlayerManagementService>()` in each (or route them through the existing shared setup helper at ~line 416).

**G2 — team retry / execution-strategy coverage (Postgres)**

- [x] Add `Nova.Integration.Tests/Data/TeamLifecycleRetryTests.cs` modeled on `PlayerLifecycleRetryTests`, reusing `RetryingTenantDbContextFactory` and `FailFirstSaveChangesInterceptor`: assert archive succeeds after one transient failure, that the interceptor failed exactly once, and that a fresh context was created per attempt.
- [x] Add `Nova.Integration.Tests/Data/TeamManagementRetryTests.cs` modeled on `PlayerManagementRetryTests`: cover retried `UpdateAsync` and the ambiguous-commit path for `CreateAsync` (verifying `CreationOperationId` prevents a duplicate team).

**G3 — authenticated HTTP parity**

- [x] Extend `Nova.Integration.Tests/Http/TeamLifecycleHttpTests.cs` beyond anonymous-401: admin archives a team (204), archive blocked by an active-campaign placement returns 409 with blocker extensions, restore returns 204, and a non-admin club member gets 403.
- [x] Extend `TeamRosterHttpTests` and `TeamDetailHttpTests` with authenticated admin and non-admin club-member reads.
- [x] Add cross-club tenant isolation cases: a Club A admin gets 404 (not 403) for a Club B team on detail, update, archive, and restore; Club B teams never appear in Club A's roster.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests\Nova.Unit.Tests.csproj` → `failed: 0`, total ≥ 729 (higher once new tests land). This is the moment the suite first goes fully green.
- `docker info` → daemon running.
- `dotnet test --project Nova.Integration.Tests\Nova.Integration.Tests.csproj` → `failed: 0`.
- `dotnet test --project Nova.Integration.Tests\Nova.Integration.Tests.csproj --filter-class "*TeamLifecycleRetryTests"` → passes, proving the Phase C fix against real Postgres.

### Phase Summary

Complete.

**G1** — the three red `PlayerDetail_*` tests (pre-existing failures inherited from the Players epic)
were fixed by a sub-agent by routing each through the file's existing
`RegisterServices(isClubAdmin: false, detailService: detailService)` helper instead of the bare
`Services.AddSingleton(detailService)`. That helper also registers `IPlayerManagementService`,
`IPlayerLifecycleService`, `IPlayerService`, and `AuthenticationStateProvider`, all of which
`PlayerDetail` takes via .NET 10 constructor injection. Three lines changed, no assertions touched.

**G2** — added `Nova.Integration.Tests/Data/TeamManagementRetryTests.cs` (5 tests): Postgres rejects
duplicate `CreationOperationId` within a club, Postgres rejects duplicate `(ClubId, Name,
GraduationYear)`, `CreateAsync` recognizes an ambiguous commit via its operation id rather than
inserting twice, and both `CreateAsync` and `UpdateAsync` retry with a fresh context after a transient
save failure. Combined with `TeamLifecycleRetryTests` from Phase C, all four team mutation paths now
have Postgres retry coverage.

**G3** — rewrote `TeamLifecycleHttpTests` from a single anonymous-401 theory into 6 authenticated
tests: archive/restore round trip returns 204 and flips `LifecycleStatus`/`ArchivedAt`; archiving a
team with active-campaign placements returns 409 with correctly shaped `archiveBlockers` extensions
and leaves the team Active; a non-admin club member gets 403 (this is the end-to-end proof of the
Phase B ClubAdmin narrowing); and cross-club access returns 404 rather than 403 for archive, restore,
detail, and update, with the roster never leaking the other club's teams. Added
`TeamRosterHttpTests.GetRoster_ReturnsRows_ForNonAdminClubMember` to prove the Phase B
`RequireClubMember` read policy. The shared club name in that file was made unique so multiple tests
can each create a club.

Verified: unit suite **747 total / 0 failed / 0 skipped** — the first fully green run, versus 729
total with 3 failures on `main`. Integration suite **104 total / 0 failed / 0 skipped**.

## Phase H: Browser validation of the teams UI

Status: Complete

Suggested executor: sub-agent w/ smaller model, driven by the `aspire-playwright-validation` skill

Invoke the `aspire-playwright-validation` skill and discover the app URL from the running AppHost
rather than hardcoding a port.

- [x] Start the AppHost (`aspire start` or the skill's documented flow) and resolve the Nova web URL.
- [x] Sign in as a ClubAdmin. Load `/teams` and assert the roster renders with Create/Edit/Archive controls visible.
- [x] Create a team through the form; assert it appears in the roster with the correct graduation year.
- [x] Edit the team's name and graduation year; assert the change persists after reload.
- [x] Attempt a graduation-year change blocked by an active placement; assert the blocker list renders with campaign/player detail and the team is unchanged.
- [x] Attempt to archive a team with an active-campaign placement; assert the archive-blocked dialog lists the blocking campaigns and the team stays Active.
- [x] Archive an unblocked team, switch the roster filter to Archived, assert it appears there and not under Active; then restore it and assert the reverse.
- [x] Exercise the name search and graduation-year filters, including a search term containing `%`, to confirm the Phase F escaping behaves in the real UI.
- [x] Navigate to `/teams/{id}`; assert profile, active placement impacts, and placement history render with Active placements listed first (Phase E).
- [x] Sign in as a non-admin club member; assert `/teams` and `/teams/{id}` still load read-only with no Create/Edit/Archive controls.
- [x] Sign in as a global `Admin` who is not a ClubAdmin; assert management controls are absent and mutation attempts are refused (Phase B).
- [x] Stop the AppHost and clean up any test data or scratch scripts.

### Verification Plan

- Every scenario above completes with its stated assertion; capture pass/fail per scenario in the Phase Summary.
- No unhandled browser console errors on `/teams` or `/teams/{id}`.
- `aspire` processes and containers are stopped afterwards (`docker ps` shows no leftover Nova test containers).

### Phase Summary

Ran the AppHost in isolated mode, discovered the web URL from `aspire describe`, and drove all
twelve scenarios through Playwright. **Result: 12/12 PASS.** No unhandled browser console errors on
either `/teams` or `/teams/{id}`. The AppHost was stopped afterwards and no Nova containers remain.

| # | Scenario | Result |
| --- | --- | --- |
| 1 | ClubAdmin loads `/teams`; roster renders with Create/Edit/Archive | PASS |
| 2 | Create a team through the form | PASS |
| 3 | Edit name and graduation year; persists after reload | PASS |
| 4 | Graduation-year change blocked by an active placement; blockers render, team unchanged | PASS |
| 5 | Archive blocked by an active-campaign placement; blocking campaigns listed, team stays Active | PASS |
| 6 | Archive an unblocked team; Archived filter shows it, Active does not | PASS |
| 7 | Restore the archived team; reverse holds | PASS |
| 8 | Name search and graduation-year filters | PASS |
| 9 | Search term containing `%` treated literally (Phase F) | PASS |
| 10 | `/teams/{id}` renders profile, impacts, and history with Active placements first (Phase E) | PASS |
| 11 | Non-admin club member sees `/teams` and `/teams/{id}` read-only, no management controls | PASS |
| 12 | User without ClubAdmin sees no management controls; mutations refused (Phase B) | PASS |

**Bug found during this phase (not in the original ten findings).** `ErrorMessage="_formError"`
passed the **literal string** `_formError` as the parameter value instead of the backing field, so a
user hitting a server-side conflict saw the text `_formError` rather than the real error message.
This is a pre-existing defect, not something this remediation introduced.

Fixed at five sites:

- `Nova.UI/Features/Teams/Pages/Teams.razor` — create form and edit form
- `Nova.UI/Features/Teams/Pages/TeamDetail.razor` — edit form
- `Nova.UI/Features/Players/Pages/PlayerDetail.razor` — edit form
- `Nova.UI/Features/Players/Pages/Players.razor` — create form and edit form (`@_mutationError`)

The three Players occurrences were outside the browser pass; I found them by grepping for the same
bug class after the sub-agent reported the Teams ones.

Added the regression test `Teams_ShowsServerErrorText_WhenUpdateReturnsConflict` in
`Nova.Unit.Tests/Teams/TeamComponentsTests.cs`, which asserts the real conflict text renders **and**
that the markup does not contain `_formError`. I proved it is a real guard by re-introducing the bug
in `Teams.razor` and confirming the test fails, then restoring the fix and confirming it passes.

Verified after the fixes: unit **748 total / 0 failed / 0 skipped**, integration **104 total /
0 failed / 0 skipped**.

## Phase I: Land the change and update GitHub

Status: Complete

Suggested executor: orchestrator

- [x] Re-run both test suites from clean and confirm zero failures.
- [x] Commit with the `Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>` trailer.
- [x] Open one PR from `eruvalca-team-epic-remediation` describing all ten findings and their resolutions, explicitly calling out the two behavior changes: global `Admin` is no longer a club operator, and `PUT /api/teams/{id}/graduation-year` is removed.
- [x] Comment on issue #8 summarizing the remediation PR and noting the removed cutoff endpoint from #43.
- [x] Note in the PR that `Teams.razor.cs` (1017 lines) and `TeamDetail.razor.cs` (633 lines) were deliberately left un-refactored as accepted follow-up debt.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests\Nova.Unit.Tests.csproj` → `failed: 0`.
- `dotnet test --project Nova.Integration.Tests\Nova.Integration.Tests.csproj` → `failed: 0`.
- `dotnet build Nova.slnx` → 0 errors, no new warnings introduced by this work.
- `gh pr view --json url,state` → PR exists and is open.
- `gh issue view 8 --repo eruvalca/Nova --comments` → the remediation comment is present.

### Phase Summary

Landed as two commits on `eruvalca-team-epic-remediation`:

- `0caebb4` — Remediate team management epic findings (44 files, +3830/−570), covering Phases B–G.
- `e10a312` — Fix ErrorMessage parameter binding in Teams and Players forms (6 files), covering the Phase H bug.

Final verification: unit **748 total / 0 failed / 0 skipped**, integration **104 total / 0 failed /
0 skipped**, solution builds clean.

- PR: https://github.com/eruvalca/Nova/pull/53
- Issue comment: https://github.com/eruvalca/Nova/issues/8#issuecomment-5154343080

The PR body carries the full findings table, both behavior changes, the migration note, and the
accepted follow-up debt. The issue comment leads with the two breaking changes so they are visible
from the epic without opening the PR.

## Final Recap

All nine phases complete. Ten findings from the epic review were verified, fixed, and covered by
tests, plus an eleventh bug found during browser validation.

**What actually shipped:**

- **Retry safety (finding 1, the only true blocker).** `TeamManagementService` and
  `TeamLifecycleService` no longer call `BeginTransactionAsync` directly. Both run inside
  `CreateExecutionStrategy().ExecuteAsync` with a fresh `DbContext` per attempt. This was a runtime
  failure on every team mutation against Postgres, not a theoretical concern.
- **Authorization narrowing (finding 4).** Global `Roles.Admin` is no longer a club operator
  anywhere: provider, policy, and UI gates all require `Roles.ClubAdmin`. Teams read standardized on
  `Policies.RequireClubMember` (finding 7).
- **Domain consolidation (findings 5, 6).** The graduation-year rule lives in the pure
  `TeamGraduationYearPolicy`; the duplicate implementation and the dead
  `PUT /api/teams/{id}/graduation-year` endpoint were removed end to end.
- **Data integrity (finding 8).** Unique `(ClubId, Name, GraduationYear)` plus `CreationOperationId`
  with a filtered unique index for idempotent create.
- **Correctness polish (findings 9, 10).** Placement ordering, `CreatedAtRoute`, and LIKE
  metacharacter escaping.
- **Test health (findings 2, 3).** Three tests that were red on `main` are green, and team mutations
  now have real Postgres coverage where they previously had none.
- **UI bug (finding 11).** `ErrorMessage` bound a literal string at five sites in Teams and Players.

**Numbers.** Unit went from 729 total / 3 failed on `main` to 748 total / 0 failed. Integration:
104 total / 0 failed. Browser: 12/12 scenarios pass.

**Lessons worth carrying forward:**

1. **Make the bug fail first.** For findings 1, 10, and 11 I reproduced the failure before fixing it
   and confirmed the fix flipped it. This paid off directly on finding 10, where a sub-agent had
   reported the LIKE-escaping fix as verified — but its SQLite tests passed both before and after,
   because SQLite's `Contains` is already literal. Only a Postgres test could prove it. Always ask
   whether the harness provider can actually reproduce the bug you think you are fixing.
2. **Grep for the bug class, not the bug.** The `ErrorMessage="_formError"` defect surfaced in Teams
   during the browser pass; searching for the same pattern found three more live occurrences in
   Players that no one was looking for.
3. **Check the baseline before blaming your change.** `main` was already red. Knowing that up front
   avoided chasing three failures that had nothing to do with this work.

**Deliberately not done:** `Teams.razor.cs` (1017 lines) and `TeamDetail.razor.cs` (633 lines) are
too large and were left un-refactored as accepted follow-up debt. Players intentionally keeps its
indexed-errors ProblemDetails shape rather than the extensions shape teams now use; unifying them is
a separate decision.

## Deployment Plan

**Migration.** This change adds `20260802001653_AddTeamUniquenessAndCreationOperationId`. It is
purely additive — one nullable `CreationOperationId` column on `Teams`, plus two indexes — so it
carries no backfill and no data rewrite.

```
dotnet ef database update --context NovaDbContext --project Nova
```

**The one real risk.** The unique index on `(ClubId, Name, GraduationYear)` will **fail to create**
if any target database already contains duplicate teams. This was written against a pre-production
database where that was known not to be the case. Before applying to any environment with real data,
check first:

```sql
SELECT "ClubId", "Name", "GraduationYear", COUNT(*)
FROM "Teams"
GROUP BY "ClubId", "Name", "GraduationYear"
HAVING COUNT(*) > 1;
```

If that returns rows, they must be reconciled before the migration will apply.

**Breaking changes to announce.**

1. `PUT /api/teams/{id}/graduation-year` is gone. It had no in-repo caller, but any external consumer
   will get a 404. Graduation-year changes go through the normal team update.
2. Users holding only the global `Admin` role lose club management access. If anyone was relying on
   `Admin` to operate on club data, they need an explicit `ClubAdmin` role assignment before this
   deploys.

**Rollback.** The code rolls back cleanly by reverting the two commits. The migration does not need
to be reverted alongside it — the added column is nullable and the indexes are compatible with the
previous code. If you do want to unwind it, `dotnet ef database update` to the prior migration drops
the column and both indexes without data loss beyond `CreationOperationId` values.

**Post-deploy checks.**

1. Create, edit, archive, and restore a team against the real database — this is the path that was
   throwing at runtime before finding 1 was fixed.
2. Confirm a `ClubAdmin` sees management controls on `/teams` and a non-admin club member does not.
3. Confirm a team search containing `%` returns literal matches rather than wildcard matches.

