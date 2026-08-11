# Nova MVP Screen Layout and Design Specifications

## Purpose

This document specifies the layout, structure, and overall visual design for every screen delivered by the MVP product workflows described in `plans/mvp-product-workflows.md`. It is the contract for drafting design mockups: a mockup agent should be able to reproduce each screen's regions, element placement, labels, states, and role-dependent behavior from this document alone.

This document intentionally excludes domain rules, service contracts, persistence details, and endpoint mechanics — those live in the product plan. It covers only what the user sees and interacts with.

## Design Conventions (Applies to All Screens)

Nova uses **Bootstrap 5** with the standard CDN/local stylesheet plus small, feature-scoped scoped-CSS files. All pages follow these conventions:

### Page Frame
- Content wrapper: `<div class="container py-4">` on every page.
- Page title: single `<h1>` at top of content, e.g. `<h1>Players</h1>`.
- Page header row pattern: `d-flex flex-wrap justify-content-between align-items-center gap-3 mb-3` — title on the left, view/action controls on the right.
- All headings inside cards use `h5`; the top-level page uses `h1`.

### Cards
- Cards use `card shadow-sm border-0` with a `card-body`.
- Card groupings: `card shadow-sm border-0 mb-3`.
- Tables live inside `card shadow-sm border-0` > `card-body` > `table-responsive` > `table align-middle mb-0` with `thead class="table-light"`.
- Stat blocks use `border rounded p-3 h-100` cells with a muted uppercase label and a `fs-4 fw-semibold` value.

### Buttons
- Primary action: `btn btn-primary` (e.g., Add player, Create campaign, Save).
- Secondary/inline edit: `btn btn-outline-primary` (small: `btn btn-sm btn-outline-primary`).
- Archive: `btn btn-sm btn-outline-warning`.
- Restore: `btn btn-sm btn-outline-success`.
- Destructive confirm: `btn btn-warning` (archive) or `btn btn-danger` (delete/leave).
- Cancel: `btn btn-outline-secondary`.
- Retry: `btn btn-sm btn-outline-danger`.
- Navigation back: `btn btn-outline-secondary btn-sm` with `← Back to …` label.
- Buttons that trigger async work are `disabled` while `_isMutating` / `_isSubmitting` is true.

### Badges and Tags
- Status badges: `badge` + contextual class. Active = success/primary; Closed = secondary/dark.
- Tag pills: `badge rounded-pill tag-pill` with inline `style` derived from the tag definition's color (`PlayerTagStyle` helper). Archived tags render with an `(archived)` suffix and a muted/`badge-archived` style.
- Status pills for lifecycle: Active (success) / Archived (secondary).

### Alerts (State Messaging)
- Success message: `alert alert-success` with `role="status" aria-live="polite"`.
- Page/load error: `alert alert-danger` containing message + a Retry button (`d-flex flex-wrap justify-content-between align-items-center gap-2`).
- Mutation error: `alert alert-danger` with `role="alert" aria-live="assertive"`.
- Warning / blockers: `alert alert-warning` with `role="alert"` and a `fw-semibold` heading line followed by a `ul`.
- Conflict: `alert alert-warning` with a "Close and reload" action.
- Truncation notice: `alert alert-info` with `role="status"`.
- Neutral info on empty regions: muted `text-muted` paragraph.

### Loading
- `card shadow-sm border-0` containing `card-body d-flex align-items-center gap-2` with `spinner-border spinner-border-sm` and a "Loading …" label.
- Full-page loading uses a centered `spinner-border` with a `visually-hidden` "Loading..." span.

### Forms
- Forms are reusable components: `PlayerForm`, `TeamForm`, `CampaignMetadataForm`, `SeasonMetadataForm`, `CampaignCreateForm`.
- Common props: `Heading`, `Model`, `SubmitButtonText`, `IsSubmitting`, `ErrorMessage`, optional `…Blockers`, `OnValidSubmit`, `OnCancel`.
- Labels use `form-label`; inputs use `form-control`; selects use `form-select`.
- Filter panels are a `card shadow-sm border-0 mb-3` > `card-body` with a `row g-3` of labeled fields.
- Forms render inline (within the page flow) rather than in modals — they appear below the page header / within the relevant card.
- Validation errors render per-field under inputs plus a form-level `ErrorMessage` alert.

### Empty States
- A `card shadow-sm border-0` with `card-body`, an `h5` title, a muted explanatory sentence, and (for administrators) a primary CTA button.
- Evaluator/read-only users get a neutral empty state with no CTA.

