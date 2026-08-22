#!/usr/bin/env node
//
// Zero-dependency WCAG contrast validation for the Nova kelp-forest Bootstrap
// theme. It:
//
//   1. Parses hex color assignments from `scss/_variables.scss`.
//   2. Computes WCAG (2.x) relative luminance and contrast ratios for the
//      documented text/background pairs.
//   3. Asserts the compiled `wwwroot/css/bootstrap-theme.css` contains none of
//      the default Bootstrap "blue" literals.
//
// Exit code 0 = all checks pass; 1 = a contrast ratio or token assertion failed.
//
// The luminance algorithm mirrors Bootstrap's `relative-luminance()` (gamma
// threshold 0.03928). For each theme color we assert the contrast of the
// foreground Bootstrap actually selects via `color-contrast()` (black or white)
// — the same ratio the browser acceptance scenario measures through
// `A11yMeasurementHelpers.AssertContrastRatioAsync`. Thresholds: 4.5:1 for normal
// text (AA), 3:1 for the UI (non-text) link/text pairing.

import { readFileSync, existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = dirname(fileURLToPath(import.meta.url));

// ---------------------------------------------------------------------------
// WCAG 2.x relative luminance + contrast ratio helpers (Bootstrap-compatible).
// ---------------------------------------------------------------------------
function channelToLinear(c) {
  const s = c / 255;
  return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
}

function relativeLuminance(hex) {
  const { r, g, b } = parseHex(hex);
  return (
    0.2126 * channelToLinear(r) +
    0.7152 * channelToLinear(g) +
    0.0722 * channelToLinear(b)
  );
}

function contrastRatio(a, b) {
  const la = relativeLuminance(a);
  const lb = relativeLuminance(b);
  const lighter = Math.max(la, lb);
  const darker = Math.min(la, lb);
  return (lighter + 0.05) / (darker + 0.05);
}

// ---------------------------------------------------------------------------
// Parse `#rrggbb` / `#rgb` hex.
// ---------------------------------------------------------------------------
function parseHex(hex) {
  const raw = hex.trim().replace(/^#/, "");
  let r, g, b;
  if (raw.length === 3) {
    r = parseInt(raw[0] + raw[0], 16);
    g = parseInt(raw[1] + raw[1], 16);
    b = parseInt(raw[2] + raw[2], 16);
  } else if (raw.length === 6) {
    r = parseInt(raw.slice(0, 2), 16);
    g = parseInt(raw.slice(2, 4), 16);
    b = parseInt(raw.slice(4, 6), 16);
  } else {
    throw new Error(`Unsupported hex color: ${hex}`);
  }
  return { r, g, b };
}

// ---------------------------------------------------------------------------
// Parse `$name: value;` assignments out of `_variables.scss`, resolving any
// value that references another variable (e.g. `$link-color: $primary;`) down
// to the final hex swatch.
// ---------------------------------------------------------------------------
function parseVariables(filePath) {
  const text = readFileSync(filePath, "utf8");
  const raw = {};
  const re = /\$([a-z0-9-]+)\s*:\s*([^;]+?)\s*;/g;
  let match;
  while ((match = re.exec(text)) !== null) {
    raw[match[1]] = match[2];
  }

  const resolve = (value, seen = new Set()) => {
    const hex = value.trim().match(/^#([0-9a-fA-F]{3,8})$/);
    if (hex) return value.trim();
    const ref = value.trim().match(/^\$([a-z0-9-]+)$/);
    if (ref) {
      const name = ref[1];
      if (seen.has(name) || !(name in raw)) return undefined;
      seen.add(name);
      return resolve(raw[name], seen);
    }
    return undefined;
  };

  const vars = {};
  for (const [name, value] of Object.entries(raw)) {
    const hex = resolve(value);
    if (hex) vars[name] = hex;
  }
  return vars;
}

// ---------------------------------------------------------------------------
// Normalize an rgb()/rgba() literal to a leading #rrggbb token so we can reject
// the known default Bootstrap blues regardless of emitted spacing/alpha.
// ---------------------------------------------------------------------------
function normalizeRgbLiteral(value) {
  const m = /rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/.exec(value);
  if (!m) return value;
  const r = Number(m[1]).toString(16).padStart(2, "0");
  const g = Number(m[2]).toString(16).padStart(2, "0");
  const b = Number(m[3]).toString(16).padStart(2, "0");
  return `#${r}${g}${b}`;
}

// ---------------------------------------------------------------------------
// Config: threshold and documented pairs.
// ---------------------------------------------------------------------------
const NORMAL_TEXT = 4.5;
const UI_OR_LARGE_TEXT = 3.0;

// Theme colors whose Bootstrap-colour-contrast foreground (black or white) is a
// *text* foreground on a solid control background — assert AA normal-text (4.5:1).
const TEXT_ON_THEME_COLORS = [
  "primary",
  "secondary",
  "success",
  "info",
  "warning",
  "danger",
  "light",
  "dark",
];

// Colors whose foreground follows the body/link semantic — assert AA normal-text.
const LITERAL_PAIRS = [
  { name: "body text on body background", fg: "body-color", bg: "body-bg" },
  { name: "link color on body background", fg: "link-color", bg: "body-bg" },
  { name: "body text on light (subtle) background", fg: "body-color", bg: "light" },
];

// UI / non-text pairing (e.g. a subtle accent against the light surface) that is
// not required to meet the stricter text threshold.
const UI_PAIR = { name: "body-color on primary-bg-subtle (UI focus ring)", fg: "body-color", bg: "primary-bg-subtle" };

let failures = 0;

function record(name, ratio, threshold) {
  const pass = ratio >= threshold;
  if (!pass) failures += 1;
  console.log(
    `${pass ? "PASS" : "FAIL"}  ${ratio.toFixed(2)}:1 (>= ${threshold})  ${name}`,
  );
}

const varsPath = join(__dirname, "..", "scss", "_variables.scss");
if (!existsSync(varsPath)) {
  console.error(`FAIL  _variables.scss not found at ${varsPath}`);
  process.exit(1);
}
const vars = parseVariables(varsPath);

console.log("== Nova Bootstrap theme contrast check ==");
console.log("");

console.log("Parsed theme variables:");
for (const [k, v] of Object.entries(vars)) {
  console.log(`  $${k}: ${v}`);
}
console.log("");

const WHITE = "#FFFFFF";
const BLACK = "#000000";

console.log(`Text foreground on each theme color (>= ${NORMAL_TEXT}:1):`);
for (const name of TEXT_ON_THEME_COLORS) {
  const bg = vars[name];
  if (!bg) {
    console.log(`FAIL  missing $${name} in _variables.scss`);
    failures += 1;
    continue;
  }
  const whiteRatio = contrastRatio(WHITE, bg);
  const blackRatio = contrastRatio(BLACK, bg);
  // Bootstrap's color-contrast() selects the foreground with the higher ratio
  // for these colors (equivalently the first candidate that meets the minimum).
  const chosenRatio = Math.max(whiteRatio, blackRatio);
  const fg = whiteRatio >= blackRatio ? "white" : "black";
  record(`$${name} (${fg} text) on $${name} ${bg}`, chosenRatio, NORMAL_TEXT);
}
console.log("");

for (const pair of LITERAL_PAIRS) {
  const fg = vars[pair.fg];
  const bg = vars[pair.bg];
  if (!fg || !bg) {
    console.log(`FAIL  missing $${pair.fg} or $${pair.bg} in _variables.scss`);
    failures += 1;
    continue;
  }
  record(`${pair.name} [$${pair.fg} ${fg} on $${pair.bg} ${bg}]`, contrastRatio(fg, bg), NORMAL_TEXT);
}
console.log("");

// UI / large-text non-strict pair.
{
  const fg = vars[UI_PAIR.fg];
  const bg = vars[UI_PAIR.bg];
  if (fg && bg) {
    record(`${UI_PAIR.name} [$${UI_PAIR.fg} ${fg} on $${UI_PAIR.bg} ${bg}]`, contrastRatio(fg, bg), UI_OR_LARGE_TEXT);
  }
}
console.log("");

// ---------------------------------------------------------------------------
// Assert the compiled CSS has no default Bootstrap blue literals.
// ---------------------------------------------------------------------------
const cssPath = join(__dirname, "..", "wwwroot", "css", "bootstrap-theme.css");
const FORBIDDEN = ["#0d6efd", "#0b5ed7", "#0a58ca", "#86b7fe", "rgba(13,110,253"];

console.log("Compiled-output token assertions:");
if (!existsSync(cssPath)) {
  console.error(`FAIL  compiled CSS not found at ${cssPath}`);
  failures += 1;
} else {
  const css = readFileSync(cssPath, "utf8").toLowerCase();
  for (const token of FORBIDDEN) {
    const normalizedToken = normalizeRgbLiteral(token);
    const present = css.includes(token) || css.includes(normalizedToken);
    if (present) {
      console.log(`FAIL  compiled CSS contains forbidden token: ${token}`);
      failures += 1;
    } else {
      console.log(`PASS  no forbidden token: ${token}`);
    }
  }
}

console.log("");
if (failures > 0) {
  console.error(`RESULT: FAIL (${failures} failing check${failures === 1 ? "" : "s"})`);
  process.exit(1);
} else {
  console.log("RESULT: PASS (all contrast ratios and token assertions met)");
  process.exit(0);
}
