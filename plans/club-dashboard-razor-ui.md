# Club Dashboard Razor UI (#109)

Replace the "Hello, world!" placeholder home (`Nova/Components/Pages/Home.razor`) with the role-aware club dashboard at `/`, consuming the completed #110 read slice (`IDashboardQueryService`, `DashboardEndpoints`, `DashboardActivityResult`) — active campaign cards with workspace links, roster/team count cards, the administrator attention card, and the bounded recent-activity feed. Razor UI only: no new services, endpoints, entities, or migrations; the existing profile-photo and create-or-join onboarding gates and role-correct bottom navigation stay unchanged. bUnit coverage proves role-aware rendering, attention visibility, empty states, and composition.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to `Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue with zero context); run the phase's **Verification Plan** and record the result before moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

Sequencing: #110 (dashboard read APIs) is merged and Complete — its contracts, endpoints, WASM client (`HttpDashboardQueryService`), and both `Program.cs` wiring files are already in place. #111 owns the later cross-slice PostgreSQL/browser validation, so **browser suite tests are not part of this plan**. Do not touch `Nova/Program.cs`, `Nova.Client/Program.cs`, `Nova/Features/Dashboard/`, `Nova.Shared/Features/Dashboard/`, or `Nova.Client/Services/Dashboard/` except to consume them from UI code.

## Design decisions (confirmed with user 2026-08-19)

1. **Page placement**: delete `Nova/Components/Pages/Home.razor` and add `Nova.UI/Features/Dashboard/Pages/ClubDashboard.razor` with `@page "/"` (Nova.UI-first rule; `Routes.razor` already includes the Nova.UI assembly). Verified: nothing references `Home.razor` — the navbar brand uses `href=""` and routes to `/` via the router.
2. **Render mode**: `@rendermode InteractiveAuto` + `@attribute [Authorize(Policy = Policies.RequireClubMember)]`, mirroring `Campaigns.razor`. The page needs event handlers (Retry) and announced loading/error states, so static SSR is insufficient; the WASM client is already registered so InteractiveAuto resolves the server service during prerender and the HTTP service after attach.
3. **Role detection in UI**: `authenticationState.User.IsInRole(Roles.ClubAdmin)` decides the empty-state Create-campaign CTA (same as `Campaigns.razor.cs:180`). The attention card renders from the role-shaped `AdminAttention` payload — the server already omits it (null) for evaluators; the card is **hidden entirely** for evaluators (two stat cards instead of three).
4. **Attention card links (user-confirmed)**: two links — "Review requests" → `/Clubs/{ClubId}/admin` (ClubId read from the `NovaClaimTypes.ClubId` claim, the pattern used by `Players`/`Teams` pages), and "Review placements" → `DashboardEndpoints.CampaignWorkspaceUrl(FirstUnresolvedCampaignId)` when present, else `/campaigns` fallback.
5. **Activity feed bound (user-confirmed)**: request the contract default (`Limit = GetDashboardActivityInput.DefaultLimit`, 50 events) and render whatever comes back — a deliberate documented bound, no pagination, no "view all" page exists.
6. **Setup-gap hints excluded (user-confirmed)**: the "no teams yet"/"no tag definitions" admin hints from the screen design are out of scope — the read contracts carry no tag counts and the issue text does not list them.
7. **Feed wording**: feature-local static display helper mapping `DashboardActivityEventKind` → verb phrase; placement rows include the outcome via the existing `CampaignRosterDisplay.OutcomeLabel`; dates use the same `"MMM d, yyyy"` format as `CampaignOverviewPanel.FormatActivityDate` (culture-sensitive per testing rules).
8. **Onboarding gates**: unchanged. `/` is not exempt in `ProfilePhotoGateMiddleware`/`ClubOnboardingGateMiddleware`, so the new dashboard automatically sits behind both gates; no `Nova/Program.cs` change.
9. **Navigation**: unchanged markup — `NavMenu` already renders Club/Campaigns/Players/Teams only when a club claim exists, which is the role-correct behavior required. Verify via existing `NavMenuTests` (no edits).
10. **No new contracts**: `ClubDashboardResult` provides everything the UI needs; anything missing from it (e.g. tag counts) is out of scope, not a reason to add endpoints.

