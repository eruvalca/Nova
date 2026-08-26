---
name: Nova
description: Calm, confident club operations for structured player evaluation.
colors:
  wayfinding-teal: "#0E7C7B"
  kelp-ochre: "#95651A"
  kelp-olive: "#65762A"
  sunlit-aqua: "#16AAA2"
  signal-amber: "#D58B22"
  copper-rust: "#B44E32"
  sea-glass: "#DDF2EC"
  abyss-forest: "#142F2E"
  foam-mist: "#F4F8F3"
  paper-white: "#FFFFFF"
typography:
  display:
    fontFamily: "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "clamp(3.25rem, 6.5vw, 6.5rem)"
    fontWeight: 750
    lineHeight: 0.94
    letterSpacing: "-0.06em"
  display-cta:
    fontFamily: "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "clamp(3rem, 7vw, 6rem)"
    fontWeight: 750
    lineHeight: 0.94
    letterSpacing: "-0.055em"
  display-collaboration:
    fontFamily: "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "clamp(2.5rem, 5vw, 4.75rem)"
    fontWeight: 750
    lineHeight: 0.98
    letterSpacing: "-0.05em"
  section-headline:
    fontFamily: "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "clamp(2.25rem, 4.5vw, 4.25rem)"
    fontWeight: 700
    lineHeight: 1
    letterSpacing: "-0.045em"
  headline:
    fontFamily: "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "clamp(2rem, 4vw, 3.25rem)"
    fontWeight: 700
    lineHeight: 1.1
    letterSpacing: "-0.04em"
  headline-wide:
    fontFamily: "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "clamp(2rem, 4vw, 3.75rem)"
    fontWeight: 700
    lineHeight: 1.05
    letterSpacing: "-0.045em"
  feature-title:
    fontFamily: "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "clamp(1.35rem, 2vw, 1.75rem)"
    fontWeight: 700
    lineHeight: 1.15
  title:
    fontFamily: "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "1.125rem"
    fontWeight: 700
    lineHeight: 1.25
  brand-title:
    fontFamily: "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "1.25rem"
    fontWeight: 750
    lineHeight: 1
  lead:
    fontFamily: "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "clamp(1rem, 1.5vw, 1.1875rem)"
    fontWeight: 400
    lineHeight: 1.65
  body:
    fontFamily: "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.7
  body-small:
    fontFamily: "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "0.875rem"
    fontWeight: 400
    lineHeight: 1.55
  label:
    fontFamily: "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "0.8125rem"
    fontWeight: 650
    lineHeight: 1.25
    letterSpacing: "0.01em"
  eyebrow:
    fontFamily: "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "0.75rem"
    fontWeight: 700
    lineHeight: 1.25
    letterSpacing: "0.08em"
  micro:
    fontFamily: "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "0.6875rem"
    fontWeight: 700
    lineHeight: 1.2
    letterSpacing: "0.06em"
  nano:
    fontFamily: "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif"
    fontSize: "0.625rem"
    fontWeight: 700
    lineHeight: 1.2
    letterSpacing: "0.08em"
rounded:
  control: "0.25rem"
  surface: "0.375rem"
  round: "50%"
spacing:
  xs: "0.25rem"
  sm: "0.5rem"
  md: "0.75rem"
  lg: "1rem"
  xl: "1.25rem"
  2xl: "1.5rem"
  3xl: "2rem"
  4xl: "3rem"
components:
  button-primary:
    backgroundColor: "{colors.wayfinding-teal}"
    textColor: "{colors.paper-white}"
    typography: "{typography.label}"
    rounded: "{rounded.control}"
    padding: "0.625rem 1rem"
    height: "2.75rem"
  button-secondary:
    backgroundColor: "{colors.paper-white}"
    textColor: "{colors.deep-forest}"
    typography: "{typography.label}"
    rounded: "{rounded.control}"
    padding: "0.625rem 1rem"
    height: "2.75rem"
  input:
    backgroundColor: "{colors.paper-white}"
    textColor: "{colors.deep-forest}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "0.625rem 0.75rem"
    height: "2.75rem"
  navigation-active:
    backgroundColor: "{colors.pale-kelp}"
    textColor: "{colors.deep-forest}"
    typography: "{typography.label}"
    rounded: "{rounded.control}"
    padding: "0.625rem 0.75rem"
    height: "2.75rem"
  board:
    backgroundColor: "{colors.paper-white}"
    textColor: "{colors.deep-forest}"
    rounded: "{rounded.surface}"
    padding: "1.25rem"
---

# Design System: Nova

## Overview

**Creative North Star: "Fieldhouse Wayfinding"**

Nova should feel like entering a well-run fieldhouse on campaign day: the route is obvious, the working surfaces are calm, and every signal has an operational meaning. The visual system combines venue-directory clarity with the warmth of the kelp-forest palette. It is factual rather than performative, confident rather than loud, and structured without becoming clinical.

