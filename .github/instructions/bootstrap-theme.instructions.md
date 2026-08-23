---
applyTo: "Nova/scss/**,Nova/package.json,Nova/package-lock.json,Nova/Nova.csproj,Nova/scripts/**,Nova/Components/App.razor,.github/workflows/**,**/*.razor.css"
description: "Bootstrap theme conventions: the Sass-compiled kelp-forest theme, its single source of truth, the Node preflight, the lockfile-aware npm install, the authoritative build/contrast commands (npm scripts and Aspire dashboard commands), and the rules against re-adding vendored Bootstrap CSS or Bootstrap-blue literals."
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
  `@import "bootstrap/scss/bootstrap"`) and the bootstrap-icons fonts.
- Bootstrap 5.3.3 has the import resolver that requires `--load-path=node_modules`; use the npm
  `build:css` script — do not invoke `sass` by hand with a different resolution.

## Build toolchain prerequisites

- Node.js 20+ is required (see `Nova/package.json` `engines.node`). `npm` is the package manager —
  never `yarn`/`pnpm`.
- `package-lock.json` is committed and CI installs with `npm ci`.
- `bootstrap` (5.3.3), `bootstrap-icons` (1.13.1) and `sass` (1.x) versions are pinned in
  `Nova/package.json`; the installed set must match `package-lock.json`.
- **Node preflight**: `Nova/Nova.csproj` runs `Nova/scripts/check-node.mjs` before any npm work.
  A missing or too-old Node fails the build with a friendly
  `Node.js 20+ is required to build the Nova theme` error instead of a cryptic MSB3073. Do not
  weaken or bypass this check.

## Build & validation (authoritative)

- Build the theme: `npm run build:css` (from `Nova/`) → writes `Nova/wwwroot/css/bootstrap-theme.css`
  (plus the bootstrap-icons fonts copied to `Nova/wwwroot/css/fonts/`).
- Validate contrast: `npm run check:contrast` (from `Nova/`) — parses `_variables.scss`, computes
  WCAG ratios for the documented pairs, and asserts the compiled CSS contains none of the default
  Bootstrap-blue literals (`#0d6efd`, `#0b5ed7`, `#0a58ca`, `#86b7fe`, `rgba(13,110,253`).
- `dotnet build Nova.slnx` (or `dotnet build Nova/Nova.csproj`) triggers
  `BuildBootstrapTheme` on `Nova/Nova.csproj` before static-web-asset discovery:
  1. `CheckNodePrerequisite` runs `Nova/scripts/check-node.mjs` every build.
  2. `RestoreNpmPackages` runs `npm ci` when `package.json` or `package-lock.json` is newer than
     `Nova/obj/npm-ci.stamp`, or when `Nova/node_modules` is missing/deleted (npm's hidden
     `node_modules/.package-lock.json` is an input, so removing the tree marks the target
     out-of-date). A manifest/lockfile change re-installs even when `node_modules` already exists
     — a stale tree would otherwise silently serve outdated packages.
  3. `BuildBootstrapTheme` runs `npm run build:css` (incrementally, only when the Sass sources,
     `package.json`, or `package-lock.json` changed), then copies + registers the fonts.
  The compiled CSS is gitignored, so a clean clone must build it before the app can serve it.

## Aspire dashboard commands (theme workflow)

The `nova` resource in `Nova.AppHost/AppHost.cs` exposes three process-backed commands (they run
`npm` on the dev machine — not in a container; the AppHost controls the process, not the app):

- `install-npm-deps` (`npm ci`) — fixes a stale or missing `node_modules` tree.
- `rebuild-theme` (`npm run build:css`) — recompiles `bootstrap-theme.css`.
- `check-contrast` (`npm run check:contrast`) — the same WCAG check CI runs.

Use these from the Aspire dashboard (resource → command) to repair or validate the theme without
a terminal. Command output streams to the resource's console logs. For a headless equivalent, run
the npm scripts above directly.

## Rules

- **Never** re-add the vendored Bootstrap CSS (`Nova/wwwroot/lib/bootstrap/dist/css/`) — the JS
  bundle (`dist/js/bootstrap.bundle.min.js`) stays, but the CSS is replaced by the compiled theme.
- **Never** hardcode Bootstrap-blue literals (`#0d6efd`, `#0b5ed7`, `#0a58ca`, `#86b7fe`,
  `rgba(13,110,253`) or reintroduce the default blue `:root` variables. In component CSS use
  semantic Bootstrap CSS variables (`var(--bs-primary)`, `var(--bs-border-color)`,
  `var(--bs-body-color-rgb)`, etc.) or Bootstrap utility classes instead of raw hex/rgb values.
- Prefer theme variables over literal hex: when you need a color from the palette in
  `*.razor.css` / `app.css`, reference the `--bs-*` CSS variable rather than copying the hex.
- The theme is Sass-compiled and **not committed** to source control; never edit the generated
  `Nova/wwwroot/css/bootstrap-theme.css` directly.
