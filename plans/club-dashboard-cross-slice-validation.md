# Club Dashboard Cross-Slice Validation (Issue #111)

Validate behavior that crosses the completed club-dashboard slices (#109 Razor UI, #110 read
APIs/contracts) against real PostgreSQL and the real browser boundary: role-aware rendering,
administrator-only visibility, empty states, tenant isolation, onboarding-gate preservation,
responsiveness, and accessibility. Deliverables: targeted Aspire/PostgreSQL integration tests at
the service/HTTP boundary plus the committed `Nova.Browser.Tests` dashboard workflow. This is the
final integration gate for epic #5; each feature child keeps owning its focused coverage, no test
here may duplicate it, and no new feature surface may be introduced — only defects in the
delivered dashboard slice get fixed.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its
status to `Complete` and write its **Phase Summary** (what was done, key decisions, anything
needed to continue with zero context); run the phase's **Verification Plan** and record the
result before moving on. When all phases are done, fill in **Final Recap** and **Deployment
Plan**.

Conventions that matter throughout:

- Integration and browser tests are **local-only** (CI runs build + unit tests only). Run them
  against the Aspire AppHost before merge, exactly as #69 and the close/reopen gate did.
- Use the `nova-testing` skill for the write/run workflow and harness internals; use
  `aspire-playwright-validation` only if a final manual acceptance pass is requested at the end.
- `dotnet format Nova.slnx --verify-no-changes` has a known pre-existing baseline: the
  Tag-feature CHARSET files and three migration warnings are already violated on `main`.
  Record the baseline in Phase 1 and never "fix" unrelated files.
- Blocking accessibility findings **in the dashboard workflow** must be fixed in this issue.
  Non-blocking, MVP-wide residuals are recorded against #13 as a comment — never expanded into
  unrelated hardening here.
