# PR 37 Review Comments Round 3

Review and resolve the latest inline review comments on PR #37 with code fixes, targeted tests, and thread resolution.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

## Phase 1: Classify latest comments

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [x] Fetch unresolved review threads from PR #37.
- [x] Judge each comment as valid/invalid.
- [x] Define a fix list for valid findings.

### Verification Plan

- `gh api graphql` review-thread query returns current unresolved thread IDs and bodies for this round.

### Phase Summary

Identified four unresolved threads and judged all valid:
1. Rebuild roster filter option lists when restoring persisted roster state on interactive attach.
2. Preserve success status messages across post-mutation roster reloads.
3. Rename protected computed properties to PascalCase naming.
4. Use `ToUpperInvariant()` consistently in non-Npgsql search path.

## Phase 2: Implement fixes and targeted tests

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [x] Refresh available filter options when `Initialized` restores persisted roster state.
- [x] Stop clearing `_statusMessage` at the top of every roster reload.
- [x] Rename protected computed filter-text properties to PascalCase and update markup references.
- [x] Use consistent casing transform on both sides of the non-Npgsql string search expression while preserving provider translation compatibility.
- [x] Add focused component coverage proving create success message remains visible after mutation + reload.
- [x] Run targeted tests.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*PlayerComponentsTests" --filter-class "*PlayerServiceTests"` passes.

### Phase Summary

Implemented all UI-side fixes in `Players`: persisted-roster attach now rebuilds available filter options; roster reload no longer clears `_statusMessage`; protected computed properties were renamed to PascalCase and markup bindings updated. Added a component test verifying create success feedback remains visible after mutation-triggered reload. For the search-casing review point, updated the non-Npgsql path to use consistent casing transforms on both sides while preserving EF SQLite translation compatibility. Targeted player component/service tests pass.

## Phase 3: Reply and resolve threads

Status: In progress <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [ ] Reply on each new thread with validity and fix summary.
- [ ] Resolve each thread after push.

### Verification Plan

- `gh api graphql` review-thread query shows all thread IDs from this round as `isResolved: true`.

### Phase Summary

_(write when phase completes)_

## Final Recap

_(write when all phases complete: summary of the entire piece of work)_

## Deployment Plan

_(write when all phases complete: step-by-step deployment instructions)_
