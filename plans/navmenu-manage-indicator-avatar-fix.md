# New Bottom NavMenu polish: flush Manage indicator + larger avatar

Follow-up to #139 (merged "navbar active indicator + icon fill polish"). The icon-first bottom
NavMenu (fixed-bottom, `navbar-expand-md`) has a kelp teal 3px active-indicator bar that is flush
with the navbar's top edge for the stacked left links (Home, club, Campaigns, Players, Teams), but
**not flush** for the right-side **Manage** link — the bar sits a few pixels below the navbar's top
border. The user also wants the current-user avatar (next to "Manage") enlarged from 1.75rem
(28px) to **2rem (32px)** so it reads clearly larger than the 1.5rem (24px) icons. Goal: make every
NavMenu item's active indicator flush with the navbar top, make the avatar 2rem, and add solid
browser + unit test coverage for both behaviors.

**Orchestration status:** implementation complete and validated locally (see Phase 1/2 summaries).
Builder turn 1 (commit + push + open PR against `main`) is in progress; Reviewer will review the
PR after it exists.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

## Phase 0: Root cause + fix design (already investigated)

Status: Complete <!-- Mark complete once verified: the design below is grounded in Bootstrap source + geometry -->

**Root cause (verified).** At `min-width: 768px`, Bootstrap's `.navbar-expand-md .navbar-collapse`
has `align-items: center`, and the two `ul.navbar-nav` (left `.me-auto`, stacked at md+; right
`.ms-auto`, inline) are flex items of the same flex line. The left stacked links are taller than
the inline Manage/Logout items, so the right `ul` — and the Manage `nav-link` inside it — is
**vertically centered**, meaning its top edge is `(tallUlHeight − shortUlHeight) / 2` pixels below
the navbar content's top edge. The indicator CSS raises the bar by a fixed
`calc(-1 * (var(--bs-navbar-padding-y) + var(--bs-border-width)))` = `-(0.5rem + 1px)` from the
link's top. That fixed offset is correct only when the link's top is at the navbar content top
(left stacked links), so the Manage bar lands below the navbar's top edge. Mobile (<md) is
unaffected: all items are inline rows on one flex line, aligned to the same top.

**Fix options considered.**
- **A (chosen, one-line):** At md+, override `.navbar-collapse { align-items: flex-start }` inside
  the existing media query (the element is rendered by NavMenu itself, so the rule stays scoped —
  no `::deep`). The right `ul.ms-auto` then top-aligns (instead of being vertically centered)
  against the taller left `ul`, so the Manage link's **top edge** moves to the navbar content top
  and the existing `top: calc(-1 * (var(--bs-navbar-padding-y) + var(--bs-border-width)))` lands
  the bar exactly at the navbar's outer top edge (verified geometry below). Because the *link top*
  now matches the left items' top, the bar is flush for **every** active item, regardless of the
  item's own height — robust to the avatar enlargement (Manage becomes 56px tall at 2rem avatar,
  but flushness depends only on link top, which no longer drifts). Mobile (<md) is untouched
  because the rule is inside the `min-width: 768px` block, and mobile uses `top: 0` (inside the
  link) by design.
- **B:** Position the bar on the `nav-item`/`ul` instead of the `nav-link`. More invasive markup;
  CSS isolation + `::deep` changes; rejected as unnecessary.
- **C:** Overlay a full-width navbar-level `::before` line at the navbar top and hide/show it per
  active item. Requires knowing which item is active in CSS (`:has()` — no precedent in repo,
  browser support fine but a bigger change); rejected.

**Verified geometry (md+, default 1280×800).** Navbar outer top `navTop` = content-top − 9px
(1px border-top + 0.5rem padding). Left stacked link = padding 0.75×2 (24px) + icon 1.5rem (24px)
+ gap 0.5rem (8px) + label 13px ≈ **69px**; Manage link = 24px + avatar 28px = **52px**. The
`align-items: center` on `.navbar-collapse` gives the right `ul` a (69−52)/2 = **8.5px** top
offset, so the Manage `::after` top = linkTop − 9px = contentTop − 0.5px ≈ **8.5px below navTop**
(left links: linkTop = contentTop → bar at navTop, exactly flush). Screenshot matches. With
`flex-start` the offset is 0 for every item.