### Responsive Behavior
- Filter grids use `row g-3` with `col-md-*` so controls stack on narrow screens.
- Header rows use `d-flex flex-wrap … gap-2` so buttons wrap.
- Tables are always wrapped in `table-responsive`.
- Split-pane layouts (campaign workspace) collapse to a single full-screen panel on narrow screens (see Campaign Workspace).

### Accessibility
- Every select has a `visually-hidden` `<label>` (e.g., `<label class="visually-hidden">Roster view</label>`).
- Buttons have `aria-label` when their visible text is ambiguous (e.g., "Edit season @name").
- Tables use `th scope="col"`.
- Focus order follows reading order; drawer next/previous supports keyboard (Arrow keys within drawer, Esc closes).
- Color is never the only signal: status is always accompanied by a text label.

---

## App Shell and Primary Navigation

The app shell is defined by `MainLayout.razor` + `NavMenu.razor`.

- **Top area**: content renders in a `main.nova-main-content px-4` region.
- **Navigation**: a **fixed-bottom** Bootstrap navbar (`navbar navbar-expand-md navbar-light bg-light border-top fixed-bottom`) — Nova deliberately uses a bottom nav, not a top header. This is the app-wide constant; all mockups must show it.
  - Left side (for authenticated club members): **Club name** (links to club detail, `Match="NavLinkMatch.Prefix"`), **Campaigns**, **Players**, **Teams**.
  - Right side: avatar + **Manage** (account management) and **Logout** (form post with antiforgery token).
  - The navbar brand ("Nova") collapses on desktop; on narrow screens the toggler expands the nav.
  - A user not in a club sees only Nova + account items (no Club/Campaigns/Players/Teams links).
- **No in-app global search**: search is per-screen and lives in each screen's filter panel. The mockups in the plan show a `[Search]` slot in the top bar; the implemented convention is per-screen search, so design mockups should place search inside each list screen's filter card, not in the shell.

### Entry Flow (existing, unchanged — included for completeness)

1. Not authenticated → **Login / Register** (Identity pages, existing layout).
2. Authenticated without profile photo → **Profile photo setup** (existing `ProfilePhotoEditor` flow).
3. Authenticated, no club membership → **Club onboarding** (`/Clubs/Onboarding`).
4. Authenticated club member → **Club dashboard** (`/`).

---

## 1. Club Onboarding (existing, `/Clubs/Onboarding`)

Purpose: create a club, search and join an existing club, or see the status of a pending join request.

Three states:

### 1a. No pending request — choose create or join
```
+---------------------------------------------------------------------------+
| Welcome to Nova                                                            |
|                                                                           |
|  +---------------------------------+  +---------------------------------+  |
|  | Create your own club            |  | Join an existing club           |  |
|  | Start fresh — you'll become the |  | Search for a club and request   |  |
|  | club admin.                     |  | to join.                        |  |
|  | [Club name___] [City___]        |  | [Search clubs________] [Search] |  |
|  | [State____] [Create club]       |  |  result rows → [Request to join]|  |
|  +---------------------------------+  +---------------------------------+  |
+---------------------------------------------------------------------------+
```
- Two equal `col-md-6` cards side by side; stack on mobile.
- Create form: club name, city, state; submit creates club and lands user on the dashboard as its first administrator.
- Join panel: search box; results list each with a **Request to join** button.

### 1b. Pending request exists
- Centered `col-md-6` card showing the club name, "pending" status, **Cancel request** button, and a **Search again** link.

### 1c. Loading / error
- Centered spinner while determining state; `alert-danger` on load failure.

---

## 2. Club Dashboard (`/`)

Purpose: the post-login home for a club member. Answers: what is active, what needs attention, and where do I go next. Replaces the current "Hello, world!" placeholder (`Components/Pages/Home.razor`).

```
+---------------------------------------------------------------------------+
| Club name                                                [Search] [Account]|
+---------------------------------------------------------------------------+
| HOME | CAMPAIGNS | PLAYERS | TEAMS | CLUB                                  |
+---------------------------------------------------------------------------+
| Active campaigns                                                          |
|  +---------------------------------------------------------------------+  |
|  | Tryouts Summer 2026      84 players   19 undecided   [Open workspace]|  |
|  | Fall Goalkeeper ID       22 players    5 undecided   [Open workspace]|  |
|  +---------------------------------------------------------------------+  |
|                                                                           |
|  +------------+  +------------+  +------------+                          |
|  | Roster     |  | Teams      |  | Admin      |                          |
|  | 126 active |  | 8 active   |  | attention  |                          |
|  | 4 archived |  | 1 archived |  | 3 requests |                          |
|  | [View]     |  | [View]     |  | [Review]   |                          |
|  +------------+  +------------+  +------------+                          |
|                                                                           |
| Recent activity                                                          |
|  +---------------------------------------------------------------------+  |
|  | Jun 12  Pat added a note to Avery Johnson (Tryouts Summer 2026)     |  |
|  | Jun 11  Admin closed Tryouts Summer 2026                            |  |
|  | Jun 10  Morgan applied tag "Defender" to Sam Rivera                 |  |
|  +---------------------------------------------------------------------+  |
+---------------------------------------------------------------------------+
```

