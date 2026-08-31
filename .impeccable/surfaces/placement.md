# Placement flow

Status: Confirmed

Issues: [#167](https://github.com/eruvalca/Nova/issues/167), child of [#163](https://github.com/eruvalca/Nova/issues/163)

Visitor mode: Operate

## Job and audience

Nova must let any approved club member turn shared evaluation evidence into an authoritative team decision without losing season history or forcing the club back into a spreadsheet. The primary job in an Active campaign is to place eligible players who do not currently have a team. A supplemental campaign also supports deliberate reshuffling: staff may find a player already assigned earlier in the same season and record a new placement that supersedes, but never rewrites, the earlier campaign's decision.

Placement commonly happens at a desk while staff compare players and team rosters, but the flow must remain usable on a phone beside the field. Members need a fast player-first queue, written eligibility and outcome states, compact evaluation evidence, and confidence that a save immediately becomes the club's shared truth. Administrators additionally need one narrow correction capability on Closed campaigns: unassign the player's effective placement with a visible audit trail.

The activation moment is resolving the next teamless player—or keeping a returning player on a compatible prior-season team—with one clear, attributable decision. Success is a season roster whose effective team assignments, unresolved count, and history remain consistent across primary and supplemental campaigns.

## Outcome and proof

- **Place** is the third URL-backed Campaign Route Marker in the non-linear **Roster → Evaluate → Place → Close** route. Every approved member may use it while the campaign is Active.
- The default working queue leads with **Needs placement**: eligible campaign participants who have no effective team and no saved terminal outcome in the current campaign.
- Automatic campaign participation is not itself a placement decision. Opening a supplemental campaign does not manufacture a new `Undecided` placement for a player who already has an effective same-season team.
- A new saved placement becomes authoritative immediately. A same-season reassignment supersedes the prior effective placement while preserving the earlier campaign as immutable history.
- Returning-player context is operational rather than decorative: the most recent prior-season assignment names its season and team, and a compatible active team receives a one-action **Keep on {Team}** fast path.
- Team context shows counts, never capacity or limits. Counts reconcile to one effective placement per player for the season and may identify the contribution from the current campaign.
- The unresolved count means exactly **eligible, teamless, and currently undecided**. Previously assigned players available for optional reassignment do not inflate it.
- Every mutation is attributable. Effective placement, player history, activity feed, and concurrency recovery agree about who changed what and when.

## Selected direction

Place is a player-first decision station inside the campaign route, not a team-column board or a spreadsheet clone. One broad flat working board contains a compact queue, a selected-player working sheet, and a team-count rail. **Needs placement** receives positional and visual priority; existing season assignments remain searchable and available for reassignment without competing with the primary queue.

On wider screens the queue and player sheet may sit adjacent, with team counts held in the same coherent working field. On a phone, the queue and selected-player sheet become successive focused stages; selecting a player preserves queue search, filters, section, and scroll position. The player sheet is a field record, not a modal card. Drag-and-drop, bulk selection, generic cards, ambient shadows, and hidden state are outside the direction.

Fieldhouse Wayfinding supplies the visual authority. Wayfinding Teal marks the selected route and primary commitment; Signal Amber marks the written unresolved count; status treatments always include words. Hairlines, spacing, and stable axes provide hierarchy. No color, badge, disabled control, or team count communicates a decision by itself.

## Participation, placement, and season truth

Campaign participation and placement decisions are separate product concepts even if the implementation currently stores them together.

- Every active club player participates automatically in an Active campaign and remains discoverable from Place.
- A participant with no saved decision is not represented to the user as a historical placement event.
- A player assigned by an earlier same-season campaign enters a supplemental campaign under **Assigned this season** with the source campaign and effective team. Nova does not create or present a new `Undecided` placement merely because the campaign opened.
- Choosing **Reassign player** begins a decision owned by the Active supplemental campaign. Saving it creates a new authoritative record; it never edits the Closed source campaign.
- The latest non-superseded season decision determines the effective team. Historical `Assigned` records remain visible but do not cause a player to count on multiple teams.
- A successful reassignment updates the source and destination team counts immediately and creates an activity event such as **Moved Avery Chen from U14 Gold to U14 Teal**.
- Closing freezes the campaign's decisions; it does not defer their effect. Reopening restores the Active editing posture subject to current authority and concurrency state.

### Same-season eligibility

This brief intentionally revises the eligibility rule recorded in epic #163 and foundation issue #178.

- `Assigned` is eligible for optional reassignment in a later same-season campaign. It is not unresolved while the existing team remains effective.
- `NotSelected` resolves the current campaign without a team and remains eligible for a later same-season campaign.
- `Withdrawn` resolves the current campaign and blocks the player from every later campaign in the same season until an administrator unassigns that outcome.
- A new season resets placement eligibility for every active player. Prior-season assignments remain historical evidence and may produce a same-team suggestion, but they are not current-season placements.
- Player graduation year remains the hard team-eligibility rule. Only active, compatible teams appear as choices for the selected player. Incompatible teams are not shown in that player's choice set and can never be overridden.

## Queue, search, and progress

The Place entry presents written sections with authoritative counts:

1. **Needs placement** — eligible players with no effective team whose current campaign decision is `Undecided` or not yet saved.
2. **Assigned this season** — players with an effective team who may be deliberately reassigned.
3. **Not selected** — players resolved without a team in this campaign.
4. **Withdrawn** — players unavailable for the rest of the season.

**Needs placement** opens first and orders players by graduation year, then player name, with stable identity as the final tie-breaker. Search by name or tryout number spans every campaign participant rather than only the open section. Filters cover graduation year, placement state, and effective team. Search and filter state survive selected-player navigation, the Evaluate handoff, browser back/forward, and responsive reflow.

Each row supplies enough context to avoid a mistaken decision: tryout number when present, full name, graduation year, written current outcome/effective team, and the most recent prior-season placement when available. A compatible prior team may expose **Keep on {Team}** directly in the queue. Missing tryout numbers and missing history are omitted or stated cleanly rather than rendered as misleading values.

The Place Route Marker and downstream close-readiness surfaces use only the **Needs placement** count. `Assigned`, `NotSelected`, and `Withdrawn` are resolved outcomes for the current campaign; a previously assigned player who is merely available for optional reshuffling is not unresolved. A player added while the campaign is Active joins the queue according to current season truth rather than being hidden by a stale total.

No bulk placement, multi-select, batch publish, or drag-and-drop exists in this phase. Collections remain bounded or paged, totals remain authoritative outside the current page, and the browser never needs the entire club history in the DOM to calculate progress.

## Selected-player working sheet

The selected state keeps identity, evidence, current truth, and the pending consequence together.

- Lead with tryout number when present and full name, followed by graduation year and written eligibility.
- Show current effective outcome/team, source campaign, **Last changed by {Member} · {Time}**, and whether the current campaign is about an initial placement or a reassignment.
- Show the most recent prior-season `Assigned` outcome as **Previous placement · {Season} · {Team}**.
- Show applied trait tags and the latest two or three evaluation notes with author and time. **View full evaluation** returns to Evaluate with this player selected and preserves a return to the current Place context.
- Show compact placement history with campaign, prior outcome/team, new outcome/team, actor, and time. Superseded decisions remain history and are never presented as simultaneous roster membership.
- Show season team counts such as **Elite Silver 28 · 16 this season · 3 from this campaign**. These are observed counts, not targets, limits, open spots, or promises of availability.

Evaluation content is read-only in Place. Notes, tags, ratings, and player-profile edits remain in their owning surfaces. Returning from Evaluate refreshes placement evidence and season truth before a decision can be saved.

## Placement and outcome interactions

### Initial placement

For a teamless eligible player, selecting `Assigned` requires one compatible active team. **Save placement** immediately commits the decision, updates counts, records attribution, and reports **Placement saved**. `Assigned` without a team and a team without `Assigned` are invalid states; the error remains local to the decision controls and preserves the user's valid choices.

The prior-team fast path appears only when the most recent prior-season team remains active and compatible. **Keep on Elite Silver 28** is itself an explicit one-action save: it records `Assigned`, announces success, updates counts, and moves focus to the next player in **Needs placement**. It does not add a second confirmation because no current-season decision is being replaced. If the historical team is archived or incompatible, the prior placement remains factual history in the player sheet but no keep action, unavailable option, or substitute team appears.

### Reassignment and outcome replacement

Any approved member may replace another member's saved placement while the campaign is Active. Replacing a resolved decision requires a local confirmation that names the player and exact consequence:

- **Move Avery Chen from U14 Gold to U14 Teal?**
- **Change Avery Chen from Assigned to Not selected for this campaign?**
- **Change Avery Chen from Withdrawn for this season to Assigned on U14 Teal?**

The confirmation actions are **Confirm change** and **Keep current placement**. Leaving `Assigned` automatically clears the team. Moving between teams supersedes the prior effective placement rather than producing two roster memberships. No mandatory written reason is required.

### Not selected and withdrawn

The outcome controls define their scope at selection time:

- **Not selected for this campaign** — no team now; eligible for a later supplemental campaign.
- **Withdrawn for this season** — unavailable for every later campaign in the season until an administrator unassigns the outcome.

`Withdrawn` requires confirmation even when selected from an undecided state because its consequence extends beyond the current campaign. `NotSelected` requires confirmation only when it replaces a resolved decision. Both immediately become shared, attributable truth after save.

## Teams and the zero-team state

Only active teams compatible with the selected player's graduation year appear anywhere in the selected-player decision context, including its team-count rail and choice controls. An unselected campaign-level overview may count every active team, but selecting a player removes incompatible and archived teams rather than rendering unavailable choices. A stale team that becomes archived or incompatible before save is rejected authoritatively; Nova refreshes the selected player's valid choices and preserves the rest of the decision context.

When no active teams exist, Place still shows the participant queue, player evidence, unresolved truth, and `NotSelected`/`Withdrawn` outcomes. Assignment is unavailable with a written explanation. Administrators receive **Manage teams**, which navigates to the durable Club → Teams surface and returns to the same selected player and queue context. Members receive the explanation without an inaccessible team-management control. Place never embeds team creation or editing.

## Collaboration, saving, and conflicts

- Each player decision has one explicit commitment. A successful save is authoritative immediately; there is no private draft, proposal, vote, approval queue, or campaign-wide publish step.
- Every placement, reassignment, outcome replacement, and admin unassign writes a player-history entry and an activity-feed event with actor, time, campaign, old state, and new state.
- Optimistic concurrency protects every mutation. If another member saved first, Nova does not overwrite them or silently merge choices.
- A stale save shows the newly authoritative outcome/team and changer, explains that the placement changed, and offers **Review latest placement**. Retrying requires a new deliberate save against the refreshed version.
- The local form preserves non-conflicting navigation context, but an obsolete team or outcome selection is never applied automatically after refresh.
- Nova does not claim live presence, show “currently editing,” or promise real-time roster updates. Opening a player, returning from Evaluate or Teams, and completing a mutation refresh authoritative counts, history, and choices.

## Closed campaigns and admin unassign

A Closed campaign preserves the same player-first board, effective season roster, counts, evidence, and history as read-only information for every approved member. Ordinary placement, reassignment, and outcome controls are absent; the surface states **Read-only — campaign is closed** in words.

An administrator may use **Unassign** only on the player's effective placement. Confirmation names the player, effective team or outcome, source campaign, and consequence: the outcome becomes `Undecided`, the team is cleared, and the player has no effective team. An older superseded placement remains history and never silently reactivates. The action records the administrator, time, cleared state, and campaign in player history and the activity feed. It does not require a written reason.

Superseded historical placements have no Unassign affordance. A member sees the same history without administrative controls. If authority, lifecycle, or effective placement changes before confirmation, the server result wins and the surface refreshes without applying the stale unassign.

## States and ranges

The design covers no participants, one player, a typical 20–200-player campaign, and larger bounded/paged rosters. It also covers no active teams, one team, many teams, no unresolved players, only unresolved players, mixed graduation years, duplicate names, missing tryout numbers, players with and without evaluations, first-season players, returning players, unavailable historical teams, primary campaigns, multiple supplemental campaigns, and many prior seasons.

Material interaction states include initial loading, incremental page loading, empty search, filtered-empty, selected player, saving, saved, confirmation open, validation failure, stale concurrency conflict, team eligibility changed, team archived, player archived, role changed, campaign closed, campaign reopened, admin unassign pending/saved/conflicted, connectivity loss, timeout, and safe retry. Aggregate counts and the selected player's effective placement must never disagree silently; when independent reads fail, the affected region names its stale or unavailable state rather than presenting an apparently exact total.

## Interaction and layout

- Keep the complete Campaign Route Markers visible and fully labeled. Route status comes from authoritative placement data, never visits.
- Use one purposeful flat working board rather than a grid of player or team cards. Queue, selected-player evidence, decision controls, and team counts share stable spatial relationships.
- On narrow screens, queue and player sheet become focused stages with explicit **Back to placements** behavior. Return restores search, filters, section, scroll position, and focus without relying on swipe gestures.
- Search, filters, section headings, rows, team choices, confirmation actions, and history remain keyboard reachable. Interactive targets are at least 2.75rem and visible focus is never clipped by scroll regions.
- Saving, success, conflict, filter totals, lifecycle changes, and effective-placement changes are announced without stealing focus unnecessarily. Color always accompanies written state, iconography, or counts.
- Long names, team names, translated labels, and large counts wrap or reflow without hiding the player, consequence, or primary action. Team and player lists use deliberate responsive scrolling or pagination rather than squeezing labels into ambiguity.
- Reduced motion preserves every orientation and success cue. Advancing to the next player after the keep-on-team fast path moves focus predictably and never depends on animation.

## Scope and boundaries

This brief covers the Active and Closed campaign Place destination: player-first discovery, unresolved progress, selected-player evidence, initial placement, prior-team fast path, same-season reassignment, outcome replacement, eligibility visibility, team counts, conflict recovery, attribution/history, and administrator unassign. It defines production-ready responsive behavior but no visual comp, direction contract, or production code.

Explicit anti-goals:

- No team capacity, roster limit, target size, open-spots calculation, or over-capacity warning.
- No team-first drag board, bulk placement, multi-select, batch approval, vote, proposal, or private placement draft.
- No incompatible or archived team in a selected player's choice set and no eligibility override.
- No inline team creation, team editing, player creation, roster picker, evaluation mutation, tag mutation, profile editing, or close/reopen action inside Place.
- No rewriting a Closed campaign's placement to represent a supplemental reassignment and no silent fallback to superseded history after unassign.
- No ratings, recommendation algorithm, automatic team assignment, or substituted team when the historical team is unavailable.
- No live-presence claim, decorative cards, ambient shadows, color-only status, hidden route labels, or CSS-level visual specification.
- No decision about whether unresolved players block campaign close; the closeout brief owns that lifecycle policy while Place supplies the authoritative count.

## Constraints and implementation consequences

- Preserve Fieldhouse Wayfinding, SSR-first Blazor architecture, club-scoped authorization, tenant-safe identifiers, written state, keyboard and touch access, bounded data, and authoritative server validation.
- Existing `CampaignPlacementsPanel` tables, mobile list items, administrator-only copy, team dropdown, and per-row save behavior are evidence only. They do not govern the new composition or role model.
- Foundation issue [#175](https://github.com/eruvalca/Nova/issues/175) must open Active-campaign placement mutations to every approved member while preserving administrator-only unassign and lifecycle authority. Hiding controls is insufficient.
- Foundation issue [#178](https://github.com/eruvalca/Nova/issues/178) currently says same-season `Assigned` blocks later placement. Its eligibility scope must be corrected before implementation: `Assigned` permits optional reassignment, `NotSelected` permits a later attempt, and only `Withdrawn` blocks the remainder of the season.
- The current `PlayerCampaignAssignmentEntity` combines enrollment, outcome, team, and concurrency. The build must represent automatic participation separately from saved placement semantics in the contract and effective-season projection, whether or not persistence remains in one table. A technical enrollment row cannot become a fabricated `Undecided` history event.
- Effective-team queries and counts must resolve supersession deterministically, count each player once, preserve Closed history, prevent prior-placement fallback after admin unassign, and remain safe under concurrent saves. Builders must not approximate this by counting every historical `Assigned` row.
- Placement query contracts must supply the unresolved total, written section state, effective season placement and source campaign, latest prior-season assignment, compatible active team choices, season/current-campaign team counts, compact evaluation evidence, actor/time attribution, history, and concurrency version without unbounded or tenant-unsafe reads.
- The existing graduation-year policy remains authoritative at mutation time even though incompatible teams are omitted from choices. Archived players and teams cannot receive new decisions.
- Evaluation owns note/tag mutation and the selected-player handoff. Campaign spine owns route topology. Closeout owns close/reopen consequences. Club → Teams owns durable team management. Place composes those contracts without duplicating their controls.
- Activity-feed support must distinguish initial placement, reassignment, outcome replacement, and unassign with enough structured old/new state to render useful written events.

## Decision record

- Use a player-first queue and selected-player working sheet; do not use team columns or drag-and-drop.
- Prioritize **Needs placement** while keeping assigned players searchable for optional supplemental reassignment.
- Separate automatic campaign participation from saved placement decisions; do not manufacture supplemental `Undecided` history for already assigned players.
- Revise same-season eligibility so `Assigned` may be reassigned, `NotSelected` may try again, and `Withdrawn` alone blocks later campaigns until admin unassign.
- Make every member's successful Active-campaign save immediately authoritative and attributable.
- Preserve Closed campaign history; record supplemental moves as new superseding decisions in the Active campaign.
- Define unresolved as eligible, teamless, and undecided. Previously assigned players available for optional reshuffling are not unresolved.
- Show count-only season team context with current-campaign contribution; introduce no capacity model.
- Offer **Keep on {Previous team}** as a one-action save when the most recent prior-season team remains active and compatible.
- Show only active, graduation-compatible team choices and permit no eligibility override.
- Confirm every replacement of a resolved decision and every new `Withdrawn` outcome; require no written reason.
- Keep evaluation evidence compact and read-only, with a context-preserving link to the full Evaluate surface.
- Audit every change and reject stale saves without overwriting the newer placement.
- Keep team creation in Club → Teams with a context-preserving return to Place.
- On Closed campaigns, allow administrators to unassign only the effective placement; leave the player teamless and never reactivate superseded history.
