# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Nova serves two equal primary audiences during club tryouts and related roster decisions:

- Club administrators organize clubs, seasons, campaigns, player pools, teams, memberships, and final placements.
- Coaches and evaluators collaborate on player observations and evaluation decisions within the workflows established by the club.

The product originated around soccer clubs, but its terminology, workflows, and implementation should remain adaptable to other club sports.

## Product Purpose

Nova gives club-sports staff one shared place to run tryouts: enroll players, coordinate evaluators, record observations, resolve team placements, and close out a campaign with a clear roster. It exists to replace fragmented spreadsheets and handoffs with collaborative evaluation and a unified workflow.

Success means administrators and evaluators can move from player intake through evaluation and placement without losing shared context, duplicating records, or reconciling disconnected tools.

## Positioning

Nova's defining advantage over spreadsheets is the combination of collaborative player evaluation and a unified end-to-end workflow. Evaluations, roster context, placement decisions, and campaign status remain connected rather than being distributed across separate files and informal handoffs.

## Operating Context

- A club is the primary working boundary for its members and data.
- Tryout work is organized into seasons and campaigns.
- Administrators establish the club context, roster, teams, campaign, and permissions.
- Coaches and evaluators review participating players, record observations, and apply evaluation tags.
- Club staff resolve team placements and close a campaign when decisions are complete.
- People may create a club or request to join an existing club before entering its workflows.

## Capabilities and Constraints

- The current application includes account management, club creation and membership, club-scoped roles, player and team management, campaigns, evaluation notes and tags, placements, campaign closeout, and profile or club imagery.
- Authorization and data access are scoped by club membership and role. Club administrators have additional management capabilities; authenticated club members can participate in permitted evaluation workflows.
- Soccer is the initial domain and source of examples, but future features should avoid unnecessary soccer-only assumptions so Nova can support other club sports.
- Nova is a work in progress. Its feature set, workflows, terminology, and product decisions may change substantially; incomplete implementation must not be treated as a durable requirement.
- Identity email delivery is currently a no-op, confirmed accounts are not required, and no third-party login providers are registered.

## Brand Commitments

- The product name is **Nova**.
- Product language should be direct, operational, and grounded in real club work. It must not invent customers, testimonials, performance claims, or other proof that is not available.

## Evidence on Hand

- The public landing page and its components contain the clearest current product narrative: `Nova/Components/Pages/Landing.razor` and `Nova.UI/Features/Landing/Components/`.
- `Nova/Components/Layout/NavMenu.razor` establishes the current primary navigation vocabulary: Dashboard, club, Campaigns, Players, Teams, and account management.
- Some authentication and account-management screens have received design-backed work.
- Most authenticated product surfaces have not received design-backed work. Their current presentation should be treated as implementation evidence, not as an approved design direction.
- The repository contains no confirmed customer stories, testimonials, pricing, benchmarks, or market proof; future work must not fabricate them.

## Product Principles

1. **Keep the workflow connected.** Preserve shared context from player enrollment through evaluation, placement, and closeout.
2. **Make collaboration first-class.** Administrators and evaluators are equal primary users whose work should combine cleanly without spreadsheet reconciliation.
3. **Respect club boundaries.** Keep permissions, roles, and data clearly scoped to the correct club.
4. **Start with soccer, generalize deliberately.** Use the original soccer context without embedding avoidable assumptions that prevent adoption by other club sports.
5. **Treat unfinished work as changeable.** Build from confirmed product truth and real user needs, not accidental constraints in incomplete screens.

## Accessibility & Inclusion

Nova should support administrators, coaches, and evaluators working across desktop and mobile web contexts, including time-sensitive on-field use. Interfaces should preserve semantic structure, keyboard access, visible focus, clear status and error communication, sufficient contrast, and touch-friendly interaction.
