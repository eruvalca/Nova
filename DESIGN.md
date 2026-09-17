---
version: alpha
name: Nova
description: Fieldhouse Wayfinding for collaborative club tryout operations.
colors:
  primary: "#0E7C7B"
  secondary: "#95651A"
  success: "#65762A"
  info: "#16AAA2"
  warning: "#D58B22"
  danger: "#B44E32"
  light: "#DDF2EC"
  dark: "#142F2E"
  body-bg: "#F4F8F3"
  paper-white: "#FFFFFF"
  border-hairline: "#DEE2E6"
typography:
  display:
    fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif'
    fontSize: "clamp(3.25rem, 6.5vw, 6.5rem)"
    fontWeight: 750
    lineHeight: 0.94
    letterSpacing: "-0.06em"
  headline:
    fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif'
    fontSize: "clamp(2.25rem, 4.5vw, 4.25rem)"
    fontWeight: 750
    lineHeight: 1.02
    letterSpacing: "-0.045em"
  page-title:
    fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif'
    fontSize: "2rem"
    fontWeight: 700
    lineHeight: 1.2
    letterSpacing: "-0.025em"
  title:
    fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif'
    fontSize: "1.25rem"
    fontWeight: 650
    lineHeight: 1.2
    letterSpacing: "0.01em"
  body:
    fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif'
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.7
    letterSpacing: "0em"
  label:
    fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif'
    fontSize: "0.875rem"
    fontWeight: 650
    lineHeight: 1.25
    letterSpacing: "0.01em"
  eyebrow:
    fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif'
    fontSize: "0.75rem"
    fontWeight: 750
    lineHeight: 1.25
    letterSpacing: "0.12em"
rounded:
  control: "0.25rem"
  board: "0.375rem"
  full: "50%"
spacing:
  micro: "0.25rem"
  tight: "0.5rem"
  compact: "0.75rem"
  base: "1rem"
  comfortable: "1.25rem"
  roomy: "1.5rem"
  spacious: "2rem"
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.paper-white}"
    typography: "{typography.label}"
    rounded: "{rounded.control}"
    padding: "0.625rem 1rem"
    height: "2.75rem"
  button-outline:
    backgroundColor: transparent
    textColor: "{colors.primary}"
    typography: "{typography.label}"
    rounded: "{rounded.control}"
    padding: "0.625rem 1rem"
    height: "2.75rem"
  input:
    backgroundColor: "{colors.paper-white}"
    textColor: "{colors.dark}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "0.625rem 0.75rem"
    height: "2.75rem"
  board:
    backgroundColor: "{colors.paper-white}"
    textColor: "{colors.dark}"
    rounded: "{rounded.board}"
    padding: "1.5rem"
  navigation-active:
    backgroundColor: "{colors.light}"
    textColor: "{colors.dark}"
    typography: "{typography.label}"
    rounded: "{rounded.control}"
    padding: "0.625rem 0.75rem"
    height: "2.75rem"
---

# Design System: Nova

## Overview

**Creative North Star: "Fieldhouse Wayfinding"**

Nova should feel like entering a well-run fieldhouse where every sign, roster sheet, and route marker helps staff make the next decision. The visual language is operational, grounded, and quietly confident: matte enamel signs provide orientation, pale field sheets hold the work, ink-dark type carries authority, and restrained state colors clarify what needs attention.

The system is tactile, legible, and restrained. It favors physical organization over software ornament: stable rails, bounded boards, precise hairlines, compact roster rows, and visible workflow stops. Marketing surfaces may expand this world into large-scale route diagrams and offset printed layers; application surfaces stay calmer and denser so club administrators and evaluators can work quickly.

**Key Characteristics:**

- Physical wayfinding metaphors without nostalgic decoration.
- Flat operational boards with precise borders and strong information hierarchy.
- Large, compressed campaign language paired with quiet, readable working text.
- Teal wayfinding, gold attention, and ink-dark structure used by semantic role.
- Responsive navigation that preserves every route and full label.

## Colors

The kelp-forest palette combines cool fieldhouse surfaces with earthy collaborative and attention colors.