### Regions
1. **Active campaigns** — a table card listing each Active campaign: name, participant count, undecided count, and an **Open workspace** primary button (links to `/campaigns/{id}`). Columns: `Campaign | Participants | Undecided | Actions`.
2. **Stat / next-action cards** — three equal `col-md-4` cards:
   - **Roster**: active player count, archived player count, **View players** button (`/players`).
   - **Teams**: active team count, archived team count, **View teams** button (`/teams`).
   - **Admin attention** (administrators only): pending join request count, unresolved decisions count (sum of Undecided across Active campaigns), **Review** button (links to `/campaigns` or a first unresolved campaign). Evaluators see a muted "No outstanding items" card or the column collapses to two cards.
3. **Recent activity** — a chronological feed of club-level events: notes added, tags applied/removed, placements set, campaign close/reopen. Each row: `{date} {actor} {verb} {object} ({campaign})`.

### Role-aware variations
- **Administrator**: sees Admin attention card; when no Active campaigns exist, sees a primary **Create campaign** CTA in the empty state; sees "setup gaps" hints when there are no teams or no tag definitions yet (e.g., muted "No teams yet — add teams so campaigns can place players" with **Add team** link).
- **Evaluator**: sees Active campaigns + Roster/Teams cards + Recent activity; a neutral empty state ("No active campaigns right now") with no CTA.

### States
- **Loading**: full-page spinner.
- **Error**: `alert-danger` + Retry.
- **Empty**: per-region empty states as described above.

---

## 3. Campaign List (`/campaigns`)

Purpose: browse active and closed campaigns grouped by season.

```
+---------------------------------------------------------------------------+
| Campaigns                                        [Active v] [Create campaign]|
+---------------------------------------------------------------------------+
| +-- Season: 2026 (Jan 1 – Dec 31)               [Edit season] ----------+ |
| | Name              Dates                Status  Part.  Unresolved  Act.| |
| | Tryouts Summer 26 06/01–06/30          ACTIVE  84     19         Edit | |
| | Fall Goalkeeper ID 08/01–08/15         ACTIVE  22     5          Edit | |
| +-----------------------------------------------------------------------+ |
| +-- Season: 2025                          [Edit season] ---------------+ |
| | Spring Assessment 26 03/01–03/15        CLOSED  76     0              | |
| |   Closed campaigns are read-only; reopen to edit.                     | |
| +-----------------------------------------------------------------------+ |
+---------------------------------------------------------------------------+
```

### Regions
1. **Page header** — title "Campaigns", a view filter `[Active v | Closed]`, and (admin) **Create campaign** primary button (`/campaigns/new`).
2. **Season groups** — each season is a `card shadow-sm border-0 mb-3`:
   - Season header: season name, date range, and (admin, Active view) **Edit season** `btn-outline-primary` button.
   - Campaign table columns: `Name | Dates | Status | Participants | Unresolved | Actions`.
   - **Name** links to the campaign workspace (`/campaigns/{id}`).
   - **Status** is a badge (Active = success, Closed = secondary). For Closed rows, a muted helper line: "Closed campaigns are read-only; reopen to edit."
   - **Actions**: admin sees **Edit** for Active campaigns (opens inline `CampaignMetadataForm`); Closed campaigns show **Reopen**.
3. **Truncation notice** — `alert-info` when the list is capped: "Showing the most recent N of Total campaigns."

### Interactions
- **Edit campaign** (inline): renders `CampaignMetadataForm` below the header (season select + create-season inline, name, dates). Saving closes the form and shows a success alert.
- **Edit season** (inline): renders `SeasonMetadataForm`.
- **Conflict**: `alert-warning` "The save was rejected due to a conflict…" with **Close and reload** button.

### States
- Loading spinner card; empty state per filter (Active: admin CTA vs neutral; Closed: neutral).

---

## 4. Create Campaign (`/campaigns/new`)

Purpose: choose or create a season, define campaign name and dates, preview enrollment, and create the campaign (immediately Active).