**Decision: fix A** — do not switch to `:has()` or navbar-level pseudo-elements unless
verification shows a residual gap.

**Avatar:** change `.nav-avatar` `width/height` 1.75rem → 2rem (32px) in the **base** rule, so it
applies at all breakpoints — resolved in Phase 2: scale at mobile too (mobile keeps the inline
`d-flex align-items-center gap-2` row, and the 32px avatar reads clearly larger than the 1.25rem
icons there as well). Keep `border-radius: 50%; object-fit: cover; border: 1px solid
var(--bs-border-color); flex-shrink: 0`.

### Verification Plan

- [x] Confirmed root cause in Bootstrap 5.3.3 `_navbar.scss` (`.navbar-expand-md .navbar-collapse { align-items: center }`).

## Phase 1: Implement the indicator fix + avatar size

Status: Complete

Suggested executor: orchestrator (small, tightly-coupled CSS + markup change; no parallelization)

- [x] Edit `Nova/Components/Layout/NavMenu.razor.css`:
  - In the existing `@media (min-width: 768px)` block, add
    `.navbar-collapse { align-items: flex-start }` (scoped direct rule; the collapse element is
    rendered by NavMenu itself, no `::deep`). This top-aligns the right `ul.ms-auto` with the left
    stacked `ul`, so the Manage link's top edge reaches the navbar content top and the existing
    `top: calc(-1 * (var(--bs-navbar-padding-y) + var(--bs-border-width)))` indicator lands flush
    at the navbar top for **every** active item.
  - Keep the `<md` behavior (`top: 0` inside the link) unchanged — the new rule is inside the md+
    media query so mobile is untouched.
  - Enlarge `.nav-avatar` to `2rem` (32px) — change the **base** rule, so it applies at all
    breakpoints (decided in Phase 2: scale at mobile too — the mobile inline row uses 1.25rem icons,
    and the 2rem avatar still reads clearly larger there; the mockup/target is 2rem, and keeping one
    base rule avoids a md+ override that would silently revert mobile to 1.75rem). Keep
    `border-radius: 50%; object-fit: cover; border: 1px solid var(--bs-border-color); flex-shrink: 0`.
- [x] Verify the change in a real browser (Playwright or manual) at 1280×800: navigate to
  `/Account/Manage` (Manage active) and confirm its kelp teal indicator bar is flush with the
  navbar top; also confirm Home/Campaigns/etc. still flush and the avatar is visibly 32px round.
- [x] No markup/code-behind changes expected (the fix is CSS-only: alignment + avatar size).

### Phase Summary

Applied the fix: (1) `@media (min-width: 768px)` block in `Nova/Components/Layout/NavMenu.razor.css`
gains `.navbar-collapse { align-items: flex-start }` — Bootstrap's default
`align-items: center` vertically centered the shorter inline right-hand `ul` (Manage/Logout)
against the taller stacked left `ul`, pushing the Manage link top below the navbar content top so
its indicator bar landed below the navbar's top edge; top-aligning makes every link's top edge
reach the navbar content top, so the existing fixed `top: calc(-1*(padding-y + border-width))`
indicator is flush for every active item. (2) `.nav-avatar` enlarged from 1.75rem to 2rem (32px)
so it reads clearly larger than the 1.5rem icons, keeping `border-radius: 50%; object-fit: cover;
border: 1px solid var(--bs-border-color); flex-shrink: 0`. No markup or code-behind changes needed
(no `.razor`/`.razor.cs` edits). Mobile (<md) alignment untouched: the alignment rule lives inside
the md+ media query and mobile uses `top: 0` (inside the link) by design. The avatar, however,
follows the base rule, so it is 2rem at all breakpoints (mobile included).

### Verification Plan

- `dotnet build Nova.slnx` → succeeds.
- Playwright/manual: load `/` and `/Account/Manage` at 1280×800; assert the active item's indicator
  bar top == navbar top (±1 px) for Manage AND for Home/Campaigns.
