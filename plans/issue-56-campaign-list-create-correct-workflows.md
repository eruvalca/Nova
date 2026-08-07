# Issue #56: Build campaign list and create/correct workflows

Build the shared Blazor campaign workflows: browse campaigns by season with Active/Closed views
(`/campaigns`), create a campaign with an existing or inline-created season (`/campaigns/new`), and
administrator-only Active campaign/season metadata correction. UI-only issue: consumes the existing
`ICampaignQueryService`, `ICampaignCreationService`, `ICampaignMetadataService`, and
`ISeasonMetadataService` through their already-registered server and WASM HTTP implementations.
No `DbContext`/`HttpContext` in components. Parent epic #9; prerequisites #55/#57/#58 are closed.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

## Key decisions (confirmed against issue text + repo conventions)

- Pages live in `Nova.UI\Features\Campaigns\Pages\`; forms in `Nova.UI\Features\Campaigns\Components\`;
  `.razor` + `.razor.cs` (+ `.razor.css` only when Bootstrap cannot express it), no `@code` blocks,
  primary-constructor injection, `NovaComponentBase` via `_Imports.razor`.
- Both pages are `InteractiveAuto` (they have `@onclick`/`@bind`) with `[PersistentState]` +
  `Initialized` guard against prerender double-load. Server DI (Nova/Program.cs) and WASM DI
  (Nova.Client/Program.cs) for all four campaign services already exist.
- `/campaigns` uses `[Authorize(Policy = Policies.RequireClubMember)]` (evaluators browse);
  `/campaigns/new` uses `[Authorize(Policy = Policies.RequireClubAdmin)]`. Admin-only controls are
  gated with `principal.IsInRole(Roles.ClubAdmin)` exactly like `Players.razor.cs` (`_canManagePlayers`).
- The list page has an Active/Closed view select (default Active) bound to
  `GetCampaignListInput.Status`, mirroring the Players lifecycle filter. Rows show name, dates,
  status, participant count, unresolved count. Truncation notice when `TotalCount` exceeds rows.
- Correction controls live inline on `/campaigns` (no campaign detail page exists; workspace is
  epic #10). Active campaigns get an Edit action (name, season select, start, planned end). Season
  groups get an admin Edit-season action (name, start, end). Closed campaigns show a read-only note
  explaining reopening is required (no mutation controls). A 409 Conflict from the metadata service
  (campaign closed concurrently) renders a conflict alert with reload.
- The campaign edit form's season dropdown reuses `ICampaignQueryService.GetCreationSetupAsync`
  (admin-only endpoint) for season choices.
- `/campaigns/new` shows live `ActivePlayerCount`/`ActiveTeamCount` preview before submission,
  explains the campaign becomes Active immediately and the planned end date does not close it,
  supports existing-season select or inline season fields (exactly-one XOR per
  `CreateCampaignInput.Validate`), generates `OperationId` per form session (idempotent retries reuse
  the same id), and navigates to `/campaigns` on success (no workspace route before epic #10).
- Form-state classes (`CampaignCreateFormState`, `CampaignMetadataFormState`,
  `SeasonMetadataFormState`) are mutable, implement `IValidatableObject`, and delegate to
  `InputValidator.Validate(...)` over the shared input records (single source of truth). Forms are
  dumb child components with `Model`, `IsSubmitting`, `ErrorMessage`, `OnValidSubmit`,
  `OnCancel` like `TeamForm`; pages own submit and mutation state.
- Navigation: add a `Campaigns` NavLink before `Players` in `Nova\Components\Layout\NavMenu.razor`
  (order per `plans/mvp-product-workflows.md`: Campaigns, Players, Teams). No other nav ownership.

## Phase 1: Campaign list page `/campaigns`

Status: Complete

- [x] Create `Nova.UI\Features\Campaigns\Pages\Campaigns.razor(.cs)` — `@page "/campaigns"`,
      `InteractiveAuto`, `RequireClubMember`, Active/Closed view select, season-grouped rendering
      (season heading with name/dates; campaign rows with name, start, planned end, status badge,
      participant count, unresolved count), loading/empty/error+retry states, truncation notice,
      `[PersistentState]` + `Initialized` guard, `ComponentCancellationToken` everywhere, Forbidden
      → `/Account/AccessDenied` forceLoad redirect.
- [x] Add admin-only "Create campaign" button navigating to `/campaigns/new`; hidden for evaluators.
- [x] Add `Campaigns` NavLink to `NavMenu.razor` (before Players); update `NavMenuTests` assertions.

### Verification Plan

- `dotnet build Nova.UI\Nova.UI.csproj` succeeds with no warnings-as-errors. **Result: PASS** (0 warnings, 0 errors).
- `dotnet test --project Nova.Unit.Tests --filter-class "*NavMenuTests"` (MTP) passes, including the new
  Campaigns link assertions. **Result: PASS** (2/2).

### Phase Summary

Added `Campaigns.razor(.cs/.css)` with season-grouped Active/Closed views (view select backed by
`?view=closed` query param, applied once in `OnParametersSet`), role gating via
`Roles.ClubAdmin`, persisted prerender state, bounded load (`GetCampaignListInput.MaxLimit`) with
truncation notice, role-aware empty states, and a "Closed — reopen to edit" note for closed rows
(edit controls arrive in Phase 3). Dates format as `MMM d, yyyy`; status badges reuse the existing
`text-bg-success`/`text-bg-secondary` convention. NavMenu gained a Campaigns link before Players;
NavMenuTests assert it in both club and no-club cases.

## Phase 2: Create campaign page `/campaigns/new`

Status: Complete

- [x] Create `Components\CampaignCreateForm.razor(.cs)` with `CampaignCreateFormState` (name, start,
      planned end, season mode toggle, existing season id, inline season name/start/end) delegating
      validation to `InputValidator.Validate(ToCreateInput())`; `ValidationResult` member names map to
      form properties so `ValidationMessage For` renders per field.
- [x] Create `Pages\NewCampaign.razor(.cs)` — `@page "/campaigns/new"`, `InteractiveAuto`,
      `RequireClubAdmin`; loads `GetCreationSetupAsync` with persisted-state guard; renders live
      Active-player enrollment and Active-team availability counts; explanatory copy (Active
      immediately; planned end date does not close the campaign); inline season option even when no
      seasons exist; empty/error+retry states for setup load; submit via `ICampaignCreationService`
      with a per-form `OperationId` (regenerated after success, reused on retry); success → navigate
      to `/campaigns`; validation/conflict/problem details surfaced as form alerts.
- [x] Evaluator navigation to `/campaigns/new` is blocked by the page policy (no UI link shown).

### Verification Plan

- `dotnet build` the solution succeeds. **Result: PASS** (Nova.UI build, 0 warnings/errors).
- Manual smoke (deferred to Phase 5 browser pass): form renders, preview counts show, validation
  blocks empty name / end-before-start / missing season choice.

### Phase Summary

Added `CampaignCreateForm` (dumb child component, TeamForm-style local clone + `EventCallback`
submit) and `NewCampaign` page. `CampaignCreateFormState` delegates all rules to
`CreateCampaignInput` via `InputValidator` and maps `InlineSeason.*` member names onto the flat form
properties. The page assigns `OperationId` (`Guid.CreateVersion7()`) once per form session so retries
stay idempotent, shows the live Active-player/Active-team preview card and the "Active immediately /
planned end does not close" explainer, and navigates to `/campaigns` on success. The existing-season
radio is disabled when no seasons exist, forcing inline creation.

## Phase 3: Administrator metadata correction on `/campaigns`

Status: Complete

- [x] Create `Components\CampaignMetadataForm.razor(.cs)` with `CampaignMetadataFormState`
      (campaign id, name, season select, start, planned end) delegating to
      `UpdateCampaignMetadataInput` validation.
- [x] Create `Components\SeasonMetadataForm.razor(.cs)` with `SeasonMetadataFormState`
      (season id, name, start, end) delegating to `UpdateSeasonMetadataInput` validation.
- [x] Wire into `Campaigns` page: admin-only Edit action per Active campaign and per season group;
      season choices loaded via `GetCreationSetupAsync` when an edit begins; success updates via
      `ICampaignMetadataService`/`ISeasonMetadataService`, sets status message, reloads list without
      wiping the success message; 409 Conflict → conflict alert explaining the campaign is Closed and
      must be reopened, with reload affordance; Forbidden → AccessDenied redirect.
- [x] Closed campaigns/seasons render a read-only note ("reopen required to edit") and no edit
      controls; evaluators never see any mutation control.

### Verification Plan

- `dotnet build` the solution succeeds. **Result: PASS** (full solution, 0 errors; only pre-existing
  NU1903 package-vulnerability warnings remain).
- Targeted bUnit coverage added in Phase 4 exercises these states; Phase 5 browser pass exercises the
  full correction flow. **Result: PASS** (see Phase 4).

### Phase Summary

Added `CampaignMetadataForm`/`SeasonMetadataForm` (dumb child components with local clone +
`EventCallback` submit) and wired them into the `Campaigns` page: Edit per Active row and Edit season
per season group, both admin-gated. Season choices load lazily via `GetCreationSetupAsync`. Conflict
responses keep the form open with the service's Closed/reopen explanation; success sets a status
message and reloads the list (the reload helper never clears `_statusMessage`). Closed campaigns show
a "Closed campaigns are read-only; reopen to edit." note under the status badge for all users and no
row actions.

## Phase 4: bUnit component tests

Status: Complete

- [x] `Nova.Unit.Tests\Campaigns\CampaignComponentsTests.cs`: role-based action visibility (admin vs
      evaluator), render-mode assertions for `Campaigns` and `NewCampaign` pages
      (`InteractiveAutoRenderMode`), loading/empty/error states, validation display (empty name,
      end-before-start, XOR season choice), persisted-state restore does not re-call services,
      server error text reaches child form `ErrorMessage` (assert markup and absence of the literal
      field-name string), Closed campaign shows read-only/reopen note and no edit buttons.
- [x] Update `NavMenuTests` for the Campaigns link (done in Phase 1, verify here).

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*CampaignComponentsTests"` passes; NavMenu
  filter passes. **Result: PASS** (26/26 campaign tests; 2/2 NavMenu tests).