```
+---------------------------------------------------------------------------+
| Create campaign                                                            |
|                                                                            |
|  +------------------------------------------------------------------------+ |
|  | Season            [2026 v]  [Create season]                            | |
|  | Name              [Tryouts Summer 2026______________________]          | |
|  | Start date        [06/01/2026]                                         | |
|  | Planned end date  [06/30/2026]                                         | |
|  |                                                                        | |
|  | Enrollment preview                                                     | |
|  | 126 active players will be enrolled.                                   | |
|  | 8 active teams will be available.                                      | |
|  |                                                    [Cancel]  [Create]  | |
|  +------------------------------------------------------------------------+ |
+---------------------------------------------------------------------------+
```

### Regions
1. **Form card** (`CampaignCreateForm`): season select + **Create season** inline action (expands `SeasonMetadataForm` — name + date range); name text input; start date and planned end date inputs (`input type="date"`-style).
2. **Enrollment preview** — computed, read-only summary lines:
   - "`{N} active players will be enrolled.`"
   - "`{M} active teams will be available.`"
   - Refreshes if the underlying counts change while the form is open.
3. **Footer actions**: **Cancel** (`btn-outline-secondary`, returns to `/campaigns`) and **Create** (`btn btn-primary`, disabled while submitting).

### Behavior notes
- There is no Draft state: creating immediately makes the campaign Active.
- Planned end date does not auto-close the campaign (label must say "Planned end date").
- On success: navigate to the campaign workspace (`/campaigns/{id}`) with a success status message.

### States
- Loading (while preview counts load); field validation errors; form-level error alert; submit-in-progress disables Create.

---

## 5. Campaign Workspace (`/campaigns/{id}`)

The most complex screen. Serves all campaign work: evaluation, placement, closeout, and read-only history.

```
+---------------------------------------------------------------------------+
| Tryouts Summer 2026                              ACTIVE      [Campaign menu]|
| Overview | Evaluate | Placements | Closeout                                |
+---------------------------------------------------------------------------+
| [Search] [Grad year v] [Tag v] [Outcome v] [Team v]      84 participants   |
+------------------------------------------+---------------------------------+
| PLAYER ROSTER                            | PLAYER DRAWER                   |
| Avery Johnson   2032  Defender           | Avery Johnson         [<] [>]   |
| Sam Rivera      2031  Keeper             | Grad 2032 / Tryout #47          |
| Jordan Lee      2033  --                 |                                 |
| ...                                      | Tags                            |
|                                          | [Defender x] [+ Add tag]        |
|                                          | Notes                           |
|                                          | [Add observation...] [Save]     |
|                                          | Jun 12 Pat: Strong recovery...  |
|                                          | Jun 10 Morgan: Comfortable...   |
+------------------------------------------+---------------------------------+
```

### Header
- Campaign name (`h1`), status badge (ACTIVE/CLOSED), date range, participant count.
- **Campaign menu** (admin): dropdown with **Edit metadata**, **Close campaign** (→ Closeout tab), and **Reopen** (only when Closed).
- **Tab bar**: `Overview | Evaluate | Placements | Closeout`.
  - Evaluate is the default tab for all members.
  - **Placements** and **Closeout** tabs are visible to everyone but their write actions are admin-only during Active; after close they are fully read-only for all.
  - Tab state persists in the URL (e.g., `/campaigns/{id}?tab=evaluate`).

### 5a. Overview Tab
Purpose: campaign snapshot and progress.
- Season name + dates; enrollment count.
- Outcome summary row (four stat blocks): Assigned / Not selected / Withdrawn / Undecided.
- Closeout readiness summary with an **Open closeout** link (admin, Active).
- Recent campaign activity (notes/tags/placements/lifecycle events) — same feed style as the dashboard but scoped to the campaign.

### 5b. Evaluate Tab — Roster + Player Drawer
This is the primary evaluator workflow. Two-pane layout on desktop; single full-screen panel on narrow screens.

**Filter bar** (top, above both panes): `[Search]` text input, `[Grad year v]`, `[Tag v]`, `[Outcome v]`, `[Team v]` selects, and the participant count label ("`84 participants`") on the right. Filters apply to the roster list only.

**Left pane — Player roster**:
- Table rows: player name (link/selectable), graduation year, tag pills.
- Row selection highlights the row and opens that player in the drawer.
- Pagination/lazy-loading keeps a bounded result set; a truncation notice shows when capped.

