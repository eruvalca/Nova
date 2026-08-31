# Club setup

Status: Confirmed

Issues: [#164](https://github.com/eruvalca/Nova/issues/164), child of [#163](https://github.com/eruvalca/Nova/issues/163)

Visitor mode: Operate

## Job and audience

Nova must give an authenticated person with no club one clear route into real club work. A club creator is usually an administrator who knows their organization but is new to Nova; a joiner is a coach or evaluator who knows which club they belong to and wants access without setup ceremony. Both may be working from a phone and neither should have to understand Nova's full campaign model before entering.

The creator's activation moment is arriving at the dashboard with a real club and first season ready for work. The joiner's activation moment is approval becoming access: they enter the club dashboard directly, with no second onboarding wizard.

## Outcome and proof

- A creator establishes exactly one club and its first season through a short, honest sequence. Teams are explicitly deferred; the completion state points toward the dashboard and the next operational task rather than extending onboarding.
- A joiner can find a club by recognizable identity and location, place one join request, understand its status, cancel it, recover from rejection, and enter the dashboard after approval.
- Club members always see one coherent Club area. Role-filtered navigation exposes Overview and Teams to members and adds Seasons, Members, Requests, Tags, and Crest for club administrators.
- The design proves its product specificity through real club identity, season timing, membership state, and request attention—not generic setup cards, invented testimonials, or tutorial content.

## Selected direction

The experience uses Fieldhouse Wayfinding as a literal route through club entry and stewardship. A stable heading names the current job; route markers show the creator's progress; broad flat boards hold forms, directories, and queues. Teal identifies the next action or current route, amber identifies unresolved requests, and every state is also expressed in words.

The creator follows three stops: **Club → First season → Review**. The joiner follows a stateful route rather than a wizard: **Search → Requested → Approved** (or **Rejected / Cancelled → Search**). Once affiliated, the Club area mirrors the account Manage pattern: a persistent, fully labeled sub-navigation frames one working hall at a time.

## Creator onboarding

### Entry choice

- The unaffiliated landing state presents two equally legible routes: **Create a club** and **Join a club**. It briefly explains the consequence of each choice instead of displaying both full workflows at once.
- Choosing a route changes the working surface while preserving an obvious way back to the choice until a join request is placed or club creation is committed.
- The page does not teach campaigns, placements, tags, or team management. It promises only the outcome delivered here: a club home and first season.

### Stop 1 — Club

- Collect the minimum recognizable identity: club name and location. A crest is optional and can be added or changed later in the Club area; image selection, crop, validation, and retry must not block progress when the user chooses to defer it.
- Explain that the creator becomes a club administrator and that Nova supports one club membership per user.
- Validate inline, preserve entered values when moving backward, and keep uploaded-image recovery local to this stop.

### Stop 2 — First season

- Collect a season name and date window using direct club language and useful examples. Explain the model in one sentence: the season groups the club's campaigns and team snapshots for this period.
- Do not ask for teams. State that durable teams can be created later from the Club area or inline when a campaign is drafted.
- Reject an invalid or overlapping date window in context, with the field and correction path identified in words.

### Stop 3 — Review and create

- Present one concise review sheet with club identity, location, optional crest, and first-season dates. Each group has an **Edit** action that returns to the relevant stop without losing other input.
- The primary action names the full commitment: **Create club and season**. Club and season are one user-visible, atomic commitment: either both are created or the creator remains on the preserved review with a retry path. Submission exposes a busy state and prevents duplicate commits.
- Crest processing is independent of that core commitment. A selected crest that cannot be saved must not strand or roll back an otherwise valid club and season; the dashboard arrival message identifies the crest problem and links to retry it in the Club area.
- On success, refresh the user's club membership claims and take them to the dashboard. A compact arrival message confirms the club and season; it does not insert another celebratory interstitial.

## Joiner journey

### Search and request

- Search accepts club name, city, or state and explains that these are the matching fields. Results show the club's name, location, and crest when present—enough to distinguish real clubs without exposing private membership data.
- The first-use state gives an example search. No-results distinguishes an empty query from a completed search and offers a direct reset. Large result sets are bounded or paged and never silently truncated.
- A result has one explicit **Request to join** action. Nova allows one pending request per person and no invitation, multi-club, or free-form application flow.

### Pending

- After submission, the search results yield to a dedicated status board naming the club, location, request date, and **Pending** state. Copy explains that a club administrator must act and that Nova does not send email notifications.
- The joiner may cancel the request. Cancellation uses an inline confirmation that names the club and consequence, then returns to search with feedback; it does not preserve a misleading pending state.
- Returning users see the same status board immediately. Loading, stale-session, and retry states must never briefly imply approval or rejection.

### Outcome

- Approval refreshes membership claims and routes the member directly to the dashboard. There is no continuation wizard or redundant “join club” confirmation after an administrator has approved them.
- Rejection is described neutrally as **Not approved**. It names the club, offers **Search for another club**, and does not speculate about the administrator's reason.
- If approval cannot complete because the person already belongs to a club or the request became stale, the joiner sees a durable conflict state and a safe route to the club they now belong to or back to search, as appropriate.

## Club area organization

The Club area is one destination with a Manage-style sub-navigation and one working hall. Routes are role-filtered, not displayed as disabled entitlements.

- **Overview** (all members): club identity, current season, and concise orientation to the club's active work.
- **Teams** (all members): the durable team directory. Members can inspect teams; administrator-only mutations remain absent for members.
- **Seasons** (administrators): a directory led by the current season, followed by archived past seasons. Opening a season shows its campaigns and a teams snapshot. **Start next season** is an explicit administrator action.
- **Members** (administrators): membership directory with promote/demote ClubAdmin, remove member, and leave-club paths. Sole-admin and last-member guards are explained before an action, not discovered after submission.
- **Requests** (administrators): the canonical pending join-request queue. The dashboard attention rail shows a count and links here; it does not duplicate approval controls.
- **Tags** (administrators): rename/archive housekeeping for club-wide evaluation tags; inline creation during evaluation remains outside this surface.
- **Crest** (administrators): add, replace, crop, retry, and remove the club crest with a recognizable preview.

The sub-navigation keeps full labels and a visible active destination. On narrow screens it becomes a compact directory control or scroll-safe route list without hiding reachable sections. When a person's role changes, both navigation and working content reconcile together; authorization remains enforced by the destination, not only by hidden links.

## Season advancement

- **Start next season** originates in the Seasons directory, never from an automatic date transition. The action collects the next season's name and date window, previews the transition, and requires explicit confirmation.
- Any Draft or Active campaign in the current season blocks advancement and links the administrator to the work that must be closed. When every campaign is Closed—or the season has no campaigns—advancement is allowed.
- Starting before the current season's recorded end date produces a clear warning in the confirmation step, but dates do not impose an absolute block.
- Completion moves the previous season into the archive and makes the new season lead the directory. A new season resets player eligibility but does not clone roster placements or require team creation.
- Date windows may inform warnings and defaults, but they never advance the club without an administrator's action.

## Join-request approval

- Requests are ordered oldest first by default so waiting people are not buried. Typical rows show requester name, request date, and clearly labeled **Approve** and **Reject** actions; a zero state says there is nothing waiting.
- Approval is immediate: it disables the row actions while working, updates the queue in place, and provides strong success feedback. Member removal supplies the later reversal path.
- Rejection first expands an inline confirmation naming the requester and consequence; the administrator can confirm or return to the unchanged row. It does not use a detached modal or require a rejection reason.
- Approval and rejection refresh the pending count. A mutation failure leaves the row visible with its state and retry path intact.
- Approval adds the person to the club and invalidates stale membership claims. Rejection changes only the request outcome; it does not create a membership.
- The dashboard attention rail is a pointer into this queue. It may show the pending count and oldest wait, but all decisions happen here.

## States and realistic ranges

- Creator form: pristine, invalid fields, crest processing, crest deferred, submitting, recoverable failure, duplicate/stale submission, and success.
- Club search: first use, typing, searching, no results, one result, many/paged results, failed search, and conflicting existing membership.
- Join request: creating, pending, cancelling, cancelled, approved, rejected, stale/conflict, and claim-refresh recovery.
- Requests queue: zero, one, many, mutation in progress, row-level success, row-level failure, and a request made stale by another administrator.
- Club directory: member versus administrator routes, no past seasons, many archived seasons, no teams, many teams, no crest, and sole-admin safeguards.

## Interaction and layout

- Desktop uses a broad working field with a stable local directory; mobile preserves route identity and uses full-width actions where space is constrained. Search results, member rows, request rows, and season entries collapse as directories rather than stacks of decorative cards.
- Every interactive target is at least 2.75rem. Progress and status never depend on color; focus remains visible; changing steps moves focus to the new heading; async results announce meaningful updates.
- Destructive or access-changing actions name their subject and consequence. Feedback appears beside the action that caused it and survives any data refresh needed to show the new state.
- Motion is optional and restrained. Reduced-motion users receive the same orientation and state changes without animation.

## Scope and boundaries

This brief covers the creator onboarding flow, joiner search/request/status/outcome flow, the organized Club area, explicit season advancement, and administrator request approval. It defines production-ready interaction and state behavior for responsive web, but no visual comp or implementation code.

Explicit anti-goals:

- No team creation during onboarding.
- No invitations, email-dependent steps, parent/guardian accounts, or player self-registration.
- No CSV intake in the Club area; intake belongs to Players.
- No automatic season advancement and no simultaneous active seasons.
- No dashboard-based approval controls.
- No separate generic member-detail and administrator-settings products.
- No generic card grid, sports decoration, invented proof, or CSS-level direction in this brief.

## Constraints and implementation consequences

- Preserve the established Fieldhouse Wayfinding system, SSR-first architecture, claim-gated onboarding, club-scoped authorization, semantic status language, keyboard access, and touch-safe responsive behavior.
- Existing club screens are behavioral evidence only. Their Bootstrap card composition is not a design constraint.
- Foundation work for first-class seasons (#176) and complete member management (#179) owns missing backend capabilities. Builders must not silently omit briefed states because current services are incomplete.
- Membership-claim refresh is part of both creation and approval completion; without it, onboarding gates can loop or show stale role-filtered navigation.

## Decision record

- Use one Club area with Manage-style, role-filtered sub-navigation; members see Overview and Teams, while administrators also see Seasons, Members, Requests, Tags, and Crest.
- Advance seasons only through an explicit administrator action in the Seasons directory.
- Keep Requests as the canonical approval surface; the dashboard attention rail links to it without duplicating decisions.
- Commit club and first-season creation atomically from the creator's perspective; optional crest recovery remains independent.
- Block season advancement while any current-season campaign is Draft or Active; an early date transition warns but does not block.
- Approve requests immediately with strong feedback; use inline confirmation for rejection and joiner cancellation.