## Phase 1: Dashboard page + display helper (`Nova.UI/Features/Dashboard/`)

Status: Complete

Suggested executor: orchestrator (markup/a11y/state details are dense; do not delegate)

- [x] Delete `Nova/Components/Pages/Home.razor` (the placeholder `@page "/"` page).
- [x] Add `Pages/ClubDashboard.razor` + `ClubDashboard.razor.cs` + `ClubDashboard.razor.css`:
  - `@page "/"`, `@rendermode InteractiveAuto`, `[Authorize(Policy = Policies.RequireClubMember)]`, `<PageTitle>Dashboard</PageTitle>`.
  - Code-behind injects `IDashboardQueryService` + `AuthenticationStateProvider` + `NavigationManager` via primary constructor, inherits `NovaComponentBase`.
  - `OnInitializedAsync` with `[PersistentState]` `Initialized` guard mirroring `CampaignOverviewPanel` (restore persisted summary/activity/error on interactive attach; never double-fetch). Load summary and activity in parallel with `ComponentCancellationToken`; `ServiceResult` failures become a page-level error (`ProblemMessage` pattern) with Retry re-invoking the load.
  - Regions (top to bottom, Bootstrap `container py-4`):
    1. **Active campaigns** card — table of `ActiveCampaigns` rows: name, participant count, undecided count, and an **Open workspace** button/link to `card.WorkspaceUrl`. No campaigns → empty-state region (admin: primary **Create campaign** button → `campaigns/new`; evaluator: neutral "No active campaigns right now" message).
    2. **Stat cards** — `row row-cols-1 row-cols-md-3 g-3` (collapses to two `col-md-6`/stacked cards for evaluators): **Roster** (active + archived players, **View players** → `players`), **Teams** (active + archived teams, **View teams** → `teams`), **Admin attention** (admins only: pending join-request count + unresolved placement count with the two links from decision 4).
    3. **Recent activity** feed — newest-first list of up to 50 items: `{MMM d, yyyy} {ActorDisplayName} {verb} ({CampaignName})` per decision 7; empty feed → muted "No recent activity."
  - States: full-page loading spinner with `role="status"`/`aria-live="polite"`; `alert alert-danger` + **Retry** button with `role="alert"`.
  - Accessibility/responsive: semantic headings/`th scope`, labelled links and buttons (`aria-label` where text is insufficient), keyboard operability (real links/buttons — no click-only divs), no color-only meaning, `rem` units in scoped CSS only.
- [x] Add `DashboardDisplay.cs` — static, pure display helper: `ActivityVerb(DashboardActivityEventKind)` → "added a note to {player}", "applied tag \"{tag}\" to {player}", "set {player}'s placement to {OutcomeLabel}", "closed the campaign", "reopened the campaign"; reuse `CampaignRosterDisplay.OutcomeLabel` for placements; `FormatActivityDate(DateTimeOffset)`.

### Verification Plan

- `dotnet build Nova.slnx` — succeeds; no duplicate `@page "/"` (Home.razor removed). **Result: `Build succeeded. 0 Warning(s), 0 Error(s)`.**
- `dotnet format Nova.slnx --verify-no-changes` after applying `dotnet format Nova.slnx`. **Result: exit 0 (no changes).**

### Phase Summary

Implemented the Razor UI-only dashboard. Deleted `Nova/Components/Pages/Home.razor` (the only `@page "/"` page — confirmed nothing references it) and added the feature-local page under `Nova.UI/Features/Dashboard/`:

