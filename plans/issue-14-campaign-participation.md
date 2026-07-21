# Issue #14 Campaign Participation Integrity

Implement the persistence and reusable server-side placement foundation from GitHub issue #14. Scope includes campaign-scoped tryout numbers, placement outcome integrity, tenant-safe administrator mutations, optimistic concurrency, an incremental migration, and focused tests; HTTP endpoints, clients, and UI are excluded.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

## Confirmed Decisions

- Implement issue #14 only in this worktree.
- Move `TryoutNumber` from `PlayerEntity` to `PlayerCampaignAssignmentEntity`.
- Add shared `PlacementOutcome` values `Undecided`, `Assigned`, `NotSelected`, and `Withdrawn`; default participation to `Undecided`.
- Enforce the outcome/team matrix in PostgreSQL and in the placement service.
- Use an application-managed `Guid` concurrency token regenerated on every successful placement mutation.
- Use `NovaDbContext`, tenant query filters, and the save interceptor; never bypass filters or call `IgnoreQueryFilters()`.
- Expose the internal server operation through native OneOf with domain-specific forbidden, validation, and conflict variants because the installed OneOf package does not provide built-in `Forbidden` or `Conflict` types.
- Keep campaign lifecycle and archived-team guards out of scope until issues #17 and #19.

## Phase 1: Update the Participation Model

Status: Complete

- [x] Add the shared `PlacementOutcome` enum.
- [x] Move `TryoutNumber` to campaign participation and add outcome plus concurrency-token properties.
- [x] Configure unique campaign/player enrollment, filtered campaign/tryout uniqueness, the outcome/team check constraint, relationships, and concurrency mapping.
- [x] Confirm the EF model builds under both SQLite and PostgreSQL providers.

### Verification Plan

- `dotnet build Nova.slnx` reports 0 errors.
- Inspect EF model metadata in focused unit tests: participation token is a concurrency token and required indexes/constraint are present.

### Phase Summary

Added the shared outcome enum with stable values, moved tryout number to participation, and added the application-managed concurrency token. Configured unique enrollment, filtered campaign/tryout uniqueness, the outcome/team check constraint, and EF concurrency metadata. `dotnet build Nova.slnx` completed with 0 errors; only the baseline package-vulnerability warnings remained. PostgreSQL migration/application verification is intentionally recorded in Phase 3.

## Phase 2: Add the Placement Mutation Service

Status: Complete

- [x] Add a DI-registered internal `CampaignPlacementService` using `IDbContextFactory<NovaDbContext>`, `ICurrentUserProvider`, native OneOf results, and source-generated logging.
- [x] Require an authenticated current-club administrator.
- [x] Load participation with its player and campaign through tenant filters, and validate optional team ownership in the same context.
- [x] Validate the outcome/team matrix and `Player.GraduationYear >= Team.GraduationYear`.
- [x] Apply the expected concurrency token as the EF original value, regenerate the token on success, and map `DbUpdateConcurrencyException` to a conflict without partial writes.
- [x] Add focused SQLite service tests for authorization, not-found/cross-tenant behavior, matrix validation, eligibility, successful mutations, and stale tokens.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*CampaignPlacementServiceTests"` discovers tests and passes.

### Phase Summary

Added and registered `CampaignPlacementService` with current-club administrator checks, tenant-filtered transactional reads, outcome/team and graduation-year validation, application-managed expected-token concurrency, and source-generated logs. The installed OneOf package lacks built-in forbidden/conflict variants, so the native union uses `Error<T>`/`NotFound` plus domain-specific `PlacementForbidden` and `PlacementConflict` records as planned. The focused test command discovered 10 tests and all 10 passed.

## Phase 3: Add Migration and Persistence Coverage

Status: Complete

- [x] Generate one named incremental migration through `NovaDbContext`.
- [x] Verify the migration removes player-level tryout number and adds participation columns, both unique indexes, the PostgreSQL filtered predicate, the check constraint, and the concurrency token.
- [x] Add/extend SQLite tenancy coverage for participation visibility and cross-tenant writes.
- [x] Add PostgreSQL integration tests for clean migration application, duplicate enrollment, filtered tryout uniqueness semantics, outcome/team constraint enforcement, and optimistic concurrency.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests --filter-class "*TenancyTests"` discovers tests and passes.
- `dotnet test --project Nova.Integration.Tests --filter-class "*CampaignParticipation*"` discovers tests and passes against a clean Aspire PostgreSQL database.
- `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext` reports no pending model changes.

### Phase Summary

Generated `AddCampaignParticipationIntegrity` as one incremental migration. The migration removes `Players.TryoutNumber`, adds participation tryout/outcome/token columns, adds both unique indexes with the exact PostgreSQL non-null filter, and adds a check constraint that rejects both invalid team combinations and undefined outcome integers. Added SQLite metadata/tenancy tests and Aspire PostgreSQL migration, uniqueness, constraint, and concurrency tests. The tenancy filter discovered and passed 23 tests; the PostgreSQL filter discovered and passed 9 tests; EF reported no pending model changes.

## Phase 4: Final Verification

Status: Complete

- [x] Run the complete unit test project.
- [x] Run the complete integration test project.
- [x] Build the solution.
- [x] Review the final diff against issue #14 acceptance criteria and confirm no endpoint/client/UI or later-child scope was introduced.

### Verification Plan

- `dotnet test --project Nova.Unit.Tests` passes.
- `dotnet test --project Nova.Integration.Tests` passes.
- `dotnet build Nova.slnx` reports 0 errors.
- `git diff --check` reports no whitespace errors.

### Phase Summary

The complete unit project passed 396/396 tests and the complete Aspire integration project passed 35/35 tests. The final solution build completed with 0 errors and only the same 12 package-vulnerability warnings present at baseline. `git diff --check` passed. A read-only code-review agent reviewed tracked and untracked issue #14 changes and reported no significant bugs, security issues, or logic errors. The diff contains no endpoints, clients, UI, campaign lifecycle, or archival behavior.

## Final Recap

Implemented issue #14 end to end. Campaign participation now owns tryout numbers, a constrained placement outcome, optional team placement, and an application-managed concurrency token. PostgreSQL enforces enrollment uniqueness, campaign-scoped non-null tryout uniqueness, and the complete outcome/team matrix. The reusable server placement service enforces current-club administrator authorization, tenant ownership, team eligibility, atomic writes, and stale-token conflicts. Added an incremental migration plus focused SQLite and Aspire PostgreSQL coverage, with all repository tests passing.

## Deployment Plan

1. Ensure any disposable local database created before this change can be reset; the migration intentionally drops the obsolete player-level tryout-number column, as approved for this foundation work.
2. Apply `AddCampaignParticipationIntegrity` through `NovaDbContext` using the normal deployment migration process.
3. Deploy the updated Nova server and shared assembly together.
4. No endpoint, client, UI, feature-flag, or user-data backfill rollout is required for this foundation-only change.
