# Authorization and tenant-isolation test coverage (issue #115)

Prove that administrator (ClubAdmin) and evaluator (club-member) authorization boundaries hold on every
write path and that all campaign workflows are tenant-isolated end to end, by filling the remaining test
coverage gaps on both the SQLite unit harness and the Postgres/Aspire integration harness — with no new
production policies, endpoints, or guards. If a test uncovers a genuinely incorrect production boundary
(e.g., a cross-tenant identifier disclosed as 403 instead of non-disclosing 404), apply the **minimal
production fix** to make the boundary correct and document it in the PR (user-approved policy).

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to
`Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue with
zero context); run the phase's **Verification Plan** and record the result before moving on. When all phases
are done, fill in **Final Recap** and **Deployment Plan**.

## Sibling boundaries (do not cross)

- **#116** owns workflow-journey integration scenarios (happy-path creation, enrollment, evaluation,
  placement, close, reopen). This plan adds only *boundary* assertions (401/403/404, non-disclosure, no
  mutation) — no journey-style scenarios.
- **#119** owns the broad policy/service-shell/provider matrix. Do not expand policy matrices beyond the
  specific gaps listed here.
- **#118** owns UI/browser coverage. No `Nova.Browser.Tests` changes.
- No production changes except the user-approved minimal boundary fix when a test proves incorrect behavior.

## Repo conventions in force

- xUnit v4 on Microsoft.Testing.Platform (MTP) + Shouldly; run with `dotnet test --project <project.csproj>`.
  Never pass VSTest-only flags. Filter with `--filter-class "*Name"`.
- Naming `Subject_Outcome_Condition`; one behavior per test; `[Theory(IncludeTestCaseIndex = true)]` for
  matrices; `TestContext.Current.CancellationToken` over `CancellationToken.None`; fixtures via
  primary-constructor injection (`IAsyncLifetime` with `ValueTask`).
- Integration tests: `[Collection(NovaAppHostCollection.Name)]`, seed via `IdentityHttpClientHelper`
  (real HTTP registration), `SeedingHelpers`, and `fixture.CreateAdminContext()`; never assert on global
  unfiltered counts (database is shared — each test seeds its own data).
- Unit service tests: `TenancyTestHarness` (shared in-memory SQLite, `EnsureCreated()`);
  `FakeCurrentUserProvider` (AsyncLocal-backed — parallel-safe; set `UserId`/`ClubId`/`IsClubAdmin`
  directly, no static state). "Evaluator" on this harness = club member with `IsClubAdmin = false`.
- Parallel execution is on (`ParallelMode.All`) — do not opt out without an inline reason.

## Phase 1: Coverage inventory and gap matrix

Status: Not started <!-- Not started | In progress | Complete -->

- [ ] Produce a write-endpoint × assertion matrix (endpoint → policy → 401 anon / 403 non-admin /
      cross-tenant 404 non-disclosing / no-mutation / SQLite unit guard / Postgres-backed HTTP) for all
      write endpoints: clubs (create, join request create/cancel, approve/reject, assign admin), players
      (create/update/archive/restore), teams (create/update/archive/restore), tags (create/update/archive/
      restore), campaigns (create, metadata PUT, season metadata PUT, placement PUT, tag application
      apply/remove, evaluation note add/edit/delete, close, reopen), dashboard entry (summary/activity +
      club-admin page gating).
- [ ] Confirm the known starting inventory (from pre-plan analysis) and mark verified/absent per cell:

  **Already covered** (verified pre-plan — re-check, don't duplicate):
  - Unit service guards: player create/update/archive, team create/archive, tag create/archive, campaign
    create/close/reopen/placement/metadata/season-metadata, evaluation notes, tag application, join
    requests, club-admin demote, club-member assign-admin.
  - HTTP: campaign create (401/403/404-season), close/reopen (401/403/404), placement (401/403/404
    assignment+team), tag application + evaluation notes (401/403 no-club, non-owner-non-admin 403,
    cross-tenant 404), player create/archive (403 member, 404 cross-tenant), team archive/restore
    (403/404), tag create/update/archive/restore as non-admin member (403), dashboard summary/activity
    (401/403, tenant isolation), club detail/admin page gating + roster cross-club 403.
  - Policy matrix: `Security/EvaluatorAuthorizationPolicyTests.cs`.
  - Postgres: tenancy interceptor, campaign creation/lifecycle/placement/tag-application/enrollment
    concurrency, evaluation notes.

  **Known gaps** (starting list — extend if inventory finds more):
  - Campaign metadata PUT + season metadata PUT: **zero HTTP coverage**.
  - Teams: create 403-for-member, update 401/403 at HTTP (404 covered elsewhere).
  - Players: update 403-for-member at HTTP.
  - Tags: cross-tenant tag id → 404 non-disclosing at HTTP (update/archive/restore).
  - Clubs: approve/reject non-admin 403 + cross-tenant 404 at HTTP; AssignClubAdmin — zero HTTP coverage;
    CancelJoinRequest — zero HTTP coverage; admin join-requests GET 403-for-member (verify).
  - Unit: `TeamManagementServiceTests.Update` non-admin guard; `TagDefinitionServiceTests.Update` non-admin
    guard; `TagDefinitionLifecycleServiceTests.Restore` non-admin guard; cross-club enrollment isolation in
    `PlayerManagementServiceTests` (verify existing test seeds another club's active campaign).
  - Non-disclosure audit: confirm every cross-tenant identifier returns **404 (not 403)** end to end.
    Known risk: `AssignClubAdmin` for a cross-club target currently returns Forbidden in
    `ClubMemberServiceTests` — decide via test expectation; if disclosure is real, apply the minimal fix
    (user-approved policy) and record it here.
- [ ] Record the completed matrix in this phase's **Phase Summary** so later phases consume it directly.

### Verification Plan

- The matrix is complete: every write endpoint row has a verdict per assertion column, backed by grep/file
  evidence. No runnable command; artifact is the Phase Summary itself.

### Phase Summary

_(write when phase completes)_

## Phase 2: HTTP authorization boundaries (Postgres-backed integration suite)

Status: Not started

Suggested executor: sub-agent (smaller model) with this spec — mechanical test authoring once Phase 1
fixes the matrix. Orchestrator reviews the diff.

- [ ] New `Nova.Integration.Tests/Http/CampaignMetadataHttpTests.cs`:
  - `UpdateCampaignMetadata_ReturnsUnauthorized_ForAnonymous`
  - `UpdateCampaignMetadata_ReturnsForbidden_ForClubMember` (evaluator boundary)
  - `UpdateCampaignMetadata_ReturnsOk_ForClubAdmin`
  - `UpdateCampaignMetadata_ReturnsNotFound_ForCrossTenantCampaign` — assert 404 (not 403) **and** verify
    via `fixture.CreateAdminContext()` that campaign data is unchanged
  - `UpdateSeasonMetadata_ReturnsUnauthorized_ForAnonymous` / `..._ForClubMember` / `..._ForClubAdmin` /
    `..._ReturnsNotFound_ForCrossTenantSeason` (+ no mutation)
  - Reuse `SeedingHelpers`; extend with a campaign/season seeding helper only if none fits.
- [ ] `TeamManagementHttpTests`: `CreateTeam_ReturnsForbidden_ForClubMember`; `UpdateTeam_ReturnsUnauthorized_ForAnonymous`,
      `UpdateTeam_ReturnsForbidden_ForClubMember`, `UpdateTeam_ReturnsOk_ForClubAdmin` (member seeded via
      existing join-request/membership patterns in `ClubDetailAdminHttpTests`).
- [ ] `PlayerManagementHttpTests`: `Update_ReturnsForbidden_ForClubMember`.
- [ ] `TagDefinitionHttpTests`: `TagMutations_ReturnNotFound_ForCrossTenantTag` — other club's admin acts on
      this club's tag for update/archive/restore → 404 (non-disclosing), no mutation.
- [ ] `ClubDetailAdminHttpTests` (or a new `ClubAdminSurfacesHttpTests` if it grows): approve/reject
      non-admin member → 403; approve/reject cross-tenant request id → 404 non-disclosing + no mutation;
      `AssignClubAdmin` 401/403/404/200 boundary; `CancelJoinRequest` 204-own/401/404-unknown boundary;
      admin join-requests GET 403 for member (if the inventory confirms it is missing).
- [ ] If any of the above exposes incorrect production behavior, apply the minimal fix (user-approved
      policy), add/keep the test, and note the production change explicitly for the PR description.

### Verification Plan

- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignMetadataHttpTests"`
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*TeamManagementHttpTests"`
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*PlayerManagementHttpTests"`
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*TagDefinitionHttpTests"`
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*ClubDetailAdminHttpTests"`
- Expected: all pass against the Aspire-hosted Postgres app. (AppHost boot is the suite's normal startup.)

### Phase Summary

_(write when phase completes)_

## Phase 3: SQLite service-shell authorization gaps (unit suite)

Status: Not started

Suggested executor: sub-agent (smaller model) with this spec — small, well-scoped unit additions.

- [ ] `Features/Teams/TeamManagementServiceTests.cs`: `Update_ReturnsForbidden_ForNonAdmin` (mirror the
      existing `Create_ReturnsForbidden_ForNonAdmin` pattern).
- [ ] `Features/Tags/TagDefinitionServiceTests.cs`: `Update_ReturnsForbidden_ForNonAdmin`.
- [ ] `Features/Tags/TagDefinitionLifecycleServiceTests.cs`: `Restore_ReturnsForbidden_ForNonAdmin`.
- [ ] `Features/Players/PlayerManagementServiceTests.cs`: cross-club enrollment isolation — seed an active
      campaign in another club, create a player as Club A admin, assert no new assignment rows in Club B's
      campaign (extend `Create_EnrollsPlayerInEveryActiveCampaign` or add a dedicated test).
- [ ] Any additional guard gaps surfaced by the Phase 1 matrix.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*TeamManagementServiceTests"`
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*TagDefinitionServiceTests"`
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*TagDefinitionLifecycleServiceTests"`
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*PlayerManagementServiceTests"`
- Expected: all pass on the in-memory SQLite harness.

### Phase Summary

_(write when phase completes)_

## Phase 4: Campaign workflow tenant-isolation assertions

Status: Not started

Suggested executor: sub-agent (smaller model) for the mechanical additions; orchestrator arbitrates any
production-fix decision.

- [ ] Walk the six workflows against the Phase 1 matrix and confirm existing coverage (do not duplicate):
      creation, late-player enrollment, evaluation (notes + tag application), placement, close, reopen —
      each must have a cross-tenant read (not visible) and write (rejected, no mutation) assertion on the
      SQLite harness **and** a Postgres-backed assertion.
- [ ] Late-player enrollment: add the Postgres cross-club isolation assertion to
      `Nova.Integration.Tests/Data/PlayerEnrollmentPostgresTests.cs` (seed active campaigns in two clubs;
      create a player through Club A's tenant context; verify Club B's assignment set is unchanged), unless
      Phase 3's SQLite test plus existing concurrency tests already prove it and the matrix says Postgres is
      redundant.
- [ ] Non-disclosure audit: for every cross-tenant identifier used in the workflows (campaign, season,
      assignment, team, tag, note, join request), confirm the response is 404-not-found rather than
      403-forbidden at both service and HTTP level; fix minimal production gaps per the approved policy.
- [ ] Record findings + any production fixes in this phase's summary.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` (full unit run — tenancy classes:
  `*TenancyTests`, service classes from the matrix).
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*PlayerEnrollmentPostgresTests"` and the workflow-specific HTTP classes touched in Phase 2.
- Expected: all pass.

### Phase Summary

_(write when phase completes)_

## Phase 5: Full-suite validation on both harnesses + format gate

Status: Not started

- [ ] `dotnet format Nova.slnx --verify-no-changes`; if it fails, apply `dotnet format Nova.slnx` and
      re-verify.
- [ ] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full SQLite unit suite green.
- [ ] `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — full Postgres/Aspire
      integration suite green (local-only; CI runs build+unit only).
- [ ] `dotnet build Nova.slnx` — solution builds clean.
- [ ] Fill in **Final Recap** and **Deployment Plan** below; summarize any production fixes with file:line
      references for the PR description.

### Verification Plan

- All four commands exit 0 with no skipped/errored tests attributed to this work.

### Phase Summary

_(write when phase completes)_

## Final Recap

_(write when all phases complete: summary of the entire piece of work, including any minimal production
fixes applied and why)_

## Deployment Plan

_(write when all phases complete: test-only work → deployment is the normal PR merge flow; CI runs build +
unit tests, so note that the integration suite was run locally. If production fixes were applied, call out
their blast radius and rollback notes.)_