- `Pages/ClubDashboard.razor` — `@page "/"`, `@rendermode InteractiveAuto`, `[Authorize(Policy = Policies.RequireClubMember)]`, `<PageTitle>Dashboard</PageTitle>`, Bootstrap `container py-4`. Renders: (1) an **Active campaigns** table (name / participants / unresolved / **Open workspace** link to `card.WorkspaceUrl`) with role-aware empty states; (2) a **stat-card row** (Roster → `players`, Teams → `teams`, and the admin-only **Admin attention** card with the two decision-4 links); (3) a **Recent activity** feed. Loading uses `role="status"`/`aria-live="polite"`; errors use `alert alert-danger` + **Retry** (`role="alert"`). Semantic headings, `th scope`, `aria-label` on the workspace link, real links/buttons throughout.
- `Pages/ClubDashboard.razor.cs` — primary-constructor DI (`IDashboardQueryService`, `AuthenticationStateProvider`, `NavigationManager`), inherits `NovaComponentBase`. `OnInitializedAsync` reads the ClubAdmin role and `NovaClaimTypes.ClubId` claim, then uses the `[PersistentState]` `Initialized` guard mirroring `CampaignOverviewPanel` to restore `PersistedSummary`/`PersistedActivity`/`PersistedPageError` on attach without a double fetch. `LoadDashboardAsync` fetches summary + activity in parallel via `ComponentCancellationToken`, maps failures to a page-level `ProblemMessage` error (Forbidden → `/Account/AccessDenied`), and `RetryAsync` re-invokes the load. A computed `StatRowClass` switches the stat-card row between `row-cols-md-3` (admin) and `row-cols-md-2` (evaluator) per the design.
- `Pages/ClubDashboard.razor.css` — comment-only scoped stylesheet (Bootstrap handles layout), matching the repo convention.
- `DashboardDisplay.cs` — internal, pure, static helper. **Deviation noted:** `ActivityVerb` takes the full `DashboardActivityItemDto` (not the bare enum) because the verb phrases require per-row player/tag/outcome context; it emits the exact phrases in the plan and reuses `CampaignRosterDisplay.OutcomeLabel`. `FormatActivityDate(DateTimeOffset)` uses `"MMM d, yyyy"`.

No changes to `Nova/Program.cs`, `Nova.Client/Program.cs`, `Nova/Features/Dashboard/`, `Nova.Shared/Features/Dashboard/`, or `Nova.Client/Services/Dashboard/`.

## Phase 2: bUnit component tests (`Nova.Unit.Tests/Dashboard/`)

Status: Complete

Suggested executor: orchestrator writes the reference tests (auth setup + persisted-state subclass); remaining cases may be delegated once those patterns exist

- [x] Add `ClubDashboardComponentTests.cs` using bUnit + NSubstitute + Shouldly. Auth setup mirrors `NavMenuTests` (`Substitute` `AuthenticationStateProvider` with a principal carrying `NovaClaimTypes.ClubId` + optional `Roles.ClubAdmin` role claim, `CascadingAuthenticationState`, `FakeNavigationManager`, `FakeAuthorizationService` + `DefaultAuthorizationPolicyProvider` for the `[Authorize]` attribute); substitute `IDashboardQueryService` returning constructed `ClubDashboardResult`/`DashboardActivityResult`.
  - Composition: populated summary renders all regions (campaign table with workspace hrefs, roster/team counts, activity rows).
  - Role-aware: admin principal + `AdminAttention` payload → attention card visible with both counts and the correct `Clubs/{id}/admin` and workspace (`/campaigns/{FirstUnresolvedCampaignId}`) links; evaluator principal (no admin role, `AdminAttention = null`) → no attention card and no Create-campaign CTA.
  - Attention link fallback: `FirstUnresolvedCampaignId = null` → "Review placements" hrefs to `campaigns`.
  - Empty states: zero active campaigns + admin role → "Create campaign" link to `campaigns/new`; zero active campaigns + evaluator → neutral message, no CTA; empty feed → "No recent activity."
  - Feed rendering: one row per `DashboardActivityEventKind` with expected verb text (culture-sensitive date strings built with the same format/culture the component uses); tag rows show the tag name, placement rows show `OutcomeLabel`.
  - Loading/error: service returning an error problem → `role="alert"` message; clicking Retry re-invokes the service and then renders data.
  - Persisted-state restore: test-only subclass seeding `Initialized` + persisted payloads (pattern from `blazor-component-tests.md`) → service `DidNotReceive()` load calls.