Public surfaces may use oversized type, plotted routes, and illustrative working boards to persuade. Authenticated surfaces prioritize scanability, bounded data, and clear task hierarchy. Both modes share the same materials: pale fields, deep forest text, compact labels, fine borders, and teal wayfinding.

**Key Characteristics:**
- Route markers and active rails communicate real lifecycle or navigation state.
- Operational boards use restrained borders, compact headings, and factual status.
- Teal leads action; amber identifies attention; neutrals carry most of the screen.
- Large type creates orientation, while dense working areas remain orderly.
- Venue-directory geometry appears in rails, grids, signs, and circular markers.

## Colors

The kelp-forest palette is grounded, legible, and functional. It follows the source photograph's hierarchy rather than distributing color evenly: abyssal teal carries the depth, sunlit aqua opens the water, and smaller olive, ochre, amber, and copper notes recall kelp fronds catching light.

### Primary
- **Wayfinding Teal (`#0E7C7B`):** The active route, primary action, selected navigation, links, and the strongest branded surface.
- **Sunlit Aqua (`#16AAA2`):** Informational support and atmospheric light where the primary teal would imply an action or selection.

### Secondary
- **Kelp Ochre (`#95651A`):** A warm supporting accent for human, collaborative, or contextual material; never a competing primary action.
- **Kelp Olive (`#65762A`):** Confirmed success, completed work, trust, and healthy status.

### Tertiary
- **Signal Amber (`#D58B22`):** Actionable attention and pending work. Use on bounded notices, not as ambient decoration.
- **Copper Rust (`#B44E32`):** Destructive actions, errors, and blocked states.

### Neutral
- **Abyss Forest (`#142F2E`):** Primary text and dark campaign-sequence surfaces.
- **Foam Mist (`#F4F8F3`):** The application canvas and broad quiet background.
- **Sea Glass (`#DDF2EC`):** Active navigation fields, subtle primary surfaces, and light branded sections.
- **Paper White:** Working boards, controls, and high-clarity content surfaces.

### Named Rules

**The Signal Integrity Rule.** Color must tell the truth: teal means route or action, amber means attention, olive means success, and copper means danger.

**The Quiet Field Rule.** Foam Mist, Sea Glass, Paper White, and Abyss Forest should carry most of every screen; accents remain scarce enough to retain meaning.

**The Sunlight Rule.** Persuade surfaces may compose larger aqua, ochre, and olive regions to recreate the photograph's depth-to-light rhythm. Operate surfaces use those colors only when their informational role is clear.

## Typography

**Display Font:** Native system sans-serif, led by Segoe UI on Windows

**Body Font:** Native system sans-serif, led by Segoe UI on Windows

**Character:** One pragmatic sans-serif family keeps operational interfaces fast and familiar. Hierarchy comes from scale, weight, compact tracking, and line length rather than decorative font pairing.

### Hierarchy
- **Display** (750, responsive oversized scale, 0.94 line-height): Public-page statements and rare campaign-level orientation moments.
- **Display CTA / Collaboration** (750, responsive expressive scales, 0.94-0.98 line-height): Public conversion and role-story statements that need more range than routine headings.
- **Section Headline / Headline Wide** (700, responsive section scales, 1-1.05 line-height): Public section openings with controlled variation for composition.
- **Headline** (700, responsive page scale, 1.1 line-height): Page titles, campaign signs, and major section openings.
- **Feature Title / Brand Title** (700-750, compact emphasis scale): Product-preview anchors, brand signatures, and feature-level labels.
- **Title** (700, compact title scale, 1.25 line-height): Board titles, empty-state headings, and prominent row labels.
- **Lead** (400, responsive 1-1.1875rem scale): Introductory persuasive copy with a bounded line length.
- **Body** (400, base scale, 1.7 line-height): Explanations and narrative copy; keep persuasive copy near 37rem and avoid long unbounded lines.
- **Body Small** (400, 0.875rem scale): Supporting descriptions and secondary operational context.
- **Label** (650, compact scale, 0.01em tracking): Navigation, metadata, table headings, statuses, and action labels.
- **Eyebrow / Micro / Nano** (700, 0.75-0.625rem scale, tracked): Uppercase route, metadata, and diagram labels; reserve Nano for dense illustrative previews.

### Named Rules

**The One Family Rule.** Do not add novelty through typefaces; create distinction through disciplined scale, weight, spacing, and composition.

**The Compressed Orientation Rule.** Large headings use tighter tracking and line-height, while working copy stays comfortably open.

## Layout

Authenticated screens use a fixed 15rem navigation rail from the medium breakpoint upward and a horizontally scrollable bottom route strip below it. Main content is centered in fields up to 90-100rem wide, with 1rem mobile gutters, 2rem desktop gutters, and 3rem wide-screen gutters.

Operational pages favor asymmetric grids: a flexible primary board beside a bounded 15-22rem status rail. Public sections can be more expressive, pairing an editorial content column with an illustrative working surface. Grids collapse to one column before content becomes cramped; actions become full-width on narrow phones; lifecycle routes scroll horizontally rather than wrapping into ambiguity.

