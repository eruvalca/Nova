# Club setup

Status: draft for final product-owner confirmation  
Visitor mode: Operate  
Scope: creator onboarding, joiner entry, and the authenticated club area

## Job and audience

- Club creators arrive authenticated, with their profile photo complete but no club, and need to establish a usable club context quickly. They may be experienced club staff or new to Nova; the flow should be short, direct, and forgiving without becoming a tutorial.
- Prospective joiners arrive in the same no-club state. They need to find the right club, understand that membership is request-based, see what happens next, and return to a clear status while they wait.
- Club members and admins operate inside one coherent club area. Members need orientation and access to the shared team context. Admins need the full set of club-management surfaces and a clear place to resolve attention items.

## Outcome and proof

- A creator succeeds when a club has been created with its required identity information and crest, a first season has been named with its date window, and the creator reaches the dashboard ready to begin work. Teams are deliberately not part of onboarding.
- A joiner succeeds when a request is submitted to the intended club, its pending status is unambiguous and cancellable, and approval takes them directly to the dashboard. A rejection returns them to club search without trapping them in a dead end.
- The proof is operational: a visible short route, a reviewable club/season summary, explicit request states, readable status words, and a single next action in every terminal state. No email dependency is assumed.

## Selected direction

Use the established **Fieldhouse Wayfinding** world as a calm operational setup desk: a visible route through required decisions, flat paper-white working boards, precise hairlines, ink-dark working type, and restrained semantic state colors. The experience should feel like staff moving from an entry sign to a prepared club board, not like completing a generic SaaS profile form.

The creator journey uses a three-stop route:

1. **Club identity** — enter club name, city, and state; upload and crop the required club crest. Explain that the crest becomes the club’s shared navigation and club-page identity. Do not allow completion without a valid crest.
2. **First season** — enter the season name and date window. Do not ask for teams, players, campaign setup, or member administration here.
3. **Ready** — show a compact summary of the club, crest, and first season, then send the creator to the dashboard.

The joiner journey is a direct search-to-status route: find a club by name, city, or state; review the matching club identity; request to join; see the pending request; cancel or return later; and, once approved, continue directly to the dashboard. No invitation system or joiner onboarding wizard is introduced.

The authenticated club area mirrors the established Manage-style sub-navigation pattern. It is one club home with role-filtered destinations, not separate disconnected admin and member products. The overview is the orientation surface; the remaining destinations are purposeful directories or work queues rather than a generic grid of summary cards.

## Scope and boundaries

In scope:

- Creator onboarding through club + first season, including required crest upload, crop, replacement, validation, review, and completion.
- Joiner club search, result review, request submission, pending status, cancellation, rejection, approval, and direct dashboard landing.
- Club-area overview, seasons directory, teams directory, members, join requests, tag housekeeping, and crest management.
- Role-filtered navigation and admin-only settings/actions. Members see the appropriate subset: Overview and Teams; admins see the full club area.
- Dashboard attention entry that links to the canonical Requests section.

Out of scope:

- Teams during onboarding; teams are durable club objects created later at campaign Draft or from the club area.
- Invitations, email notifications, parent/guardian surfaces, player self-registration, or CSV intake in the club area.
- Campaign workflow, evaluation, placement, closeout, and backend implementation work owned by the related Phase B/C issues.
- A multi-club switcher; each user belongs to at most one club.

## States and ranges

Creator states include initial entry, loading, validation error, crest upload/crop error, incomplete step, review, successful completion, and recoverable server failure. The crest is required, but the creator can replace it before continuing; previously entered fields remain intact when correcting the image or moving between steps.

Joiner states include:

- No query yet: explain what can be searched and what club identity information will be shown.
- Results: show club name and location with one clear **Request to join** action per result.
- No results: suggest a broader name/location search and preserve the search field.
- Request confirmation: make the selected club and the request-based approval model explicit.
- Pending: show the selected club, request date, current status, cancel action, and a way to check again.
- Rejected: explain the outcome without blame and offer **Search for another club**.
- Approved: state the approval in words and offer one action to continue to the dashboard.
- Failure or stale state: explain what could not be confirmed and offer retry; never imply approval from a failed refresh.

Club-area states include first-season/current-season, archived past seasons, no teams, no members beyond the current user, no pending requests, empty tags, crest missing or present, loading, permission denial, and mutation success/failure. Empty states name what will live there, why it matters, and the next permitted action.

## Interaction and layout

- The setup route keeps progress visible and labels every stop. Back navigation is safe; completed values persist while correcting later steps. The final review is the last point before creating the club and first season.
- Required crest work is real functionality in the identity step: choose, preview, crop, replace, and receive field-level errors. The requirement is explained at the moment it matters, not hidden behind a later settings page.
- Joiner search supports keyboard submission and touch-sized controls. Results remain readable at narrow widths; the status surface makes cancellation and re-checking distinct actions.
- The club area begins with a strong club identity/orientation region and the current season context. The Manage-style sub-navigation keeps the route legible across desktop and mobile, preserving full labels and reachable destinations.
- Admin navigation order: Overview, Seasons, Teams, Members, Requests, Tags, Crest. Member navigation order: Overview, Teams. Requests is the canonical approval queue; dashboard attention links into it but does not duplicate its controls.
- The Seasons directory leads with the current season, archives past seasons, and opens each season to its campaigns and team snapshot. Starting the next season is an explicit admin action with a review/confirmation step; date windows never advance a season silently.
- Members management includes promote/demote ClubAdmin, remove member, and leave club, with guardrails for the final member or sole admin. Tags and crest management are housekeeping surfaces inside the same club area.
- Boards stay flat and bounded, with hairline separation, compact operational density, semantic Bootstrap color roles, visible focus, readable status labels, and touch targets of at least 2.75rem. State is never conveyed by color alone.
- Responsive collapse is content-driven: the navigation remains complete, tables/directories scroll when necessary, and primary actions stay reachable without truncation or hidden required steps.

## Constraints and resolved decisions

- Product truth comes from `PRODUCT.md` and `DESIGN.md`; this surface inherits Fieldhouse Wayfinding and operates in the established world. Existing club screens are evidence only, not visual constraints.
- The flow is web-first and must support keyboard, mobile touch use, visible focus, clear errors, and reduced motion. No external email delivery is available, so join-request status and approval are in-app.
- Implement the surface in `Nova.UI` using the repository’s feature organization and SSR-first conventions when this brief is built. Keep club authorization and membership boundaries explicit.
- Resolved: one coherent club area with role-filtered sub-navigation; explicit admin **Start next season**; Requests section as the canonical join-request home with dashboard attention links.
- Builders must not invent invitation flows, onboarding teams, automatic season advancement, a second club area, or a generic dashboard-style card wall for this surface.

