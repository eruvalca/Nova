---
applyTo: "Nova/scss/**,Nova/package.json,Nova/Nova.csproj,Nova/Components/App.razor,.github/workflows/**,**/*.razor.css"
description: "Bootstrap theme conventions: the Sass-compiled kelp-forest theme, its single source of truth, the authoritative build/contrast commands, and the rules against re-adding vendored Bootstrap CSS or Bootstrap-blue literals."
---

# Bootstrap theme (kelp-forest) conventions

The application loads a single Sass-compiled Bootstrap 5.3.3 theme. The compiled stylesheet is a
**generated asset**: never edit it directly, and never commit it. Everything is driven from the
Sass sources under `Nova/scss/`.

## Single source of truth

- `Nova/scss/_variables.scss` is the **only** place the palette is defined. It sets the kelp-forest
  theme colors (primary `#0E7C7B`, secondary `#A67B4C`, success `#3C8D5A`, info `#2E9E9C`, warning
  `#E8A33D`, danger `#C25E4E`, light `#E6F2F1`, dark `#1F2D2B`), the body text/background, the link
  color, the `$min-contrast-ratio` (4.5), and the neutralized Bootstrap blue (`$blue: $primary`).
- `Nova/scss/bootstrap-theme.scss` imports `variables` then `bootstrap` (via
  `@import "bootstrap/scss/bootstrap"`).
- Bootstrap 5.3.3 has the import resolver that requires `--load-path=node_modules`; use the npm
  `build:css` script — do not invoke `sass` by hand with a different resolution.

## Build & validation (authoritative)

- Build the theme: `npm run build:css` (from `Nova/`) → writes `Nova/wwwroot/css/bootstrap-theme.css`.
- Validate contrast: `npm run check:contrast` (from `Nova/`) — parses `_variables.scss`, computes
  WCAG ratios for the documented pairs, and asserts the compiled CSS contains none of the default
  Bootstrap-blue literals (`#0d6efd`, `#0b5ed7`, `#0a58ca`, `#86b7fe`, `rgba(13,110,253`).
- The MSBuild `BuildBootstrapTheme` target on `Nova/Nova.csproj` runs `npm ci` (only when
  `node_modules` is absent) then `npm run build:css` before `Build`, incrementally (it reruns only
  when `scss/**/*.scss` or `package.json` changes). The compiled CSS is gitignored.

## Toolchain

- Node.js 20+ is required. `npm` is the package manager — never `yarn`/`pnpm`.
- `package-lock.json` is committed and CI installs with `npm ci`.
- `bootstrap` is pinned to `5.3.3` and `sass` to a recent 1.x in `Nova/package.json`.

## Rules

- **Never** re-add the vendored Bootstrap CSS (`Nova/wwwroot/lib/bootstrap/dist/css/`) — the JS
  bundle (`dist/js/bootstrap.bundle.min.js`) stays, but the CSS is replaced by the compiled theme.
- **Never** hardcode Bootstrap-blue literals (`#0d6efd`, `#0b5ed7`, `#0a58ca`, `#86b7fe`,
  `rgba(13,110,253`) or reintroduce the default blue `:root` variables. In component CSS use
  semantic Bootstrap CSS variables (`var(--bs-primary)`, `var(--bs-border-color)`,
  `var(--bs-body-color-rgb)`, etc.) or Bootstrap utility classes instead of raw hex/rgb values.
- Prefer theme variables over literal hex: when you need a color from the palette in
  `*.razor.css` / `app.css`, reference the `--bs-*` CSS variable rather than copying the hex.
- The theme is Sass-compiled and **not committed** to source control.