**Right pane — Player drawer**:
- Header: player name, **previous/next** arrow buttons (`[<] [>]`) that step through players while preserving the current filter and sort order; the drawer title shows "N of M".
- Sub-line: `Grad {year} / Tryout #{number}`.
- **Tags** section: applied tag pills each with an `×` remove affordance, plus **+ Add tag** (opens a tag picker). Remove is shown only to the applying user or an admin. Archived tag definitions render with `(archived)` and are **not** removable.
- **Notes** section: an "Add observation…" text input with **Save**; then the note feed newest-first. Each note shows `{author} · {date}` and, for the author or admins, edit/delete affordances (inline edit expands a textarea; delete opens the confirm dialog).
- All evaluation inputs are **disabled** when the campaign is Closed, with a banner at the top of the drawer: "This campaign is closed. Notes and tags are read-only."
- **Keyboard**: Arrow Up/Down or Left/Right steps players; `Esc` closes the drawer; focus is trapped within the drawer while open.

**Narrow screens**: the drawer becomes a full-screen overlay panel; closing it returns to the roster with the scroll position and filters preserved.

### 5c. Placements Tab
Purpose: set final outcomes and teams. Admin-write during Active; read-only after close.

```
+---------------------------------------------------------------------------+
| Placements                    [Grad year v]  [x] Unresolved only           |
+---------------------------------------------------------------------------+
| Player           Grad    Outcome          Team                             |
| Avery Johnson    2032    [Assigned v]     [U12 - cutoff 2032 v]           |
| Sam Rivera       2031    [Not selected]   --                               |
| Jordan Lee       2033    [Undecided v]    --                               |
+---------------------------------------------------------------------------+
| 65 assigned | 11 not selected | 2 withdrawn | 6 undecided                  |
+---------------------------------------------------------------------------+
```

- **Filter bar**: `[Grad year v]` select and an **Unresolved only** checkbox.
- **Table**: columns `Player | Grad | Outcome | Team`.
  - Outcome is a per-row select with options Undecided / Assigned / Not selected / Withdrawn.
  - Team is a per-row select listing active eligible teams (format `{Team} - cutoff {year}`). It is:
    - required and enabled only when Outcome = Assigned;
    - forced to `--` (null) when Outcome changes away from Assigned;
    - **hard-blocked** for ineligible teams (player graduation year below team cutoff) — the option is either excluded or shown disabled with a muted "ineligible" label; attempting an ineligible assignment shows an inline `alert-warning` explaining the graduation-year rule.
  - Rows dirty since last save show a pending indicator; saving persists changed rows.
- **Summary footer**: `{n} assigned | {n} not selected | {n} withdrawn | {n} undecided`, updated live.
- **Conflict handling**: if a concurrent edit invalidates the change, an `alert-warning` appears: "The placement was changed by someone else. Close and reload to see the current state." with a **Close and reload** button.
- **Closed state**: table is read-only; controls render as static text; a muted banner explains results are frozen.

### 5d. Closeout Tab
Purpose: validate readiness and close (or reopen) the campaign.

```
+---------------------------------------------------------------------------+
| Close campaign: Tryouts Summer 2026                                        |
+---------------------------------------------------------------------------+
| Readiness                                                                  |
| [x] 84 participants enrolled                                               |
| [x] 78 final outcomes                                                      |
| [!] 6 players still undecided                     [Review unresolved]      |
|                                                                            |
| Closing freezes notes, tags, outcomes, and placements.                     |
|                                              [Cancel]  [Close campaign]    |
+---------------------------------------------------------------------------+
```

- **Readiness checklist**: each line a checkbox-style row — checked (`[x]`) when satisfied, warning (`[!]`) when not, with the offending count and a **Review unresolved** button that jumps to the Placements tab pre-filtered to "Unresolved only".
  - Item 1: "`{n} participants enrolled`".
  - Item 2: "`{n} final outcomes`".
  - Item 3: "`{n} players still undecided`" (or "All players have final outcomes").
- **Explanatory text**: "Closing freezes notes, tags, outcomes, and placements."
- **Actions**: **Cancel** (returns to Evaluate) and **Close campaign** (`btn-primary`, **disabled** until all items are satisfied).
- **Closed state** — the same tab becomes a **Campaign summary**:
  - Closure metadata: `Closed {date} by {admin}`.
  - Final outcome summary (the four stat blocks) and per-team roster.
  - A muted read-only banner.
  - Admin action: **Reopen campaign** (`btn-outline-warning`) behind a confirm dialog explaining that reopening restores editing without discarding outcomes and is recorded for audit.
- **Readiness is presented, not recalculated**: the UI shows policy-provided blockers verbatim and does not re-derive rules.

### Workspace states (all tabs)
- Loading spinner; not-found (`alert-warning` card "Campaign not found"); load error + Retry; role-denied state (club member without admin attempting admin write during Active gets a permission error alert).