- `dotnet test --project Nova.Unit.Tests` full suite passes. **Result: PASS** (1015/1015).
- `dotnet build` the solution with zero new warnings. **Result: PASS** (0 errors; only pre-existing
  NU1903 warnings).

### Phase Summary

26 component tests covering both pages and all three forms: render-mode file assertions (repo
convention reads the `.razor` source), loading/empty/error+retry states, role matrix (evaluator sees
no Create/Edit controls), Active/Closed view switching with correct `GetCampaignListInput.Status`
values, season grouping with counts, truncation notice, closed-campaign read-only state, metadata
update success/conflict paths, persisted-state restore without service re-calls, creation with
existing vs inline season, creation conflict feedback, post-create navigation to `/campaigns`, and
the no-seasons inline fallback. One behavioral note discovered: DataAnnotations skips
`IValidatableObject` when attribute errors exist, so cross-field messages (XOR season choice,
end-before-start) only render once attribute-level fields are valid — tests reflect that
(intentional, matches server behavior).

## Phase 5: Aspire + Playwright acceptance pass

Status: Complete

- [x] Run the skill's procedure against the AppHost; as administrator: browse `/campaigns` Active and
      Closed views; open `/campaigns/new`, observe live preview counts, create a campaign with an
      inline season, confirm it appears Active in the list with the correct participant count; create
      one with an existing season; correct Active campaign metadata and season metadata; confirm a
      Closed campaign (seed/close one via service or existing data) shows the reopen-required state.
