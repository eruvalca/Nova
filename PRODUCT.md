# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Nova serves club administrators and coaches/evaluators as equal primary users during youth sports tryouts.

- Club administrators organize clubs and campaigns, manage rosters and staff access, oversee progress, and decide final placements.
- Coaches and evaluators assess players, record notes and tags, and contribute evidence for placement decisions.

Both roles need to move through an active tryout together without losing context between roster management, evaluation, and placement.

## Product Purpose

Nova gives club staff one shared workflow for running an active tryout from roster enrollment through final team placement. It exists to replace fragmented coordination across spreadsheets, forms, and disconnected conversations with a clear, role-aware operating system for the campaign.

Success means both administrators and evaluators can understand the current state of a tryout, complete the work appropriate to their role, and move players toward a defensible placement without reconstructing context elsewhere.

## Positioning

Nova's durable advantage is a single roster-to-placement workflow with role-aware collaboration. Administrators and evaluators work from the same campaign state while retaining distinct responsibilities and permissions.

## Operating Context

The defining usage moment is a live tryout campaign:

1. Create the club and campaign.
2. Enroll players into the campaign roster.
3. Invite staff and evaluate participants with notes and tags.
4. Review the shared evidence and resolve team placements.
5. Close the campaign.

The interface must support quick scanning and confident action during active tryouts as well as deliberate review between sessions.

## Account Experience

The account experience is the utility layer that admits club staff into Nova's tryout operating system. Its real task is narrow and operational: get the right person into the club workflow quickly and keep their access correct.

1. **Register** — create a Nova club-staff account from the registration page and confirm the email before entering.
2. **Sign in** — authenticate with a local account (or an external identity provider where configured) and land in the user's active work.
3. **Recover access** — request a password reset, resend the email confirmation, and recover authentication with two-factor codes, recovery codes, or passkeys.
4. **Manage profile** — maintain email, password, external logins, two-factor authentication, passkeys, profile photo, and personal data from one manage center.

Because Nova's users are club administrators, coaches, and evaluators rather than end consumers, these surfaces favor fast, familiar identity flows over marketing-grade sign-in pages.

## Capabilities and Constraints

- Club-based multi-tenancy keeps each club's data isolated.
- Role-based access controls what administrators, coaches, and evaluators can see and do.
- Current product areas include clubs, campaigns, players, teams, staff invitations, evaluations, notes, tags, placements, campaign closeout, and account/profile management.
- The product is an SSR-first .NET Blazor web application with interactive behavior added only where the workflow requires it.
- "Campaign," "roster," "evaluation," "placement," "club administrator," "coach," and "evaluator" are established product terms.
- Email delivery and external identity providers are not current product capabilities.

## Commitments for the Account Experience Redesign

- **Zero functional change.** The account experience redesign restyles and re-chromes only; every endpoint, form, cookie, redirect, validation rule, and security behavior stays exactly as implemented. No account behavior changes.
- **Identity pages stay static SSR in `Nova`.** The authentication and account-management pages remain server-rendered static SSR inside the `Nova` host, with no interactive render mode added and no client-side state introduced.
- **Kelp-forest world fixed.** The kelp-forest color world remains the binding identity constraint. The account redesign deals with composition and structure only, not visual identity.
- **Vestigial screens restyled, not removed.** Screens that no longer serve a mainstream path (personal data, reset authenticator, recovery-code warnings, and similar) keep their behaviors and get styled to match; nothing is deleted or repurposed.
- **Onboarding gates untouched.** Registration and email-confirmation gates keep their current sequence and enforcement; the redesign does not change when users are admitted or what they must do first.

## Brand Commitments

- The product name is Nova.
- Product copy must be direct, factual, and operational rather than promotional or inflated.
- The recently established kelp-forest color palette remains a binding identity constraint for future design work.

## Evidence on Hand

- The implemented dashboard, campaign workspace, player, team, club, evaluation, placement, and closeout flows are the authoritative evidence of current product behavior.
- The redesigned landing page and its feature components contain repository-verifiable product copy and an explicitly illustrative product preview.
- The repository supports claims about role-based access and club data isolation.
- No verified customers, testimonials, usage metrics, pricing, benchmarks, press, or third-party security certifications are currently available. Future work must not fabricate them.

## Product Principles

1. Keep administrators and evaluators in one shared campaign reality.
2. Make the next appropriate action obvious without obscuring the overall tryout state.
3. Preserve evidence and context from enrollment through placement.
4. Match authority to role while making collaboration legible.
5. Prefer factual clarity over unsupported claims or ornamental complexity.

## Accessibility & Inclusion

Nova must provide a responsive, keyboard-accessible web experience with semantic structure, visible focus, sufficient color contrast, and interfaces that do not rely on color alone to communicate state.
