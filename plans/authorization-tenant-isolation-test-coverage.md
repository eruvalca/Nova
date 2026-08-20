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

Status: Complete <!-- Not started | In progress | Complete -->

- [x] Produce a write-endpoint × assertion matrix (endpoint → policy → 401 anon / 403 non-admin /
      cross-tenant 404 non-disclosing / no-mutation / SQLite unit guard / Postgres-backed HTTP) for all
      write endpoints: clubs (create, join request create/cancel, approve/reject, assign admin), players
      (create/update/archive/restore), teams (create/update/archive/restore), tags (create/update/archive/
      restore), campaigns (create, metadata PUT, season metadata PUT, placement PUT, tag application
      apply/remove, evaluation note add/edit/delete, close, reopen), dashboard entry (summary/activity +
      club-admin page gating).
- [x] Confirm the known starting inventory (from pre-plan analysis) and mark verified/absent per cell:

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
- [x] Record the completed matrix in this phase's **Phase Summary** so later phases consume it directly.

### Verification Plan

- The matrix is complete: every write endpoint row has a verdict per assertion column, backed by grep/file
  evidence. No runnable command; artifact is the Phase Summary itself.

### Phase Summary

Produced the write-endpoint × assertion matrix from the pre-plan inventory plus a fresh read of the
service shells, endpoint mappers, and HTTP test files. Every cell is now backed by an existing test or a
test added in Phases 2–4. The matrix lists only the *write* endpoints in scope; read-only surfaces
(dashboard summary/activity, admin pages, roster) were re-verified for their existing 401/403/isolation
coverage and left untouched.

| Endpoint | 401 anon | 403 non-admin | 404 cross-tenant (non-disclosing) | no-mutation | SQLite unit | Postgres HTTP |
|---|---|---|---|---|---|---|
| Club create | ✓ | — (any user) | n/a | — | — | existing |
| Join request create | ✓ | n/a | n/a | — | `ClubJoinRequestServiceTests` | existing |
| Join request approve/reject | ✓ (policy) | ✓ (policy) | ✓ 404 | ✓ | `ClubJoinRequestServiceTests` | `ClubAdminSurfacesHttpTests` (new) |
| Assign ClubAdmin | ✓ (policy) | ✓ (policy) | ✓ 404 **production fix** | ✓ | `ClubMemberServiceTests` (updated) | `ClubAdminSurfacesHttpTests` (new) |
| Cancel join request | ✓ (policy) | 403 own-only | ✓ 404-unknown | ✓ | `ClubJoinRequestServiceTests` | `ClubAdminSurfacesHttpTests` (new) |
| Player create | ✓ | ✓ | n/a | — | `PlayerManagementServiceTests` | `PlayerManagementHttpTests` |
| Player update | ✓ | ✓ (new) | ✓ 404 | ✓ | `PlayerManagementServiceTests` | `PlayerManagementHttpTests` (new) |
| Player archive/restore | ✓ | ✓ | ✓ 404 | — | existing | existing |
| Team create | ✓ | ✓ (new) | n/a | — | `TeamManagementServiceTests` | `TeamManagementHttpTests` (new) |
| Team update | ✓ (new) | ✓ (new) | ✓ 404 | ✓ | `TeamManagementServiceTests` (new) | `TeamManagementHttpTests` (new) |
| Team archive/restore | ✓ | ✓ | ✓ 404 | — | existing | existing |
| Tag create | ✓ | ✓ | n/a | — | `TagDefinitionServiceTests` | existing |
| Tag update | ✓ | ✓ (new) | ✓ 404 (new) | ✓ | `TagDefinitionServiceTests` (new) | `TagDefinitionHttpTests` (new) |
| Tag archive/restore | ✓ | ✓ (restore new) | ✓ 404 (new) | ✓ | `TagDefinitionLifecycleServiceTests` (new) | `TagDefinitionHttpTests` (new) |
| Campaign create | ✓ | ✓ | ✓ 404-season | — | existing | existing |
| Campaign metadata PUT | ✓ (new) | ✓ (new) | ✓ 404 (new) | ✓ (new) | existing | `CampaignMetadataHttpTests` (new) |
| Season metadata PUT | ✓ (new) | ✓ (new) | ✓ 404 (new) | ✓ (new) | existing | `CampaignMetadataHttpTests` (new) |
| Placement PUT | ✓ | ✓ | ✓ 404 | — | existing | existing |
| Tag application apply/remove | ✓ | ✓ | ✓ 404 | — | existing | existing |
| Evaluation note add/edit/delete | ✓ | ✓ | ✓ 404 | — | existing | existing |
| Close/reopen | ✓ | ✓ | ✓ 404 | ✓ | existing | existing |
| Dashboard entry (summary/activity + admin gating) | ✓ | ✓ | ✓ | — | — | existing |

