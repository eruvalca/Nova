# Campaign closeout

Status: Confirmed

Issues: [#172](https://github.com/eruvalca/Nova/issues/172), child of [#163](https://github.com/eruvalca/Nova/issues/163)

Visitor mode: Operate

## Job and audience

Nova must give club staff a dependable boundary between active decision-making and an official campaign record. Every approved club member reaches Close to understand whether the campaign is ready, review the complete outcome, and return to unresolved placement work. A club administrator alone commits the lifecycle transition. After close, every member can read and export the final campaign record, while only an eligible administrator can reopen it.

Closeout is not a celebratory finish screen or a passive totals dashboard. It is the last operational checkpoint in the non-linear **Roster → Evaluate → Place → Close** route. Its job is to prove that every participant has an explicit outcome, expose genuine placement-integrity problems, state exactly what closing changes, and preserve the resulting campaign as stable history.

The activation moment is an administrator seeing a complete, credible roster and confidently closing the campaign without reconciling a spreadsheet. Success is a Closed campaign whose outcome record, audit trail, print view, and CSV agree—and whose history is not rewritten by later supplemental campaigns.

## Outcome and proof

- `Undecided` means a participant is still in active evaluation and placement work. Every participant must become `Assigned`, `Not selected`, or `Withdrawn` before closing; an unresolved count greater than zero is a blocker, not a warning.
- Assigned participants must remain eligible for an active team. Ineligible assignments and assignments to archived teams are also hard blockers.
- Closing is administrator-only. It makes evaluation and placement read-only for everyone and preserves every campaign outcome until an eligible administrator explicitly reopens that campaign.
- Closed campaigns permit no unassign, placement replacement, note mutation, or tag mutation. Later same-season changes happen in a new supplemental campaign, preserving the earlier campaign's record.
- Only the most recently opened campaign in the club's current season may reopen. A later Draft does not matter; any campaign opened later permanently makes the earlier campaign ineligible to reopen.
- Every approved club member can print or download the Closed campaign's final roster. Exports are campaign-specific records and never drift when a later supplemental campaign changes the effective season roster.

These rules intentionally supersede the soft-close and post-close admin-unassign decisions currently recorded in epic #163, issue #172, and the placement brief. Builders must follow this closeout contract for lifecycle and Closed-campaign mutation behavior.

## Selected direction

Closeout is a **final roster board** at the last Campaign Route Marker. Its upper field gives one written verdict—**Ready to close** or **Work remains**—with authoritative counts and direct correction paths. Below it, one roster confirmation field organizes assigned players by team and separates the terminal non-roster outcomes. The administrator's lifecycle action sits beside its consequence, after the evidence rather than before it.

When Closed, the same surface changes posture instead of becoming a different product: readiness controls give way to closure attribution, the stable campaign record, shared export actions, and narrowly constrained reopen authority. Fieldhouse Wayfinding remains operational and flat. Wayfinding Teal identifies the primary lifecycle action; Signal Amber identifies unresolved work; Copper Rust is reserved for failed operations or destructive/error states. Every status has words, counts, and semantic structure rather than color alone.

## Active closeout working surface

Close retains the shared campaign header and complete Route Markers. The selected Close marker comes from the URL and never implies that earlier destinations were completed merely because someone visited them.

The working surface presents, in order:

1. **Readiness verdict** — a written status, participant total, resolved total, and authoritative blocker counts.
2. **Outcome summary** — `Assigned`, `Not selected`, `Withdrawn`, and `Undecided`, with the unresolved count using the exact **Needs placement** definition from the placement brief.
3. **Final roster confirmation** — assigned players grouped by active team, followed by distinct `Not selected` and `Withdrawn` sections. `Undecided` appears as work remaining rather than as part of a final roster.
4. **Lifecycle checkpoint** — the effect of closing, administrator authority, and the primary close action when every blocker is cleared.

All approved members see the same readiness truth and roster evidence. Members do not receive a disabled or decorative close button; they receive plain language that a club administrator closes the campaign. Administrators see the action only when the campaign is Active and the authoritative snapshot is ready.

Roster confirmation is bounded and searchable for large campaigns. Whole-campaign counts remain authoritative outside the current page. Each participant row carries enough identity to prevent a mistaken confirmation: full name, tryout number when present, graduation year, written outcome, and team when assigned. It does not duplicate evaluation notes, tags, placement controls, or player-profile editing.

## Blockers and correction paths

Closeout uses one blocker language, but distinguishes the work each condition requires:

- **Needs a decision — _N_ players:** participants who are eligible, teamless, and currently `Undecided`. The correction path opens Place with **Needs placement** selected and preserves a return to Close.
- **Ineligible assignment — _N_ players:** assigned participants whose team is incompatible under the authoritative eligibility policy. The correction path opens the affected placement records for a new valid outcome.
- **Archived team assignment — _N_ players:** assigned participants referencing a team that can no longer carry the final roster. The correction path opens the affected placement records for reassignment or another explicit terminal outcome.

Blocker counts and participant sets come from authoritative server projections; the browser never derives readiness from a loaded page. A correction in Place refreshes Close when the member returns. Empty blocker groups state their satisfied condition in words without filling the surface with success decoration.

The zero-unresolved case is **All participants have an outcome**, not “Undecided satisfied.” The interface never suggests changing unresolved players automatically, bulk-converting them to `Not selected`, or manufacturing an outcome at close.

## Closing checkpoint

Closing uses a dedicated inline review board, not a detached modal. It names the campaign and season, repeats the current participant and outcome counts, and states the consequence: evaluation and placement become read-only, the final campaign record becomes available to the club, and a later change requires an eligible reopen or a new supplemental campaign.

The primary action is **Close campaign**. It is unavailable while any blocker exists and never asks the administrator to type the campaign name. Prevent duplicate submission and keep progress perceivable without removing the campaign identity or consequence.

The displayed readiness is advisory. On commitment, Nova acquires the lifecycle mutation lock and evaluates a fresh authoritative snapshot. A player auto-enrolled, placement changed, team archived, or eligibility change discovered after preview leaves the campaign Active, refreshes the affected blocker, and preserves the administrator's place on Close. An ambiguous failure verifies whether the close committed before offering retry.

On success, remain on Close, move focus to the Closed status heading, and announce that the campaign closed. Replace active controls with closure attribution and the official record. The entire route remains readable, but Evaluate and Place expose their explicit Closed, read-only posture.

## Closed campaign record

The Closed surface is the official record for that campaign, not a view of the club's current effective season roster. It shows:

- Campaign, season, written `Closed` status, latest close time, and closing administrator.
- Final campaign counts for `Assigned`, `Not selected`, and `Withdrawn`.
- Assigned players grouped by their final team, plus separate `Not selected` and `Withdrawn` sections.
- A bounded lifecycle and decision audit that preserves who closed, reopened, changed outcomes while Active, and closed again.
- Shared **Print roster** and **Download CSV** actions.

Every campaign outcome is immutable while the campaign remains Closed. No member—including an administrator—can unassign, reassign, replace an outcome, edit evaluation content, or use an apparent correction shortcut from the record. An older Closed campaign remains unchanged when a later supplemental campaign moves or resolves the same player.

If the current season needs another round of decisions, staff create or work in a supplemental campaign. If the latest campaign itself closed in error, an eligible administrator may reopen it. The interface never edits Closed history in place or silently reactivates a superseded placement.

## Reopen semantics

Reopen is administrator-only and appears on the Closed record only when all of these statements are true:

- The campaign belongs to the club's current active season.
- It is the most recently opened campaign in that season, determined by authoritative lifecycle order.
- No later campaign in the season has reached Active, even if that later campaign is now Closed.
- Reopening would preserve the invariant that the club has at most one Active campaign.

Later unopened Drafts may coexist and do not make the latest operational campaign historical. A campaign from an earlier season or one followed by any later opened campaign cannot reopen. Its record explains the reason in words and links to the current/latest campaign when that provides a useful next step; it does not render a permanently disabled action without explanation.

Reopening uses inline confirmation that names the campaign and consequence: existing outcomes and audit history remain, evaluation and placement become editable again, and exports cease to represent a final record until the campaign closes again. Reopen does not reset players to `Undecided`, create a Draft, discard outcomes, or alter later unopened Drafts.

On success, remain on Close, move focus to the Active status, announce **Campaign reopened**, and restore the working-surface posture and complete active route. Staff deliberately choose Evaluate or Place rather than being redirected. Re-closing evaluates the full fresh blocker set. The new outcomes and latest close attribution become the one official campaign record; lifecycle and placement audit trails preserve the earlier close, reopen, changes, and subsequent close without creating a downloadable roster-version system.

## Print and CSV export

Exports are available only while the campaign is Closed and to every approved club member. They are generated on demand from the authoritative final campaign record. Reopening removes the final-export actions until the campaign closes again; re-closing makes newly generated exports reflect the new official outcome.

The print view is communication-focused:

- Lead with club, season, campaign, written Closed status, latest close attribution, and generation time.
- Group assigned players by team with stable team ordering and page-break behavior that keeps headings recognizable.
- Show full name, tryout number when present, and graduation year for each player.
- Follow assigned rosters with clearly separated `Not selected` and `Withdrawn` appendices.
- Remove application navigation, controls, filters, and interactive-only status treatment from print while preserving readable hierarchy in color and monochrome.

The CSV is operationally complete and contains one row per campaign participant. It includes campaign and season, player name, tryout number when present, graduation year, final outcome, final team when assigned, decision author, and decision time. Values use stable documented headings and unambiguous human-readable outcomes. Formula-like user content is escaped safely, encoding supports real names, and no evaluation notes, tags, profile imagery, private account data, or internal-only diagnostics are exported.

Print and CSV use the same authoritative participant set and outcomes. An export failure leaves the Closed record intact, names which format failed, and offers a local retry. Generating or downloading an export does not create a lifecycle event or imply that the roster was externally delivered.

## States and ranges

The design covers one participant, a typical 20–200-player campaign, and larger bounded/paged campaigns; one team, many teams, mixed graduation years, duplicate names, missing tryout numbers, and long team or player names. It covers all-assigned rosters, mixtures of all three terminal outcomes, all-non-roster outcomes, and each blocker alone or in combination.

Material states include initial loading, partial region failure, authoritative refresh, filtered-empty roster, ready, blocked, close pending, close conflict, close committed after an ambiguous response, Closed, export pending/failed/succeeded, print preparation, reopen eligible, reopen ineligible because of season or lifecycle order, reopen pending/conflicted, reopened, permission changed, and a campaign changed by another administrator.

A legitimately Closed record cannot contain `Undecided`. If legacy or inconsistent data produces that state, Nova identifies it as a record-integrity problem rather than presenting a valid final roster or silently repairing it in the browser.

## Interaction and layout

- Desktop uses one broad flat roster board: readiness and lifecycle consequence remain visible without turning counts into a generic card grid. Team sections support dense scanning and deliberate pagination.
- Mobile stacks verdict, blockers, roster sections, and lifecycle action in task order. Full Route Marker labels remain available, participant identity never collapses into initials alone, and print/export actions remain distinct.
- Every interactive target is at least 2.75rem. Keyboard focus is visible and deliberately moved after close or reopen. Async feedback is announced; blocker counts, read-only posture, and lifecycle eligibility never rely on color.
- Search, filters, pagination, and correction handoffs preserve URL-backed context and browser navigation. Returning from Place restores Close and refreshes authoritative readiness.
- Reduced motion preserves every orientation and status cue. Long and localized labels reflow without separating an action from its consequence.

## Scope and boundaries

This brief covers the Active Close destination, readiness and final-roster review, administrator close, the Closed campaign record, constrained reopen, and shared print/CSV export experience. It defines production-ready responsive behavior but no visual comp, direction contract, or production code.

Explicit anti-goals:

- No soft close, automatic `Not selected`, or allowed `Undecided` outcome in a valid Closed campaign.
- No post-close unassign, placement correction, evaluation mutation, inline outcome control, or history rewrite.
- No reopening an older campaign, a campaign from an inactive season, or a campaign followed by another opened campaign.
- No automatic redirect after reopen, reset to Draft, outcome reset, or deletion of lifecycle history.
- No export of evaluation notes, tags, photos, account details, or a live effective roster assembled from later campaigns.
- No separate downloadable snapshot for every close/reopen cycle and no claim that an export was delivered to players or families.
- No bulk placement, roster editing, team management, player management, or spreadsheet import inside Close.
- No generic completion dashboard, card mosaic, celebratory interstitial, ornamental sports imagery, ambient shadow, color-only state, or CSS-level visual specification.

## Constraints and implementation consequences

- Preserve Fieldhouse Wayfinding, the complete Campaign Route Markers, SSR-first Blazor architecture, URL-backed correction handoffs, club-scoped authorization, semantic status language, keyboard and touch access, bounded queries, and authoritative server validation.
- The current `CampaignClosurePolicy` already blocks `Undecided`, ineligible assignments, and archived-team assignments, but its projections and messages must align with the placement brief's authoritative **Needs placement** semantics rather than treating enrollment as a fabricated decision event.
- The current closeout panels, Bootstrap cards, nav tabs, summary boxes, and blocker copy are behavioral evidence only. They do not govern the new composition.
- Reopen enforcement must be server-authoritative and concurrency-safe. It needs current-season identity plus deterministic opened-order evidence under the lifecycle mutation lock; planned dates and client ordering are insufficient.
- Closed-campaign mutation authorization must remove the previously designed administrator unassign path. Existing placement contracts, services, endpoints, and tests that permit post-close unassign must be revised during the owning build/foundation work.
- Closed record and export queries must project campaign-specific final outcomes, not recalculate historical campaigns from the current effective season roster.
- Roster export implementation belongs to [#182](https://github.com/eruvalca/Nova/issues/182). The campaign-loop build may own the Close surface and handoff, but it must not simulate CSV or print data from a partially loaded client page.
- Preserve lifecycle mutation locks, optimistic concurrency, retry verification, append-only lifecycle events, actor/time attribution, tenant-safe identifiers, and explicit paging or truncation. The server owns every close/reopen invariant.
- The confirmed placement brief and epic #163 still contain superseded soft-close and post-close-unassign language. They must be reconciled before the related build is considered ready; this brief is normative for the closeout boundary.

## Decision record

- Require every participant to be `Assigned`, `Not selected`, or `Withdrawn`; `Undecided` blocks close.
- Keep ineligible assignments and archived-team assignments as hard blockers.
- Show readiness and the complete final-roster evidence to every member; reserve close and reopen lifecycle actions for administrators.
- Make all evaluation and placement data immutable while Closed. Do not allow administrator unassign after close.
- Preserve each Closed campaign as its own historical outcome record, unaffected by later supplemental campaigns.
- Allow only the most recently opened campaign in the current active season to reopen; ignore later unopened Drafts, but treat any later opened campaign as a permanent boundary.
- Preserve outcomes when reopening, stay on Close after success, and require normal readiness again before re-closing.
- Maintain one current official record per campaign while retaining complete lifecycle and placement audit trails across close/reopen cycles.
- Give every approved club member on-demand print and CSV exports of the Closed campaign record.
- Use team-grouped communication print plus a one-row-per-participant operational CSV; export no evaluation content or private account data.
