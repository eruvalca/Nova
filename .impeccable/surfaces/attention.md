# In-app attention and activity

Status: Confirmed

Issues: [#173](https://github.com/eruvalca/Nova/issues/173), child of [#163](https://github.com/eruvalca/Nova/issues/163)

Visitor mode: Operate

## Job and audience

Nova must help approved club members answer two different questions without turning the product into a notification inbox: **Where is the club's work now?** and **What changed recently?** Every member needs a direct route into the Active campaign and a readable club activity record. Club administrators additionally need a compact, trustworthy account of work that is waiting specifically for them: pending join requests and placements that still need a decision.

The member may arrive on a phone during evaluation or return at a desk between campaigns. With an Active campaign, the dashboard should put that campaign and its evaluation context first. Without one, the dashboard should remain useful as the current season's club home: members can browse current teams and their rosters, review the latest Closed campaign when one exists, and understand that an administrator opens the next campaign. Administrators see the same club context plus the relevant Draft, open, or create path.

The activation moment is immediate orientation, not inbox completion. A member sees the current campaign and can begin evaluating; an administrator sees exactly which unresolved work exists and can enter its canonical surface in one action. Success means nobody must inspect several directories, infer urgency from dates, or treat club activity as a list that must be “cleared.”

## Outcome and proof

- The dashboard is the attention home. It leads with the one Active campaign when present, provides a useful season-idle state when absent, keeps administrator attention in a stable rail, and carries a bounded club activity feed below the primary work.
- Attention and activity remain distinct. **Attention** is current, actionable, administrator-only work; **activity** is an append-only, role-shaped account of meaningful club changes for every approved member.
- Pending join requests route to Club → Requests. Unresolved placements route to the Active campaign's Place destination with **Needs placement** selected. The dashboard never duplicates approval or placement controls.
- Navigation badges appear only on the routes that resolve the work: pending requests on the Club route and unresolved placements on Campaigns. There is no notification bell, aggregate Dashboard badge, unread count, or new Notifications destination.
- The feed records campaign lifecycle, placement, membership, and role changes. Evaluation notes and tag applications stay in their campaign/evaluation context and do not flood the club-level feed.
- The design proves its product specificity through one Active campaign, current-season team rosters, campaign outcomes, placement decisions, club membership, and role authority—not generic announcements, engagement notifications, or a task inbox.

## Selected direction

Fieldhouse Wayfinding treats attention as route information. The Active campaign is the large current sign; the administrator rail is a small dispatch board; destination badges are compact route annotations; and activity is a dated club log. Wayfinding Teal identifies the route and primary action. Signal Amber appears only where unresolved administrator work exists. Cleared, historical, and informational states use quiet ink and neutral surfaces, and every state is written in words.

The dashboard stays one operational field rather than becoming a card mosaic. Active work occupies the first viewport. The administrator rail remains adjacent on wide screens and follows the campaign state on narrow screens. Recent activity is subordinate to current work and uses a compact chronological directory. The feed has no read/unread posture because recorded history does not become less true when one person views it.

## Dashboard attention hierarchy

### Active campaign

- When a campaign is Active, it is the dashboard's primary working context for every approved member. Show its season, campaign name, written Active status, participant scale, and authoritative **Needs placement** count when relevant.
- A member's primary action is **Evaluate players**, deep-linking to the Active campaign's Evaluate destination. A secondary **Open campaign** path exposes the complete Roster → Evaluate → Place → Close route without duplicating it on the dashboard.
- Administrators retain the same campaign orientation. Their placement correction path lives in the attention rail rather than replacing the member-facing evaluation route.
- The campaign projection is authoritative and singular. The interface must not render competing “current” campaigns or infer Active state from dates.

### Season-idle member state

When no campaign is Active, the dashboard becomes the current season's quiet club home rather than an empty error or a campaign-creation prompt the member cannot use.

- Lead with the current season's name and date window plus the written state **No campaign is active**. Explain plainly that a club administrator opens the next campaign.
- Make **Browse teams and rosters** the primary member action. It opens the member-visible Club → Teams directory, where each durable team exposes its current-season roster.
- When a Closed campaign exists in the current season, provide **Review latest campaign** as a secondary action into that campaign's stable record. Older campaign history remains in the Campaigns directory.
- Keep Recent activity available below the idle state so a member can understand the latest club movement without being told merely to “check back.”
- If the current season has no teams, preserve the browse route and let the canonical Teams surface explain the empty roster truth. Do not invent sample teams or give members an administrative creation action.

### Season-idle administrator state

- The administrator sees the same season, team-roster, latest-campaign, and activity context as members.
- The primary next action follows authoritative campaign state: **Continue preparing {Draft}** when an appropriate Draft exists, **Open {Draft}** when it is ready and no campaign blocks it, or **Create campaign** when no Draft exists. Multiple Drafts route through the Campaigns directory instead of letting the dashboard choose silently.
- Team creation, Draft preparation, opening review, and season advancement remain in their canonical surfaces. The idle dashboard points to them; it does not embed their forms.
- If no current season exists because of legacy or inconsistent state, say so explicitly. Administrators receive **Start next season**; members retain the durable Teams directory but are told that a current-season roster snapshot is unavailable.

## Administrator attention rail

The administrator attention rail is visible only to current club administrators and occupies a stable dashboard position. It contains at most two actionable rows:

1. **Pending join requests — _N_** links to Club → Requests, the canonical queue. Include the oldest request's waiting duration when available so age is visible without manufacturing severity.
2. **Needs placement — _N_** links to the Active campaign's Place destination with the Needs placement filter selected. Name the campaign so the count has context.

Counts represent current work, not unseen events. They come from authoritative, tenant-scoped queries and are not derived from the loaded feed or a partially loaded roster.

- Show an attention row only when its count is greater than zero. Signal Amber marks the written count and unresolved state without recoloring the whole rail.
- When both counts are zero, keep the rail in place with a quiet **No admin work waiting** state. Do not use amber, celebration, or a permanent “0” badge.
- A successful approval, rejection, placement, reassignment, outcome change, or administrator-owned later-campaign supersession of a prior `Withdrawn` outcome refreshes the relevant count in the surface where it happened. Normal enhanced navigation refreshes dashboard and shell counts; no live push channel or background polling is required.
- If one attention query fails, label only that row as temporarily unavailable and keep the other result. Never translate an unavailable count into zero. Offer a local retry without discarding the dashboard's campaign or activity context.
- Attention is a pointer, not an alternate workflow. No join-request decision or placement mutation occurs inside the rail.

## Navigation badges

Badges annotate the existing route that resolves each kind of administrator work:

- The primary **Club** route carries the pending join-request count.
- The primary **Campaigns** route carries the unresolved placement count for the Active campaign.
- Badges are administrator-only, absent at zero, and display `99+` when the underlying count exceeds 99. Their accessible names include both the count and subject, such as “3 pending join requests.”
- Badge counts use Signal Amber because they always represent unresolved work. Color never carries the meaning alone; the count and accessible label remain present.
- Badge failure is omission, not a misleading zero or stale warning color. The dashboard rail is the richer place to expose a regional load failure and retry.

Badges stay inside the existing full-label navigation rows and preserve the One Route Rule across the desktop rail, opened mobile sheet, and scripting-disabled fallback. They never replace a route label, leading icon, active-route marker, or club crest; they do not create a second link target inside the row. There is no badge on Dashboard, Evaluate, Place, the account route, or a new notification bell.

## Activity event taxonomy and visibility

The feed is role-shaped on the server. An ordinary member must never receive an administrator-only event and rely on the UI to hide it. Draft existence and join-request identity remain protected at the query boundary.

| Event family | Recorded events | Audience | Rendering consequence |
|---|---|---|---|
| Campaign lifecycle | Draft created; Draft deleted | Administrators | Name the Draft and actor. Link a surviving Draft to preparation; a deleted Draft remains readable without a dead link. |
| Campaign lifecycle | Opened; Closed; Reopened | All approved members | Name the campaign, season when useful, actor, and resulting written state. Link to the campaign's current authorized destination. |
| Placement | Assigned to a team; changed to `Not selected`; changed to `Withdrawn` | All approved members | Name the player, actor, campaign, and new outcome or team. Link to the player's campaign placement record when available. |
| Placement | Reassigned; outcome replaced; administrator superseded a prior-campaign `Withdrawn` outcome | All approved members | Preserve and render meaningful old and new state, such as “moved Avery Chen from U14 Gold to U14 Teal.” Every event belongs to the later Active campaign and leaves the Closed source record unchanged; never render or imply a post-close unassign. |
| Join request | Submitted; cancelled; rejected | Administrators | Name the requester, actor where applicable, club context, and outcome. Link only a still-actionable request to Club → Requests. |
| Join request | Approved | Administrators and members, role-shaped | Administrators may see the approval action; members see the resulting club event, “Jordan Lee joined the club,” without pending-request details. Emit or present one semantic event, not two duplicate feed rows. |
| Member role | Promoted to ClubAdmin; demoted from ClubAdmin | All approved members | Name the affected member, actor, and resulting role in words. |
| Membership | Removed by an administrator; left voluntarily | All remaining approved members | Distinguish removal from voluntary departure and retain enough stable display context to remain readable after membership ends. |

Explicit exclusions:

- Adding, editing, or deleting evaluation notes.
- Applying, creating, renaming, or archiving tags.
- Opening a page, viewing a player, downloading an export, filtering a directory, or other observational behavior.
- Campaign metadata edits that do not change lifecycle, routine player/team CRUD, authentication events, and system diagnostics.
- Technical enrollment rows created automatically when a campaign opens or a player is added. Participation is not a fabricated placement decision.

## Activity rendering and history

Recent activity lives on the dashboard below current campaign or season-idle context.

- Load the newest 20 visible events first. Group them under **Today**, **Yesterday**, or a localized calendar date and order events newest-first with a deterministic server tie-breaker.
- Each entry uses one category icon, a concise past-tense sentence, actor or subject as the grammar requires, relevant campaign/team/outcome context, and a localized time. Assistive text exposes the full date and time; the design does not require a live-updating relative-time timer.
- Make the whole event readable before any link. When an authorized target still exists, provide one clear destination link. A deleted Draft, departed member, superseded outcome, or newly restricted destination leaves durable text rather than a broken or forbidden link.
- **Show older activity** appends the next bounded page in the same chronology while preserving focus and announcing the number of entries added. Stop with a written end state. Do not use infinite scroll.
- The feed has no read/unread state, mark-all-read action, dismissal, subscription settings, category filter, toast replay, or per-person cursor. Viewing history changes nothing.
- With no visible events, state **No club activity yet** and explain that campaign, placement, and membership changes will appear here. Do not show sample events.

Event sentences must remain specific and attributable. Representative grammar includes:

- “Morgan Reyes opened Fall Supplemental.”
- “Taylor Kim placed Avery Chen on U14 Teal.”
- “Taylor Kim moved Avery Chen from U14 Gold to U14 Teal.”
- “Jordan Lee joined the club.”
- “Morgan Reyes promoted Jordan Lee to club administrator.”
- “Jordan Lee left the club.”

Avoid vague verbs such as “updated,” internal enum names, duplicated campaign names, and sentences that expose inaccessible Draft or request context.

Placement activity records only transitions authorized while a campaign is Active. A later-campaign reassignment or administrator supersession may name the earlier effective state for context, but the event is owned by the later Active campaign and never suggests that Nova edited or unassigned a Closed outcome.

## States and realistic ranges

The design covers a new club with no feed events; one event; a typical burst of 5–50 events during campaign work; hundreds or thousands of historical events reached through paging; several events sharing a timestamp; duplicate member or player names; long names; removed actors; deleted Drafts; archived teams; and targets the viewer can no longer access.

Dashboard states include one Active campaign; no Active campaign with Drafts; no Active campaign with a latest Closed campaign; a current season with teams and rosters; a current season with no teams; no current season; no activity; no administrator work; one or both attention kinds present; counts greater than 99; and member versus administrator visibility.

Material interaction states include initial load, older-page load, end of history, feed-only failure, one attention-region failure, stale count after a concurrent mutation, role changed during the session, campaign opened or closed elsewhere, request resolved elsewhere, placement resolved elsewhere, connectivity loss, safe retry, and an event whose destination was deleted or became unauthorized. Independent regions identify their own stale or unavailable state instead of collapsing the entire dashboard.

## Interaction and layout

- On wide screens, the current campaign or season-idle board owns the broad working field and administrator attention occupies a narrower adjacent rail. Recent activity spans the stable reading field below them.
- On narrow screens, stack current context, administrator attention, and activity in that order. Do not place history ahead of the current route. Team-roster and campaign actions become full-width when required by the established responsive rules.
- Activity remains a semantic list grouped by dated headings. It is not a table squeezed onto a phone or a stack of ornamental cards.
- Every action and navigation target is at least 2.75rem. Keyboard focus remains visible after paging, regional retry, or navigation. New content is announced without moving focus unexpectedly.
- Badge and feed icons are redundant cues. Counts, event kinds, outcomes, and lifecycle states remain legible in text. Reduced-motion users receive the same updates without animation-dependent meaning.
- Long names wrap without hiding the actor, subject, count, or destination. Localized dates, pluralization, and `99+` labels remain meaningful to assistive technology.

## Scope and boundaries

This brief covers the dashboard's active and season-idle attention hierarchy, the administrator attention rail, route-level badges, the club activity taxonomy and rendering, role-shaped visibility, paging, and responsive behavior. It defines production-ready interaction and state behavior but no visual comp, direction contract, or implementation code.

Explicit anti-goals:

- No email, SMS, push notification, browser notification, or third-party delivery.
- No notification inbox, bell menu, Notifications route, unread state, dismissal, snooze, preference center, or mark-all-read action.
- No member-facing unresolved-work badge and no disclosure of pending requests or Draft campaigns to ordinary members.
- No inline request approval or placement editing on the dashboard.
- No evaluation-note or tag-application noise in the club feed.
- No real-time presence, websocket requirement, background polling, urgency animation, red error semantics for ordinary pending work, or color-only status.
- No generic card grid, social feed reactions, comments, avatars as the only identity, decorative sports imagery, ambient shadows, or CSS-level specification.
- No redesign of campaign, evaluation, placement, closeout, Teams, Requests, or member-management workflows; this brief defines their attention entry points and recorded outcomes.

## Constraints and implementation consequences

- Preserve Fieldhouse Wayfinding, the One Route Rule, full mobile route labels, SSR-first Blazor architecture, club-scoped authorization, keyboard and touch access, explicit paging, semantic status language, and partial-region failure handling.
- The current dashboard feed derives note, tag, placement, close, and reopen rows from unrelated tables. The intended taxonomy instead needs durable, append-only activity evidence for lifecycle, structured placement old/new state, join requests, role changes, removals, and voluntary departure. Builders must not synthesize history from mutable current state or continue club-level note/tag noise.
- The current `DashboardActivityEventKind` and DTO assume every event belongs to a campaign. Membership and role events do not. Future contracts must represent family-specific context without fake campaign identifiers or an expanding set of ambiguous nullable fields.
- Event evidence must preserve tenant, event kind, timestamp, stable ordering key, actor, subject, role-shaped visibility, and structured old/new values needed for plain-language rendering. It must remain readable when an actor leaves, a Draft is deleted, a team is archived, or a name later changes, without retaining unrelated private account data.
- Authorization is applied before paging and totals. Filtering administrator-only rows after fetching a page would leak counts, create short pages, and produce inconsistent chronology.
- The pending-request badge and rail link to the Requests section established by the club-setup brief. The unresolved-placement badge and rail use the placement brief's authoritative **Needs placement** definition: eligible, teamless, and currently undecided. Enrollment rows and optional same-season reassignment do not inflate it.
- Navigation counts need a bounded shell-level projection available during SSR and interactive rendering. Count retrieval must not delay or remove the route labels themselves. A failed badge query omits the badge while preserving navigation.
- The current provisional `ClubDashboard.razor` thesis, card cap, Bootstrap composition, and shell without badges are implementation evidence only. The dashboard build issue [#169](https://github.com/eruvalca/Nova/issues/169), club surfaces [#171](https://github.com/eruvalca/Nova/issues/171), campaign loop [#170](https://github.com/eruvalca/Nova/issues/170), and member-management foundation [#179](https://github.com/eruvalca/Nova/issues/179) must consume this brief rather than independently inventing attention behavior.
- Campaign lifecycle foundations [#178](https://github.com/eruvalca/Nova/issues/178) own one-Active enforcement and Draft visibility. Closeout is normative for Closed immutability; an activity event records an authorized mutation but never grants or implies mutation authority.
- Placement activity has no `Unassigned` event in the final product taxonomy. Ordinary members may supersede eligible `Assigned` and `NotSelected` decisions in a later Active campaign; only administrators may supersede a prior-campaign `Withdrawn` decision. In every case the event describes the new Active-campaign decision and preserves the Closed source.

## Decision record

- Keep attention in-app only.
- Make the dashboard the attention home: Active campaign first, administrator rail beside it, and Recent activity below.
- Route members directly to Evaluate while a campaign is Active; preserve the complete campaign route as a secondary path.
- When no campaign is Active, lead with current-season truth, make current teams and rosters browsable, and offer the latest Closed campaign when available.
- Give administrators state-appropriate Draft/open/create actions without embedding campaign management on the dashboard.
- Keep Requests and Place as the canonical resolution surfaces; the dashboard and badges only route into them.
- Put pending-request badges on Club and unresolved-placement badges on Campaigns; hide zero, cap at `99+`, and add no aggregate Dashboard badge or notification bell.
- Keep the administrator rail stable and show **No admin work waiting** when both categories are clear.
- Show all members lifecycle, placement, approved-membership, and role/removal/departure activity; keep Draft and unresolved join-request events administrator-only.
- Record placement activity only for authorized Active-campaign decisions; introduce no post-close unassign event or wording.
- Exclude evaluation notes and tags from the club feed.
- Show 20 events initially, group by day, page older history with an explicit action, and introduce no read/unread or dismissal state.
