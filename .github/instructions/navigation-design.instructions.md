---
applyTo: "Nova/Components/Layout/NavMenu.razor,Nova/Components/Layout/NavMenu.razor.cs,Nova/Components/Layout/NavMenu.razor.css,Nova/Components/Layout/NavMenu.razor.js,Nova.Browser.Tests/*Navigation*.cs"
description: "The authenticated navigation rail, mobile sheet, scripting fallback, and uniform icon-lane contract."
---

# Navigation design

Follow PRODUCT.md and DESIGN.md for product and visual semantics. These rules apply to the
navigation surface; other pages retain their own composition.

## Navigation semantics

- Authenticated app nav (`Nova/Components/Layout/NavMenu`): **left rail at ≥768px** (15rem, fixed)
  and a **fixed bottom bar below 768px** that shows the brand lockup and a hamburger only. The
  hamburger opens one paper sheet listing **every** route — primary (Dashboard, club, Campaigns,
  Players, Teams) and account (Manage/Logout or Login) — as full-width, left-aligned rows with
  legible full-size (0.875rem) labels (Bootstrap `.show`) — functionality stays reachable, never
  hidden, and labels never shrink or truncate into "Dash…". The sheet/hamburger contract is the
  **default** below 768px, so it does not depend on a script marker: with scripting disabled all
  routes fall back to inline items in a horizontally scrollable strip and the hamburger hides (it
  could never open without JS) via a component-local `scripting: none` media query;
  with only a single Login tab (anonymous) the tab stays inline and the hamburger hides.
- Active item = sea-glass field + teal indicator: top marker (3px) on the mobile menu rows, left edge rail
  (`0.25rem` inset) on the rail. The field is `--bs-primary-bg-subtle` on the md+ rail; in the opened
  mobile sheet the field must be the token-derived deeper sea-glass blend
  (`color-mix(in srgb, var(--bs-primary) 10%, var(--bs-light))`), never `--bs-primary-bg-subtle` —
  the theme defines it byte-identical to `--bs-light`, so on the unified sheet surface the field
  would vanish (issue #159 review). Icons swap outline→fill via the active class
  (`nav-icon`/`nav-icon-fill`). Every leading slot shares one uniform 2rem icon lane
  (`nav-icon-slot` and `nav-avatar-slot` are both 2rem, glyphs render 1.25rem centered inside
  the lane) at md+ AND in the opened mobile sheet, so the club crest and profile avatars render
  inside their own 2rem slot (`nav-avatar-slot`) beside the label — never inside the 1.25rem
  glyph slot — and every row (including Logout) has the same leading box, so no row reads larger
  or off-lane and every label starts at the same x-offset at every breakpoint. The only
  exception: the scripting-disabled inline strip keeps the compact 1.25rem glyph slot by design
  (it is the stacked-tab fallback, not the menu sheet).