### Primary

- **Wayfinding Teal** (`#0E7C7B`): the route color for primary actions, active navigation rails, selected workflow stops, and deliberate orientation cues.

### Secondary

- **Kelp Ochre** (`#95651A`): a human, club-centered accent for collaboration and supporting context; it never competes with the primary action.
- **Kelp Olive** (`#65762A`): success and completed collaborative states.
- **Sunlit Aqua** (`#16AAA2`): informational emphasis and cool marketing-layer accents.
- **Signal Amber** (`#D58B22`): unresolved work and attention states only.
- **Copper Rust** (`#B44E32`): destructive actions, errors, and failure states only.

### Neutral

- **Sea Glass** (`#DDF2EC`): active fields, quiet tinted surfaces, and light enamel details.
- **Abyss Forest** (`#142F2E`): primary ink, dark signs, and the deepest structural surface.
- **Foam Mist** (`#F4F8F3`): the default page field and soft paper ground.
- **Paper White** (`#FFFFFF`): bounded working boards and controls that need separation from Foam Mist.
- **Roster Hairline** (`#DEE2E6`): borders, dividers, table rules, and structural separation.

### Named Rules

**The Wayfinding Rule.** Wayfinding Teal belongs to primary action and orientation, never ambient decoration.

**The Gold Means Attention Rule.** Signal Amber marks unresolved work; it must not become a competing brand accent.

**The State Has Words Rule.** Color never carries status alone; pair it with a label, icon, count, or accessible state.

## Typography

**Display Font:** the native system sans stack, led by `system-ui` and Segoe UI.

**Body Font:** the same native system sans stack.

**Character:** One pragmatic sans-serif family keeps the product immediate and durable. Personality comes from proportion: campaign headlines are heavy, tightly tracked, and compressed; working copy, labels, and metadata stay neutral and highly legible.

### Hierarchy

- **Display** (750, `clamp(3.25rem, 6.5vw, 6.5rem)`, 0.94): public hero statements and rare campaign-scale declarations.
- **Headline** (750, `clamp(2.25rem, 4.5vw, 4.25rem)`, 1.02): major landing sections and large route moments.
- **Page Title** (700, `2rem`, 1.2): stable operational screen identity in account, dashboard, and other working surfaces.
- **Title** (650, `1.25rem`, 1.2): directory headings, brand names, and compact operational section titles.
- **Body** (400, `1rem`, 1.7): explanatory and instructional copy; keep sustained text near 65–70 characters per line.
- **Label** (650, `0.875rem`, 0.01em): controls, navigation, form labels, and working metadata.
- **Eyebrow** (750, `0.75rem`, 0.12em, uppercase): section numbers, route names, and sparse directional captions.

### Named Rules

**The One Family Rule.** Build hierarchy through scale, weight, tracking, and case before introducing another typeface.

**The Working Text Rule.** Operational labels remain at least `0.875rem`; smaller type is reserved for brief metadata or non-interactive captions.

## Layout

Nova uses a campaign-map spatial model: stable navigation frames a broad working field, and content is arranged as sequences, directories, and boards rather than interchangeable card mosaics.

Authenticated layouts use a fixed `15rem` left rail at `768px` and above, with content padding increasing from `2rem` to `3rem` on wider screens and a broad `100rem` maximum working field. Below `768px`, navigation becomes a fixed bottom bar whose hamburger opens one complete paper sheet containing every route. Anonymous one-route states remain inline, and no-script mode exposes a horizontally scrollable fallback.

Public surfaces use Bootstrap containers, asymmetric two-column compositions where content benefits, and generous fluid section spacing. The core rhythm is a `0.25rem` half-step with recurring `0.5rem`, `1rem`, `1.5rem`, and `2rem` groupings. Use the established content breakpoints at `575.98px`, `767.98px`, and `991.98px`; collapse grids when their content becomes cramped, not merely because a device label changed.

Touch targets are at least `2.75rem`. Tables stay inside responsive scroll regions, route-marker sequences remain horizontally scrollable when their minimum width exceeds the viewport, and focus indicators must never be clipped by those regions.

