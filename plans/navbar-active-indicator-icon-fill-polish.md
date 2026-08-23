# Navbar Polish: Flush Active Indicator, Filled Icons, Stacked Icon-Above-Label Layout

Follow-up to issue #134 (bottom navbar redesign, merged in #136). Bring the NavMenu closer to the reference mockup: (1) align the active kelp-teal indicator bar flush with the **top edge of the navbar** instead of floating in between the icons and the navbar top, (2) fill the Bootstrap icon of the active nav item (e.g. `bi-house-fill`), and (3) stack the icon above the label on desktop with more spacing between items, matching the reference layout. Mobile (<md) keeps the current inline icon+label row inside the collapsed menu.

**Scope decisions (confirmed with user 2026-08-22):**
- Stacked icon-above-label layout applies on **desktop (md+) only**; inline row stays on mobile.
- Active icon uses the **official Bootstrap Icons `-fill` variants** (`house-fill`, `building-fill`, `calendar-check-fill`, `people-fill`, `shield-fill`). Both outline + fill spans are rendered and toggled by CSS off the NavLink's `.active` class — no code-behind interactivity required (NavMenu is static SSR that becomes interactive when the page uses `InteractiveAuto`; `.active` is computed at render time and refreshed by the existing `LocationChanged` subscription).
- Indicator bar stays a **narrow bar above the active item** (not full-width), moved up to be flush with the navbar top edge.
- **Color stays kelp teal** (`--bs-primary`, #0E7C7B) — confirmed with user (2026-08-23): the mockup's blue is *not* to be followed; use the theme colors only. The active label tint (kelp teal vs dark) is a minor implementation-time choice: keep the label dark unless kelp teal looks clearly better; any tinted label must still pass `npm run check:contrast`.
- Logout (`bi-box-arrow-right`) has no `-fill` variant in Bootstrap Icons 1.13.1; it has no active state, so it stays outline (matches the reference).
- No Sass/theme changes needed — this is component CSS + Razor markup only. Expected: `npm run build:css` / `check:contrast` untouched by implementation, but rerun them anyway if any `scss/` change sneaks in.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to `Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue with zero context); run the phase's **Verification Plan** and record the result before moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

Key files: `Nova/Components/Layout/NavMenu.razor` (markup), `Nova/Components/Layout/NavMenu.razor.css` (scoped CSS — note the `::deep` comment explaining why NavLink content must be reached via `::deep`), `Nova/Components/Layout/NavMenu.razor.cs` (unchanged, but `OnLocationChanged`/`StateHasChanged` is what keeps `.active` fresh on client-side navigation), `Nova.Browser.Tests/NavbarBrowserTests.cs` (browser acceptance, must be updated), `Nova.Unit.Tests/Components/NavMenuTests.cs` (bUnit, may add a fill-span assertion).

## Phase 1: Stacked icon-above-label layout with spacing (desktop md+)

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator (single tightly-coupled component; not worth delegating)

- [x] In `NavMenu.razor`, change each authorized `NavLink` (Home, club, Campaigns, Players, Teams) from `class="nav-link d-flex align-items-center gap-2"` to a stack-aware class set: `class="nav-link d-flex align-items-center gap-2 flex-md-column text-md-center"` (flex row/`align-items-center` on mobile via the existing `d-flex align-items-center`; `flex-md-column` stacks on md+; `text-md-center` centers label + icon when stacked).
- [x] Keep the `Manage` NavLink and Logout button inline (no stacking) per the reference — they remain icon+label rows on the right.
- [x] In `NavMenu.razor.css`, add a `@media (min-width: 768px)` block that:
  - increases item spacing via Bootstrap's own variable: `--bs-navbar-nav-link-padding-x: var(--bs-spacer)` (1rem) — or `1.25rem` if 1rem feels too tight vs the reference; pick at implementation time and record in the Phase Summary.
  - gives the stacked links room: `.nav-link { padding: 0.75rem 1.5rem; }`-style vertical padding (tune so the navbar body is ~4-5rem tall like the reference), plus a small label font-size (`0.8125rem`) for `.nav-label` and a slightly larger icon (`1.5rem`) via the existing `.nav-link .bi` rule inside the media query.
- [x] Verify the collapsed `<md` mobile menu still renders the inline row correctly (the `navbar-expand-md` collapse + `d-flex align-items-center` remain intact).

### Verification Plan

- [x] `dotnet build Nova.slnx` succeeds (component compiles; scoped CSS is bundled into `Nova.styles.css` automatically).
- [x] `dotnet format Nova.slnx --verify-no-changes` passes.
- [x] Aspire + Playwright manual check (`aspire-playwright-validation` flow, or the committed browser tests after Phase 3): sign in as the seeded admin, load `/`, assert the navbar `nav.navbar` is present and, at ≥ md viewport, each authorized nav item has its icon vertically stacked above its label (assert `flex-direction: column` on the `.nav-link` and that the icon's bounding box top is above the label's). At a <md viewport (e.g. 375px), the menu collapses behind the toggler; expanding it shows inline icon+label rows.
- [x] Existing `NavbarBrowserTests` NB1 still passes after class-list changes (it locates `span.bi-house` etc. — add `nav-icon` class only if the locator still matches; do not rename glyph classes).

### Phase Summary

- Spacing: `--bs-navbar-nav-link-padding-x: 1rem` in the md+ media query (matches `$spacer`; note Bootstrap 5.3.3 does **not** emit `--bs-spacer` as a CSS variable, so the plan's `var(--bs-spacer)` is replaced with the literal `1rem`). `gap` is *not* overridden: Bootstrap's `gap-2` utility carries `!important`, so a plain `gap` rule cannot win and was dropped.
- Vertical padding: `.nav-link { padding-top/bottom: 0.75rem }` at md+ (horizontal padding left to Bootstrap's var-based rule). Icon `1.5rem`; label `0.8125rem`/`line-height: 1`. Navbar total height at md+ is ~4.7rem (content ~3.2rem + 1rem padding + 1px border), matching the mockup's proportions.
- `MainLayout.razor.css` padding-bottom bumped `5rem` → `6rem` so the fixed-bottom, slightly taller navbar no longer overlaps page content.
- NB1 (unchanged selectors) passes against the live AppHost; NB6 asserts `flex-direction: column` per link at 1280×800.

## Phase 2: Flush active indicator + filled active icon

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator (CSS positioning + markup toggling; small enough to do directly)

- [x] **Indicator flush with navbar top edge**: in `NavMenu.razor.css`, adjust `::deep .nav-link.active::after` from `top: 0` to `top: calc(-1 * var(--bs-navbar-padding-y))` (Bootstrap defines `--bs-navbar-padding-y` on `.navbar`, default `0.5rem`) — or `top: calc(-1 * (var(--bs-navbar-padding-y) + var(--bs-border-width)))` if the bar must also cover the `border-top`. Keep height `3px`, kelp teal `var(--bs-primary)`, and the existing `left/right 0.75rem` insets. Scope the new `top` to the md+ media query if mobile needs the old position. The visual result: bar sits flush against the very top edge of the navbar, above the active item only.
- [x] **Filled icon on active**: in `NavMenu.razor`, for each of Home, club, Campaigns, Players, Teams, render the fill variant next to the outline glyph:
  ```razor
  <span class="bi bi-house nav-icon" aria-hidden="true"></span>
  <span class="bi bi-house-fill nav-icon-fill" aria-hidden="true"></span>
  ```
  (pairs: `house`/`house-fill`, `building`/`building-fill`, `calendar-check`/`calendar-check-fill`, `people`/`people-fill`, `shield`/`shield-fill`). In `NavMenu.razor.css`: hide `.nav-icon-fill` by default (`display: none`), show it and hide `.nav-icon` under `::deep .nav-link.active`. To avoid layout shift when the glyph swaps (outline vs fill widths differ slightly), prefer overlaying the fill glyph: give the icon slot a fixed box (`position: relative`) and absolutely position the fill span over the outline span, toggling `opacity`/`visibility` instead of `display` — decide during implementation and record the choice.
- [x] Logout button glyph stays `bi-box-arrow-right` (no `-fill` variant, no active state).
- [x] Optional (record in Phase Summary): active label color `::deep .nav-link.active { color: var(--bs-primary); }` to echo the reference's active-item emphasis **only if** the kelp teal passes `npm run check:contrast` against `$light`/`$body-bg` (kelp teal on pale kelp is ~4.5:1 — verify; if it fails, keep the label dark and note why). Colors must come from the theme palette — never Bootstrap blue.

### Verification Plan

- [x] `dotnet build Nova.slnx` succeeds; `dotnet format Nova.slnx --verify-no-changes` passes.
- [x] Playwright (via the committed `NavbarBrowserTests` after they're updated in Phase 3, or `aspire-playwright-validation`): sign in as seeded admin, load `/` at desktop viewport:
  - active Home link has the indicator bar whose `top`/bounding box is vertically flush with the navbar's top edge — assert `getComputedStyle(link, '::after').top` equals the negative navbar padding (or, more robustly, assert the link's `::after` bounding box top ≈ the `nav.navbar` element's top edge within 1-2px).
  - the active link's `::before`/icon font-family stays `bootstrap-icons` (font still loads) and the visible glyph class is the `-fill` variant (e.g. `span.bi-house-fill` is visible/opacity 1, `span.bi-house` hidden).
  - navigate `Home` → `/campaigns`: indicator moves to Campaigns, Home's icon returns to outline, Campaigns' icon becomes filled, and no horizontal/vertical layout jump occurs (icon slot box constant).
- [x] `npm run check:contrast` (from `Nova/`) passes — no Bootstrap-blue literals added.

### Phase Summary

- **Overlay approach (chosen over `display` toggle)**: each authorized link now wraps the outline + fill glyphs in a fixed `.nav-icon-slot` box (`width/height: 1.25rem` at base, `1.5rem` inside the md+ media query; `position: relative`). Both spans are `position: absolute; top: 0; left: 0` inside the slot. `.nav-icon-fill` is hidden with `opacity: 0; visibility: hidden` (not `display: none`, so the box never changes size) and swapped via `::deep .nav-link.active .nav-icon { opacity: 0 }` / `::deep .nav-link.active .nav-icon-fill { opacity: 1 }`. No code-behind changes — `.active` comes from NavLink + the existing `LocationChanged` subscription.
- **Flush indicator**: base `::deep .nav-link.active::after { top: 0 }` kept for mobile; the md+ media query raises it to `top: calc(-1 * (var(--bs-navbar-padding-y) + var(--bs-border-width)))` = `-(0.5rem + 1px)`, so the 3px kelp teal bar covers the navbar's `border-top` and sits flush with the very top edge. `left/right: 0.75rem` insets and `border-radius: 0 0 3px 3px` unchanged.
- **Active label color**: kept dark (default `$body-color`). Kelp teal `#0E7C7B` on pale kelp `#E6F2F1` (the `navbar-light bg-light` surface) measures **4.378:1** — below the 4.5:1 AA threshold for normal-size text, so the optional tint was dropped to stay WCAG-safe; the mockup's blue is deliberately not used (theme colors only). (Kelp teal on `$body-bg` #F4F9F8 is 4.717:1, but the label sits on the pale-kelp navbar.)
- **No Sass/`package.json` change**: all styling is component-scoped CSS. `npm run build:css` was run to confirm the theme compiles but nothing in `scss/` changed; contrast check passes.
- NB2 (extended) passes flush/opacity assertions against the live AppHost.

## Phase 3: Update and extend automated tests

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: sub-agent w/ smaller model (test edits are mechanical and well-scoped once Phases 1–2 are merged), verified by orchestrator.

- [x] `Nova.Browser.Tests/NavbarBrowserTests.cs`:
  - Update the class doc summary to reflect the stacked layout + fill behavior.
  - In NB1/`AssertIconLinkAsync`/`AssertBootstrapIconGlyphAsync`: keep the existing `span.<glyphClass>` selectors working (they match the outline span only; do not rename glyph classes); optionally assert the paired `-fill` span exists in the markup.
  - In NB2 (`Navbar_ActiveItem_ShowsKelpTealTopIndicator`): extend to assert (a) the indicator `::after` top is flush with the navbar's top edge (e.g. compare against the `nav.navbar` bounding box) and (b) the active item shows the filled glyph (`span.bi-house-fill` visible, outline hidden) and that the previously-active item returned to outline.
  - Add a new NB test (e.g. `Navbar_Desktop_StacksIconAboveLabel`): at desktop viewport assert the `.nav-link` computes `flex-direction: column` and each item's icon box sits above its label box; skip/annotate that mobile (<md) behavior keeps inline rows (may need a viewport resize or a second context; keep it simple — if resizing mid-test is flaky, assert only the desktop case and cover mobile via the collapsed-menu manual check).
- [x] `Nova.Unit.Tests/Components/NavMenuTests.cs`: add/extend one assertion that the razor markup renders the `-fill` span for each authorized link (bUnit `cut.Markup.ShouldContain("bi-house-fill")` etc.) — cheap, no DOM event needed.
- [x] Update `.github/instructions/testing.instructions.md` only if the browser-suite conventions docs need a note about the navbar tests (otherwise leave untouched).

### Verification Plan

- [x] `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — all green (NavMenu render tests + new fill-span assertion).
- [x] `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` — requires the Aspire AppHost running (PostgreSQL 18 provisioned via `dotnet run --project Nova.AppHost`) and the one-time Chromium install (`Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1 install chromium`); all `NavbarBrowserTests` including the new stacked/fill tests pass. If Postgres/AppHost is unavailable locally, record the result as "not run — blocked by environment" in the Phase Summary and rely on `aspire-playwright-validation` instead.
- [x] `dotnet build Nova.slnx` and `dotnet format Nova.slnx --verify-no-changes` still pass.

### Phase Summary

- **NavbarBrowserTests** (6 tests, all pass locally against the Aspire AppHost + Postgres 18 + Azurite + cached Chromium):
  - Class doc updated to describe the stacked md+ layout, flush indicator, and fill-glyph overlay.
  - NB1 unchanged selectors (`span.bi-house`, etc. resolve to the outline span, still count 1). `AssertIconLinkAsync`/`AssertBootstrapIconGlyphAsync` untouched.
  - NB2 extended: `AssertKelpTealTopIndicatorFlushAsync` (kelp teal RGB, 3px height, `::after` doc-space top within 2px of `nav.navbar` top, narrow width < link width), `AssertActiveFillGlyphAsync` (fill opacity 1, outline opacity 0), and after navigating Home → /campaigns `AssertInactiveOutlineGlyphAsync` asserts Home returned to outline.
  - NB6 new: `Navbar_Desktop_StacksIconAboveLabel` at a 1280×800 viewport asserts `flex-direction: column`, icon box above label box, horizontally centered (±1px) for Home, club, Campaigns, Players, Teams; and Manage stays `flex-direction: row`.
- **NavMenuTests** bUnit: `Render_RendersClubLink_WhenUserHasClubNameClaim` now asserts the exact glyph-span markup (`bi-house nav-icon`, `bi-house-fill nav-icon-fill`, … all five pairs) plus `nav-icon-slot`.
- Run commands: unit tests via `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-restore` (1745 passed); browser tests via the MTP runner `Nova.Browser.Tests.exe --filter-class Nova.Browser.Tests.NavbarBrowserTests` (6 passed). `dotnet test` with `--no-build`/`--nologo` reported a misleading "Zero tests ran" (exit 5) in this environment — the exe/direct runner and the exact CI command run green.
- `testing.instructions.md` left untouched (no convention changes needed).

## Final Recap

Polish of the #134 bottom navbar, bring it closer to the reference mockup with three changes:

1. **Stacked icon-above-label on desktop (md+)** — the five authorized items (Home, club, Campaigns, Players, Teams) now render `flex-md-column text-md-center` at ≥768px with `--bs-navbar-nav-link-padding-x: 1rem` item spacing, `padding-top/bottom: 0.75rem`, 1.5rem icons, and 0.8125rem labels. Mobile (<md) keeps the inline row inside the collapsed menu (base `d-flex align-items-center gap-2`; Manage/Logout stay inline at all sizes). `MainLayout.razor.css` content padding-bottom grew 5rem → 6rem for the taller fixed navbar.
2. **Flush kelp-teal active indicator** — the `::after` bar (3px, `var(--bs-primary)`, `left/right: 0.75rem`) is raised to `top: calc(-1 * (var(--bs-navbar-padding-y) + var(--bs-border-width)))` at md+ so it covers the navbar's border-top and sits flush with the very top edge; mobile keeps `top: 0`.
3. **Filled active icons** — each authorized link wraps the outline + `-fill` glyph pair in a fixed `.nav-icon-slot`; both are absolutely positioned overlays toggled by `opacity`/`visibility` off NavLink's `.active` (no code-behind changes; no layout shift). Logout stays `bi-box-arrow-right` (no fill variant). Active label stays dark (kelp teal on pale kelp is 4.378:1, below AA).

Tests: NB1–NB5 kept green; NB2 extended (flush + fill + outline-return assertions); NB6 added (stacked layout at 1280×800); bUnit NavMenu test asserts all five `-fill` glyph pairs in the markup. Full build/format/unit/browser validation passed locally.

## Deployment Plan

1. Push this branch and open a PR against `main` (title: "Polish navbar: flush active indicator, filled active icons, stacked icon-above-label layout"; body links the work as a follow-up to #134 — **not** `Closes #134`, which is already merged/closed).
2. CI on the PR runs `dotnet build Nova.slnx` + `npm run check:contrast` (build job) and `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-restore` (unit-tests job). Both must be green before merge. Browser tests are local-only (CI does not run them) and were run locally against the AppHost — 6/6 NavbarBrowserTests passed.
3. No migrations, no environment changes, no new packages, no Sass/`package.json` changes — merge is a normal deploy pipeline run (same as #136), no special steps.
