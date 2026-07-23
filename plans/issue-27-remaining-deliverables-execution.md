# Issue 27 Remaining Deliverables Execution

Close the remaining `/players` ticket gaps by adding missing coverage and UI behavior for state handling, accessibility, and end-to-end workflow confidence without expanding backend feature scope.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

## Phase 1: Expand Players component behavior coverage

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [x] Add bUnit tests for loading/empty/error retry states and role-based mutation visibility.
- [x] Add bUnit tests for filter/search interactions and roster-context detail link preservation.
- [x] Add bUnit tests for mutation conflict rendering (graduation-year blockers and archive blockers).

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*PlayerComponentsTests"` should pass and discover the new tests.

### Phase Summary

Added comprehensive `PlayerComponentsTests` coverage for loading, empty, transport-error retry, role matrix, lifecycle/grad/tag/search filters, detail-link context preservation, update-conflict blocker rendering, archive blocker rendering, and form validation messaging.
Used existing bUnit/NSubstitute patterns in this repository and verified with the targeted class filter command.

## Phase 2: Strengthen HTTP-integrated workflow coverage

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [x] Add an integration workflow test that exercises admin create/update/archive/restore plus active/archived roster reads.
- [x] Add an integration assertion that evaluator/member callers are read-only for permanent roster mutations (forbidden on create/archive/restore).

### Verification Plan

- `dotnet test --project Nova.Integration.Tests --filter-class "*PlayerRosterHttpTests" --filter-class "*PlayerManagementHttpTests" --filter-class "*PlayerLifecycleHttpTests"` should pass and discover at least one test per filter.

### Phase Summary

Extended `PlayerRosterHttpTests` with `PlayerRosterWorkflow_AdminRoundTrip_AndEvaluatorReadOnly`, covering create/update/archive/restore plus active/archived query visibility and evaluator forbidden behavior on mutation endpoints.
Ran the repository-provided filtered integration command for roster/management/lifecycle classes.

## Phase 3: Finish UX/accessibility behavior and run full validation

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator

- [x] Implement any missing Players-page accessibility/focus/context behavior identified by the new tests.
- [x] Run a browser/Playwright validation pass for administrator and evaluator `/players` flows and record the result.
- [x] Run `dotnet build Nova.slnx`.

### Verification Plan

- Playwright/browser: administrator can create/edit/archive/restore and see lifecycle/context behavior; evaluator cannot access mutation controls/operations.
- `dotnet build Nova.slnx` should succeed.

### Phase Summary

Implemented missing roster-context restoration by binding `/players` query parameters (`view`, `search`, `graduationYear`, `tag`) and applying them to initial filter state; this closes context preservation on detail round-trips.
Executed browser validation using Aspire + Playwright MCP against a live isolated AppHost:
- Admin flow: register with required profile photo, create club, open `/players`, create player, edit player, archive player, switch to archived view, restore player, switch back to active view.
- Evaluator/read-only flow: register evaluator, assign same club membership, refresh membership claims, open `/players`, verify roster visibility and absence of mutation controls.
During browser execution, fixed two user-facing blockers discovered in the real UI path:
1. Enabled custom claim propagation for interactive WASM by setting `SerializeAllClaims = true` in `AddAuthenticationStateSerialization(...)` in `Nova/Program.cs`, which restores `nova:club_id` availability in the interactive roster page.
2. Removed an invalid `IsEdit` parameter usage from `Players.razor` create-form markup that caused runtime component rendering failures.
Completed a full solution build and reran targeted player component tests after these fixes.

## Final Recap

Remaining deliverables were executed end-to-end: broader component-state coverage, HTTP-integrated admin/evaluator workflow coverage, and an actual browser validation pass over the `/players` admin/evaluator flows.
The browser run also surfaced and resolved two production-impacting interactive issues (missing custom claims in WASM auth serialization and invalid PlayerForm parameter wiring), and the roster experience now behaves correctly in real UI execution.

## Deployment Plan

1. Merge this branch into the target integration branch.
2. Run `dotnet test --project Nova.Unit.Tests --filter-class "*PlayerComponentsTests"`.
3. Run `dotnet test --project Nova.Integration.Tests --filter-class "*PlayerRosterHttpTests" --filter-class "*PlayerManagementHttpTests" --filter-class "*PlayerLifecycleHttpTests"`.
4. Run `dotnet build Nova.slnx`.
5. Aspire smoke validation (recommended): `aspire start --isolated --non-interactive`, `aspire wait nova --non-interactive`, then a quick `/players` sign-in path check.
6. Deploy with the standard Nova environment pipeline used by this repository.