Evaluation applies this model as a finder beside a flat player sheet when space permits, then one focused finder or selected-player stage on narrower screens. The single-stage sheet is centered with a bounded reading width and a compact leading inset. Identity and campaign headings wrap rather than force horizontal page overflow. Shared observations use ruled rows and bounded previews so history can grow without displacing the next capture; placement and trait provenance remain available through inline disclosures. These are applications of the existing board and working-text rules, not a new global grid or type scale.

### Named Rules

**The One Route Rule.** Navigation and workflow progress remain visible as one legible route; never hide required destinations or abbreviate labels into ambiguity.

**The Board, Not Cards Rule.** Group operational work into purposeful fields, directories, and bounded boards instead of repeating generic summary cards.

### Active Close Review Layout

The Active Close review keeps verdict and campaign totals beside blocker links in one flat board, followed by a full-width roster and the shared lifecycle checkpoint. Below `768px`, the review becomes one column and the blocker divider disappears. The roster toolbar wraps; below `576px`, its search form takes the available width while the table scrolls within its own region. Bound the roster vertically so its paging and the following checkpoint remain reachable without traversing the entire page of rows.

## Elevation & Depth

Application surfaces are flat by default. Foam Mist, Paper White, Sea Glass, one-pixel hairlines, and compact spacing create depth without ambient shadow. Marketing compositions may use crisp, zero-blur offset layers in the palette to suggest stacked field sheets or printed boards; this is structural illustration, not a general card treatment.

### Shadow Vocabulary

- **Sunlit Sheet Offset** (`1.25rem 1.25rem 0 var(--bs-info-bg-subtle)`): large public campaign previews only; reduce the offset on narrow screens.
- **Kelp Sheet Offset** (`1rem 1rem 0 var(--bs-secondary-bg-subtle)`): public collaboration boards only.
- **Focus Ring** (`0 0 0 var(--bs-focus-ring-width) var(--bs-focus-ring-color)`): keyboard and focused input state, never ambient decoration.

### Named Rules

**The Flat Operations Rule.** Working boards, forms, directories, tables, and navigation rest without ambient shadows.

**The Printed Layer Exception.** Crisp offset layers belong only to marketing compositions that depict physical sheets or signs.

## Shapes

Controls, buttons, navigation rows, and alerts use gently squared `0.25rem` corners. Bounded boards use `0.375rem` corners. These small radii keep the system tactile without making it soft or playful.

Circles are semantic: people, status points, route stops, or the punched dot inside the Nova mark. The mark itself is a teal square rotated into a diamond with a centered Sea Glass dot. Edge rails stay narrow and functional; thick decorative side borders are not part of the language.

### Named Rules

**The Functional Circle Rule.** Use circles only for people, status, route markers, or the brand dot—never as decoration.

**The Precise Edge Rule.** Borders are one-pixel hairlines; the active navigation rail may grow to `0.25rem` because it carries route state.

## Components

Components feel tactile, legible, and restrained. Their identity comes from semantic color, precise borders, compact radii, and clear states rather than ornament.

### Buttons

- **Shape:** gently squared (`0.25rem`) with a minimum `2.75rem` height.
- **Primary:** Wayfinding Teal with Paper White text, label-weight type, and compact `0.625rem 1rem` padding.
- **Hover / Focus:** deepen or strengthen the teal without changing semantic role; keyboard focus uses the theme focus ring with a small offset.
- **Secondary / Ghost:** transparent or Paper White with a Wayfinding Teal border and text; avoid low-contrast tonal buttons for critical actions.

### Status Labels

- **Style:** labeled state treatments use the semantic subtle background, emphasis text, and border for success, information, attention, or danger.
- **State:** always include readable status text; pills are reserved for true compact statuses and never used as decoration.

### Cards / Containers

- **Corner Style:** bounded boards use `0.375rem` corners.
- **Background:** Paper White or Foam Mist depending on the surrounding field.
- **Shadow Strategy:** flat in application surfaces; see the Printed Layer Exception for public compositions.
- **Border:** one Roster Hairline around a purposeful region.
- **Internal Padding:** usually `1rem` on compact screens and `1.5rem` on wider screens.

