# Season foundation verification status

## Requirement evidence

| Requirement | Evidence |
| --- | --- |
| Tenant-consistent current-season persistence | `SeasonFoundationPostgresTests` verifies the migration is applied, cross-club pointers fail at the PostgreSQL boundary, and additional historical seasons leave the pointer unchanged. The migration backfill is deterministically ordered by `StartDate DESC, SeasonId DESC`. |
| First current season | `SeasonCommandServiceTests` verifies no-current creation, pointer installation, trimming, exact-operation replay, duplicate/current conflicts, and administrator enforcement. |
| Bounded tenant-safe reads | `SeasonQueryServiceTests` verifies current-first ordering, deterministic history, campaign paging/counts, membership enforcement, and cross-tenant non-disclosing not-found behavior. |
| Metadata integrity | Unit and PostgreSQL HTTP tests verify token rotation, stale-token conflict, exact-name uniqueness, pointer immutability, administrator-only writes, and independent `StartDate`/`EndDate` attribution for lower, upper, and combined campaign-window failures. |
| Atomic advancement | Unit tests verify no-current/stale/open-campaign/duplicate blockers, reject operation-currentness collisions, and preserve campaigns, teams, assignments, placement fields, and concurrency tokens. PostgreSQL tests verify transient retry, ambiguous-commit recovery, and one-winner/one-conflict concurrent advancement. |
| Campaign integration | Campaign unit, PostgreSQL, HTTP, and bUnit tests verify current-only existing-season selection, inline creation only from no-current, pointer installation, nullable current-season setup, hidden inline controls when currentness exists, and actionable duplicate-name recovery. |
| HTTP and WASM contracts | Season HTTP tests verify policies, traced validation/not-found/conflict ProblemDetails, resolvable `201 Location` values, cross-tenant detail isolation, and same-operation replay. WASM tests reject invalid input before transport, require exact list/detail paging echoes, tolerate eventually-consistent totals, and reject empty season concurrency tokens. |

## Assertion-quality review

- Scope: the original season-foundation suite plus 22 focused unit regressions and 2 deterministic PostgreSQL race regressions added during replacement-PR review carry-forward and PR #227 review.
- No assertion-free, trivial-only, always-true, or self-referential tests were found.
- Assertions cover equality/boolean outcomes, negative paths, collections/order, endpoint metadata, HTTP structure, and persisted state/side effects.
- The review regressions pin current-season conflicts, durable campaign status/season identity, provider-safe empty pages at `int.MaxValue`, nonnegative but eventually-consistent totals, `[AsParameters]` binding, reachable endpoint metadata, and both sides of the club-season concurrency invariant.
- PR #227 regressions separately pin page and page-size echoes, request short-circuiting, both operation-collision sequences, lower/upper/combined metadata errors, empty write tokens, inline-control visibility plus normalized submission state, and exact recovery guidance.
- The strongest state assertions compare the durable campaign/team/assignment fields before and after advancement, prove the new season has no inherited campaigns or placements, and prove competing metadata/reopen operations cannot leave an out-of-window or Active historical campaign.

## Pseudo-mutation gap review

Verdict: **Strong**. Core authorization denials, validation boundaries, tenant isolation, currentness transitions, retry recovery, paging overflow and response fidelity, eventually-consistent count handling, endpoint metadata, lock ordering, and preservation side effects are directly observable in assertions. Removing either new club-season lock, allowing a historical campaign target/reopen, accepting an operation/current-season collision or empty write token, restoring unchecked offset arithmetic, reinstating the old total-count predicate, misattributing either date boundary, or exposing inline mode with a current season changes an asserted public result or durable state. The remaining migration-backfill risk is implementation-level upgrade simulation; the checked-in SQL and applied-migration/provider tests provide the current evidence without mutating the shared integration database to an old schema.

## Final commands

- `dotnet build Nova.slnx` — passed, 0 warnings and 0 errors.
- `dotnet format Nova.slnx --verify-no-changes` — passed.
- `dotnet ef migrations has-pending-model-changes --project Nova --context NovaDbContext --no-build` — no model changes.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — 2,000 passed.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` — 401 passed.
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` — 111 passed, 7 opt-in screenshot evidence tests skipped.