- Browser: nav bar top edge visible; no horizontal scrollbar; avatar (2rem/32px) larger than the
  1.5rem icons and still circular.
- `npm run check:contrast` (no Sass changes expected — run only if `scss/` touched).

## Phase 2: Test coverage

Status: Complete

Suggested executor: orchestrator (tests are highly-coupled to the CSS/geometry; keep in one pass with Phase 1)

- [x] **Browser — `Nova.Browser.Tests/NavbarBrowserTests.cs`:**
  - Add new test (follow NB3 numbering, e.g. **NB8**): sign in, navigate to `/Account/Manage`,
    assert the Manage link is active, its kelp teal indicator is flush with the `nav.navbar` top
    (reuse/extend `AssertKelpTealTopIndicatorFlushAsync`), and assert the avatar
    (`img.nav-avatar`) bounding box is ≥ 2rem (32px min) and circular (border-radius / 50%).
  - **Extend NB2** (or add NB8-alt covering all active items): assert the flush indicator for the
    Manage link at `/Account/Manage` in addition to Home/Campaigns. Ensure the flush assertion is
    also true **after** navigating between pages (active item switches, bar moves and remains flush).
  - Add an **avatar-size assertion** (bounding box width/height in [31.5, 33) px) — the mockup is
    32px.
  - Ensure mobile (<md, 480×800) still keeps inline layout and the indicator inside the link
    (NB7 already covers inline; add a Manage-flush-at-mobile check if the `<md` behavior is
    intentionally package-scoped — confirm whether "flush" is desktop only or both; plan keeps it
    mobile-safe but assert what we intend).
- [x] **Unit — `Nova.Unit.Tests/Components/NavMenuTests.cs`:**
  - If the markup changes (e.g. a class on the Manage `ul` or link), assert the new class(es)
    exist. If the Manage link needs a new class for alignment, assert `cut.Markup` contains it.
  - Add a test that the avatar renders when the principal carries `HasProfilePhoto` claim (and is
    absent otherwise) if this behavior isn't already asserted elsewhere.
- [x] Keep existing NB1–NB7 green (no behavioral regression).

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` → pass (note: use plain `dotnet test`; `--no-build --nologo` reported "Zero tests ran" in this environment).
- Browser suite: start `dotnet run --project Nova.AppHost`, then
  `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` → all NB1–NB9 pass.
- MTP runner note: prefer the direct exe runner (`Nova.Browser.Tests.exe --filter-class ...`) if
  `dotnet test` hangs on the generator; browser tests require
  `Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1 install chromium` once per machine.

### Phase Summary

Added browser coverage in `Nova.Browser.Tests/NavbarBrowserTests.cs`:
- **NB8** `Navbar_ManageActive_IndicatorFlushAndAvatarLarger` (1280×800): navigates to
  `/Account/Manage`, asserts the Manage link is active with a kelp teal 3px indicator flush with
  the navbar top (reusing `AssertKelpTealTopIndicatorFlushAsync`) and the avatar renders as a 2rem
  (32px, 31.5–32.5px asserted) circle.
- **NB9** `Navbar_Desktop_ManageLinkTopAlignedWithStackedItems` (1280×800): asserts the Manage
  link's bounding-box top matches the Home link's top (±1px) — the alignment rule guard.
- New helper `AssertNavAvatarAsync`: asserts one `img.nav-avatar`, its bounding box is 31.5–32.5px
  square (2rem = 32px border-box; sub-pixel rounding only), and `border-radius` contains `50%`.

Added unit coverage in `Nova.Unit.Tests/Components/NavMenuTests.cs`:
- `Render_RendersAvatarWithPhotoUrl_WhenUserHasProfilePhotoClaim`: asserts `class="nav-avatar"`,
  `src="/api/users/7/photo?size=small"`, `alt="Profile photo"` when the principal carries the
  `HasProfilePhoto` claim.
- `Render_OmitsAvatar_WhenUserHasNoProfilePhotoClaim`: asserts no avatar/alt when the claim is
  absent.
- `CreatePrincipal` gained an optional `hasProfilePhoto` parameter (defaults false; existing
  tests unchanged).

Existing NB1–NB7 (incl. NB6 stacked/Manage-inline and NB7 mobile-inline) and existing unit tests
stay green — no regression.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` → pass (note: use plain `dotnet test`; `--no-build --nologo` reported "Zero tests ran" in this environment).
- Browser suite: start `dotnet run --project Nova.AppHost`, then
  `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` → all NB1–NB9 pass.