---

## 6. Player Roster (`/players`)

Purpose: search, filter, and manage the club's active and archived players.

```
+---------------------------------------------------------------------------+
| Players                                  [Active v]          [+ Add player]|
| [Search name or tryout number] [Grad year v] [Tag v]                      |
+---------------------------------------------------------------------------+
| Name              Grad year   Tags               Active campaigns          |
| Avery Johnson     2032        Defender, Fast      Summer Tryouts           |
| Sam Rivera        2031        Keeper              Summer Tryouts, Fall ID  |
| ...                                                                            |
+---------------------------------------------------------------------------+
```

### Regions
1. **Header**: title + view filter `[Active v | Archived]` + **Add player** (`btn-primary`, admin only).
2. **Filter card**: `Search name or tryout number` text input (placeholder "e.g. Avery, 12"), `Graduation year` select, `Tag` select.
3. **Results table**: `Name | Graduation year | Tags | Active campaigns` and (admin) an `Actions` column with **Edit**, **Archive**/**Restore**.
   - Name links to `/players/{id}` (preserving filter context in the return URL).
   - Tags render as colored pills; "None" in muted text when empty.
   - Active campaigns column lists campaign names; "None" when empty.
   - Archived view: archived badge and **Restore** instead of Archive.
4. **Truncation notice** when bounded: "Showing first N of M players. Refine filters to narrow the roster."
5. **Helper text** under the table: "Restoring reactivates the profile only; campaigns missed while archived are not backfilled automatically."

### Interactions
- **Add/Edit** render the inline `PlayerForm` below the header (fields: first name, last name, date of birth, gender, graduation year, optional jersey number). Required fields marked.
- **Graduation-year edit blockers**: if the edit would invalidate an active assigned placement, the form surfaces a structured `alert-warning` blocker list (campaign name, placement details) and writes nothing until resolved.
- **Archive**: confirmation card with checkbox "I understand this player will be moved to the archived roster." and a **Archive player** (`btn-warning`) button. If the player has unresolved active-campaign participation, a structured blocker list is shown and Archive is disabled until resolved.
- **Restore**: direct action button; success alert.

### States
Loading, error+Retry, success alert, empty state ("No players found. Try changing filters or add a new player.").

---

## 7. Player Detail (`/players/{id}`)

Purpose: view/edit a player profile and browse campaign history grouped by campaign.

```
+---------------------------------------------------------------------------+
| ← Back to roster                                                           |
+---------------------------------------------------------------------------+
| Avery Johnson                                        [Edit] [Archive]      |
| Graduation year 2032 · ACTIVE                                             |
| Date of birth  MMM d, yyyy     Gender M/F    Jersey # ...                 |
+---------------------------------------------------------------------------+
| Current tags                                                               |
| [Defender] [Fast]                                                          |
+---------------------------------------------------------------------------+
| Campaign history                                                           |
| v Tryouts Summer 2026   ACTIVE        Tryout #47                           |
|   Outcome: Assigned · Team: U12 (2032)                                    |
|   Tags   [Defender by Pat · Jun 12]                                       |
|   Notes  Jun 12 Pat: Strong recovery run and communication.               |
|          Jun 10 Morgan: Comfortable receiving under pressure.             |
| > Spring Assessment 2026   CLOSED     Outcome: Not selected               |
+---------------------------------------------------------------------------+
```

### Regions
1. **Back link**: `← Back to roster`, honoring the preserved roster context (active/archived view + filters).
2. **Profile card**: name (`h1`), muted line `Graduation year {year} · {lifecycle badge}`; admin actions **Edit** / **Archive** / **Restore**; detail field grid (`Date of birth`, `Gender`, `Jersey number`) in `col-md-4` labeled blocks.
3. **Current tags card**: pill list, empty → "No active tags."
4. **Campaign history**: a vertical stack of `<details>` accordion cards, newest campaign first. Each expanded item shows:
   - Header: campaign name + status badge, `Tryout #{n}`, dates.
   - Outcome line: `Outcome: {value}` and `Team: {name} ({grad year})` when assigned.
   - Tags block: pills with `by {user} · {date}`; archived tags show `(archived)`.
   - Notes block: each note with content and `{author} · {date}`.
   - Empty → "No notes or tags for this campaign."
- If the campaign history is empty → "No campaign history yet."

### Interactions / states
- Same edit/archive/restore + blocker flows as the roster (inline forms and confirmations).
- Not found state: centered card "Player not found. This player does not exist or is not visible to your account." with a **Return to roster** link.
- Loading and error states as standard.

---

## 8. Team List (`/teams`)

Purpose: manage persistent active and archived teams.

```
+---------------------------------------------------------------------------+
| Teams                                        [Active v]       [+ Add team] |
| [Search team name]  [Graduation year v]                                   |
+---------------------------------------------------------------------------+
| Team       Graduation year   Active placements    Status                  |
| U12        2032              14                   Active                  |
| U13        2031              16                   Active                  |
+---------------------------------------------------------------------------+
```

### Regions
1. **Header**: title + view filter + **Add team** (admin).
2. **Filter card**: `Search team name` (placeholder "e.g. U16 Blue"), `Graduation year` select.
3. **Results table**: `Team | Graduation year | Active placements | Status` and (admin) `Actions` (**Edit**, **Archive**/**Restore**). Team name links to `/teams/{id}`.