### Inputs / Fields

- **Style:** Paper White field, one Roster Hairline, `0.25rem` corners, and at least `2.75rem` height.
- **Focus:** Wayfinding Teal border plus the theme focus ring.
- **Error / Disabled:** Copper Rust messaging below the field; disabled state remains legible and clearly unavailable.

### Navigation

- The authenticated shell is a `15rem` Sea Glass rail on larger screens and a fixed bottom bar with a complete menu sheet below `768px`.
- Active items use a Sea Glass field, stronger ink, a filled icon, and a teal edge marker: left rail on desktop, top marker in the mobile sheet.
- Leading icons and avatars share a uniform `2rem` lane so every label begins on the same axis.
- Public navigation keeps the Nova diamond, name, and descriptor together, with full-size action buttons and a compact mobile collapse.

### Route Markers

Connected circular stops turn campaigns and multi-step workflows into a visible route. The active stop uses Wayfinding Teal and Sea Glass; inactive stops use quiet hairlines and ink. Route markers supplement labels and never replace them.

### Shared Evidence Sheet

A flat, attributable working record extends the existing board language for evaluation.

- **Identity and context:** tryout number and full name lead the sheet. Graduation year, written campaign outcome, and effective season context remain subordinate. Compact placement provenance opens inline; directional SVGs accompany Back to results and the Active-campaign Place player handoff.
- **Shared traits:** wrapping, gently squared Sea Glass labels use emphasis text and a disclosure chevron. Their expanded state reveals actor and time attribution and any permitted removal action. The treatment identifies shared applied evidence; it does not assign meaning to trait colors.
- **Observation rows:** author and time frame readable text above a hairline. Notes preserve line breaks, wrap long content, and bound sustained reading to `70ch`. Older history and full long-note text use explicit disclosure; author actions retain both the minimum target width and height.
- **Capture and state:** Paper White inputs sit within the sheet. The note composer keeps sharing/count help beside its label, followed by distinct full-width Save note and Find another player actions. Visible focus, written mutation feedback, and inline read-only explanation carry state. Closed history preserves evidence and any retained text while removing mutation affordances.

