# PR 37 Review Comments Round 4

Review and resolve the latest PR #37 comments by classifying validity, applying required fixes, and closing threads with implementation details or rationale.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

## Phase 1: Classify newest comments

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [x] Fetch unresolved review threads for PR #37.
- [x] Judge each comment as valid/invalid with technical rationale.
- [x] Define code-change scope for valid items only.

### Verification Plan

- `gh api graphql` query over PR #37 review threads returns the unresolved thread IDs and comment text used in classification.

### Phase Summary

Found two unresolved comments:
1. `GetPlayerRosterEndpoints.GetRosterUrl` should normalize/clamp `lifecycleStatus` to values accepted by contract validation — **valid**.
2. `SerializeAllClaims = true` may expose unneeded claims — **not actionable in current framework surface** because `AddAuthenticationStateSerialization` in this app currently exposes only a global boolean toggle, no per-claim allowlist; existing interactive components rely on role + club claims during prerender/attach transitions. Will respond with rationale and keep current behavior.

## Phase 2: Implement valid fixes and tests

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [x] Update roster URL builder to include lifecycle status only for allowed values (`active`/`archived`).
- [x] Add/update contract tests proving invalid lifecycle status is omitted from built URLs.
- [x] Run targeted contract tests.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*GetPlayerRosterContractTests"` passes.

### Phase Summary

Updated `GetPlayerRosterEndpoints.GetRosterUrl` to normalize lifecycle status using a strict allowlist and emit canonical lowercase values only for valid states. Invalid values are now omitted from the query string instead of being propagated. Added a contract test (`GetRosterUrl_OmitsLifecycleStatus_WhenOutsideAllowedValues`) and re-ran the focused contract test class successfully.

## Phase 3: Reply and resolve threads

Status: In progress <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [ ] Reply to lifecycleStatus thread with fix summary and resolve.
- [ ] Reply to SerializeAllClaims thread with invalid/non-actionable rationale and resolve.

### Verification Plan

- `gh api graphql` review-thread query confirms all threads in this round are `isResolved: true`.

### Phase Summary

_(write when phase completes)_

## Final Recap

_(write when all phases complete: summary of the entire piece of work)_

## Deployment Plan

_(write when all phases complete: step-by-step deployment instructions)_