**Non-disclosure audit result:** every cross-tenant identifier in scope returns **404 (not 403)** except
`AssignClubAdmin` targeting a user in another club, which previously returned 403 and disclosed the
target's existence. Applied the minimal production fix (below) and updated the unit test to assert 404.

**Production fix (user-approved policy):** `Nova/Features/Account/ClubMemberService.cs:92-97` — the
cross-club target check in `AssignClubAdminAsync` now returns `ServiceProblem.NotFound` instead of
`ServiceProblem.Forbidden`.

**Out-of-scope observation (not fixed; owned by #119):** `ClubAdminService.DemoteClubAdminAsync`
(`Nova/Features/Clubs/ClubAdminService.cs:146-150`) has the identical cross-club 403-disclosure pattern.
Demote is not part of this plan's write-endpoint matrix (it is listed under #119's broader
policy/service-shell/provider matrix), so it was left unchanged and is flagged here for #119.

## Phase 2: HTTP authorization boundaries (Postgres-backed integration suite)

Status: Complete

Suggested executor: sub-agent (smaller model) with this spec — mechanical test authoring once Phase 1
fixes the matrix. Orchestrator reviews the diff.

- [x] New `Nova.Integration.Tests/Http/CampaignMetadataHttpTests.cs`:
  - `UpdateCampaignMetadata_ReturnsUnauthorized_ForAnonymous`
  - `UpdateCampaignMetadata_ReturnsForbidden_ForClubMember` (evaluator boundary)
  - `UpdateCampaignMetadata_ReturnsOk_ForClubAdmin`
  - `UpdateCampaignMetadata_ReturnsNotFound_ForCrossTenantCampaign` — assert 404 (not 403) **and** verify
    via `fixture.CreateAdminContext()` that campaign data is unchanged
  - `UpdateSeasonMetadata_ReturnsUnauthorized_ForAnonymous` / `..._ForClubMember` / `..._ForClubAdmin` /
    `..._ReturnsNotFound_ForCrossTenantSeason` (+ no mutation)
  - Reuse `SeedingHelpers`; extend with a campaign/season seeding helper only if none fits.
- [x] `TeamManagementHttpTests`: `CreateTeam_ReturnsForbidden_ForClubMember`; `UpdateTeam_ReturnsUnauthorized_ForAnonymous`,
      `UpdateTeam_ReturnsForbidden_ForClubMember`, `UpdateTeam_ReturnsOk_ForClubAdmin` (member seeded via
      existing join-request/membership patterns in `ClubDetailAdminHttpTests`).
- [x] `PlayerManagementHttpTests`: `Update_ReturnsForbidden_ForClubMember`.
- [x] `TagDefinitionHttpTests`: `TagMutations_ReturnNotFound_ForCrossTenantTag` — other club's admin acts on
      this club's tag for update/archive/restore → 404 (non-disclosing), no mutation.
- [x] New `Nova.Integration.Tests/Http/ClubAdminSurfacesHttpTests.cs` (grew past `ClubDetailAdminHttpTests`):
      approve/reject non-admin member → 403; approve/reject cross-tenant request id → 404 non-disclosing +
      no mutation; `AssignClubAdmin` 401/403/404/200 boundary; `CancelJoinRequest` 204-own/401/404-unknown
      boundary; admin join-requests GET 403 for member (confirmed missing by inventory).