- [x] Add render-mode and route assertions (per `blazor-component-tests.md`): `ClubDashboard` carries an `InteractiveAutoRenderMode` `RenderModeAttribute` and a `RouteAttribute` with template `"/"`.
- [x] (If needed) run `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*NavMenu*"` to confirm the untouched nav tests stay green.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*Dashboard*"` — new component tests (plus the #110 Dashboard tests) pass. **Result: 65 passed, 0 failed.**
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full unit suite green (baseline ~1693 + new). **Result: 1707 passed, 0 failed.**
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*ClubDashboardComponentTests*"` — **Result: 14 passed, 0 failed.**
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*NavMenu*"` — **Result: 2 passed, 0 failed.**

### Phase Summary

Added `Nova.Unit.Tests/Dashboard/ClubDashboardComponentTests.cs` (14 tests) using `BunitContext`, NSubstitute, and Shouldly, mirroring `CampaignComponentsTests`' auth/service registration helpers:

- **Composition** — `ClubDashboard_RendersAllRegions_WhenPopulated`: campaign table + workspace href `/campaigns/42`, roster (`5 active`/`2 archived`) and team (`8 active`/`1 archived`) counts, `players`/`teams` links, and an activity row.
- **Role-aware** — `ClubDashboard_ShowsAdminAttention_ForClubAdmin` (attention card with counts and `Review requests` → `/Clubs/42/admin`, `Review placements` → `/campaigns/77`) and `ClubDashboard_HidesAdminAttention_ForEvaluator` (no attention card/links).
- **Attention link fallback** — `ClubDashboard_FallsBackToCampaignList_WhenNoUnresolvedCampaign` (`Review placements` → `/campaigns`).
- **Empty states** — admin → `Create campaign` to `campaigns/new`; evaluator → neutral message with no CTA; empty feed → `No recent activity.`.
- **Feed rendering** — `ClubDashboard_RendersEachActivityKind_WithVerb`: 5 rows (one per kind) with culture-sensitive date built via `ToString("MMM d, yyyy")` and per-kind verb substrings (tag name `Leader`, outcome `Assigned`).
- **Loading/error/retry** — `role="status"`/`aria-live="polite"` loading spinner; error → `role="alert"`; `Retry` re-invokes the service and renders data.
- **Persisted-state restore** — test-only `PersistedStateClubDashboard` subclass seeds `Initialized` + persisted payloads; asserts `DidNotReceive()` on both service methods.
- **Render mode + route** — reflection assertions (`RenderModeAttribute` → `InteractiveAutoRenderMode`; `RouteAttribute` → `/`), plus a string-based `@rendermode InteractiveAuto` source assertion matching the repo convention.

## Phase 3: Full validation and wrap-up

Status: Complete

