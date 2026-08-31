# Campaign lifecycle spine

Status: Confirmed

Issues: [#166](https://github.com/eruvalca/Nova/issues/166), child of [#163](https://github.com/eruvalca/Nova/issues/163)

Visitor mode: Operate

## Job and audience

Nova must give club administrators one dependable route from campaign intent to live club work, while giving every club member an immediate reading of what campaign is current and where work happens. Administrators create, prepare, open, close, and reopen campaigns; coaches and evaluators enter Active campaigns to work from the shared roster and return to Closed campaigns as history. People may be preparing at a desk or evaluating on a phone beside the field, so lifecycle state and the next valid action must be legible without reconstructing them from dates or navigation tabs.

The administrator's activation moment is opening a prepared campaign and seeing the complete active-player roster ready for evaluation. A member's activation moment is entering the Active campaign and understanding the shared route—Roster, Evaluate, Place, Close—without mistaking that route for a rigid wizard.

## Outcome and proof

- Campaigns follow one explicit lifecycle: **Draft → Active → Closed**. Drafts are durable, administrator-only preparation spaces; Active and Closed campaigns are visible to all approved club members.
- Opening is a deliberate operational boundary. Nova names the campaign, season, dates, and exact roster consequence before enrolling every active club player in one idempotent commitment.
- The Active workspace replaces generic navigation tabs with the design system's **Campaign Route Markers**: **Roster → Evaluate → Place → Close**. The route stays complete and navigable while evaluation and placement overlap.
- The campaigns directory makes the club the durable home and seasons the rhythm. It leads with current work, preserves season-grouped history, and communicates status, scale, progress, and the next action in each row.
- The design proves its product specificity through real season boundaries, automatic enrollment, team readiness, placement progress, and lifecycle authority—not a generic project wizard, card dashboard, or schedule inferred from dates.

## Selected direction

The campaign spine applies Fieldhouse Wayfinding literally: the directory is the club's campaign board, Draft is a preparation sheet, opening is a named checkpoint, and the Active workspace is one connected route. Broad flat boards hold coherent preparation and review tasks. Compact directory rows support scanning across seasons. Wayfinding Teal identifies the current route and primary action; Signal Amber identifies readiness problems or unresolved work; every state and consequence is also written in words.

The Campaign Route Markers are navigation with lifecycle awareness, not a visited-step tracker. The selected stop follows the current URL. Completion or attention treatments come only from authoritative campaign data and never from whether a person has opened a screen. Roster, Evaluate, and Place remain reachable throughout Active; Close exposes readiness and the administrator-only lifecycle action without hiding the complete route from members.

## Lifecycle contract

### Draft

- **Create campaign** begins in the campaigns directory and is visible only to club administrators. The creation form collects the campaign's essential identity and boundary: name, required season, campaign start, and optional planned end. If the club has no season, the administrator creates one inline with its name and date window.
- Existing domain rules remain visible at the point of correction: campaign dates must fit the selected season, finite season windows require a compatible planned end, and campaign and inline-season names must be unique in their existing scopes.
- Saving valid essentials creates a real Draft and lands on its preparation workspace; it never opens the campaign implicitly. Input survives validation, retry, and back navigation. Submission prevents duplicate commits and recovers safely from an ambiguous result.
- A club may have multiple Drafts while another campaign is Active. Only one campaign may be Active, so Draft preparation can continue but opening remains unavailable until the Active campaign closes.
- Drafts are absent from member directories, counts, search results, deep-link responses, and activity intended for ordinary members. Authorization is enforced by queries and destinations, not only by hiding links.

### Active

- A Draft becomes Active only through the explicit opening checkpoint. It cannot return to Draft.
- Opening enrolls every active club player as a campaign participant. Players created while the campaign remains Active participate automatically through the player-intake lifecycle. Participation does not save an `Undecided` placement or create a placement history/activity event; a participant with no applicable saved decision appears in the unresolved working state until staff record an explicit outcome.
- Dates are descriptive planning boundaries; they never open or close a campaign automatically.
- The Active workspace exposes the complete Route Markers sequence to every club member. The detailed Roster, Evaluate, Place, and Close experiences remain governed by their respective briefs and role rules.

### Closed

- Closing moves Active to Closed through the administrator-only closeout flow. Closed campaigns remain visible to all club members as immutable season history and do not disappear from the directory. Evaluation and placement mutations, including post-close unassign, are unavailable for every role.
- Reopening returns Closed directly to Active; it never creates a new Draft or bypasses the one-Active-campaign rule. Only the most recently opened campaign in the current season may reopen, as defined by the closeout brief.
- Active and Closed campaigns cannot be deleted. Complete-close readiness, unresolved outcomes, immutable Closed detail, constrained reopening, and roster export are owned by the closeout brief and supporting export work.

## Draft preparation workspace

The Draft workspace is one preparation surface organized around readiness, not a miniature version of the Active campaign.

- **Campaign details:** show the name, season, and date window with an administrator edit path. The season relationship and its constraints remain explicit.
- **Roster preview:** show the current number of active club players with the plain-language consequence that they will be enrolled when the campaign opens. Do not create assignments early or offer manual roster selection. Link an empty roster to player intake rather than embedding a second player-management product.
- **Team readiness:** show the count and recognizable names of active club teams. Administrators may create a durable team inline during Draft preparation or go to the Club area's Teams directory. Teams belong to the club, not the Draft, and remain if the Draft is later deleted.
- **Opening readiness:** place the blockers, warnings, and primary opening action together. Another Active campaign and zero active players are blockers. Zero active teams is a written warning because evaluation can begin before placement teams exist; it does not block opening.
- **Delete draft:** provide an administrator-only action with inline confirmation that names the campaign and explains permanence. Deletion removes only the unopened campaign; it never removes durable teams created during preparation. Failure leaves the Draft and its context intact with a local retry path.

Draft preparation supports pristine, saving, saved, invalid, stale, recoverable failure, and permission-changed states. A stale Draft caused by another administrator opening or deleting it must reconcile to the authoritative state instead of presenting a second commitment path.

## Opening checkpoint

Opening uses a dedicated review board in the Draft workspace, not a detached modal.

- The review names the campaign and season, repeats the campaign date window, shows the exact current active-player count, and states that every one of those players will be enrolled immediately.
- Team readiness is visible alongside roster readiness. A zero-team warning explains that teams may be added before placement; it does not visually compete with an actual blocker.
- If another campaign is Active, the board names it and links to that campaign. The opening action stays unavailable until it is Closed.
- With zero active players, the board links to player intake and explains why opening is unavailable.
- The primary commitment includes its consequence: **Open campaign and enroll _N_ players**. A nearby sentence makes the state transition explicit and says that an opened campaign cannot return to Draft.
- The readiness data is refreshed when the administrator commits. If club data changed after the preview, the authoritative opening operation enrolls the fresh active-player set and the success feedback reports the actual count. A newly discovered blocker leaves the review intact and identifies the correction path.
- While opening, prevent duplicate actions and keep the commitment's status perceivable. A recoverable or ambiguous failure must verify the authoritative lifecycle state before offering retry, so the interface never opens or enrolls twice.
- On success, navigate to the Active campaign's **Roster** stop, move focus to its heading, and announce that the campaign opened with the actual enrollment count.

## Active campaign route

The Active workspace preserves one campaign header with season, campaign name, written status, date window, and participant scale. Immediately beneath it, a horizontally connected Route Markers sequence replaces the current nav-tabs treatment:

1. **Roster** — the automatically enrolled participant field and campaign-specific roster context.
2. **Evaluate** — shared, author-attributed observations and club-wide trait tags.
3. **Place** — collaborative placement work, eligibility constraints, and unresolved outcomes.
4. **Close** — closeout readiness and the transition to history.

The route is intentionally non-linear. Roster, Evaluate, and Place remain available while the campaign is Active because evaluation and placement may overlap. The selected marker comes from the URL and remains deep-linkable. Roster may communicate that initial enrollment is established; Evaluate must not claim completion merely because notes exist; Place and Close use only the readiness semantics established by their owning briefs. Marker state never means “the user visited this page.”

All members see the complete route and its written statuses. Role-filtered controls live inside destinations: all members participate in evaluation and placement under the confirmed journey map, while only administrators receive close, reopen, campaign-management, or other lifecycle mutation controls. A member reaching Close sees the campaign's readiness context without receiving the administrator action.

## Campaigns directory

The directory is one season-grouped operational board, not a grid of campaign cards.

- Lead with the current season and follow with prior seasons in descending order. Within the current season, show the Active campaign first, then Drafts, then Closed campaigns in recency order. Historical seasons remain reachable and are bounded or paged when numerous; the interface never silently truncates them.
- Administrators see Draft, Active, and Closed campaigns. Other approved members see only Active and Closed campaigns, with no empty gap, hidden count discrepancy, or status hint that exposes Draft existence.
- Each row shows campaign name, written status, date window, participant scale, placement or unresolved progress when meaningful, and one contextual next action. Draft rows describe the current roster consequence—such as “24 active players will enroll”—rather than pretending participants are already assigned.
- The Active row receives positional priority and a clear **Continue campaign** route. Draft rows lead to preparation. Closed rows lead to history and final outcomes. Secondary management actions do not compete with the row's one next action.
- Season headings carry the time rhythm; rows do not repeat season metadata unnecessarily. The current season is identified in words, and prior seasons remain recognizable by name and date window.
- A club with no campaigns gets an operational empty state. Administrators receive **Create campaign**; members receive a truthful explanation that no campaign is available, without an unauthorized action.

The directory supports no campaigns, one Active campaign, multiple Drafts, multiple sequential campaigns in one season, many historical seasons, bounded/paged results, loading, failed retrieval, stale lifecycle changes, and role changes. A refresh must move a just-opened or just-closed campaign to its new position without losing focus or presenting contradictory row actions.

## Interaction and layout

- Desktop uses a broad directory or workspace field with stable campaign identity and an uninterrupted route. Mobile preserves full labels; Route Markers may scroll horizontally when their minimum legible width exceeds the viewport, but focus rings and the selected stop remain visible.
- Directory rows collapse into compact labeled records on narrow screens rather than becoming decorative cards. Primary actions become comfortably reachable without duplicating the same command above and below the content.
- Every interactive target is at least 2.75rem. State never depends on color; selected markers use semantic structure and `aria-current`; lifecycle feedback is announced; focus moves deliberately after creation, opening, deletion, filtering, or route navigation.
- Async mutations keep the subject, current state, and retry path together. Destructive confirmation is inline and names its consequence. Reduced-motion users receive the same orientation without animated progress assumptions.
- Campaign and season lists are bounded or paged with explicit result counts. Large rosters remain filterable and paged inside their owning surfaces rather than expanding the lifecycle spine into one unbounded page.

## Scope and boundaries

This brief covers the campaigns directory, campaign creation, the saved Draft workspace, opening a campaign, the lifecycle frame around Active and Closed, and the Route Markers topology. It defines production-ready responsive behavior but no visual comp or implementation code.

Explicit anti-goals:

- No immediate Active campaign creation and no scheduled or date-triggered opening or closing.
- No member visibility into Draft existence or content.
- No manual campaign-roster selection; opening enrolls every active club player.
- No requirement to create teams before evaluation can begin.
- No return from Active to Draft and no deletion of Active or Closed history.
- No gated stepper, visit-based progress, generic nav tabs, or hidden workflow destinations.
- No generic card grid, ornamental sports imagery, celebratory interstitial, or CSS-level direction.
- No redesign of player intake, evaluation details, placement decisions, closeout rules, season management, or export behavior; their owning briefs and foundation issues supply those contracts.

## Constraints and implementation consequences

- Preserve Fieldhouse Wayfinding, the Campaign Route Markers component, SSR-first Blazor architecture, URL-backed destinations, club-scoped authorization, semantic status language, keyboard access, touch-safe behavior, and meaningful loading/error feedback.
- Existing campaign screens are behavioral evidence only. Their Bootstrap creation form, status filter, and nav-tabs composition are not design authority.
- Foundation issue [#178](https://github.com/eruvalca/Nova/issues/178) owns Draft persistence, one-Active enforcement, query-time Draft visibility, opening-time enrollment backfill, transaction safety, and eligibility foundations. Campaign creation currently writes Active immediately and must not be treated as the intended lifecycle.
- Draft deletion is required by this brief but is not present in the current API or explicitly owned by #178. The campaign-loop build must add or separately track an administrator-only, tenant-safe delete capability rather than simulating deletion in the client.
- Placement participation depends on [#175](https://github.com/eruvalca/Nova/issues/175); first-class season behavior depends on [#176](https://github.com/eruvalca/Nova/issues/176); detailed close/reopen behavior depends on [#172](https://github.com/eruvalca/Nova/issues/172); roster export depends on [#182](https://github.com/eruvalca/Nova/issues/182). This spine frames those destinations without pre-empting their detailed briefs.
- Preserve authoritative validation, idempotency keys, lifecycle mutation locks, transactions, retry verification, append-only lifecycle events, bounded queries, and explicit truncation or paging. The opening preview is advisory; the locked server mutation owns the final roster and one-Active invariant.

## Decision record

- Save campaign essentials as an administrator-only Draft, then prepare and open it from a persistent Draft workspace.
- Allow multiple Drafts, including while another campaign is Active; permit only one Active campaign per club.
- Block opening when another campaign is Active or no active players exist. Warn, but do not block, when no active teams exist.
- Confirm opening in a dedicated review board with **Open campaign and enroll _N_ players**; land on Roster after success.
- Use Roster, Evaluate, Place, and Close as non-linear, URL-backed Route Markers whose state comes from real campaign data rather than visits.
- Keep the full route visible to members while restricting lifecycle mutations to administrators.
- Group the directory by season with current work first; show Active, then Draft, then Closed within the current season, and expose Drafts only to administrators.
- Allow administrators to delete only unopened Drafts through inline confirmation; durable teams survive that deletion.
- Treat opening as irreversible to Draft. Reopening moves Closed directly to Active and remains governed by the closeout flow.
- Keep automatic participation separate from saved placement decisions: opening or later intake creates no fabricated `Undecided` history/activity event.
- Keep Closed evaluation and outcomes immutable. Allow only the most recently opened campaign in the current season to reopen, subject to the one-Active-campaign rule.
