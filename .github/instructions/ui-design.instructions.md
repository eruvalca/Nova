---
applyTo: "**/*.razor,**/*.razor.css,Nova/scss/**,Nova/Components/Pages/**,Nova/Features/**"
description: "UI design rules for the Fieldhouse Wayfinding design system: PRODUCT.md/DESIGN.md and .impeccable/surfaces are the sources of truth, semantic color roles, flat boards, radii, navigation and route-marker semantics, comment-block convention, motion/touch-target rules, and responsive collapse behavior."
---

# UI design (Fieldhouse Wayfinding)

## Sources of truth

- `PRODUCT.md` (product intent, users, principles) and `DESIGN.md` (token set, named rules,
  navigation semantics) are the **design-system source of truth** for all UI work.
- Per-surface briefs live in `.impeccable/surfaces/*.md` (slugs map to razors, e.g.
  `i-features-campaigns-pages-campaignworkspace-razor`). Read the surface brief before designing a
  feature, and `DESIGN.md` before choosing colors or layout.
- `Nova/scss/_variables.scss` is the token source for the compiled theme; component CSS references
  `--bs-*` variables (or Sass tokens), never raw hex.

## Semantic color roles (kelp-forest)

- **Teal** (`--bs-primary`) = wayfinding and primary action. It owns deliberate regions only; never
  decoration. - **Sunlit aqua** (`--bs-info`) = information.
- **Kelp ochre/olive** (`--bs-secondary`/`--bs-success`) = human/collaborative and success.
- **Signal amber** (`--bs-warning`) = attention only; never competes with primary action.
- **Copper** (`--bs-danger`) = danger/destructive.
- **Abyss forest** (`--bs-dark`/body text) = ink/text; sea glass (`--bs-light`),
  foam mist/paper white = surfaces.
- Color communicates state; nothing that conveys state may rely on color alone (badge text,
  icon + label, aria state).

## Surfaces and shapes

- Boards are **flat**: paper-white/body background, `1px solid var(--bs-border-color)`,
  `border-radius: 0.375rem`, no ambient shadow, no gradient/grid for depth or glass.
- Controls and inputs use the `0.25rem` radius; badge/status chips are labeled pills only where
  semantics require (never decorative pills; circles only for people/status/route markers/brand dot).
- Secondary text on colored surfaces must derive from the surface hue (e.g. `color-mix(in srgb,
  var(--bs-white) 93%, transparent)` on teal) and keep ≥4.5:1 contrast for normal-size text
  (3:1 applies only to non-text UI components).
- No thick decorative side borders; edge rails (≤0.25rem) are reserved for the active navigation item.

## Navigation semantics

- Authenticated app nav (`Nova/Components/Layout/NavMenu`): **left rail at ≥768px** (15rem, fixed)
  and a **fixed bottom bar below 768px** that shows the brand lockup and a hamburger only. The
  hamburger opens one paper sheet listing **every** route — primary (Dashboard, club, Campaigns,
  Players, Teams) and account (Manage/Logout or Login) — as full-width, left-aligned rows with
  legible full-size (0.875rem) labels (Bootstrap `.show`) — functionality stays reachable, never
  hidden, and labels never shrink or truncate into "Dash…". The sheet/hamburger contract is the
  **default** below 768px, so it does not depend on a script marker: with scripting disabled all
  routes fall back to inline items in a horizontally scrollable strip and the hamburger hides (it
  could never open without JS) via the deterministic `<noscript><style>` block in `App.razor`;
  with only a single Login tab (anonymous) the tab stays inline and the hamburger hides.
- Active item = sea-glass field (`--bs-primary-bg-subtle`) + teal indicator: top marker (3px) on the
  mobile menu rows, left edge rail (`0.25rem` inset) on the rail. Icons swap outline→fill via the
  active class (`nav-icon`/`nav-icon-fill`). Every leading slot shares one uniform 2rem icon lane
  (`nav-icon-slot` and `nav-avatar-slot` are both 2rem, glyphs render 1.25rem centered inside
  the lane) at md+ AND in the opened mobile sheet, so the club crest and profile avatars render
  inside their own 2rem slot (`nav-avatar-slot`) beside the label — never inside the 1.25rem
  glyph slot — and every row (including Logout) has the same leading box, so no row reads larger
  or off-lane and every label starts at the same x-offset at every breakpoint. The only
  exception: the scripting-disabled inline strip keeps the compact 1.25rem glyph slot by design
  (it is the stacked-tab fallback, not the menu sheet).
- Route-marker tab pattern (campaign workspace tabs): 4 stop buttons in a grid with a
  `min-width: 36rem` scroll container (`overflow-x: auto`), markers connected by a line, active stop
  teal + sea glass. Horizontally scrollable at ≤36rem viewport widths.
- Brand lockups keep mark+name+descriptor; on very small screens the mark must not overflow.

## Touch and motion

- Touch targets ≥2.75rem (44px) on phones; focus never clipped inside scroll containers.
- Reduced-motion-safe: no motion required to convey state; transitions benign.

## Responsive collapse

- Prefer the content-driven breakpoints already in use: 575.98 / 767.98 / 991.98px.
- Grids collapse to one column below the grid's comfortable width; CTA buttons go full-width at
  ≤575.98px; table changes stay inside `.table-responsive` scroll containers.

## Comment-block convention

Surface razor files carry an explanatory comment block near the top:

```
THESIS:   what this surface is, in one sentence.
OWN-WORLD: the visual grammar/motifs it uses.
STORY:    what the user experiences in sequence.
FIRST VIEWPORT: what attention lands on first.
FORM:     layout/mapping structure notes.
```

Keep or update it when the surface changes; it is part of the design artifact.
