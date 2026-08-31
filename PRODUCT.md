# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Nova serves approved club staff. There are two product roles:

- Club administrators establish and steward the club, season, campaign lifecycle,
  memberships, bulk intake, and final close or reopen decisions.
- Club members, including coaches and evaluators, share player intake, evaluation,
  and placement work. Every approved member may evaluate and place players.

Players are staff-managed records, not Nova users. Nova has no player,
parent/guardian, or family-facing account, registration, invitation, or review
surface. A person belongs to at most one club.

Nova originated around soccer clubs, but its terminology, workflows, and
implementation should remain adaptable to other club sports.

## Product Purpose

Nova gives club-sports staff one shared place to run tryouts and related roster
decisions: maintain the player pool, prepare campaigns, capture collaborative
observations, make attributable placements, and preserve a complete campaign
record. It replaces fragmented spreadsheets and informal handoffs with one
connected, authoritative workflow.

Success means staff can move from intake through evaluation, placement, and
closeout without losing shared context, duplicating players, inventing roster
state, or reconciling competing records.

## Positioning

Nova's defining advantage over spreadsheets is the combination of collaborative
player evaluation and a unified, history-preserving roster workflow. Club,
season, campaign, evaluation, effective placement, and Closed-campaign history
remain connected while later decisions supersede rather than rewrite prior
records.

## Operating Context

- The club is the durable working and authorization boundary. One current season
  at a time groups current work and history; season advancement is an explicit
  administrator action.
- Campaigns are season episodes with a **Draft → Active → Closed** lifecycle.
  Drafts are administrator-only, Active and Closed campaigns are visible to all
  approved members, and a club may have at most one Active campaign.
- Teams are durable club objects. They are not cloned per season or campaign.
  Current-season team rosters are derived from each player's effective placement
  decision.
- Opening a campaign enrolls all active club players as participants. Players
  created during an Active campaign participate automatically. Participation is
  not itself a saved `Undecided` placement or an activity/history event.
- Active campaign work follows the non-linear **Roster → Evaluate → Place →
  Close** route. Evaluation and placement may overlap.
- Staff may work at a desk or on a phone beside the field. Lookup, capture,
  decisions, and error recovery must support time-sensitive mobile use as well
  as roster-scale desktop work.

The complete product model and eight end-to-end flows are recorded in the
[Phase 2 journey map](docs/journey-map.md).

## Capabilities and Constraints

- Unaffiliated staff either create a club and its first season or find a club by
  search and request to join. An administrator approves or rejects the request.
  Nova has no invitation system.
- Every approved member may manually add, edit, archive, and restore player
  records. Administrators additionally own the bounded CSV
  **Template → Upload → Review → Finish** import flow.
- Every approved member may find campaign participants, create shared
  author-attributed evaluation notes, apply trait tags, and create missing trait
  tags inline. Note authors own their edits/deletes. Administrators own tag
  rename, archive, and restore. Nova has no structured ratings, scores, rubrics,
  or private evaluation model.
- Every approved member may save placement decisions while a campaign is Active.
  `Assigned` remains eligible for optional reassignment in a later same-season
  campaign and is not unresolved while its effective team remains valid.
  `NotSelected` remains eligible. `Withdrawn` remains unavailable until an
  administrator records a superseding decision in a later Active campaign. A new
  season resets eligibility.
- The latest non-superseded same-season decision determines the effective team.
  Supersession never rewrites a prior campaign, and a player counts on at most
  one effective current-season roster.
- Closing is administrator-only and complete. Every participant needs an
  explicit `Assigned`, `NotSelected`, or `Withdrawn` outcome; `Undecided`, an
  ineligible assignment, or an archived-team assignment blocks close.
- Evaluation and placement outcomes are immutable while Closed. There is no
  post-close unassign or correction mutation. Only the most recently opened
  campaign in the current season may reopen, subject to the one-Active rule.
- Every approved member may print or download CSV for a Closed campaign's
  immutable, campaign-specific final roster. Later supplemental decisions do
  not change that record.
- Attention is in-app only: role-shaped append-only activity for approved
  members plus administrator attention for pending join requests and players who
  need placement. Nova has no email, push, notification inbox, bell, unread
  state, or background urgency model.
- Authorization and data access are club-scoped and server-enforced. Hidden or
  disabled controls are never the authorization boundary.
- Identity email delivery is a no-op, confirmed accounts are not required, and
  no third-party login providers are registered.

## Brand Commitments

- The product name is **Nova**.
- Product language is direct, operational, and grounded in real club work. It
  must not invent customers, testimonials, performance claims, or other proof
  that is not available.

## Evidence on Hand

- The [Phase 2 journey map](docs/journey-map.md) is the durable source for actors,
  domain relationships, permission boundaries, and end-to-end product flows.
- Confirmed per-flow decisions live in `.impeccable/surfaces/`. `DESIGN.md` and
  `.impeccable/design.json` define the established Fieldhouse Wayfinding design
  system.
- The public landing page and its components contain useful current product copy:
  `Nova/Components/Pages/Landing.razor` and
  `Nova.UI/Features/Landing/Components/`.
- Existing authenticated screens, services, entities, and tests are evidence of
  current data, authorization, edge cases, and technical constraints. They are
  not product, visual, or interaction authority when they conflict with this
  record, the journey map, or a confirmed brief.
- The repository contains no confirmed customer stories, testimonials, pricing,
  benchmarks, or market proof; future work must not fabricate them.

## Product Principles

1. **Keep one connected club truth.** Preserve context from intake through the
   effective season roster and each campaign's immutable history.
2. **Separate participation from decisions.** Automatic roster participation is
   not an invented placement event; every saved outcome remains deliberate and
   attributable.
3. **Collaborate openly within clear authority.** Members evaluate and place
   together while administrator-only lifecycle, membership, bulk, and
   housekeeping actions remain explicit and server-enforced.
4. **Supersede; never rewrite history.** Later Active campaigns may change
   effective season truth without mutating Closed records.
5. **Treat unfinished implementation as evidence.** Build from confirmed product
   truth and real constraints, not accidental behavior or presentation in
   provisional screens.

## Accessibility & Inclusion

Nova supports staff working across desktop and mobile web contexts, including
time-sensitive on-field use. Interfaces must preserve semantic structure,
keyboard access, visible focus, clear written status and errors, sufficient
contrast, touch-friendly interaction, bounded data, and reduced-motion-safe
orientation. Role-filtered experiences must not disclose inaccessible club,
Draft, request, or member information.