- [x] As evaluator (approved non-admin member): `/campaigns` browsable, no Create/Edit controls,
      `/campaigns/new` forbidden.
- [x] Keyboard accessibility spot-check (tab order, labels, focus) and responsive layout at a narrow
      viewport; no unhandled browser console errors.

### Verification Plan

- Playwright: each scenario above passes against the live Aspire URL. **Result: all scenarios PASS.**

### Phase Summary

Ran the full pass against an isolated AppHost (`aspire start --isolated`), registering an
administrator and an evaluator through the real UI (including required profile photo), creating a
club, and approving the evaluator's join request.

Scenario results (all PASS):
- Admin empty state, navigation entry, and view select on `/campaigns`.
- `/campaigns/new` live preview counts (0/0 initially; 1 player / 1 team after seeding roster data).
- Client validation blocks empty campaign name before submit.
- Inline season creation: campaign appears Active in the list under the new season with correct
  dates and participant count.
- Existing-season creation, including a server contextual validation error
  ("A campaign in a finite season must have a planned end date.") rendered from field-level
  ProblemDetails, then successful retry (same OperationId).
- Auto-enrollment of a player added after campaign creation reflected in both campaigns' counts.
- Admin campaign metadata correction (rename + start date) and season metadata correction (rename),
  both showing success messages and reloading the list without losing feedback.