### Interactions
- **Add/Edit** inline `TeamForm` (name, graduation-year cutoff). Editing a cutoff surfaces structured blocker details for any active placements that would become ineligible; writes nothing until resolved.
- **Archive** confirmation with blockers for active placements; **Restore** direct.

### States
Same conventions as the player roster (loading, error, empty "No teams found", success).

---

## 9. Team Detail (`/teams/{id}`)

Purpose: edit team rules and view current/historical placement context.

```
+---------------------------------------------------------------------------+
| ← Back to teams                                                            |
+---------------------------------------------------------------------------+
| U12                                                 [Edit] [Archive]       |
| Graduation year 2032 (minimum eligible) · ACTIVE                          |
+---------------------------------------------------------------------------+
| Active placements                                                          |
| Tryouts Summer 2026: 14 players                                           |
| +---------------------------------------------------------------------+   |
| | Avery Johnson · Sam Rivera · Jordan Lee · … (linked to player pages) |   |
| +---------------------------------------------------------------------+   |
+---------------------------------------------------------------------------+
| Placement history                                                         |
| v Spring Assessment 2026 (CLOSED) — 12 players placed                    |
| > Summer Tryouts 2025 (CLOSED) — 15 players placed                       |
+---------------------------------------------------------------------------+
```

### Regions
1. **Back link** preserving team-list context.
2. **Team header card**: name, muted line `Graduation year {year} · {lifecycle badge}`, admin Edit/Archive/Restore.
3. **Active placements**: for each Active campaign, a "`{campaign}: {n} players`" section listing placed players (links to player details).
4. **Placement history**: `<details>` accordion per historical campaign — campaign name, status badge, placed player count, and player links.
5. Empty → "No placements yet."

### Interactions / states
Same form/archive/blocker/not-found conventions as player detail.

---

## 10. Club Detail (`/Clubs/{ClubId}`) and Club Administration (`/Clubs/{ClubId}/admin`)

Purpose: membership, join requests, and administrator management. Existing screens; the design here formalizes their regions for mockup consistency.

### 10a. Club Detail (all members)
```
+---------------------------------------------------------------------------+
| Club Name                     City, State               [Admin] (admins)  |
+---------------------------------------------------------------------------+
| Members                                                                   |
| +-----------------------------------------------------------------------+ |
| | Pat Smith                                         (Current user: You) | |
| | Morgan Lee                                          [Admin] badge     | |
| +-----------------------------------------------------------------------+ |
+---------------------------------------------------------------------------+
```

- Header: club name (`h1`), `City, State`, and an **Admin** button (admins only) linking to the admin page.
- **Members**: list-group of members; "Current user (You)" highlighted; admin members carry an `[Admin]` badge.
- Empty → "No members found."

### 10b. Club Administration (admin only, `/Clubs/{ClubId}/admin`)
```
+---------------------------------------------------------------------------+
| Club Administration                                                        |
+---------------------------------------------------------------------------+
| Club Name                     City, State                                  |
|  +----------+ +----------+ +----------+ +----------+                      |
|  | Members  | | Admins   | | Pending  | | Players  |                      |
|  | 42       | | 3        | | 3        | | 126      |                      |
|  +----------+ +----------+ +----------+ +----------+                      |
+---------------------------------------------------------------------------+
| Join requests                          | Club admins                       |
| +-------------------------------------+ +--------------------------------+ |
| | Requested by Pat Smith   [Approve]  | | Pat Smith         [Demote]     | |
| |                [Reject]             | | Morgan Lee        [Demote]     | |
| +-------------------------------------+ | [Add admin] picker             | |
|                                         +--------------------------------+ |
+---------------------------------------------------------------------------+
```