- [x] `dotnet build Nova.slnx` — clean, 0 warnings/errors. **Result: `Build succeeded. 0 Warning(s), 0 Error(s)`.**
- [x] `dotnet format Nova.slnx --verify-no-changes` (run `dotnet format Nova.slnx` to apply fixes first). **Result: exit 0 (no changes).**
- [x] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — full unit suite green. **Result: 1707 passed, 0 failed.**
- [x] `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*Dashboard*"` — the existing 8 #110 Dashboard HTTP/Postgres tests still pass against the Aspire AppHost (local-only sanity; no endpoint changes expected). **Result: 8 passed, 0 failed (Aspire AppHost + PostgreSQL started successfully via Docker).**
- [x] Optional local acceptance (not committed coverage; #111 owns the browser suite): `dotnet run --project Nova.AppHost`, then an `aspire-playwright-validation` pass — admin login lands on the populated dashboard (attention card + CTA states), evaluator login shows no admin-only UI, and both roles' pages are keyboard-navigable at narrow viewport. **Result: deferred to #111 (browser suite owns real-login/UI-flow coverage); unit + integration coverage above proves role-aware rendering, attention visibility, empty states, and composition. No committed browser tests were added, per scope.**
- [x] Walk the issue acceptance criteria and confirm each is satisfied (member lands on the dashboard; onboarding gates untouched and reachable; admin setup/attention items absent for evaluators; evaluators reach active campaigns with no admin actions; empty states role-correct; wide/narrow layouts keyboard operable, labelled, announced states). **Result: satisfied — `/` now routes to the role-aware dashboard; onboarding gates (`ProfilePhotoGateMiddleware`/`ClubOnboardingGateMiddleware`) unchanged and `/` stays behind them; `AdminAttention` renders only from the admin-only payload (evaluators get two stat cards, no CTA); real `<a>`/`<button>` controls are keyboard-operable, headings/`th scope`/`aria-label`/`role` live regions in place; responsive grid collapses 3→2 columns for evaluators.**
- [x] Confirm via `git status` diff review: only `Nova.UI/Features/Dashboard/**`, the deleted `Nova/Components/Pages/Home.razor`, and `Nova.Unit.Tests/Dashboard/**` — no service/endpoint/`Program.cs`/migration changes. **Result: confirmed — diff contains only those paths plus the updated `plans/club-dashboard-razor-ui.md`. No `Program.cs`, service, endpoint, entity, migration, or browser-test changes.**
- [x] Commit on this branch with the standard Co-authored-by trailer. **Result: committed with `Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>`.**

### Verification Plan

- All commands above exit clean; the diff contains only the UI page/helper, the Home.razor deletion, and the new component test file. **Result: confirmed.**

### Phase Summary

Full validation passed: clean build (0 warnings/errors), `dotnet format --verify-no-changes` clean, 1707 unit tests green (baseline 1693 + 14 new), the 8 #110 Dashboard Aspire integration tests green, and the 2 untouched `NavMenu` tests green. The Aspire environment (CLI 13.5.0, Docker, DCP) was available, so the integration tests ran for real against PostgreSQL 18 — no blocker to document. The optional browser acceptance pass was intentionally deferred to #111 (which owns the browser suite); no committed browser tests were added. The diff was reviewed and contains only the dashboard UI/helper, the `Home.razor` deletion, the new component test file, and the plan update. Work was committed on the branch with the required Co-authored-by trailer.

## Final Recap

Delivered the Razor UI-only club dashboard for #109: replaced the `"Hello, world!"` placeholder `Nova/Components/Pages/Home.razor` with `Nova.UI/Features/Dashboard/Pages/ClubDashboard.razor` (`@page "/"`, `InteractiveAuto`, `[Authorize(Policy = Policies.RequireClubMember)]`). The page consumes the merged #110 read slice (`IDashboardQueryService`, `DashboardEndpoints`, `ClubDashboardResult`, `DashboardActivityResult`) with no new services/endpoints/entities/migrations, and leaves onboarding gates and navigation untouched. It renders active campaign cards with workspace links, role-aware empty states, roster/team stat cards, the administrator-only attention card (with the two decision-4 links and the `/campaigns` fallback), and the bounded (up-to-50) recent-activity feed with a feature-local `DashboardDisplay` helper reusing `CampaignRosterDisplay.OutcomeLabel`. Prerender double-fetch is prevented with the `[PersistentState]` `Initialized` guard. Fourteen bUnit component tests prove composition, role-aware rendering, attention-link fallback, empty states, every activity kind, loading/error/retry, persisted-state restore, and route/render-mode declarations. Validation: clean build, clean format, 1707 unit tests, 8 Aspire integration tests, and 2 untouched nav tests all green.

One deliberate deviation from the plan text: `DashboardDisplay.ActivityVerb` takes the full `DashboardActivityItemDto` (rather than the bare `DashboardActivityEventKind`) because the required verb phrases embed per-row player/tag/outcome context. The emitted phrases match the plan exactly.

## Deployment Plan

1. Merge the PR into `main` (build + unit tests run in CI; integration/browser suites are run locally per repo policy and were verified locally for the Dashboard integration slice).
2. No database migration, seed, or configuration change is required — this is a Razor UI-only change consuming the already-merged #110 read endpoints.
3. After deploy, `/` resolves to the new dashboard for any authenticated club member. Both onboarding gates (`ProfilePhotoGateMiddleware` then `ClubOnboardingGateMiddleware`) continue to intercept `/` until their claims are satisfied, so there is no new pre-onboarding exposure.
4. Verify an admin lands on the dashboard with the attention card + create-campaign CTA states, and an evaluator lands without admin-only UI, on both wide and narrow viewports (followed up end-to-end by the #111 browser suite).
