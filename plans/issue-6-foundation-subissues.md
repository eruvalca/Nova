# Issue #6 Foundation Sub-Issue Restructure

Restructure GitHub issue #6 into six contributor-ready foundation issues, attach them as formal sub-issues, and replace coarse epic dependencies with precise child-to-epic dependencies. The children own entities, EF configuration, incremental migrations, reusable server-side domain operations, and focused tests; HTTP endpoints, WASM clients, and UI remain in downstream epics #7-#12.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

Use the authenticated GitHub CLI at `C:\Program Files\GitHub CLI\gh.exe` if `gh` is not on the VS Code process `PATH`. Never print authentication tokens. Repository: `eruvalca/Nova`.

## Confirmed Decisions

- Keep #6 as the tracking epic and create six implementation issues beneath it.
- Use incremental migrations, one migration per schema-bearing issue. Existing data is local development/test data only, so destructive cleanup of ambiguous note/tag structures and local database resets are allowed.
- Preserve the existing migration chain; do not regenerate the initial migration.
- Each child owns persistence plus reusable server-side domain operations and invariant enforcement. It does not own HTTP endpoints, WASM clients, or UI.
- Use an application-managed `Guid` concurrency token for campaign participation, regenerated for placement mutations and checked by EF optimistic concurrency.
- Store `ArchivedAt` and FK-less `ArchivedById` explicitly on archivable records.
- Split assignment-scoped notes and campaign tag applications into separate issues.
- Formalize evaluator authorization in a separate issue; an evaluator is any approved club member and is not a new role.
- Label children `mvp` and `enhancement`; leave them unassigned.
- Add ownership/dependency sections to #7-#12 without removing their product scope.
- Remove the coarse #6 blocking relationships to #7 and #8 after precise child dependencies are in place.

## Required Issue Body Shape

Every child issue must contain these sections so a contributor can begin without reconstructing context:

1. `Goal` - one outcome-oriented paragraph.
2. `Parent and downstream work` - link #6, list child prerequisites, list epics unblocked by the issue, and state that endpoints/client/UI are downstream.
3. `Current state` - name the existing entities/configurations/migrations that make the change necessary.
4. `Required design` - state the confirmed schema and service decisions below; do not leave core decisions as “consider” or “TBD”.
5. `Implementation scope` - concrete entities, configurations, DbSets, services, migration, and tests to add or change.
6. `Integrity and authorization rules` - transactional rules, tenant checks, actor rules, and delete behavior.
7. `Acceptance criteria` - observable, binary completion conditions.
8. `Verification` - runnable commands with expected results.
9. `Out of scope` - explicitly exclude HTTP endpoints, WASM clients, Razor UI, and unrelated downstream workflows.
10. `Repository guidance` - link `plans/mvp-product-workflows.md`, `.github/instructions/ef-core-tenancy.instructions.md`, `.github/instructions/csharp-conventions.instructions.md`, and `.github/instructions/testing.instructions.md`; add service-layer guidance where the issue changes services.

For every schema-bearing issue, require:

- A named incremental migration generated with `NovaDbContext`.
- `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext` to report no pending changes.
- Provider-agnostic tests in `Nova.Unit.Tests` using `TenancyTestHarness`.
- PostgreSQL integration coverage in `Nova.Integration.Tests` for the migration, filtered indexes, check constraints, concurrency behavior, and other provider-specific behavior touched by that issue.
- A clean-database migration check through the Aspire-backed integration fixture.
- `dotnet build Nova.slnx` and the relevant focused test classes to pass.

## Child Issue Specifications

### Child A: Campaign Participation Integrity and Optimistic Concurrency

Suggested title: `Add campaign participation integrity and optimistic concurrency`

This issue establishes `PlayerCampaignAssignmentEntity` as the campaign-participation aggregate and should be created first.

Required scope:

- Move `TryoutNumber` (`int?`) from `PlayerEntity` to `PlayerCampaignAssignmentEntity`.
- Add a shared `PlacementOutcome` enum with `Undecided`, `Assigned`, `NotSelected`, and `Withdrawn`; default new participation to `Undecided`.
- Keep nullable `TeamId`; enforce `Assigned` requires a team and `Undecided`, `NotSelected`, and `Withdrawn` require no team.
- Add unique `(CampaignId, PlayerId)` enrollment protection.
- Add a PostgreSQL filtered unique index on `(CampaignId, TryoutNumber)` where the tryout number is non-null.
- Add an application-managed `Guid` concurrency token configured with `IsConcurrencyToken`; placement operations accept the expected token, regenerate it on successful placement mutation, and return a conflict result on `DbUpdateConcurrencyException`.
- Add a reusable server-side placement mutation operation that validates current-club ownership of participation, player, campaign, and optional team in one transaction; validates `Player.GraduationYear >= Team.GraduationYear`; and applies outcome/team atomically.
- Do not attempt to implement campaign Closed or archived-team guards before those models exist; Child B and Child C must extend this operation with those guards.
- Add relationship, duplicate-enrollment, duplicate-tryout-number, outcome/team matrix, eligibility, cross-tenant, and concurrency tests.
- Migration may destructively remove the old player-level tryout-number column because no durable data exists.