- [x] If any of the above exposes incorrect production behavior, apply the minimal fix (user-approved
      policy), add/keep the test, and note the production change explicitly for the PR description.

### Verification Plan

- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignMetadataHttpTests"`
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*TeamManagementHttpTests"`
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*PlayerManagementHttpTests"`
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*TagDefinitionHttpTests"`
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*ClubAdminSurfacesHttpTests"`
- Expected: all pass against the Aspire-hosted Postgres app. (AppHost boot is the suite's normal startup.)

### Phase Summary

Added the missing HTTP boundary tests. All pass against the Aspire-hosted PostgreSQL app.

- **`CampaignMetadataHttpTests.cs` (new, 8 tests)** — campaign/season metadata PUT for anonymous (401),
  club-member (403), club-admin (200), and cross-tenant (404 non-disclosing + no-mutation verified via
  `fixture.CreateAdminContext()`). Added `SeedingHelpers.SeedSeasonAndCampaignAsync` (returns
  `SeededSeasonAndCampaign`) for participant-less season/campaign seeding.
- **`TeamManagementHttpTests.cs` (+4 tests)** — create 403-for-member, update 401/403/200.
- **`PlayerManagementHttpTests.cs` (+1 test)** — `Update_ReturnsForbidden_ForClubMember`.
- **`TagDefinitionHttpTests.cs` (+1 test)** — `TagMutations_ReturnNotFound_ForCrossTenantTag` (update/
  archive/restore → 404, tag still `Active` and name unchanged).
- **`ClubAdminSurfacesHttpTests.cs` (new, 10 tests)** — approve/reject non-admin 403; approve/reject
  cross-tenant 404 (request stays `Pending`); `AssignClubAdmin` 401/403/404/200; `CancelJoinRequest`
  204-own/401/404-unknown; admin join-requests GET 403-for-member.

The `AssignClubAdmin` cross-club 404 test exposed the disclosure bug described in Phase 1; the minimal
production fix is applied and documented there.

## Phase 3: SQLite service-shell authorization gaps (unit suite)

Status: Complete

Suggested executor: sub-agent (smaller model) with this spec — small, well-scoped unit additions.

- [x] `Features/Teams/TeamManagementServiceTests.cs`: `Update_ReturnsForbidden_ForNonAdmin` (mirror the
      existing `Create_ReturnsForbidden_ForNonAdmin` pattern).
- [x] `Features/Tags/TagDefinitionServiceTests.cs`: `Update_ReturnsForbidden_ForNonAdmin`.
- [x] `Features/Tags/TagDefinitionLifecycleServiceTests.cs`: `Restore_ReturnsForbidden_ForNonAdmin`.
- [x] `Features/Players/PlayerManagementServiceTests.cs`: cross-club enrollment isolation — seed an active
      campaign in another club, create a player as Club A admin, assert no new assignment rows in Club B's
      campaign (added a dedicated `Create_DoesNotEnrollInOtherClubsActiveCampaign` test).
- [x] Any additional guard gaps surfaced by the Phase 1 matrix.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*TeamManagementServiceTests"`
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*TagDefinitionServiceTests"`
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*TagDefinitionLifecycleServiceTests"`
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*PlayerManagementServiceTests"`
- Expected: all pass on the in-memory SQLite harness.

### Phase Summary

Added the missing service-shell authorization guards on the in-memory SQLite harness.

- `TeamManagementServiceTests.Update_ReturnsForbidden_ForNonAdmin` — evaluator (club member,
  `IsClubAdmin = false`) is denied before the team lookup.
- `TagDefinitionServiceTests.Update_ReturnsForbidden_ForNonAdmin` — same evaluator boundary for tag edits.
- `TagDefinitionLifecycleServiceTests.Restore_ReturnsForbidden_ForNonAdmin` — same for tag restore.
- `PlayerManagementServiceTests.Create_DoesNotEnrollInOtherClubsActiveCampaign` — seeds an active campaign
  in Club B, creates a player as Club A admin, and asserts no `PlayerCampaignAssignment` rows reference
  Club B's campaign (the tenant filter scopes enrollment to the caller's club).