- MTP runner note: prefer the direct exe runner (`Nova.Browser.Tests.exe --filter-class ...`) if
  `dotnet test` hangs on the generator; browser tests require
  `Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1 install chromium` once per machine.

**Verification results recorded:**
- `dotnet build Nova.slnx` → **Build succeeded, 0 warnings, 0 errors**.
- Unit tests (direct exe runner, since `dotnet test --no-build --nologo` reports "Zero tests ran"):
  `Nova.Unit.Tests.exe` → **1747 passed / 0 failed** (includes 4 NavMenuTests: 2 existing + 2 new).
- Browser tests (direct exe runner against Aspire AppHost via `aspire start --isolated`):
  `Nova.Browser.Tests.exe --filter-class Nova.Browser.Tests.NavbarBrowserTests` → **9 passed /
  0 failed** (NB1–NB9, includes the new NB8+NB9).
- `dotnet format Nova.slnx --verify-no-changes -v q` → **clean** (exit 0).
- No Sass changes → `npm run check:contrast` not required.

## Final Recap

Fixed the bottom NavMenu so **every** active item's kelp teal indicator bar is flush with the
navbar's top edge, and enlarged the current-user avatar to 2rem.

**Root cause (desktop only).** Bootstrap's `.navbar-expand-md .navbar-collapse` uses
`align-items: center`; the right-hand inline `ul.ms-auto` (Manage/Logout) is shorter than the left
stacked `ul`, so it was vertically centered — pushing the Manage link's top ~8.5px below the
navbar content top. The indicator's fixed upward offset (0.5rem + 1px) is geometry for links whose
top is at the content top, so the Manage bar landed below the navbar edge. Mobile was unaffected
(all items inline, `top: 0`).

**Fix (CSS-only, 1 rule + 1 size change in `Nova/Components/Layout/NavMenu.razor.css`):**
- `@media (min-width: 768px) { .navbar-collapse { align-items: flex-start } }` — top-aligns the
  right `ul`, so every link's top reaches the navbar content top and the existing indicator offset
  is flush for every active item (Manage on `/Account/Manage` now matches Home/Campaigns/Teams).
  Mobile untouched.
- `.nav-avatar { width/height: 2rem }` (from 1.75rem; 32px), keeping circle/cover/border.

**Tests:**
- Browser (NB8, NB9 in `NavbarBrowserTests`): Manage active indicator flush + avatar 2rem circle;
  Manage top-aligned with stacked items at md+. All 9 Navbar browser tests pass.
- Unit (2 new in `NavMenuTests`): avatar renders with `HasProfilePhoto` claim (correct
  `/api/users/{id}/photo?size=small` src) and omits without it. All 1747 unit tests pass.
- `dotnet build` clean, `dotnet format --verify-no-changes` clean.

## Deployment Plan

Code-only front-end fix; no schema/migration, no configuration, no environment variables.
1. Merge this branch (PR against `main`).
2. CI runs build + unit tests on merge; those pass (verified locally: build 0 warnings, 1747 unit
   tests pass, format clean).
3. Browser tests are local-only (not in CI); the local run verified all
   `Nova.Browser.Tests.NavbarBrowserTests` (9/9) against an Aspire-hosted AppHost. If desired
   pre-merge, run `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` against a
   local `aspire start` (requires `playwright.ps1 install chromium` once per machine).
4. Redeploy the web app (normal app deployment). No cache-busting concern for the `.razor.css`
   bundle: Blazor uses integrity/versioned static assets, and the change ships with the app
   assembly. No restart of background services required.
5. Post-deploy spot check (desktop): open `/Account/Manage`, verify Manage's indicator bar sits
   flush at the top of the bottom navbar, and all other links (Home/Campaigns/etc.) behave the
   same; avatar appears as a 32px circle next to "Manage".