- Playwright/Blazor hard-won facts (from `testing.instructions.md`, rediscovered painfully in
  #69): assertions live in static `Microsoft.Playwright.Assertions`; Blazor client-side
  `NavigateTo` never fires a document load, so `WaitForURLAsync`/`GoBackAsync`/`GoForwardAsync`
  must use `WaitUntilState.Commit`; SSR-prerendered content can swallow interaction until the
  interactive circuit attaches — wait for stable content before driving the page.
- Latest recorded suite baselines (close/reopen gate): unit 1642, integration 294, browser
  30 total (28 passed + 2 `NOVA_A11Y_SCREENSHOTS`-gated skips). Record the fresh baseline in
  Phase 1.
- The dashboard summary composes the **real** `ICampaignQueryService`
  (`DashboardQueryService.GetDashboardAsync`), so summary-count coverage must run at the HTTP
  boundary (a service-level test with a substituted campaign service proves nothing about card
  composition). Activity-feed provider coverage already exists in
  `DashboardQueryPostgresTests` and stays there.

## Phase 1: Baseline verification and matrix-to-coverage audit

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator (verifies every coverage claim against actual test bodies;
the baseline runs themselves may be delegated to a sub-agent w/ smaller model).

- [x] Run the full unit suite: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj`.
      Recorded: **1708 passed** (up from 1642 — the merged #109/#110 dashboard slices added unit coverage).
- [x] Run the full integration suite:
      `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj`.
      Recorded: **302 total with one pre-existing failure** (`ProfilePhotoHttpTests.RegisterUploadFetchComplete_FullOnboardingFlow_Succeeds` — see Phase 4).
- [x] Run the full browser suite: `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj`.
      Recorded: **30 total, 28 passed + 2 env-gated skips**.
- [x] Record the `dotnet format Nova.slnx --verify-no-changes` baseline. Actual: **clean (exit 0,
      "Formatted 0 of 640 files")** — the pre-drafted "Tag CHARSET + migration warnings" note is stale on this branch.
- [x] Produce an explicit row-by-row table mapping every line of the issue's "Required
      validation matrix" (5 PostgreSQL rows, 6 browser rows) to the test(s) that prove it,
      marked `covered` / `gap` / `partial`, recorded in this plan (replace the pre-drafted
      table under `### Coverage matrix` when verified). Verify every claim against the actual
      test bodies before keeping it — no claim written without having read the test.
- [x] For each `gap`/`partial` row, finalize the concrete test spec (file, method name
      `Subject_Outcome_Condition`, seed shape, assertions) into Phase 2/3; keep each invariant
      at the lowest effective test layer per `testing.instructions.md`.
- [x] Confirm no new endpoint/route/policy is required for any planned test (all new tests
      exercise the existing `/api/dashboard` + `/api/dashboard/activity` surface and the
      existing `/` page plus onboarding gates). Confirmed: only the existing surface is exercised.
- [x] Confirm the member seeded by `UpdateUserAsync(..., clubId: club.ClubId, ...)` is an
      approved (direct) club member for the query-boundary claims (no separate approval field
      on `NovaUserEntity` — verify against the close/reopen audit's finding). Confirmed:
      `NovaUserEntity` carries only `ClubId` (no approval field); `Policies.RequireClubMember` =
      `RequireAuthenticatedUser` + `RequireClaim(ClubId)`, so a seeded `ClubId` is an approved direct member.

### Coverage matrix (verified against test bodies)

| Required validation row | Verified coverage | Status |
| --- | --- | --- |
| Admin vs evaluator/approved-member at the dashboard query boundary; admin-only counts never disclosed to evaluators | `DashboardHttpTests.GetSummary_AdminSeesAttention_EvaluatorOmits` (HTTP); `DashboardQueryServiceTests.GetDashboard_ReturnsAttention_ForAdmin` / `..._OmitsAttention_ForEvaluator` (SQLite); `ClubDashboardComponentTests` role-aware bUnit | covered |
| Counts authoritative and tenant-scoped: active campaigns, roster/team counts, unresolved placements, pending join requests | `DashboardSummaryHttpTests.GetSummary_ReturnsAuthoritativeTenantScopedCounts` (HTTP/PostgreSQL, new); `DashboardQueryServiceTests.GetDashboard_ReturnsActiveCardsCounts_AndOmitsAttention_ForEvaluator` + `GetDashboard_AdminUnresolvedCount_IsAuthoritativeBeyondCardCap` (SQLite) | covered |
| Club with no campaigns/players/teams returns correct empty contracts, not errors | `DashboardSummaryHttpTests.GetSummary_EmptyClub_ReturnsZeroCountsAndEmptyContracts` + `GetSummary_EmptyClub_EvaluatorOmitsAttention` (HTTP/PostgreSQL, new); bUnit empty-state rendering | covered |
| Cross-tenant club identifiers preserve non-disclosing behavior throughout | `DashboardSummaryHttpTests.GetSummary_IsTenantIsolated` (HTTP/PostgreSQL, new); `DashboardQueryServiceTests.GetDashboard_IsTenantIsolated` + `GetActivity_IsTenantIsolated` (SQLite) | covered |
| Activity-feed provider behavior (four-source translation, `timestamptz` ordering/round-trip) | `DashboardQueryPostgresTests.GetActivity_Postgres_TranslatesAllFourSources_AndOrdersByPolicy` + `..._PlacementModifiedAt_RoundTrips` | covered |
| Browser: admin logs in, lands on dashboard, sees active campaigns with working workspace links, roster/team counts, attention card | `DashboardBrowserTests.Dashboard_Admin_SeesCampaignsRosterTeamsAndAttention_WithWorkingLinks` (BS1, new) | covered |
| Browser: evaluator sees campaigns and recent evaluation activity without admin-only items/actions | `DashboardBrowserTests.Dashboard_Evaluator_SeesCampaignsAndActivity_WithoutAdminAttention` (BS2, new) | covered |
| Browser: user without a club routes through create-or-join; user without a profile photo routes through photo setup | `DashboardBrowserTests.Dashboard_OnboardingGates_PhotoLessAndClubLessUsers_AreRedirected` (BS3, new); `ProfilePhotoHttpTests` HTTP gate proof | covered |
| Browser: no-campaign club shows admin Create campaign action and evaluator neutral empty state | `DashboardBrowserTests.Dashboard_NoCampaignClub_AdminSeesCreateCta_EvaluatorSeesNeutralState` (BS4, new); bUnit empty states | covered |
| Browser: direct dashboard URLs and Back navigation preserve entry context | `DashboardBrowserTests.Dashboard_DirectUrlAndBackNavigation_PreserveEntryContext` (BS5, new) | covered |
| Browser: wide/narrow viewports usable with keyboard nav, visible focus, programmatic labels, status/error announcements, no color-only reliance | `DashboardBrowserTests.Dashboard_KeyboardAndA11y_AcrossViewports` (BS6, new) + `Dashboard_A11yEvidence_CapturesScreenshots` (env-gated); loading/error/retry announcements stay at the bUnit layer | covered |

### Verification Plan

- Re-run the audit greps and confirm the matrix covers all 11 rows and every existing
  dashboard-related test file is accounted for (`DashboardHttpTests` 4, `DashboardQueryPostgresTests` 2,
  `DashboardQueryServiceTests` 8, `DashboardActivityQueryServiceTests` 10, `DashboardActivityFeedPolicyTests` 5,
  `HttpDashboardQueryServiceTests` 12, `ClubDashboardComponentTests` 14, plus the
  `ProfilePhotoGateMiddlewareTests` / `ClubOnboardingGateMiddlewareTests` gate units and
  `ProfilePhotoHttpTests`).
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*Dashboard*"` — all green.

### Phase Summary

Completed. Baselines recorded in one session: unit **1708 passed**, integration **302 with one
pre-existing failure**, browser **30 total (28 passed + 2 env-gated skips)**. `dotnet format
--verify-no-changes` is clean (exit 0, "Formatted 0 of 640 files").

**Blocking finding discovered in Phase 1 (fixed in Phase 4):** `ProfilePhotoHttpTests
.RegisterUploadFetchComplete_FullOnboardingFlow_Succeeds` failed deterministically — a photo-less
user hitting `/` was redirected to `/Account/AccessDenied` instead of `/Account/ProfilePhoto`. Root
cause: the dashboard slice (#109) added `[Authorize(Policy = RequireClubMember)]` to `/`, and the
implicit `WebApplication` pipeline places `UseAuthorization` before user middleware, so the
onboarding gates never ran before the policy denial. Fixed in `Nova/Program.cs` (explicit
`UseAuthentication` before the gates, `UseAuthorization` after); the full integration suite is now
**306 passed**.

Coverage matrix verified against actual test bodies — all 11 rows now `covered` (see above). The
member seeded by `UpdateUserAsync(..., clubId)` is an approved direct member (no separate approval
field). No new endpoint/route/policy was required.

## Phase 2: PostgreSQL/Aspire integration additions (HTTP boundary)

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: sub-agent w/ smaller model (well-specified; mirrors the existing
`DashboardHttpTests` two-client + `CreateAdminContext` seeding patterns), with the orchestrator
reviewing the final diff.

- [x] New file `Nova.Integration.Tests/Http/DashboardSummaryHttpTests.cs`
      (collection: `NovaAppHostCollection`), reusing `IdentityHttpClientHelper`,
      `SeedingHelpers`, and direct `fixture.CreateAdminContext()` inserts exactly like
      `DashboardHttpTests`. Rationale for HTTP (not service-level): `GetDashboardAsync`
      composes the real `ICampaignQueryService`, and the AppHost behind the fixture provides
      real PostgreSQL in the same boundary.
- [x] Test `GetSummary_ReturnsAuthoritativeTenantScopedCounts`: register admin + applicant
      (photo-complete), create club, seed via admin context — two active campaigns (one manual
      like `DashboardHttpTests` with an undecided participant, one via
      `SeedCampaignWithParticipantsAsync` with `PlacementOutcome.Undecided`), a mix of
      active/archived players and teams, and one pending `ClubJoinRequestEntity` from the
      applicant. Assert: card count/order, per-card name, `ParticipantCount`, `UnresolvedCount`,
      and `WorkspaceUrl == DashboardEndpoints.CampaignWorkspaceUrl(id)`; `Roster.ActivePlayers`
      / `ArchivedPlayers`; `Teams.ActiveTeams` / `ArchivedTeams`; attention
      `PendingJoinRequestCount` and `UnresolvedPlacementCount` equal the seeded undecided
      total across both campaigns, and `FirstUnresolvedCampaignId` is the first card with an
      undecided participant.
- [x] Test `GetSummary_EmptyClub_ReturnsZeroCountsAndEmptyContracts` (admin variant): club with
      no campaigns/players/teams/requests → 200, empty `ActiveCampaigns`, zero roster/team
      counts, `AdminAttention` present with zero counts and null `FirstUnresolvedCampaignId`
      (never an error/problem shape).
- [x] Test `GetSummary_EmptyClub_EvaluatorOmitsAttention` (evaluator variant): same empty club
      → 200, zero counts, `AdminAttention` is null.
- [x] Test `GetSummary_IsTenantIsolated`: two clubs, two admins; seed decoy rows (campaign,
      players, team, join request) into club B; club A's admin GETs the summary → cards contain
      only club A's campaign name, all counts reflect only club A's rows, and the club B
      campaign name is absent from both summary and activity results.
- [x] Guard against duplication: do **not** re-test activity-feed ordering/translation
      (`DashboardQueryPostgresTests` owns it) or the admin-vs-evaluator attention shaping
      (`GetSummary_AdminSeesAttention_EvaluatorOmits` owns it).

### Verification Plan

- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*DashboardSummaryHttpTests"` — all green (4/4).
- Full integration suite — all green (306 passed; watch the shared-database rule: never assert on global
  unfiltered counts; every test seeds its own data with database-generated ids).

### Phase Summary

Completed. Added `DashboardSummaryHttpTests` with the four planned tests, all green against the
Aspire AppHost/PostgreSQL. Two seed-correction lessons were applied while writing: (1) the
`CK_PlayerCampaignAssignments_PlacementOutcomeTeam` check constraint requires `Assigned` outcomes to
carry a `TeamId` and `Undecided`/`NotSelected`/`Withdrawn` to be team-less, so the manual campaign
uses `Undecided` and the tenant-isolation decided assignment uses `Assigned` + `TeamId`; (2) the
evaluator/member client needs `SeedingHelpers.RefreshClubMembershipCookieAsync` after
`UpdateUserAsync(clubId)` or the dashboard API returns 403 (stale cookie without a `ClubId` claim).
The full integration suite went from 302 (one pre-existing failure) to **306 passed**.

## Phase 3: Browser workflow scenarios (`Nova.Browser.Tests`)

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator (role/hydration/gate subtleties need judgment); the mechanical
seed class may be delegated to a sub-agent w/ smaller model once the orchestrator fixes the seed
shape.

- [x] New `Nova.Browser.Tests/DashboardSeed.cs` mirroring `CloseoutSeed`: registers admin,
      evaluator, and a third "applicant" user via `IdentityHttpClientHelper`
      (photo-complete), `SeedingHelpers.CreateClubAsync` for the club, `UpdateUserAsync` to
      assign the evaluator to the club, then seeds via the fixture's `CreateAdminContext`:
      two active campaigns (one with undecided participants via
      `SeedCampaignWithParticipantsAsync`), active + archived players and teams, one pending
      `ClubJoinRequestEntity` from the applicant, and one `NoteEntity` on a seeded assignment
      (gives the evaluator scenario real recent-activity rows with actor names). Also expose
      the photo-less and club-less variant users (registration-only / photo-complete-no-club).
- [x] New `Nova.Browser.Tests/DashboardBrowserTests.cs` (collection: `BrowserSuite`), one test
      per scenario, seeded by `DashboardSeed`, using the fixture's `NewSignedInContextAsync`
      (explicit viewports for the responsive scenarios):
  - [x] **BS1 — Admin happy path:** admin signs in → lands on `/` (Dashboard heading) → sees
        the seeded campaign rows with participant/unresolved counts → "Open workspace" link
        navigates to `/campaigns/{id}` (assert URL; the link is a plain `<a>`, full
        navigation) → Back returns to the dashboard → roster/team cards show the seeded
        counts → attention card shows pending join requests + unresolved placements with
        working "Review requests" (`/Clubs/{ClubId}/admin`) and "Review placements"
        (`/campaigns/{FirstUnresolvedCampaignId}`) links.
  - [x] **BS2 — Evaluator role:** evaluator signs in → lands on `/` → sees the campaign rows
        and at least one recent-activity entry with the seeded actor's display name → no
        Admin attention card, no "Review requests"/"Review placements" links.
  - [x] **BS3 — Onboarding gates:** (a) photo-less user (registered via `RegisterUserAsync`
        only) signs in → lands on `/Account/ProfilePhoto` ("Profile photo" heading);
        (b) photo-complete club-less user signs in → lands on `/Clubs/Onboarding`
        ("Welcome to Nova" heading). Assert the landing URL and heading only — do not drive
        the cropper JS flow or create a club.
  - [x] **BS4 — No-campaign club:** a club seeded with no campaigns/players/teams: admin
        sees the Create campaign CTA (`campaigns/new` link); evaluator sees the neutral
        empty state and no CTA.
  - [x] **BS5 — Direct URL + Back navigation:** navigating directly to `/` in a fresh page
        renders the dashboard without redirect; from the dashboard, Open workspace → browser
        Back (`GoBackAsync` with `WaitUntilState.Commit`) restores the dashboard with its
        counts intact.
  - [x] **BS6 — Keyboard + a11y across viewports:** wide (1280×800) and narrow (~480 px)
        contexts: tab order reaches the dashboard controls with visible focus, the workspace
        link carries its programmatic label (`aria-label="Open workspace for {name}"`),
        labelled regions resolve (`aria-labelledby` sections), and no assertion relies on
        color alone (assert names/labels). Added the env-gated evidence-capture test
        (`Dashboard_A11yEvidence_CapturesScreenshots`, gated behind `NOVA_A11Y_SCREENSHOTS=1`
        with `Assert.Skip(...)` when unset, mirroring `Closeout_A11yEvidence_CapturesScreenshots`)
        writing screenshots to `%TEMP%\nova-a11y-screenshots`.
  - [x] Loading/error/retry announcements stay at the bUnit layer (already proven by
        `ClubDashboardComponentTests`); no racy browser assertions for transient loading states.
- [x] Any blocker found gets fixed in product code (not papered over in the test), scoped to
      the delivered dashboard slice only, with the affected scenario rerun before concluding —
      follow the #69 precedent (A1/A2).

### Verification Plan

- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --filter-class "*DashboardBrowserTests"` — all scenarios green (7 total: 6 passed + 1 env-gated skip).
- Full browser suite — all existing evaluation/placement/closeout scenarios stay green (**37 total: 34 passed + 3 env-gated skips**).

### Phase Summary

Completed. Added `DashboardSeed` (full workspace + empty-club variant) and `DashboardBrowserTests`
with BS1–BS6 plus the env-gated `Dashboard_A11yEvidence_CapturesScreenshots`. BS3 (onboarding gates)
passes only because of the Phase 4 middleware-ordering fix in `Nova/Program.cs`. One compile-time
correction: `ILocator` has no `IsFocusedAsync`, so keyboard focus uses the auto-retrying
`Expect(target).ToBeFocusedAsync(Timeout=400)` inside the tab-advance loop. The full browser suite
went from 30 total (28+2) to **37 total (34 passed + 3 env-gated skips)**.

## Phase 4: Defect repair (contingent, dashboard slice only)

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator (judgment; decides blocking vs MVP-wide residual).

- [x] Triage every finding from Phases 2–3: blocking findings in the dashboard workflow are
      fixed here (product code, plus the minimum bUnit/unit coverage per `nova-testing`); the
      affected integration/browser scenario is rerun until green.
- [x] Non-blocking, MVP-wide residuals are collected (itemized) for the Phase 6 comment on
      #13 — never expanded into unrelated hardening. (None identified.)
- [x] No new feature surface: fixes touch only the delivered #109/#110 slice.

### Verification Plan

- Rerun the affected scenario/class, then the full suite of the project it lives in.

### Phase Summary

Completed. One blocking finding, fixed: `ProfilePhotoHttpTests
.RegisterUploadFetchComplete_FullOnboardingFlow_Succeeds` failed because the dashboard slice (#109)
added `[Authorize(Policy = RequireClubMember)]` to `/`, and the implicit `WebApplication` pipeline
runs `UseAuthorization` before user middleware, so a photo-less/club-less user was denied
(`/Account/AccessDenied`) before the onboarding gates could redirect. Fix in `Nova/Program.cs`:
explicit `app.UseAuthentication()` before the gates and `app.UseAuthorization()` after them. The
fix is covered by the existing HTTP e2e test (now passing) and the new BS3 browser scenario; no new
unit/bUnit test was needed because the invariant is the middleware ordering itself. The full
integration suite went from 302 (1 failing) to **306 passed**. No non-blocking MVP-wide residuals
were produced, so no #13 comment is required.

## Phase 5: Deduplication audit

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator.

- [x] With the final matrix complete, walk every invariant and confirm it lives at the lowest
      effective test layer: pure policy (feed order/bound) → `DashboardActivityFeedPolicyTests`;
      SQLite service shell (role shaping, tenancy, beyond-cap) → `DashboardQueryServiceTests`;
      provider/HTTP boundary (count translation, empty serialization, cross-tenant) → the new
      Phase 2 tests + `DashboardQueryPostgresTests`; browser workflow → Phase 3 scenarios;
      states impractical in the browser (loading/error/retry, link fallbacks) → bUnit.
- [x] Remove or repair lower-level coverage only where the final matrix exposes true overlap
      (same invariant, same layer, no additional boundary proof). Record removals/repairs in
      the Phase Summary; when in doubt, preserve.

### Verification Plan

- After any removal, the full unit + integration suites stay green with the new counts recorded.

### Phase Summary

Completed. No removals/repairs needed. The unit `DashboardQueryServiceTests` composes the **real**
`CampaignQueryService` against SQLite, while the new `DashboardSummaryHttpTests` proves the same
composition over PostgreSQL + the HTTP boundary — complementary layers, not overlap. Empty-state
(bUnit) and empty-serialization (HTTP), and tenant-isolation (SQLite vs PostgreSQL) likewise differ
by layer. All invariants live at the lowest effective layer; nothing was duplicated.

## Phase 6: Final verification gate and handoff

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator (reruns may be delegated to a sub-agent w/ smaller model).

- [x] Full regression in one session: `dotnet build Nova.slnx`, then the unit, integration,
      and browser suites — all green, counts recorded (unit **1708**, integration **306**,
      browser **37 total = 34 passed + 3 env-gated skips**).
- [x] `dotnet format Nova.slnx --verify-no-changes` — clean (exit 0, "Formatted 0 of 640 files");
      none of the files changed by this issue are flagged.
- [x] Post residual MVP-wide a11y/UX concerns (if any) as a comment on #13 — none identified.
- [x] Update this plan: Phase Summaries, Final Recap, Deployment Plan.
- [x] Note completion on issue #111 (acceptance-criteria status + link to this plan), per the
      sibling gates' handoff convention.
- [x] Remove temporary browser-automation artifacts from repo paths (screenshots/measurements
      live in `%TEMP%`; nothing was written into repo paths).

### Verification Plan

- All three suites green in a single session; format baseline diff clean.
- #13 comment posted (if residuals exist); #111 acceptance criteria all satisfied; plan
  summaries filled in.

### Phase Summary

Completed. Full regression in one session: `dotnet build Nova.slnx` (0 warnings/errors), unit
**1708 passed**, integration **306 passed**, browser **37 total (34 passed + 3 env-gated skips)**.
`dotnet format --verify-no-changes` clean. No #13 comment required (no residuals). PR opened against
`main` from this branch linking `Closes #111`.

## Final Recap

Validated the completed club-dashboard slice against PostgreSQL and the real browser boundary, the
final integration gate for epic #5. Added `DashboardSummaryHttpTests` (4 HTTP/PostgreSQL tests:
authoritative tenant-scoped counts, empty-club admin + evaluator contracts, cross-tenant isolation)
and `DashboardBrowserTests` + `DashboardSeed` (BS1–BS6 plus an env-gated a11y evidence capture).
Found and fixed one blocking regression from #109: `[Authorize(Policy = RequireClubMember)]` on `/`
made the implicit `UseAuthorization` deny photo-less/club-less users before the onboarding gates
could redirect — fixed by explicitly ordering `UseAuthentication` → gates → `UseAuthorization` in
`Nova/Program.cs`. The coverage matrix's 11 required rows are now all `covered`. No new
endpoint/route/policy surface was introduced; no deduplication removals were needed; no
MVP-wide residuals were left over.

## Deployment Plan

- Merge the PR for this branch into `main` (CI runs build + unit tests only; integration and
  browser suites were run locally against the Aspire AppHost and are green).
- No database migration, environment variable, or configuration change is introduced — deployment
  is code-only. The `Nova/Program.cs` middleware ordering change ships with the app binary.
- The two new test projects' additions (`DashboardSummaryHttpTests`, `DashboardBrowserTests`,
  `DashboardSeed`) are local-only regression coverage and require no CI/agent changes.