Downstream dependencies: blocks #7, #9, #11, and #12. Child B and Child C also depend on it.

### Child B: Archival Lifecycle for Players, Teams, and Tag Definitions

Suggested title: `Add archival lifecycle for players, teams, and tag definitions`

Required scope:

- Add an explicit shared Active/Archived lifecycle representation to `PlayerEntity`, `TeamEntity`, and `PlayerTagEntity`.
- Add `ArchivedAt` and FK-less `ArchivedById` to each entity. Active rows require both fields to be null; Archived rows require both fields to be populated.
- Backfill existing rows as Active; local data may be reset if needed.
- Add reusable administrator-only archive/restore operations for players, teams, and tag definitions.
- Player archive must reject unresolved participation in an Active campaign rather than rewriting outcomes.
- Team archive and graduation-cutoff changes must reject active placements that would become invalid.
- Archived players remain in history and are excluded from future enrollment by downstream creation workflows.
- Archived teams remain valid historical references but cannot receive new placements; extend Child A's placement operation accordingly.
- Archived tag definitions remain readable in history but cannot receive new applications; Child F enforces this in tag-application operations.
- Restoring clears `ArchivedAt` and `ArchivedById`; audit `ModifiedAt`/`ModifiedById` continues to be stamped by the interceptor.
- Add status/metadata consistency, authorization, history-preservation, tenant isolation, cross-tenant write, and migration tests.

Prerequisite: Child A. Downstream dependencies: blocks #7, #8, #10, and #11.

### Child C: Campaign Lifecycle, Close/Reopen Events, and Write Guards

Suggested title: `Add campaign lifecycle persistence and auditable transitions`

Required scope:

- Add `CampaignStatus` with `Active` and `Closed`; replace `IsComplete => EndDate.HasValue` with status-based behavior.
- Treat the existing end date as a planned end date; rename it only if the change can be made consistently without expanding into downstream UI work.
- Add `ClosedAt` and FK-less `ClosedById`. Active campaigns require both null; Closed campaigns require both populated.
- Add a tenant-owned `CampaignLifecycleEventEntity` with campaign FK, club FK, a Closed/Reopened event type, and `BaseEntity` audit fields. Events are append-only through the domain service.
- Add administrator-only reusable close/reopen operations. Close validates every participant has a final outcome and every Assigned participant has an eligible team in the same transaction; reopen clears current closure metadata without deleting prior events or outcomes.
- Extend Child A's placement operation to reject writes to Closed campaigns.
- Require downstream evaluation operations to reject writes to Closed campaigns.
- Do not derive status from existing `EndDate`; there is no durable data, so initialize/reset development campaigns as Active.
- Add lifecycle transition, readiness blocker, event history, authorization, tenancy, concurrency, relationship, and migration tests.

Prerequisites: Child A and Child B. Downstream dependencies: blocks #9, #10, #11, and #12.

### Child D: Reusable Evaluator Authorization Policy

Suggested title: `Formalize reusable club evaluator authorization`

Required scope:

- Add a clearly named evaluator policy constant and registration whose MVP semantics are: authenticated user with an approved club membership/club claim.
- Do not add an Evaluator role or campaign-staff assignment model.
- Preserve administrator-only ownership of roster, teams, campaigns, placement, and closeout operations.
- Require server-side evaluation domain operations to repeat membership/tenant authorization rather than relying only on endpoint policy enforcement.
- Add policy tests for unauthenticated users, users without a club, approved club members, club administrators, and platform administrators where applicable.
- Document that #10 consumes this policy for evaluation endpoints/UI.

This issue has no migration and may proceed independently. Downstream dependency: blocks #10. Child E and Child F depend on it.

### Child E: Assignment-Scoped Evaluation Notes

Suggested title: `Associate evaluation notes with campaign participation`

Required scope:

- Replace the player-only note relationship with a required `PlayerCampaignAssignmentId` relationship; participation supplies player and campaign context.
- Keep `ClubId` and `ITenantOwnedEntity` behavior explicit; keep `BaseEntity.CreatedAt` and FK-less `CreatedById` as note timestamp/authorship.
- Configure the relationship on the dependent and cascade notes when participation is deleted.
- Remove the obsolete player-note navigation/relationship. No legacy/general-note model is needed because there is no durable data; destructive cleanup is allowed.
- Add reusable add/edit/delete note operations. Any approved club member may add notes to an Active campaign. Only the note author or a club administrator may edit/delete while Active. Closed campaigns are read-only.
- Validate assignment and note ownership against the current club in the same operation; use physical deletion for MVP unless repository behavior requires otherwise.
- Add relationship, author/admin authorization, closed-campaign, tenancy, cross-tenant write, cascade, audit, and migration tests.

Prerequisites: Child A, Child C, and Child D. Downstream dependencies: blocks #7, #10, and #12.

### Child F: Explicit Campaign Tag Applications

Suggested title: `Replace implicit player tags with campaign tag applications`

Required scope:

- Keep `PlayerTagEntity` as the club-owned tag definition and consume its lifecycle from Child B.
- Replace the implicit player/tag many-to-many join with an explicit tenant-owned campaign tag-application entity containing its own key, required `PlayerCampaignAssignmentId`, required `PlayerTagId`, `ClubId`, and `BaseEntity` audit fields.
- Add unique `(PlayerCampaignAssignmentId, PlayerTagId)` protection.
- Configure relationships on the dependent; preserve tag definitions and applications in history when definitions are archived.
- Remove the implicit join and obsolete `PlayerEntity.Tags` navigation. No backfill is needed; destructive removal of local-only join data is allowed.
- Add reusable apply/remove operations. Any approved club member may apply an Active definition in an Active campaign. Only the applying user (`CreatedById`) or a club administrator may remove it. Physical deletion is the MVP behavior.
- Reject duplicate applications, archived definitions, Closed campaigns, and all cross-tenant references.
- Add relationship, uniqueness, author/admin removal, archive/history, closed-campaign, tenancy, cross-tenant write, audit, and migration tests.

Prerequisites: Child A, Child B, Child C, and Child D. Downstream dependencies: blocks #7, #10, and #12.

## Phase 1: Draft and Validate the Six Issue Bodies

Status: Complete

Suggested executor: orchestrator

- [x] Fetch #6-#13 immediately before drafting and record their current bodies, database IDs, labels, assignees, sub-issues, and dependency relationships so concurrent backlog edits are not overwritten.
- [x] Draft all six bodies from the Required Issue Body Shape and Child Issue Specifications above.
- [x] Give each body explicit file anchors to current entities in `Nova/Entities/`, configurations in `Nova/Data/Configurations/`, migrations in `Nova/Data/Migrations/`, tenancy tests in `Nova.Unit.Tests/Data/TenancyTests.cs`, and PostgreSQL tests in `Nova.Integration.Tests/Data/PostgresTenancyTests.cs`.
- [x] Name expected new/changed test classes and give focused MTP commands using `dotnet test --project ... --filter-class "*Name"`.
- [x] Check every acceptance criterion is binary and every verification step is autonomous.
- [x] Check no issue contains endpoint, WASM-client, Razor-component, or UX implementation work.
- [x] Check cross-child ownership is unambiguous, especially later extensions to placement write guards.

### Phase 1 Verification Plan

- Review all six drafts against the ten Required Issue Body Shape sections; all sections must be present.
- Search drafts for `TBD`, `consider`, `maybe`, and vague unchecked decisions; expect no unresolved core design decisions.
- Build a traceability table from every unchecked item currently in #6 to exactly one child issue or a named downstream epic; expect no orphaned or multiply owned requirement.

### Phase 1 Summary

Captured the live #6-#13 bodies, IDs, labels, assignments, sub-issues, and dependencies before mutation. Drafted six bodies with explicit entity/configuration/service/test anchors, binary acceptance criteria, autonomous MTP/EF/build checks, and consistent exclusion of endpoint, WASM, Razor, and UX work. A requirement trace confirmed every #6 item belongs to one child or a named downstream epic.

## Phase 2: Create and Attach the Foundation Issues

Status: Complete

Suggested executor: orchestrator

