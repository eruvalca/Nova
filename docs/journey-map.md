# Nova Phase 2 journey map

Status: Confirmed product truth for Phase 2

Issues: [#181](https://github.com/eruvalca/Nova/issues/181), entry gate for
[#163](https://github.com/eruvalca/Nova/issues/163)

This map records the durable product model behind the confirmed Phase 2 surface
briefs. It is intentionally independent of the current screens and persistence
shape. Later foundation and surface work must preserve these relationships and
flows even when the existing implementation represents them differently.

## Actors and authority

Nova is staff-only. Players are club records; players, parents, and guardians do
not sign in or receive a Nova surface.

| Actor | Product authority |
| --- | --- |
| Unaffiliated authenticated person | Create one club and its first season, or search for one club and submit/cancel one join request. |
| Approved club member | View authorized club, season, team, player, Active-campaign, and Closed-campaign data; manually maintain players; evaluate; create/apply trait tags; place players during Active campaigns; view role-shaped activity; print/download Closed campaign records. |
| Club administrator | All member authority plus season advancement, durable team management, Draft creation/preparation/deletion, campaign open/close/eligible reopen, member/request management, tag rename/archive/restore, bulk CSV intake, and the narrow later-campaign override of a prior `Withdrawn` outcome. |

Authorization is club-scoped and enforced at every server query and mutation.
Role-filtered navigation may omit unavailable destinations, but hidden or
disabled controls are never the security boundary. A person belongs to at most
one club.

## Durable product model

### Club, season, and campaign

- **Club is the durable home.** Membership, players, teams, tags, seasons,
  campaigns, activity, and attention belong to one club boundary.
- **One season is current at a time.** An administrator advances the club
  explicitly; dates inform warnings and defaults but never cause an automatic
  transition. Draft or Active work blocks advancement.
- **Campaigns are episodes within a season.** Their lifecycle is **Draft → Active
  → Closed**. Draft is administrator-only; Active and Closed are visible to all
  approved members. Multiple Drafts may exist, but the club has at most one
  Active campaign.
- **Supplemental campaigns are sequential.** Later campaigns in the same season
  can resolve new participants, retry `NotSelected` players, or deliberately
  supersede an earlier placement without changing the earlier campaign's record.
- **Teams are durable club objects.** Teams may be created independently or
  during Draft preparation. They are not cloned for seasons or campaigns.

### Players, participation, and decisions

- A player is one durable, staff-managed club record. Archiving preserves
  history; restoring does not backfill campaigns missed while archived.
- Opening a campaign makes every active player a participant. Creating or
  importing a player while a campaign is Active makes that player a participant
  automatically.
- Participation and placement are different concepts. Enrollment does not save
  an `Undecided` placement and does not create a placement activity/history
  event. A participant with no applicable saved decision is presented as needing
  a decision, not as if a historical `Undecided` mutation occurred.
- Every participant must have an explicit campaign outcome before close:
  `Assigned`, `NotSelected`, or `Withdrawn`. `Undecided` is the unresolved
  working state, not a terminal outcome and not a fabricated enrollment event.

### Eligibility, supersession, and effective rosters

- `Assigned` remains eligible for optional reassignment in a later campaign in
  the same season. While its active, compatible team remains effective, the
  player is not unresolved.
- `NotSelected` resolves its campaign without a team and remains eligible in a
  later same-season campaign.
- `Withdrawn` resolves its campaign and makes the player unavailable for the
  remainder of the season unless a club administrator records a superseding
  decision in a later Active campaign. The Closed source outcome is unchanged.
- Starting a new season resets placement eligibility for every active player.
  Prior-season assignments remain history and may inform a same-team suggestion,
  but they are not current-season placements.
- The latest non-superseded same-season decision is the effective placement.
  Each player contributes to at most one effective team roster. Earlier decisions
  remain attributable campaign history and never silently reactivate.
- Graduation-year compatibility and active-team state remain hard assignment
  constraints. An ineligible or archived-team assignment blocks close.

## Eight end-to-end flows

### 1. Create a club and first season

1. An unaffiliated person chooses **Create a club**.
2. They enter the minimum club identity and the first season's name and date
   window; a crest is optional and independently recoverable.
3. Review commits club and season as one user-visible operation. The creator
   becomes a club administrator.
4. Membership claims refresh and the administrator lands on the dashboard with
   a real club and current season.

Onboarding stops there. It does not create teams, campaigns, players, invitations,
or tutorial data. Durable teams are created later in Club → Teams or during
campaign Draft preparation.

### 2. Find and join an existing club

1. An unaffiliated person searches by club name, city, or state.
2. They submit one request to the recognizable club and see a durable Pending
   state; they may cancel and return to search.
3. A club administrator resolves the request in the canonical Club → Requests
   queue.
4. Approval refreshes claims and sends the new member directly to the dashboard.
   Rejection returns a neutral **Not approved** state and another search path.

Nova uses search → request → approval only. There is no invitation, email-based
step, free-form application, multi-club membership, or second onboarding wizard.

### 3. Prepare, open, and navigate a campaign

1. An administrator creates a season-bound Draft with campaign identity and
   planning dates.
2. Draft preparation shows the current active-player roster consequence, durable
   team readiness, and blockers. Zero active players or another Active campaign
   blocks opening; zero teams warns but does not block evaluation.
3. The administrator confirms **Open campaign and enroll _N_ players**. The
   authoritative operation enrolls the fresh active-player set once and makes
   the campaign Active.
4. All members work through the non-linear, URL-backed **Roster → Evaluate →
   Place → Close** route. Route state comes from authoritative data, not visits.
5. The campaigns directory groups Draft, Active, and Closed work by season;
   ordinary members never receive Draft evidence.

Dates never open or close a campaign automatically. Active and Closed campaigns
cannot return to Draft or be deleted.

### 4. Bring players into the club

1. Any approved member may manually add, edit, archive, or restore a player.
2. Administrators may instead use the bounded CSV **Template → Upload → Review →
   Finish** route. Review is read-only, identifies validation and duplicates by
   source row, and commits only the explicitly ready rows through one idempotent,
   set-based operation.
3. The final receipt reconciles created, skipped, duplicate, and newly blocked
   rows. Source-spreadsheet corrections return through upload; Nova does not
   become an inline spreadsheet editor.
4. Authoritative campaign state determines participation at commit: a player
   joins the one Active campaign immediately or waits as an active club player
   for the next campaign opening.

Manual intake and bulk import produce the same durable player model. CSV intake
is administrator-only, limited to 1,000 data rows, and lives in Players—not the
Club area. It never creates accounts, assignments, evaluations, or invitations.

### 5. Evaluate players collaboratively

1. Any member finds a participant by name or tryout number, verifies the player,
   and opens a campaign-scoped working record.
2. They explicitly save shared notes and apply existing trait tags. Notes retain
   author/time attribution; authors edit or delete their own notes.
3. A member may create and apply a missing trait tag inline. Normalized duplicate
   names reconcile to the shared club definition. Administrators own later tag
   rename, archive, and restore housekeeping.
4. Staff may hand the selected player to Place and return without losing lookup
   context. Evaluation does not require a placement or invent completion from
   note/tag counts.

Evaluation is optimized for a phone beside the field as well as desk review.
There are no structured ratings, scores, private notes, required evaluation
order, or automatic next-player commitment. Closed evaluation is read-only.

### 6. Decide placements and derive the season roster

1. Place opens on **Needs placement**: eligible, teamless participants with no
   terminal current-campaign decision. Existing effective assignments remain
   searchable for optional reassignment without inflating this count.
2. Any member may save an initial `Assigned`, `NotSelected`, or `Withdrawn`
   decision while the campaign is Active. Eligible prior-season context may offer
   an explicit **Keep on {Team}** action.
3. A later same-season Active campaign may save a new decision that supersedes
   earlier effective truth. `Assigned` and `NotSelected` remain eligible; only an
   administrator may supersede a prior-campaign `Withdrawn` decision.
4. Each successful mutation is immediately authoritative, attributable, and
   concurrency-protected. Effective team counts and the current-season roster
   refresh from the same supersession rule.

The flow is player-first. It has no capacity model, team-column drag board, bulk
placement, vote, private draft, eligibility override, or rewrite of a Closed
source campaign.

### 7. Close, review, reopen, and export a campaign

1. Every member can review close readiness and the complete proposed final
   record. `Undecided`, ineligible assignments, and archived-team assignments are
   hard blockers with correction paths back to Place.
2. A club administrator confirms close against a fresh authoritative snapshot.
   Success freezes evaluation and placement and establishes the Closed campaign's
   official, campaign-specific record.
3. Every approved member may read, print, and download CSV for that Closed record.
   A later supplemental campaign never changes it.
4. Only the most recently opened campaign in the current season may reopen, and
   only when doing so preserves the one-Active rule. Reopen retains outcomes and
   audit history, restores Active editing, and removes final-export posture until
   the campaign closes again.

There is no soft close, automatic `NotSelected`, post-close unassign, Closed
placement/evaluation correction, or reopening of older-season or superseded
campaigns.

### 8. Orient staff through attention and activity

1. The dashboard leads with the one Active campaign or a truthful current-season
   idle state. Members route to evaluation, teams/current rosters, or the latest
   Closed campaign as appropriate.
2. Administrators receive at most two authoritative attention kinds: pending join
   requests and players who need placement. The dashboard and route badges point
   to Club → Requests and campaign Place; they never duplicate those mutations.
3. All approved members receive a bounded, paged, append-only activity record of
   authorized campaign lifecycle, placement, joined membership, role, removal,
   and departure transitions. Draft and unresolved join-request events remain
   administrator-only.
4. Activity sentences preserve actor, subject, old/new placement context, and
   durable meaning when a target later disappears or becomes inaccessible.

Attention means current actionable work, not unread history. Nova has no email,
SMS, push, notification bell, inbox, unread count, dismissal, background polling,
or evaluation-note/tag noise in the club feed. Automatic enrollment is not an
activity event.

## Closed history and authorized transitions

- All evaluation and placement mutations occur against an Active campaign.
- Closing makes that campaign's evaluation content and outcomes immutable.
- A supplemental decision may reference and supersede a Closed decision for
  effective-season purposes, but it is a new decision owned by the later Active
  campaign. The Closed source never changes.
- Activity records the authorized Active-campaign transition. It must never use
  “unassign” language or imply a post-close mutation.
- Reopening is the only route back to editing a Closed campaign, and it is limited
  to the most recently opened campaign in the current season under the
  one-Active-campaign invariant.

## Artifact authority and implementation evidence

When evidence conflicts, use this order:

1. `PRODUCT.md` and this journey map for durable product truth.
2. `DESIGN.md` and `.impeccable/design.json` for global Fieldhouse Wayfinding
   rules.
3. Confirmed `.impeccable/surfaces/*.md` briefs for per-flow behavior.
4. Matching `.github/instructions/*.instructions.md` rules for architecture,
   implementation, and validation.
5. Existing screens, services, persistence, and tests as evidence only.

Existing implementation is useful for discovering data, authorization, failure,
concurrency, and migration constraints. It is not visual or interaction authority
and does not silently override the product model. If two higher-authority
artifacts conflict, stop the affected build, reconcile the documents in a small
reviewable change, and only then continue. An implementation PR must not choose
an unresolved product rule by accident.