- **Summary stat row**: Members / Admins / Pending join requests / Players (four `border rounded p-3` cells).
- **Sole-admin warning**: when the current user is the only admin, `alert-warning`: "You are the only admin for this club. Consider promoting another member before you demote yourself or leave the club."
- **Join requests**: pending requests with **Approve** / **Reject** actions.
- **Club admins**: list with **Demote** per member and an **Add admin** member picker.
- **Members** list (full roster with role badges).
- Status and error alerts as standard.

---

## 11. Tag Definition Management (new, admin only)

Location: reachable from the Club administration area (a "Tags" section) or a dedicated `/Clubs/{ClubId}/tags` route. The plan's nav has no standalone "Tags" item; mockups should show this as a section within Club administration with a link from the dashboard's setup-gap hint.

Purpose: create/edit/archive/restore club tag definitions used during evaluation.

```
+---------------------------------------------------------------------------+
| Tag definitions                                    [+ Add tag]             |
| Used in evaluation notes across active campaigns.                         |
+---------------------------------------------------------------------------+
| Name          Color         Used in         Status        Actions         |
| Defender      [green chip]  23 applications  Active       Edit  Archive  |
| Fast          [blue chip]   12 applications  Active       Edit  Archive  |
| Keeper        [gray chip]   0 applications   Archived    Edit  Restore   |
+---------------------------------------------------------------------------+
```

### Regions
1. **Header**: title, muted subtitle, **Add tag** (admin).
2. **Table**: `Name | Color | Used in | Status | Actions`.
   - Color: a color swatch/`badge` pill using the tag color, with the hex shown in a tooltip or label.
   - "Used in": count of current applications; zero shows "0 applications".
   - Status: Active / Archived badge.
   - Actions: **Edit** (opens inline name/color form), **Archive**/**Restore**.
3. **Helper text**: "Archived tags remain visible in history and cannot be applied to new players. Existing applications cannot be removed once the definition is archived."

### Interactions
- Add/Edit inline form (name, color picker). Archived definitions are excluded from the drawer's "+ Add tag" picker and the roster tag filter.
- Archive/Restore with confirmations; no structured blockers (tag definitions don't invalidate placements) but a confirm describing the constraint above.

---

## 12. Shared Interaction Patterns

### 12a. Optimistic Concurrency / Conflict
Any mutation that fails a concurrency check renders an `alert-warning`: "The save was rejected due to a conflict. Close the form and reload to see the current state before trying again." with a **Close and reload** button. Used on campaign metadata/season edits and placement rows.

### 12b. Structured Blockers
Archive and graduation-year/cutoff edits can return structured blocker lists. Rendering contract: `alert-warning` with a `fw-semibold` heading ("Archive blockers:" / "Placement blockers:") followed by a `<ul>` where each item names the campaign and the affected participation/placement IDs. The destructive/confirm action is disabled while any blocker exists.

### 12c. Delete / Destructive Confirmation
Destructive actions use the shared `ConfirmDeleteDialog` or an inline confirmation card (see Archive flows). Confirmations always include a typed or checkbox acknowledgment for irreversible effects.

### 12d. Success / Status Messaging
Post-mutation success shows `alert-success` with `role="status" aria-live="polite"` at the top of the page; it is replaced or cleared on the next navigation/action.

### 12e. Truncation / Bounded Results
List screens show an `alert-info` notice when results are bounded: "Showing first N of M … Refine filters to narrow results."

### 12f. Not-Found
Detail screens render a centered `card shadow-sm border-0` with an `h4`-level title ("Player not found", "Campaign not found", etc.), a muted explanation ("does not exist or is not visible to your account"), and a **Return to …** `btn-outline-secondary btn-sm` link.

---

## 13. Cross-Screen Navigation and Context Preservation

- **Roster → detail → back**: Player and team detail pages preserve the source list's filter state (active/archived view, search, filters) in the return URL so **Back to roster/teams** lands the user where they left off.
- **Campaign workspace → player detail**: player links from the roster pane, placements tab, and closeout summary open the player detail; returning restores the workspace tab and filters.
- **Closeout → Placements drill-down**: "Review unresolved" switches to the Placements tab with **Unresolved only** pre-checked.
- **Campaign menu**: admin campaign metadata edit opens the inline form; close/reopen navigate to the Closeout tab.

## 14. Explicitly Out of Scope for Design Mockups

Per the plan's deferred list — do not design: player/parent self-registration portals, CSV import, numeric ratings/rubrics, evaluator tag votes, append-only tag history, campaign-specific team customization, invitations/notifications, public result publication, automated season closeout, or analytics dashboards. Also out of scope: the profile-photo and Identity account-management screens (existing, unchanged), though their entry/exit points are referenced in the Entry Flow.