Spacing follows a compact quarter-rem rhythm, with 1-1.5rem as the default internal board padding and 1.5-3rem between major regions. Preserve visible grouping before adding more containers.

### Named Rules

**The Working Field Rule.** Give the main task the flexible column and bound supporting context to a narrower rail.

**The Route Continuity Rule.** Navigation and lifecycle routes may scroll, but their order, labels, and connecting line must remain intact.

## Elevation & Depth

The system is flat by default. Depth comes from tonal layering, fine borders, overlapping route geometry, and occasional hard offset blocks on persuasive surfaces. Authenticated working boards do not float. Soft ambient shadows are reserved for system-level overlays; branded previews may use a solid offset shadow to evoke a posted fieldhouse placard.

### Shadow Vocabulary
- **Posted Board:** A solid Sea Glass or warm-subtle offset of 0.625-1.25rem behind public preview boards; never use on routine app panels.
- **Overlay Lift:** A restrained ambient shadow for dialogs and reconnect surfaces that must sit above the application.

### Named Rules

**The Flat-by-Default Rule.** Working surfaces earn separation through tone and a one-pixel border, not ambient card shadows.

## Shapes

Nova uses gently squared geometry. Controls use a 0.25rem radius and boards use a 0.375rem radius, preserving a practical, constructed feel. Circles are reserved for people, status dots, route markers, and the dot inside the rotated Nova brand mark.

Borders are fine and quiet. Strong silhouettes come from the 45-degree brand tile, horizontal route lines, bounded signs, and hard offset public previews rather than oversized pill shapes.

### Named Rules

**The Earned Circle Rule.** A circle identifies a person, a point on a route, or a live status; it is not a generic container shape.

**The No Decorative Pill Rule.** Use rounded pills only where the component is semantically a badge or filter state.

## Components

### Buttons
- **Shape:** Compact, gently squared controls using the control radius.
- **Primary:** Wayfinding Teal with Paper White text, a minimum 2.75rem target, and confident semibold labeling.
- **Hover / Focus:** Darken the teal without shifting hue; preserve a clear native-compatible focus ring and never rely on color alone.
- **Secondary / Ghost:** Paper White or transparent fields with Abyss Forest text and a quiet border; text links remain visibly underlined where they sit inside prose.

### Chips
- **Style:** Pale semantic background, corresponding emphasis text, compact label type, and a quiet border when needed for separation.
- **State:** Selected filters may use a teal-tinted field; status chips must keep their semantic color assignment.

### Cards / Containers
- **Corner Style:** Gently squared board radius.
- **Background:** Paper White for working boards, Sea Glass for selected or branded light fields, and Abyss Forest for focused public sequences.
- **Shadow Strategy:** Flat in the application; hard offset only for featured public previews.
- **Border:** One-pixel neutral or semantic border.
- **Internal Padding:** Usually 1-1.25rem, expanding to 1.5rem for campaign signs and spacious public boards.

### Inputs / Fields
- **Style:** Paper White field, Abyss Forest text, quiet neutral stroke, and the control radius.
- **Focus:** Teal border and a visible restrained focus ring.
- **Error / Disabled:** Copper semantic treatment for errors; disabled controls lower contrast but remain readable and retain their boundary.

### Navigation
- **Style:** Compact semibold labels with line icons at rest and filled icons when active. The active state uses Sea Glass, Abyss Forest emphasis, and a teal edge rail.
- **Responsive behavior:** The desktop rail becomes a fixed bottom route strip with horizontally scrollable destinations and a top active marker on small screens.

### Campaign Route

A connected sequence of circular markers and labels represents the campaign lifecycle. Only factual current state may be highlighted. The active step uses Wayfinding Teal, a Sea Glass field, and a clear edge indicator; the route remains ordered and horizontally scrollable when space is limited.

### Working Board

Tables, rosters, status rows, and activity feeds share a bordered Paper White board. Use a compact heading band, full-width interactive rows, restrained hover tint, and bounded scrolling for long working sets.

## Do's and Don'ts

### Do:
- **Do** use route markers, rails, and signs when they clarify real navigation or lifecycle state.
- **Do** reserve Wayfinding Teal for primary actions, active destinations, and meaningful links.
- **Do** surface actionable attention in a bounded Signal Amber region with explicit copy.
- **Do** keep operational data factual, aligned, and scannable in working boards or tables.
- **Do** collapse grids and widen action targets before content becomes cramped.
- **Do** use visual hierarchy before adding another card or border.

### Don't:
- **Don't** use gradient color fields, glass effects, glossy sports imagery, or scoreboard styling. CSS gradients may construct restrained grid or route-line patterns, but never simulate depth or decoration.
- **Don't** use decorative progress, fabricated metrics, or route markers that do not correspond to real state.
- **Don't** turn every section into an isolated card or generic equal-weight card grid.
- **Don't** use thick colored side borders as decoration; edge rails belong to active navigation.
- **Don't** introduce Bootstrap-blue literals or bypass the Sass theme source of truth.
- **Don't** use ambient shadows on routine authenticated surfaces.