- Closed view (campaign closed via direct SQL since close UI is epic #12): closed campaign listed
  with badge, "Closed campaigns are read-only; reopen to edit." note, and zero row actions for both
  roles; evaluator sees no mutation controls anywhere and is denied `/campaigns/new`
  (AccessDenied redirect).
- Keyboard traversal follows field order (repeated stops are native date-input segments);
  390px viewport has no page-level horizontal overflow (tables scroll inside `table-responsive`).
- No unhandled console errors in campaign flows (the two logged errors were the intentional 400
  validation probe and a cropper error from an intentionally invalid 1x1 test image).

Blockers found and fixed during the pass:
1. `CampaignCreateForm.razor`: raw `<input type="radio" checked="@bool">` did not update the live
   DOM checked property after user interaction — replaced with `InputRadioGroup<bool>` /
   `InputRadio` bound to `UseInlineSeason` (inline season fields were unreachable in the real
   browser despite passing bUnit). bUnit tests updated to use the radio string value (`"True"`).
2. `NewCampaign.razor.cs` / `Campaigns.razor.cs`: field-level validation problems with empty
   `Detail` rendered the generic fallback; added `FlattenValidationErrors` (precedent:
   `ProfilePhotoEditor`) so contextual server rules like the finite-season planned-end requirement
   are visible. Regression test added.
3. `Campaigns.razor`: added `text-nowrap` to the Dates cell to avoid per-word wrapping on mobile.

Cleanup: AppHost stopped; `.playwright-mcp/` and the temporary avatar PNG/screenshot removed.

## Final Recap

Issue #56 is complete. Two new interactive pages and three form components deliver the campaign
workflows on top of the existing server/HTTP services (no server or contract changes were needed):

- `Nova.UI/Features/Campaigns/Pages/Campaigns.razor(.cs/.css)` — season-grouped `/campaigns` list
  with Active/Closed views, counts (participants, unresolved), truncation notice, role-aware empty
  states, admin-only Create/Edit entry points, inline Active campaign and season metadata correction,
  conflict messaging for Closed campaigns, and read-only reopen notes on closed rows.
- `Nova.UI/Features/Campaigns/Pages/NewCampaign.razor(.cs)` — admin-only `/campaigns/new` with live
  enrollment/team preview counts, Active-immediately explainer, existing-season select or inline
  season creation, per-session `OperationId` idempotency, and navigation to `/campaigns` on success.
- `CampaignCreateForm`, `CampaignMetadataForm`, `SeasonMetadataForm` — dumb TeamForm-style child
  components whose form-state classes delegate all validation to the shared input records via
  `InputValidator`.
- `NavMenu.razor` gained a Campaigns link (before Players, per the product navigation order).

Coverage: 52 bUnit tests (`CampaignComponentsTests`) plus NavMenu assertions; full suite 1041/1041;
solution build clean, and the whole-solution `dotnet format --verify-no-changes` passes. The Aspire +
Playwright pass validated
every acceptance criterion for both roles and fixed three real browser blockers (InputRadioGroup
binding, field-error flattening, mobile date wrapping). Three review passes added hardening:
query-param-before-load and stale-load versioning, transport-failure retryability, edit-selection
versioning, view-switch form dismissal (including same-component query navigation), season-cache
invalidation, accessible action names, and season-truncation disclosure.

## Deployment Plan

1. Merge this branch into `main` via PR (no migrations, no config changes, no new packages).
2. Pre-merge verification: `dotnet build Nova.slnx`; `dotnet test --project Nova.Unit.Tests`.
3. Optional smoke: `aspire start --isolated --non-interactive`, `aspire wait nova --non-interactive`,
   then as a club admin create a campaign from `/campaigns/new` and confirm it appears on
   `/campaigns`.
4. No data migration or rollback considerations; reverting the merge fully removes the feature.