- `Account/ClubMemberServiceTests.AssignClubAdminAsync_ReturnsNotFound_WhenTargetInDifferentClub` — updated
  from `ReturnsForbidden` to assert the non-disclosing 404 after the production fix (see Phase 1).

No additional guard gaps surfaced beyond the Phase 1 matrix. The full unit suite (1,712 tests) is green.

## Phase 4: Campaign workflow tenant-isolation assertions

Status: Complete

Suggested executor: sub-agent (smaller model) for the mechanical additions; orchestrator arbitrates any
production-fix decision.

- [x] Walk the six workflows against the Phase 1 matrix and confirm existing coverage (do not duplicate):
      creation, late-player enrollment, evaluation (notes + tag application), placement, close, reopen —
      each must have a cross-tenant read (not visible) and write (rejected, no mutation) assertion on the
      SQLite harness **and** a Postgres-backed assertion.
- [x] Late-player enrollment: add the Postgres cross-club isolation assertion to
      `Nova.Integration.Tests/Data/PlayerEnrollmentPostgresTests.cs` (seed active campaigns in two clubs;
      create a player through Club A's tenant context; verify Club B's assignment set is unchanged), unless
      Phase 3's SQLite test plus existing concurrency tests already prove it and the matrix says Postgres is
      redundant.
- [x] Non-disclosure audit: for every cross-tenant identifier used in the workflows (campaign, season,
      assignment, team, tag, note, join request), confirm the response is 404-not-found rather than
      403-forbidden at both service and HTTP level; fix minimal production gaps per the approved policy.
- [x] Record findings + any production fixes in this phase's summary.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` (full unit run — tenancy classes:
  `*TenancyTests`, service classes from the matrix).
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*PlayerEnrollmentPostgresTests"` and the workflow-specific HTTP classes touched in Phase 2.
- Expected: all pass.

### Phase Summary

Walked the six campaign workflows against the Phase 1 matrix. Cross-tenant reads (not visible) and
writes (rejected, no mutation) were already asserted on both harnesses for: **creation**
(`CampaignCreationHttpTests` cross-tenant season 404; `TenancyTests`), **evaluation** (evaluation-note and
tag-application 404 suites), **placement** (`CampaignPlacementHttpTests` cross-tenant assignment/team 404),
**close**, and **reopen** (`CampaignLifecycleHttpTests` 401/403/404 + no mutation). No duplication was
added.

**Late-player enrollment** (the one remaining gap) now has both:
- SQLite: `PlayerManagementServiceTests.Create_DoesNotEnrollInOtherClubsActiveCampaign` (Phase 3).
- Postgres: `PlayerEnrollmentPostgresTests.PlayerCreation_DoesNotEnrollInOtherClubsActiveCampaign` (new) —
  seeds an active campaign in each of two clubs, creates a player through Club A's tenant context, and
  asserts the new player is enrolled only in Club A's campaign (exactly one assignment; never Club B's).

**Non-disclosure audit** (campaign, season, assignment, team, tag, note, join request): all cross-tenant
identifiers return **404 (not 403)** at both service and HTTP level, verified against the service shells
(`NotFound` for cross-tenant lookups) and the HTTP tests added in Phase 2. The only disclosure found was
`AssignClubAdmin` targeting a cross-club user (returns 403) — fixed in Phase 1
(`ClubMemberService.cs:92-97`) with the unit test updated to assert 404 and the HTTP test asserting 404.
No other production fixes were required.

## Phase 5: Full-suite validation on both harnesses + format gate

Status: Complete

- [x] `dotnet format Nova.slnx --verify-no-changes`; if it fails, apply `dotnet format Nova.slnx` and
      re-verify.