The evaluation composition and locked comp remain in the [evaluation surface brief](.impeccable/surfaces/evaluation.md) and [approved Shared evidence notebook image](.impeccable/mocks/decision/issue-198-shared-notebook.png). The [issue #198 finish record](.impeccable/review/issue-198/finish-review.md) records the independent reviewer's scoped ship verdict: all eight fixes resolved with no material regressions. The user approved sheet-relative Evaluate measurement with a separate preserved-shell review. The [measurement manifest](https://github.com/eruvalca/Nova/blob/b577001319b3302da723d93de60356852b3ba00b/.impeccable/review/issue-198/sheet-relative/manifest.json) and [report](https://github.com/eruvalca/Nova/blob/b577001319b3302da723d93de60356852b3ba00b/.impeccable/review/issue-198/sheet-relative/report.json) record **76.11% against the unchanged 72% threshold**, using one border-based translation of 113 native pixels, with no resizing or additional regional shift. The original full-frame **73.43%** report and raw regional missing/drift labels remain preserved; the scoped approval does not convert them into an unqualified mechanical pass. The reviewer resolved those regional findings through visible-content inspection and the accepted touch-context and system-typography adaptations. The [separate shell and coverage review](.impeccable/review/issue-198/sheet-relative/shell-review.md) preserves incumbent navigation and campaign context; older history is evidenced in mobile/desktop full-page captures, outside the hero's quantitative coverage. This records visual acceptance for source `381d5019`, not publication or an independent rerun of its behavioral validation.

No new palette, type, spacing, radius, or acceptance-threshold token is introduced by this surface. The observed score is not a design rule. Existing system-display, eyebrow, and marketing-offset guidance is preserved as incumbent authority under the explicit preservation scope; this merge does not extend those devices into new evaluation rules or erase their craft-floor conflicts.

### Active Close Review

- **Readiness:** written verdicts use the existing warning or success subtle field, emphasis text, and border. Whole-campaign totals remain above the roster; blocker rows pair a written condition with a Review players link. Loading and failed reads retain explicit status and retry text.
- **Roster:** search and blocker controls precede one semantic table. Quiet column headings and Sea Glass group bands separate structure from participant rows. Group counts explicitly describe the current page; campaign-local outcomes remain distinct from subordinate inherited-decision context. Participant and paging links retain `44px` minimum heights and visible teal focus outlines.

**The Scoped Totals Rule.** In Close review, distinguish whole-campaign outcome totals from filtered participant counts and page-local group counts in visible text.

### Shared Lifecycle Checkpoint

A hairline separates lifecycle action from the evidence above it. Current state and the consequence of changing it precede the available action; members see an administrator explanation, and unavailable actions carry a written reason. Eligible review reveals an inline Paper White confirmation containing campaign and season identity, outcome totals, consequences, and separate commit and Cancel controls. Busy, unavailable, and request-result feedback remain written beside the checkpoint; an unavailable read offers a read retry. After a successful change and refreshed evidence, focus returns to the checkpoint heading.

**The Review Before Commit Rule.** The shared lifecycle checkpoint presents refreshed evidence in a separate inline confirmation before exposing the final lifecycle commit.

The Active Close review and shared checkpoint retain the approved reference and comparison disposition in the [Close surface brief](.impeccable/surfaces/issue-256-closeout.md). The finished #257 Closed record is documented below. No new palette, type, spacing, radius, or acceptance-threshold token is introduced. Source-defined ready, confirmation, and recovery states are recorded as component behavior, not as a claim of captured visual acceptance. Existing system-display, eyebrow, and marketing-offset guidance remains preserved incumbent authority, not a new Close convention.

### Closed Campaign Record

A flat Paper White record keeps final outcomes beside their original attribution, within the existing campaign shell and four Route Markers.

- **Closure and scope:** campaign and season identity, closing actor and time, and whole-campaign totals precede discovery. Written read-only context explains that later campaigns do not change these outcomes. The Scoped Totals Rule continues through matching-participant counts and page-local group labels.
- **Discovery and roster:** labeled name/tryout search and outcome filters use a native GET form and URL-backed paging. One semantic table groups final outcomes by team or outcome with Sea Glass bands; the selected row shares that field and its participant link carries accessible current state. Archived player/team labels and decision actor/time remain readable beside the original outcome.
- **Inline history:** selecting a participant opens campaign-only decision changes below the roster, separated by a hairline. Actor, time, and written change descriptions lead each ruled row. Earlier/latest links page history, Close history dismisses the selection, and Read evaluation uses the existing evaluation destination. Recent lifecycle activity occupies a separate bounded board before the existing shared checkpoint; it does not create another lifecycle command surface.
- **Bounds and responsive behavior:** the roster scroll region is keyboard-focusable and bounded to `24rem`; participant and lifecycle history lists are bounded to `20rem`. The closure summary stacks below `768px`. Below `576px`, filter fields and the submit button fill the available width while the table scrolls internally. Links and controls retain `44px` minimum heights, visible teal focus, and wrapping identity text.
- **Read states:** record, selected history, and lifecycle activity each expose written loading, failure, and retry states. Native empty, filtered-empty, and out-of-range pages have distinct explanations and applicable recovery links. Interactive participant selection focuses its history heading after loading.

**The Campaign-Local Evidence Rule.** In the Closed record, keep the final outcome and its attributable campaign-only history together; distinguish lifecycle activity in its own labeled region and retain lifecycle actions in the shared checkpoint.

Source: [Closed record markup](Nova.UI/Features/Campaigns/Components/CampaignClosedRecord.razor), [read and navigation behavior](Nova.UI/Features/Campaigns/Components/CampaignClosedRecord.razor.cs), and [component styles](Nova.UI/Features/Campaigns/Components/CampaignClosedRecord.razor.css). The [#257 surface brief](.impeccable/surfaces/issue-257-closed-record.md) records the approved code-led extension; no new comp applies. The [independent finish review](.impeccable/review/issue-257/finish-review.md) returned **ship (scoped PASS)** against the incumbent Fieldhouse system, with no separate quality-bar card. Its [desktop](.impeccable/review/issue-257/captures/desktop.png), [phone history](.impeccable/review/issue-257/captures/phone-history.png), [phone filtered](.impeccable/review/issue-257/captures/phone-filtered.png), and [phone native empty](.impeccable/review/issue-257/captures/phone-native-empty.png) captures are finish evidence, not shipping raster assets or proof of uncaptured runtime states. The historical #256 comp and local hero-state disposition remain unchanged. This scoped addition introduces no tokens and does not refresh unrelated incumbent sidecar guidance.

### Players Directory

The club directory extends the flat working field with a page title, manual Add action, counted lifecycle destinations, compact discovery, result information, one bounded table, and paging.

- **Lifecycle and scope:** Active and Archived are ordinary destination links with written counts; the current link uses teal text, a bottom rule, and `aria-current="page"`. Club-wide totals remain separate from filtered result counts. Missing totals display unavailable evidence rather than a zero.
- **Discovery and return:** labeled name/tryout search, graduation year, and tag fields use a native GET form with interactive enhancement. Filters and paging survive ordinary record and Add/Edit links and the return to the directory. A saved tag missing from the available choices stays named and removable.
- **Rows and bounds:** twenty results occupy a keyboard-focusable scroll region with sticky column headings. Names, tags, and campaign text wrap inside the table; horizontal overflow stays local. Record links, row actions, lifecycle links, and discovery controls retain the existing `2.75rem` minimum height. At narrower widths, search and Apply each span the discovery grid while year and tag remain paired; on phones, the club total takes its own line.
- **Recovery:** club totals, tag choices, and player results have separate written unavailable/retry states. Empty lifecycle, filtered-empty, and out-of-range pages retain contextual recovery. Manual mutations use the existing form host and inline archive confirmation; this directory does not establish a new form design.

**The Directory Scope Rule.** Keep club-wide lifecycle totals distinct from filtered player results, and name unavailable regional evidence without converting it to zero or clearing retained discovery state.

Source: [directory markup](Nova.UI/Features/Players/Pages/Players.razor), [styles](Nova.UI/Features/Players/Pages/Players.razor.css), and [URL state](Nova.UI/Features/Players/Services/PlayersUrlState.cs). The [player-intake brief's #263 section](.impeccable/surfaces/player-intake.md) owns the approved composition, reference A, delivery boundaries, and validation link. This extension adds no palette, type, spacing, radius, or acceptance-threshold tokens. Incumbent system-display, eyebrow, and marketing-offset guidance remains preserved authority; those craft-floor conflicts are not new directory conventions.

## Do's and Don'ts

### Do:

- **Do** use Wayfinding Teal for the primary action, active route, or deliberate orientation cue.
- **Do** build operational hierarchy with flat boards, hairlines, spacing, and typography.
- **Do** preserve complete navigation with full labels across responsive states.
- **Do** keep controls and mobile actions at least `2.75rem` high.
- **Do** use semantic Bootstrap variables in component styles and keep the Sass palette as the implementation token source.
- **Do** generalize fieldhouse and workflow metaphors across club sports rather than relying on soccer-only decoration.

### Don't:

- **Don't** fall back to generic SaaS card grids or walls of interchangeable summary tiles.
- **Don't** use ornamental sports motifs, stadium clichés, or soccer-specific decoration as product identity.
- **Don't** use gradient surface fills, glass effects, or ambient shadows; hairline construction gradients are acceptable only when they render structural rules rather than depth.
- **Don't** use decorative pills or circles; every rounded compact shape must communicate a real status, person, route stop, or control.
- **Don't** hardcode palette hex values in component CSS; use the semantic `--bs-*` variables.
- **Don't** let Signal Amber compete with primary action or use color as the only expression of state.