- [x] Create Child A through Child F with `gh issue create --repo eruvalca/Nova --label mvp --label enhancement --body-file <draft>` and leave assignees empty.
- [x] Capture each created issue number, API database ID, node ID, and URL in this plan's Phase Summary.
- [x] Attach every new issue to #6 with the GitHub sub-issues API using the child's API database ID.
- [x] Order formal sub-issues A, B, C, D, E, F unless GitHub's ordering API requires D to be placed earlier to reflect parallel execution; record the final order.
- [x] Add child-to-child blocked-by relationships: B blocked by A; C blocked by A and B; E blocked by A, C, and D; F blocked by A, B, C, and D. D has no child prerequisite.
- [x] Re-fetch each issue after creation and ensure title, body, labels, assignment, parent, and dependencies match the drafts.

### Phase 2 Verification Plan

- `gh api repos/eruvalca/Nova/issues/6/sub_issues --paginate` returns exactly the six new issue numbers in the intended order.
- Each child reports `mvp` and `enhancement`, no assignee, and one parent (#6).
- Each child's `dependencies/blocked_by` endpoint matches the child dependency matrix above.
- No duplicate issue with the same goal exists outside the #6 sub-issue list.

### Phase 2 Summary

Created and attached six unassigned `mvp` + `enhancement` issues in logical A-F order:

- A: [#14](https://github.com/eruvalca/Nova/issues/14), database ID `4925672290`, node ID `I_kwDOSz2VcM8AAAABJZfLYg`
- B: [#19](https://github.com/eruvalca/Nova/issues/19), database ID `4925672297`, node ID `I_kwDOSz2VcM8AAAABJZfLaQ`
- C: [#17](https://github.com/eruvalca/Nova/issues/17), database ID `4925672293`, node ID `I_kwDOSz2VcM8AAAABJZfLZQ`
- D: [#18](https://github.com/eruvalca/Nova/issues/18), database ID `4925672295`, node ID `I_kwDOSz2VcM8AAAABJZfLZw`
- E: [#15](https://github.com/eruvalca/Nova/issues/15), database ID `4925672292`, node ID `I_kwDOSz2VcM8AAAABJZfLZA`
- F: [#16](https://github.com/eruvalca/Nova/issues/16), database ID `4925672291`, node ID `I_kwDOSz2VcM8AAAABJZfLYw`

Verified the formal parent, order, labels, empty assignments, required issue-body sections, and child dependency DAG through GitHub REST endpoints.

## Phase 3: Rewrite #6 as the Tracking Epic

Status: Complete

Suggested executor: orchestrator

- [x] Preserve #6's goal, integrity rules, product reference, `epic`/`mvp` labels, and current assignee.
- [x] Replace its implementation checklist with a linked checklist of the six formal children and a concise statement of what each owns.
- [x] Add the confirmed decisions: no durable data/backfill requirement, incremental migrations with destructive local cleanup allowed, application-managed GUID concurrency, explicit archive actors, evaluator-as-club-member semantics, and no endpoint/client/UI work in the foundation children.
- [x] Add the child dependency order and explain that evaluator authorization can proceed independently.
- [x] Add a downstream ownership table for #7-#12.
- [x] Define epic completion as all six children closed with migrations/tests verified; do not duplicate child acceptance criteria in the parent.
- [x] Preserve any concurrent edits discovered in Phase 1 unless they conflict with confirmed decisions; reconcile rather than overwrite them.

### Phase 3 Verification Plan

- Fetch #6 and confirm its formal sub-issue summary reports six total children.
- Confirm every original #6 scope/backlog item maps to a linked child or explicitly named downstream epic.
- Confirm #6 remains open, labeled `epic` and `mvp`, and assigned as before.

### Phase 3 Summary

Rewrote #6 as a tracking epic with a linked six-child checklist, dependency order, confirmed design decisions, preserved integrity rules, downstream ownership table, and completion criteria. #6 remains open, labeled `epic` + `mvp`, assigned to `eruvalca`, and reports six incomplete formal children.

## Phase 4: Update Downstream Epic Ownership and Dependencies

Status: Complete

Suggested executor: orchestrator

- [x] Append a `Foundation dependencies and remaining ownership` section to #7 through #12; preserve all existing goals, scope, acceptance criteria, backlog additions, labels, and assignees.
- [x] In #7, state that A/B/E/F provide participation, archival, and history persistence; #7 still owns roster contracts, endpoints, clients, UI, creation/edit/archive workflows, late-player enrollment orchestration, and player history presentation.
- [x] In #8, state that A/B provide placement integrity and team lifecycle; #8 still owns team contracts, endpoints, clients, UI, create/edit/archive workflows, and affected-placement presentation.
- [x] In #9, state that A/C provide participation and campaign status; #9 still owns season/campaign contracts, creation transaction, enrollment orchestration, endpoints, clients, and UI.
- [x] In #10, state that B/C/D/E/F provide tag lifecycle, campaign guards, evaluator policy, notes, and tag operations; #10 still owns workspace queries, contracts, endpoints, clients, filters, responsive UI, and interaction/accessibility behavior.
- [x] In #11, state that A/B/C provide outcome, concurrency, archive, eligibility, and campaign guards; #11 still owns placement contracts, endpoints, clients, table UI, refresh/retry UX, and summary queries.
- [x] In #12, state that A/C/E/F provide readiness state, lifecycle transitions, and closed-write guards; #12 still owns closeout contracts, endpoints, clients, blocker presentation, history UI, and reopen UX.
- [x] Add precise child-to-epic dependencies: A blocks #7/#9/#11/#12; B blocks #7/#8/#10/#11; C blocks #9/#10/#11/#12; D blocks #10; E blocks #7/#10/#12; F blocks #7/#10/#12.
- [x] Only after all precise dependencies are verified, remove #6 itself as a blocker of #7 and #8.
- [x] Do not add #6 as a coarse blocker of #9-#12.

### Phase 4 Verification Plan

- Fetch `dependencies/blocked_by` for #7-#12 and compare it to the exact matrix above.
- Fetch `dependencies/blocking` for #6; expect no direct downstream blockers after replacement.
- Fetch #7-#12 bodies and confirm each has one ownership section without altering its original scope or acceptance criteria.
- Confirm no downstream epic is blocked by an unrelated foundation child.

### Phase 4 Summary

Appended one ownership section to each of #7-#12 and added the precise child blockers. Removed only #6's coarse links to #7/#8. Preserved the pre-existing product sequence discovered through the full dependency endpoints: #9 also depends on #7/#8, #10 on #9, #11 on #8/#9, and #12 on #10/#11. #6 now directly blocks no issue.

## Phase 5: Audit the Result as a New Contributor

Status: Complete

Suggested executor: a fresh read-only sub-agent with no prior conversation context

- [x] Ask a fresh agent to select each child in turn and report the first implementation action, expected files, dependencies, acceptance criteria, and verification commands using only the issue body and linked repository guidance.
- [x] Resolve every ambiguity the fresh-agent audit finds by editing the appropriate child, parent, or downstream ownership section.
- [x] Verify each #6 requirement has exactly one implementation owner and each downstream epic clearly distinguishes supplied foundation from remaining feature work.
- [x] Verify issue labels and assignment state remain correct after edits.
- [x] Record final issue URLs, hierarchy, dependency matrix, and any API limitations/workarounds in the Phase Summary.

### Phase 5 Verification Plan

- Fresh-agent audit returns no blocking question for any child before implementation can begin.
- `gh api repos/eruvalca/Nova/issues/6/sub_issues --paginate` reports six children and zero duplicates.
- GitHub dependency endpoints match the child and downstream matrices in this plan.
- `git diff -- plans/issue-6-foundation-subissues.md` contains only execution-status updates and summaries made while carrying out this plan; no application source files are changed by backlog restructuring.

### Phase 5 Summary

The first fresh-agent audit requested four clarifications: exact Npgsql filtered-index syntax in #14, explicit Undecided/cutoff blockers in #19, close-time team eligibility/archive revalidation in #17, and destructive orphan-note handling in #15. Applied all four and added nonzero-test-discovery checks. A second fresh-agent audit returned PASS for all six children with no blocking ambiguities, ownership overlaps, migration gaps, or endpoint/client/UI scope creep. GitHub MCP handled issue creation/update/sub-issue attachment; authenticated `gh api` handled dependency writes because the available MCP surface did not expose them.

## Final Recap

Restructured #6 into six ordered, contributor-ready formal sub-issues (#14, #19, #17, #18, #15, #16), each with explicit design decisions, implementation boundaries, binary acceptance criteria, autonomous verification, labels, and dependencies. Rewrote #6 as their tracking epic, clarified remaining ownership in #7-#12, replaced coarse parent blocking with precise child blockers, preserved existing epic sequencing, and passed both metadata and independent contributor-readiness audits.

## Deployment Plan

No application deployment is required. This work changed GitHub issue bodies, labels/assignments on newly created issues, formal sub-issue relationships, and blocked-by relationships only. Contributors can begin with unblocked #14 and #18; subsequent work should follow GitHub's dependency graph. No project-board or milestone update was required because the repository currently has no milestones and no project-board requirement was identified.
