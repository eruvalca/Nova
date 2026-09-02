# Season foundation test plan

| Requirement | Planned evidence |
| --- | --- |
| Current-season persistence and tenant integrity | PostgreSQL constraint/backfill tests plus existing tenancy harness coverage |
| First-season command behavior | `SeasonCommandServiceTests` for validation/auth/conflict/success/idempotency-visible state |
| Bounded tenant-safe reads | `SeasonQueryServiceTests` for current-first paging, detail ordering, counts, and cross-tenant not-found |
| Metadata invariants | `SeasonCommandServiceTests` for token mismatch, duplicate name, and campaign-window conflicts |
| Atomic advancement | PostgreSQL season foundation tests for concurrent winners, retry/commit recovery, blockers, and unchanged related rows |
| HTTP contracts | `SeasonHttpTests` for auth, validation, 201/Location/follow, read success, conflicts, and non-disclosing 404 |
| WASM contracts | `HttpSeasonServiceTests` for URL/body behavior, ProblemDetails, malformed success bodies, and DTO invariant validation |
| Campaign integration | Extend campaign creation/query service and PostgreSQL/HTTP tests for current-only selection and inline first currentness |
| Campaign metadata versus advancement | Extend campaign metadata service/unit coverage and PostgreSQL race coverage so club-season is acquired before campaign and Active campaigns remain in the current season |
| Historical reopen versus advancement | Extend campaign lifecycle service/unit coverage and PostgreSQL race coverage so club-season is acquired before campaign and historical campaigns cannot reopen |
| Paging and client concurrency boundaries | Add unit cases for `int.MaxValue` pages and eventually-consistent totals smaller than the returned page |
| HTTP query validation and metadata | Bind query DTOs with `[AsParameters]`; extend endpoint metadata and HTTP validation assertions for 401, route/query ranges, and reachable outcomes |

Implementation proceeds production-first, then the narrow unit and PostgreSQL/HTTP suites, followed by solution validation. Final assertion and pseudo-mutation reviews will be recorded in `.testagent/status.md`.
