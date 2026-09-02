# Season foundation verification status

## Requirement evidence

| Requirement | Evidence |
| --- | --- |
| Tenant-consistent current-season persistence | `SeasonFoundationPostgresTests` verifies the migration is applied, cross-club pointers fail at the PostgreSQL boundary, and additional historical seasons leave the pointer unchanged. The migration backfill is deterministically ordered by `StartDate DESC, SeasonId DESC`. |
| First current season | `SeasonCommandServiceTests` verifies no-current creation, pointer installation, trimming, exact-operation replay, duplicate/current conflicts, and administrator enforcement. |
| Bounded tenant-safe reads | `SeasonQueryServiceTests` verifies current-first ordering, deterministic history, campaign paging/counts, membership enforcement, and cross-tenant non-disclosing not-found behavior. |
| Metadata integrity | Unit and PostgreSQL HTTP tests verify token rotation, stale-token conflict, exact-name uniqueness, campaign-window rejection, pointer immutability, and administrator-only writes. |
| Atomic advancement | Unit tests verify no-current/stale/open-campaign/duplicate blockers and preservation of campaigns, teams, assignments, placement fields, and concurrency tokens. PostgreSQL tests verify transient retry, ambiguous-commit recovery, and one-winner/one-conflict concurrent advancement. |
| Campaign integration | Campaign unit, PostgreSQL, and HTTP tests verify current-only existing-season selection, inline creation only from no-current, pointer installation, and nullable current-season setup. |
| HTTP and WASM contracts | Season HTTP tests verify policies, traced validation/not-found/conflict ProblemDetails, resolvable `201 Location` values, cross-tenant detail isolation, and same-operation replay. WASM tests verify first-class URLs and reject invalid successful payloads. |

## Assertion-quality review

- Scope: 23 focused unit tests and 10 focused PostgreSQL/HTTP tests in the new Seasons slice.
- No assertion-free, trivial-only, always-true, or self-referential tests were found.
- Assertions cover equality/boolean outcomes, negative paths, exceptions, collections/order, HTTP structure, and persisted state/side effects.
- The strongest state assertions compare the durable campaign/team/assignment fields before and after advancement and prove the new season has no inherited campaigns or placements.

## Pseudo-mutation gap review

Verdict: **Strong**. Core authorization denials, validation boundaries, tenant isolation, currentness transitions, retry recovery, concurrency, and preservation side effects are directly observable in assertions. The remaining migration-backfill risk is implementation-level upgrade simulation; the checked-in SQL and applied-migration/provider tests provide the current evidence without mutating the shared integration database to an old schema.

## Final commands

- `dotnet build Nova.slnx` — passed, 0 warnings and 0 errors.
- `dotnet format Nova.slnx --verify-no-changes` — passed; EF-generated migration retains the repository's generated block-namespace style and emits one non-failing IDE0161 suggestion.
- `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext --no-build` — no model changes.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — 1,977 passed.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — 398 passed.
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` — 111 passed, 7 opt-in screenshot evidence tests skipped.