- [x] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full SQLite unit suite green.
- [x] `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — full Postgres/Aspire
      integration suite green (local-only; CI runs build+unit only).
- [x] `dotnet build Nova.slnx` — solution builds clean.
- [x] Fill in **Final Recap** and **Deployment Plan** below; summarize any production fixes with file:line
      references for the PR description.

### Verification Plan

- All four commands exit 0 with no skipped/errored tests attributed to this work.

### Phase Summary

All validation gates pass:

- `dotnet format Nova.slnx --verify-no-changes` → exit 0 (no changes required).
- `dotnet build Nova.slnx` → 0 warnings, 0 errors.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` → **1,712 passed, 0 failed, 0 skipped**.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` → **331 passed, 0 failed, 0
  skipped** (Aspire-hosted PostgreSQL 18).
- New/changed integration classes also verified individually:
  - `*CampaignMetadataHttpTests` → 8 passed.
  - `*ClubAdminSurfacesHttpTests` → 10 passed.
  - `*TeamManagementHttpTests` → 7 passed.
  - `*PlayerManagementHttpTests` → 8 passed.
  - `*TagDefinitionHttpTests` → 13 passed.
  - `*PlayerEnrollmentPostgresTests` → 4 passed.

## Final Recap

Filled the remaining authorization and tenant-isolation coverage gaps for issue #115 across both harnesses
with no new production policies, endpoints, or guards — except one minimal, user-approved boundary fix.

**Test additions (integration, `Nova.Integration.Tests`):**
- `Http/CampaignMetadataHttpTests.cs` (new, 8 tests) — campaign/season metadata PUT authorization and
  non-disclosing cross-tenant 404.
- `Http/ClubAdminSurfacesHttpTests.cs` (new, 10 tests) — join-request approve/reject, assign-admin,
  cancel-join-request, and admin join-requests listing boundaries.
- `Http/TeamManagementHttpTests.cs` (+4), `Http/PlayerManagementHttpTests.cs` (+1),
  `Http/TagDefinitionHttpTests.cs` (+1), `Data/PlayerEnrollmentPostgresTests.cs` (+1), and
  `Http/SeedingHelpers.cs` (new `SeedSeasonAndCampaignAsync` helper).

**Test additions (unit, `Nova.Unit.Tests`):**
- `Features/Teams/TeamManagementServiceTests.cs` (+1), `Features/Tags/TagDefinitionServiceTests.cs` (+1),
  `Features/Tags/TagDefinitionLifecycleServiceTests.cs` (+1), `Features/Players/PlayerManagementServiceTests.cs`
  (+1 cross-club enrollment isolation), and `Account/ClubMemberServiceTests.cs` (updated one test's
  expectation).

**Minimal production fix (user-approved):** `Nova/Features/Account/ClubMemberService.cs:92-97` —
`AssignClubAdminAsync` returned `Forbidden` (403) for a target in another club, disclosing the target's
existence. It now returns `NotFound` (404), matching the non-disclosing boundary used everywhere else.
The unit test was updated to assert 404 and a new HTTP test asserts 404.

**Out-of-scope note:** `ClubAdminService.DemoteClubAdminAsync` (`Nova/Features/Clubs/ClubAdminService.cs:146-150`)
has the same 403-disclosure pattern but is outside this plan's matrix (owned by #119) and was left
unchanged.

## Deployment Plan

This is primarily test-only work, so deployment is the normal PR merge flow: CI runs `dotnet build` and the
unit test suite only. The **Postgres/Aspire integration suite was run locally** and is green (331 tests);
it is not run by CI.

The one production change is a two-line status-code correction in `AssignClubAdminAsync` — a cross-club
target now returns 404 instead of 403. Blast radius is limited to the assign-admin endpoint's
cross-club failure response; no route, policy, DI, or data-shape change. Rollback is a trivial revert of
`Nova/Features/Account/ClubMemberService.cs:92-97` (and the corresponding unit-test expectation).
