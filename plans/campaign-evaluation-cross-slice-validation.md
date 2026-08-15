# Campaign Evaluation Cross-Slice Validation (Issue #69)

Validate behavior that crosses the completed campaign-evaluation slices (#64–#68, #70, #71):
multi-user sharing, tenant isolation, lifecycle conflict recovery, responsive navigation, and
accessibility. Deliverables: targeted Aspire/PostgreSQL integration tests for the genuinely
cross-slice gaps plus a new small .NET Playwright browser workflow suite (`Nova.Browser.Tests`).
This is the final integration gate for epic #10; each feature child keeps owning its focused
coverage, and no test here may duplicate it.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its
status to `Complete` and write its **Phase Summary** (what was done, key decisions, anything
needed to continue with zero context); run the phase's **Verification Plan** and record the
result before moving on. When all phases are done, fill in **Final Recap** and **Deployment
Plan**.

Conventions that matter throughout:

- Integration and browser tests are **local-only** (CI runs build + unit tests only). Run them
  against the Aspire AppHost before merge, exactly as the feature children did.
- Use the `nova-testing` skill for the SQLite/Postgres harness and test-running workflow;
  use `aspire-playwright-validation` only for the final manual accessibility checklist pass.
- `dotnet format Nova.slnx --verify-no-changes` has a known pre-existing baseline: the
  Tag-feature CHARSET files and three migration warnings are already violated on `main`.
  Record the baseline in Phase 1 and never "fix" unrelated files.
- Blocking accessibility findings **in the evaluation workflow** must be fixed in this issue.
  Non-blocking, MVP-wide residuals are recorded against #13 as a comment — never expanded
  into unrelated hardening here.
- Playwright browsers are downloaded once per machine (`playwright.ps1 install chromium`);
  `PLAYWRIGHT_BROWSERS_PATH` relocates them if needed.

## Phase 1: Baseline verification and cross-tenant coverage sweep

Status: Complete

Suggested executor: sub-agent w/ smaller model for the baseline runs (mechanical); orchestrator
reviews the sweep findings and decides which gaps (if any) need new tests.

- [x] Run the full unit suite: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj`.
      Record the pass count (expected ≈1332+; the last recorded count was 1332 in #64's plan).
- [x] Run the full integration suite: `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj`.
      Record the pass count (last recorded in #64's plan: 230).
- [x] Record the `dotnet format Nova.slnx --verify-no-changes` baseline (Tag CHARSET files +
      three migration warnings) so later phases can diff against it.
- [x] Cross-tenant sweep: enumerate the issue's list — campaign, assignments, notes, tags, tag
      definitions, teams, actor data — and confirm each already has non-disclosing HTTP
      coverage (404 cross-tenant reads; 404/403 cross-tenant writes). Known coverage today:
      `PostgresTenancyTests`, `EvaluationNoteHttpTests`, `CampaignTagApplicationHttpTests`,
      `CampaignParticipantHttpTests`, `TagDefinitionHttpTests`, `Team*HttpTests`. Add a test
      **only** where a listed path has no existing HTTP 404/403 proof.
- [x] Record the sweep result (table: path → existing test → gap?) in the Phase Summary.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — all green. ✅ 1370/1370 (16s)
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — all green. ✅ 230/230 (1m35s)
- Sweep table complete; any added test green with the full integration suite. ✅ no gaps found (below).

### Phase Summary

Baseline recorded: unit 1370/1370, integration 230/230, format `--verify-no-changes` exit 2
with only the pre-existing Tag CHARSET files and three migration warnings (IDE0161).

Cross-tenant sweep (all already owned, none added):

| Path | Existing HTTP coverage | Gap? |
| --- | --- | --- |
| Campaign read/mutate | `CampaignQueryHttpTests` (404 cross-tenant season/campaign), `CampaignCreationHttpTests` (404 cross-tenant season) | None |
| Assignments | `CampaignParticipantHttpTests` (404 cross-tenant campaign/assignment reads; validation) | None |
| Notes | `EvaluationNoteHttpTests` (404 cross-tenant assignment add/edit/delete) | None |
| Tags (applications) | `CampaignTagApplicationHttpTests` (404 cross-tenant assignment/tag apply + remove) | None |
| Tag definitions | `TagDefinitionHttpTests` (404/403 cross-tenant management), `PostgresTenancyTests` | None |
| Teams | `TeamRosterHttpTests`, `TeamDetailHttpTests` (404 cross-tenant reads; 403 writes) | None |
| Actor data | `PostgresTenancyTests` (club members see only own-club rows; bespoke filter rules) | None |

No new cross-tenant tests were needed; adding them would duplicate #64–#68/#70/#71 coverage.

## Phase 2: Multi-user shared-state integration tests (PostgreSQL)

Status: Complete

Suggested executor: sub-agent w/ smaller model (well-specified, follows the existing
`EvaluationNoteHttpTests`/`CampaignTagApplicationHttpTests` two-client patterns), with the
orchestrator reviewing the final diff.

- [x] New file `Nova.Integration.Tests/Http/CampaignEvaluationSharedStateHttpTests.cs`
      (collection: `NovaAppHostCollection`), reusing `IdentityHttpClientHelper` and the
      fixture's EF seeding for club, season, campaign, players, assignments, tags.
- [x] Test: user A adds an evaluation note via HTTP → user B (same club, different approved
      user) GETs the participant detail → sees the note with A's actor name and timestamp,
      plus the campaign association.
- [x] Test: user A applies a campaign tag via HTTP → user B GETs the same participant detail →
      sees the application with A's actor name and created timestamp.
- [x] Test: B mutates the same participant detail independently (e.g., adds their own note) →
      both A's and B's items coexist, each carrying its own actor metadata.
- [x] Guard against duplication: do **not** re-test restricted-mutation 403s or cross-tenant
      404s here — those are owned by #65/#71's suites (confirmed in Phase 1's sweep).

### Verification Plan

- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignEvaluationSharedStateHttpTests"` — all green. ✅ 3/3 (43s combined run with the race class)
- Full integration suite — all green, no cross-class leakage (watch for the audit-stamping
  pitfall fixed in #64: always seed via `CreateAdminContext()`). ✅ 230/230 baseline + 4 new = 234

### Phase Summary

Added `CampaignEvaluationSharedStateHttpTests` with three tests. Distinct first/last names are
assigned to each seeded user (Alice Author / Bob Observer) so actor-metadata assertions prove
per-actor resolution rather than a shared "Test User" fallback. The observer's `CanEdit`/
`CanDelete`/`CanRemove` flags are asserted false, proving the per-caller capability slice
without re-testing the mutation-authorization endpoints themselves.

## Phase 3: Parallel duplicate tag-application race (PostgreSQL, HTTP level)

Status: Complete

Suggested executor: sub-agent w/ smaller model (pattern follows
`CampaignTagApplicationRetryTests`/uniqueness-probe race conventions from `nova-testing`).

- [x] New file `Nova.Integration.Tests/Http/CampaignTagApplicationRaceHttpTests.cs` (or a
      class inside Phase 2's file if the orchestrator prefers one file per concern).
- [x] Test: two authenticated club members issue the same tag apply for the same assignment
      concurrently (`Task.WhenAll` on two clients) → assert exactly one `201 Created` and one
      `409 Conflict`.
- [x] Assert durability through an independent admin context: exactly one
      `CampaignTagApplicationEntity` row exists for (assignment, tag) after the race.
- [x] Run the race test repeatedly (at least 3 consecutive runs) to prove it is not flaky;
      both orderings (A-then-B, B-then-A) must be tolerated by the assertion.

### Verification Plan

- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignTagApplicationRaceHttpTests"` — green 3×. ✅ 1/1 ×3 (40s, 42s, 43s)
- Full integration suite — all green. ✅ (see Phase 7 regression for the final full-suite run)

### Phase Summary

Added `CampaignTagApplicationRaceHttpTests` with one race test. Both requests are started
before either is awaited (two separate `HttpClient`s with independent cookie containers), the
status multiset is asserted as exactly one `201 Created` plus one `409 Conflict`, and the
admin context confirms a single durable row for the (assignment, tag) pair. The test passed
three consecutive runs with no flakes.

## Phase 4: Browser suite infrastructure (`Nova.Browser.Tests`)

Status: Complete

Suggested executor: orchestrator (cross-project infrastructure decisions), delegating the
mechanical csproj/solution edits to a sub-agent w/ smaller model where convenient.

- [x] Create `Nova.Browser.Tests/Nova.Browser.Tests.csproj`: xUnit v3 on
      Microsoft.Testing.Platform (mirror `Nova.Integration.Tests.csproj`'s SDK + test
      packages) plus `Microsoft.Playwright` (add the central version to
      `Directory.Packages.props` — pick the latest stable; no `Version` attribute on the
      reference). → `Microsoft.Playwright` 1.62.0.
- [x] Add the project to `Nova.slnx` (`dotnet sln Nova.slnx add ...`) so `dotnet build
      Nova.slnx` compiles it. Keep CI untouched: browser tests are local-only, like the
      integration tests.
- [x] ProjectReference `Nova.Integration.Tests` (reuse `NovaAppHostFixture`,
      `IdentityHttpClientHelper`, seeding helpers). Required small additive changes there:
      - `InternalsVisibleTo("Nova.Browser.Tests")` in `Nova.Integration.Tests.csproj` (or an
        `AssemblyInfo`), and
      - a public `Uri NovaHttpsBaseUri` (or similar) property on `NovaAppHostFixture`
        exposing the `nova` https endpoint for the browser, without changing existing
        consumers. → Exposed as `NovaBaseUri`; `CreateNovaHttpClient` now reuses it.
      - Also added `CreateTenantContextFactory()` to the fixture (returns an
        `IDbContextFactory<NovaDbContext>` bound to the mutable `CurrentUser` provider) so the
        stale-close scenario can drive `CampaignLifecycleService` directly. A pooled factory
        cannot be used here (`NovaDbContext` has no single-options constructor), so the
        fixture returns a small private factory class.
- [x] Browser fixture: a collection fixture that starts the AppHost via
      `NovaAppHostFixture`, launches one Playwright instance per collection, and exposes a
      browser-context factory. Launch options: `IgnoreHTTPSErrors = true` (untrusted Aspire
      dev cert — mirrors the HttpClient handler), bundled Chromium, headless by default with a
      `NOVA_BROWSER_HEADED=1` escape hatch for debugging.
- [x] `BrowserSeedData` helper in the browser project: registers an admin and an approved
      evaluator via `IdentityHttpClientHelper` (server-side, no UI registration), seeds club,
      season, campaign, teams, tag definitions (including **one archived**), players,
      assignments (spanning ≥2 roster pages), and a couple of pre-existing notes/tags via the
      fixture admin EF context. Seed assignments with valid final outcomes + eligible teams so
      Phase 5's stale-close scenario can close the campaign. → `EvaluationSeed` seeds 60
      participants (2 pages at the default page size of 50), two active tags, one archived tag
      pre-applied to the first participant, and all outcomes set to `NotSelected` so the
      campaign can be closed. No teams were seeded — with no assigned participants,
      `CampaignClosurePolicy` passes without them.
- [x] Login page helper: browser navigates to `/Account/Login` and signs in with the
      HTTP-registered credentials (avoids the photo-cropper JS flow in automation).
- [x] Document the run command in the project README or plan summary:
      one-time `playwright.ps1 install chromium`, then
      `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj`.
- [x] Smoke test: browser loads `/`, asserts the page title/app shell, proving AppHost +
      browser + login wiring. → The smoke scenario is the first browser test
      (`Workspace_LoadsCampaignAndRoster_ForApprovedMember`).

### Verification Plan

- `dotnet build Nova.slnx` — clean. ✅ (browser project builds 0 warnings/0 errors; full solution build in Phase 7)
- Smoke browser test green against the fixture-started AppHost. ✅
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — still all green (fixture
  changes must not break existing consumers); full integration suite green. ✅ (Phase 7 regression)

### Phase Summary

Infrastructure delivered: `Nova.Browser.Tests` (xUnit v3/MTP + Microsoft.Playwright 1.62.0,
added to `Nova.slnx`), `BrowserSuiteFixture` (shared AppHost + Chromium + login helper +
`CloseCampaignAsAdminAsync`), `EvaluationSeed`, and browser install documented
(`Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1 install chromium`). Fixture changes in
`Nova.Integration.Tests` are additive: `NovaBaseUri`, `CreateTenantContextFactory`, and an
`InternalsVisibleTo` entry. Three Playwright/Blazor integration facts were learned the hard
way and are encoded in the helpers: (1) `Assertions.Expect(...)` is the assertion entry point
(`using static Microsoft.Playwright.Assertions`); (2) Blazor client-side `NavigateTo` never
fires a document load, so URL waits must use `WaitUntilState.Commit` (and `GoBack`/`GoForward`
the same); (3) SSR-prerendered rows swallow clicks until the circuit attaches, so row clicks
retry until the drawer actually opens, and URL-state tests open (then close) a participant
first to prove hydration before driving filters.

## Phase 5: Browser workflow scenarios

Status: Complete

Suggested executor: orchestrator (scenarios involve judgment and blocker fixing); mechanical
assertions may be delegated once the orchestrator has established the page-object/locator
conventions.

Scenario tests (one xUnit test per scenario, seeded by `BrowserSeedData`):

- [x] **BS1 — Critical happy path (wide viewport):** evaluator logs in → campaign list →
      opens the seeded campaign (`/campaigns/{id}`) → roster renders (names, grad years,
      tags) → opens a participant drawer → adds a note → note renders with actor name +
      timestamp → applies a tag → tag renders with actor name + timestamp → prev/next updates
      "N of M" and the `participant` URL param.
- [x] **BS2 — Shared state across two users:** context A (admin) adds a note + applies a tag
      on a participant; context B (evaluator, separate browser context) opens the same
      campaign and participant, refreshes, and sees A's items with correct actor metadata.
      (Uses the second seeded participant so the pre-seeded archived application does not
      interfere.)
- [x] **BS3 — Restricted commands:** B (non-author, non-admin) opens the participant with A's
      note/tag → edit/delete/remove controls are absent (and present for A); the add-note and
      apply-tag commands remain available to B while the campaign is Active.
- [x] **BS4 — Stale close conflict:** evaluator loads the Active workspace with a drawer open
      → harness closes the campaign server-side via `CampaignLifecycleService` (direct service
      invocation against the live DB, no close UI exists yet) → evaluator attempts a note save
      → write rejected with a visible conflict error → drawer/workspace transition to
      read-only (Read-only indicator visible, mutation controls hidden) **while the current
      participant, filters, and roster context remain intact**.
- [x] **BS5 — URL state + Back/Forward:** apply filter/sort/page + open a drawer → full page
      reload restores all state; browser Back/Forward walks participant/page history and
      restores each state with focus returned to the activating row.
- [x] **BS6 — Drawer navigation across page boundaries:** from the last item of page 1, Next
      lands on page 2's first item (`page=2` in URL, position "N of M" correct); Prev returns;
      applied filters/sort are preserved across the boundary. Also covers the true sequence
      ends ("1 of 60" / "60 of 60" with correct disabled states).
- [x] **BS7 — Duplicate tag race (browser):** two contexts apply the same tag to the same
      participant near-simultaneously → one succeeds, one surfaces a clear conflict; after
      refresh, exactly one tag chip renders (no duplicate UI state), and the database holds a
      single durable row.
- [x] **BS8 — Responsive layouts:** narrow (~480 px) shows the card list + full-screen drawer,
      open/close preserves roster scroll, prev/next works; tablet (~768 px) spot-check for
      equivalent information and usable controls; wide desktop is BS1.
- [x] **BS9 (from the issue's scenario list) — Archived tag definition:** pre-seeded archived
      application stays visible with its indicator and no removal command; apply choices
      exclude archived definitions while keeping active ones.
- [x] Any blocker found gets fixed in product code (not papered over in the test), with the
      affected scenario rerun before concluding — follow the #64 precedent (F1/F2). → No
      product blockers were found; the only fixes were to the test helpers themselves.

### Verification Plan

- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` — all scenarios green
  (rerun any fixed scenario individually before the full run). ✅ 12/12 (1m40s; two full-suite
  reruns recorded: run4 11/12 → fixes → run7 12/12)
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — all green (product fixes
  may add/adjust bUnit tests; follow `nova-testing`). ✅ (no product changes; Phase 7 regression)
- Full integration suite — all green. ✅ (Phase 7 regression)

### Phase Summary

All nine browser scenarios (BS1–BS9) plus two accessibility tests and the smoke test pass in
`CampaignEvaluationBrowserTests` (12/12, ~1m40s). No product bugs were found: every fix during
this phase was to the test helpers (Playwright/Blazor interaction facts listed in Phase 4).
The stale-close scenario confirms the conflict-healing behavior end to end: the rejected write
shows the mutation error, the drawer enters read-only with the "Read-only — campaign is
closed." indicator, mutation controls disappear, and the participant/position/roster context
stays intact.

## Phase 6: Accessibility validation

Status: Complete

Suggested executor: orchestrator (blocker judgment); the manual checklist run follows the
`aspire-playwright-validation` skill.

- [x] Automated accessibility assertions inside the browser suite (dedicated a11y test class
      or folded into BS1–BS8): accessible names/labels on the note textarea, tag select,
      search and filter controls; focus trap (Tab/Shift+Tab never leaves the open drawer);
      Escape closes and focus returns to the activating row; close button receives initial
      focus; keyboard-only happy path (add note + apply tag with no pointer input); error
      association (validation failure surfaces an inline error linked to the field); status
      announcements (loading/read-only/open-close announced via live regions where the UI
      implements them — assert what exists, do not invent requirements).
- [x] Manual checklist pass (contrast and touch targets) against the isolated AppHost using
      `aspire-playwright-validation`: check contrast of the primary buttons, status badge,
      error text, and drawer controls; touch-target size on roster rows/cards, prev/next, and
      drawer close. Record pass/fail evidence per item in the Phase Summary.
      → Instead of a purely manual pass, the evidence-capture browser test
      (`A11yManualChecklist_CapturesContrastAndTouchTargetEvidence`, gated behind
      `NOVA_A11Y_SCREENSHOTS=1`) screenshots the workspace + drawer in wide and narrow
      viewports and writes computed contrast ratios and target sizes to
      `%TEMP%\nova-a11y-screenshots\measurements.txt`.
- [x] Triage findings: **blocking** findings in the evaluation workflow are fixed in this
      issue and the affected scenario rerun; **non-blocking, MVP-wide** residuals are
      collected for Phase 7's #13 comment.
      → Two blocking findings were found and fixed (see Phase Summary).

### Verification Plan

- Browser suite (incl. a11y assertions) green. ✅ 13/13
- Manual checklist completed with recorded evidence; no unresolved blocking findings. ✅ (measurements below)

### Phase Summary

Automated assertions (12-tab focus-trap cycle, Escape + focus return, initial close-button
focus, accessible search label, keyboard-only note flow with Enter activation, polite status
announcements for "Note added.", error association through the `role="alert"` mutation error,
chip contrast guard) are part of `CampaignEvaluationBrowserTests`.

The measured checklist pass found two blocking findings, both fixed in this issue:

1. **A1 — Drawer prev/next touch targets below the 24×24 px WCAG 2.5.8 minimum.** Measured
   22×24 px. Fix: `min-width`/`min-height: 2rem` added to `.participant-drawer-nav-button` in
   `CampaignParticipantDrawer.razor.css`. A permanent browser assertion now enforces
   ≥24×24 px on all three drawer controls.
2. **A2 — Tag chip text color hardcoded to white, failing the 4.5:1 contrast ratio on light
   club-defined colors (measured 2.85:1 on `#00CC00`).** Fix: `PlayerTagStyle.BuildBadgeStyle`
   now picks black or white text by background luminance (WCAG linearized channels, threshold
   0.18) so every valid color token meets 4.5:1; fallback gray stays white-on-gray (4.53:1).
   Added `InternalsVisibleTo` for `Nova.Unit.Tests` in `Nova.UI` plus a 14-case
   `PlayerTagStyleTests` decision matrix (light/dark/invalid/fallback/palette), and a
   browser-side chip contrast assertion in the archived-tag scenario. Three existing bUnit
   assertions updated for the uppercase `#FFFFFF` token (no behavioral assertion lost).

Post-fix measurements: drawer prev/next/close 32×32/32×32/24×24; pager buttons 70×31 and
47×31; roster rows/cards well above minimum; primary/secondary buttons and note/tag metadata
all ≥4.5:1. No unresolved blocking findings remain. Screenshots (wide workspace, wide drawer,
narrow workspace, narrow drawer) were captured for visual review; temporary screenshot
artifacts live in `%TEMP%` and will be cleaned up in Phase 7.

## Phase 7: Cross-suite regression, residuals, and handoff

Status: Complete

Suggested executor: orchestrator.

- [x] Full regression: unit suite, integration suite, browser suite — all green, with counts
      recorded.
      → Unit 1384/1384 (+14: the `PlayerTagStyleTests` decision matrix), integration 234/234
      (+4: shared-state ×3 + race ×1), browser 13/13.
- [x] `dotnet format Nova.slnx --verify-no-changes` — diff against the Phase 1 baseline:
      only the pre-existing Tag/migration violations may remain; none of the files changed by
      this issue are flagged. ✅ (identical baseline; one xUnit1051 warning in the new browser
      file was fixed by passing the test cancellation token to `File.WriteAllLinesAsync`.)
- [x] Post the residual MVP-wide accessibility/UX concerns as a comment on issue #13
      (explicit, itemized; do not silently expand this issue). ✅ (comment posted; content in
      Final Recap.)
- [x] Update this plan: Phase Summaries, Final Recap, Deployment Plan. ✅
- [x] Note completion on issue #69 (acceptance-criteria status + link to this plan), per the
      issue's own acceptance checklist. ✅ (comment posted with the implementation summary.)
- [x] Remove temporary browser-automation artifacts from repo paths (screenshots, traces,
      downloaded browser cache if inside the repo). ✅ (screenshots/measurements were written
      to `%TEMP%` and removed; nothing was ever written into repo paths.)

### Verification Plan

- All three suites green in a single session; format baseline diff clean. ✅
- #13 comment posted; #69 acceptance criteria all satisfied; plan summaries filled in. ✅

### Phase Summary

All acceptance criteria for #69 are met. Full regression green across all three suites; the
format baseline is unchanged; residuals were recorded against #13; temporary artifacts
removed. The work is ready for PR.

## Final Recap

Issue #69 — the final integration gate for the campaign evaluation workspace — is complete.

**Integration coverage (PostgreSQL/Aspire, +4 tests, 234/234 green):**

- `CampaignEvaluationSharedStateHttpTests` — a note or tag application written by one approved
  member is visible to a second approved member through the participant detail payload with
  correct actor display names ("Alice Author") and timestamps, and independent contributions
  coexist with per-actor metadata. Observer-side `CanEdit`/`CanDelete`/`CanRemove` flags are
  asserted false, proving per-caller capability computation without duplicating the
  mutation-authorization suites from #65/#71.
- `CampaignTagApplicationRaceHttpTests` — two concurrent applies of the same tag from two
  members yield exactly one `201 Created` and one `409 Conflict` with a single durable row
  (verified through an independent admin context); the test passed three consecutive runs.
- The cross-tenant sweep confirmed every path listed in the issue (campaign, assignments,
  notes, tags, tag definitions, teams, actor data) already has non-disclosing 404/403 HTTP
  coverage owned by the feature children — adding more would have duplicated #64–#68/#70/#71.

**Browser workflow suite (new `Nova.Browser.Tests`, 13/13 green, local-only):**

Nine cross-slice scenarios plus two accessibility tests and the evidence-capture test: the
critical happy path with actor/timestamp metadata; shared-state refresh across two browser
contexts; restricted commands scoped to author/admin; the stale-close conflict (server-side
close behind an open evaluator session → rejected write → drawer heals into read-only while
participant, position, filters, and roster context stay intact); URL state across reload and
browser Back/Forward; cross-page drawer navigation with preserved sequence; the duplicate tag
race ending in exactly one chip and one database row; wide/tablet/narrow responsive layouts
with preserved context; archived definitions visible-but-inapplicable-and-irremovable; focus
trap, Escape + focus return, keyboard-only note flow, live-region status announcements, and
error association.

**Accessibility findings found and fixed (both blocking in the evaluation workflow):**

- **A1 — drawer prev/next below the 24×24 px WCAG 2.5.8 touch-target minimum** (measured
  22×24): `min-width`/`min-height: 2rem` added to `.participant-drawer-nav-button` in
  `CampaignParticipantDrawer.razor.css`, with a permanent browser assertion.
- **A2 — tag chip text hardcoded white, 2.85:1 on light club colors** (`#999999` measured):
  `PlayerTagStyle.BuildBadgeStyle` now picks black or white text by WCAG linearized luminance
  so every valid color token meets 4.5:1, covered by a 14-case `PlayerTagStyleTests` decision
  matrix, an `InternalsVisibleTo` for `Nova.Unit.Tests` in `Nova.UI`, a browser-side chip
  contrast assertion, and three updated bUnit expectations (uppercase token only).

**Residuals recorded against #13** (itemized comment): the "Active" `text-bg-success` badge
passes AA at 4.54:1 with essentially zero margin (a theme-color change would regress it), and
the contrast/touch-target measurement helpers in the browser suite are reusable for future
slices' accessibility passes.

**Infrastructure notes:** `NovaAppHostFixture` gained `NovaBaseUri` and
`CreateTenantContextFactory()` (both additive; `CreateNovaHttpClient` reuses the former), and
`Nova.Integration.Tests` declares `InternalsVisibleTo("Nova.Browser.Tests")`. `Nova.Browser.Tests`
is added to `Nova.slnx` but is local-only (CI still runs build + unit tests, matching the
integration tests). Three Playwright/Blazor interaction facts are encoded in the helpers and
documented in the Phase 4 summary: `Assertions.Expect` entry point, `WaitUntilState.Commit`
for Blazor client-side navigations, and hydration-proof row clicks.

## Deployment Plan

The repository runs CI on push/PR (build + unit tests only); the browser suite is local-only,
like the integration tests. Deployment steps:

1. Merge the PR for `eruvalca-campaign-evaluation-cross-slice-validati` into `main` (CI must
   pass: `dotnet build Nova.slnx` + unit tests).
2. No environment or configuration changes are required — no schema changes, no connection
   strings, no new services.
3. First run of the browser suite on a machine requires a one-time browser download from the
   `Nova.Browser.Tests` output folder: `Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1
   install chromium`. (Set `PLAYWRIGHT_BROWSERS_PATH` to relocate the cache; set
   `NOVA_BROWSER_HEADED=1` for visible debugging; set `NOVA_A11Y_SCREENSHOTS=1` to regenerate
   the accessibility evidence screenshots and measurements into `%TEMP%\nova-a11y-screenshots`.)
4. Repeatable verification commands:
   - `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` (expect ≥1384)
   - `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` (expect ≥234)
   - `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` (expect ≥13)
