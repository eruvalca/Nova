# Nova — Executive Brief

## In One Sentence

**Nova is a club-management platform that helps youth soccer clubs run organized tryout campaigns — from roster and team management, through qualitative player evaluation, to final team placement and an auditable closeout — so every decision is informed, consistent, and never lost in a spreadsheet.**

---

## The Problem Nova Solves

Youth soccer clubs run tryouts every season with the same recurring pain points:

- **Scattered information.** Player lists, coach observations, and placement decisions live in spreadsheets, paper forms, group chats, and individual memory.
- **Unstructured evaluation.** Coaches jot down impressions with no shared format, no timestamp, and no way for other evaluators to benefit from them.
- **Losing history.** Last season's decisions vanish. Next season starts from scratch, and there is no record of why a player was placed on a given team.
- **Unreliable placement.** Assigning players to teams by feel leads to mismatches, disputes, and no accountability for who decided what, when.
- **Uncontrolled access.** Anyone with a copy of the spreadsheet can change it; there is no notion of who is allowed to evaluate or decide.

Nova replaces this with a single, structured, club-owned system of record for the entire tryout lifecycle.

---

## What Nova Provides

Nova is organized around **persistent club records** (players, teams, seasons, campaigns) and a **repeatable campaign workflow** (evaluate → place → close) that any season can reuse.

### 1. Club Setup and Membership

- Anyone can register, complete a profile photo, and either **create a club** (becoming its first administrator) or **search for and join** an existing club.
- Club administrators manage **members, administrators, and join requests**.
- Every club's data is private to that club — members only see their own club's players, teams, and campaigns.

### 2. A Permanent Player Roster and Team List

- **Players** are persistent club records (name, date of birth, gender, graduation year). They are never re-entered for each tryout.
- **Teams** are persistent club records with a graduation-year cutoff (e.g., U12 for players graduating 2032 or later).
- Players and teams can be **archived** (kept for history) or **restored**, rather than deleted.
- When a new player is added, Nova automatically enrolls them in every active campaign — no double-entry.

### 3. Seasonal Tryout Campaigns

- An administrator creates a **campaign** (e.g., "Tryouts Summer 2026") inside a **season**, with a name and planned dates.
- Creating a campaign **automatically enrolls every active player** and makes every active team available for placement — in one step.
- A campaign is **Active** immediately (no draft stage) and is later **Closed** once placements are final.

### 4. Shared Qualitative Evaluation

- Approved club members (coaches/evaluators) open a campaign workspace and work through a **filterable player roster** — search by name or tryout number, and filter by graduation year, tag, outcome, or team.
- Each player has a **profile drawer** where evaluators add:
  - **Notes** — timestamped, attributed observations (e.g., "Strong recovery run and communication").
  - **Tags** — club-defined labels (e.g., "Defender", "Fast", "Keeper") applied per player per campaign.
- Evaluation is **qualitative and shared**: all approved club members see the same evaluation stream. It is not numeric scores or mystery notes.
- A note can be edited or deleted only by its author or an administrator; a tag can be removed only by whoever applied it or an administrator.

### 5. Team Placement with Guardrails

- An administrator sets each player's final outcome: **Assigned**, **Not selected**, or **Withdrawn**.
- Assigned players are matched to an eligible team; Nova **hard-blocks** assignments that violate a player's graduation-year eligibility against a team's cutoff.
- A live summary tracks how many players are assigned, not selected, withdrawn, and still undecided.

### 6. Campaign Closeout, History, and Reopen

- Nova **validates readiness** before close: every participant must have a final outcome, and every assigned player must sit on an active, eligible team.
- Closing **freezes** notes, tags, outcomes, and placements — the campaign becomes a permanent, read-only record.
- Final results (per-player history and per-team rosters) remain viewable by all approved club members.
- An administrator can **reopen** a closed campaign if needed; every close and reopen is recorded for audit.

---

## The Workflow at a Glance

```
Set up club → Add teams and players → Create a season campaign
    → Evaluators add notes and tags → Admin places players on teams
        → Validate readiness and close → Results are frozen and viewable
```

Every step feeds the next, and nothing is re-entered manually between seasons.

---

## Roles at a Glance

| Capability | Club administrator | Evaluator / coach |
| --- | --- | --- |
| View dashboard, roster, teams, campaigns | ✅ | ✅ |
| Create/edit/archive players and teams | ✅ | — |
| Create / close / reopen campaigns | ✅ | — |
| Add evaluation notes and tags | ✅ | ✅ |
| Edit/delete any note, remove any tag | ✅ | Only their own |
| Set placements and closeout outcomes | ✅ | — |
| Manage members and administrators | ✅ | — |

---

## Benefits to the Club

- **One source of truth.** Rosters, teams, evaluations, and results live in one place instead of scattered spreadsheets and notes.
- **A repeatable, professional process.** Every tryout follows the same structured campaign workflow, so quality is consistent season after season.
- **Controlled access.** Only approved club members can see decisions; only administrators can change records, place players, or close campaigns.
- **Full accountability and auditability.** Every note, tag, placement, close, and reopen is attributed to a person with a timestamp — nothing is anonymous.
- **No more do-overs.** History persists across seasons: a player's prior campaigns and outcomes are always one click away, so placement decisions can build on the past.
- **Safe changes.** Nova prevents harmful mistakes — for example, it blocks changing a player's graduation year or a team's cutoff when it would break an existing placement, and blocks closing a campaign while players are still undecided.

## Benefits to Coaches and Evaluators

- **A shared evaluation stream.** Every coach's notes and tags are visible to the whole staff, so no observation is lost and everyone evaluates against the same picture.
- **Structured, fast observation.** Filter the roster to the players you need to watch, open each player's drawer, and record a note or tag in seconds — with keyboard shortcuts to move to the next player without losing your place.
- **Better-informed placements.** Administrators see a complete, filtered view of every player (outcomes, tags, notes, history) before assigning teams, with guardrails preventing ineligible matches.
- **Respect for their time.** No re-entering player data, no chasing down last season's files, no rework after an accidental overwrite.

---

## Trust and Data Governance

- **Club tenancy.** Every club's data is strictly isolated — members can never see another club's roster or results.
- **Role-based control.** Read access is open to approved members; write and decision-making power is reserved for administrators.
- **Concurrency-safe.** When two people edit the same record at the same time, Nova detects the conflict and prompts one to reload — preventing silent overwrites.
- **Retention-friendly.** Archive, not delete, is the norm: players, teams, and tags can be retired from active use while remaining part of club history.

---

## MVP Scope Snapshot

**In scope for the MVP:** club onboarding and membership, the role-aware dashboard, player and team management, season and campaign creation, qualitative evaluation (notes + tags), team placement, campaign close/reopen with frozen history, and tag-definition management.

**Deliberately deferred (post-MVP):** player/parent self-registration and result portals, email invitations and notifications, numeric ratings or evaluation rubrics, CSV import, public result publication, and advanced analytics.

---

## Bottom Line

Nova turns the chaotic, spreadsheet-driven tryout season into a calm, structured, repeatable process. For the club it means a professional, accountable, and auditable system of record. For coaches it means a shared workspace where observations stick, history accumulates, and placement decisions are better — every single season.
