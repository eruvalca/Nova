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
| Start-next operation collision safety | Add `SeasonCommandServiceTests` for first-season operation reuse and for reusing an advancement operation with the new current ID |
| Season client request/response paging fidelity | Add `HttpSeasonQueryServiceTests` for local invalid-input rejection and mismatched list/detail paging metadata |
| Campaign season token validation | Add `HttpCampaignQueryServiceTests` for an otherwise-valid group with `Guid.Empty` concurrency token |
| Current-season-only campaign UI | Add bUnit coverage proving the new-campaign page hides inline mode when currentness exists, the form resets stale inline state, and the no-current flow still exposes inline fields |
| Metadata field attribution | Add `SeasonCommandServiceTests` for lower-only, upper-only, and combined linked-campaign window violations |
| Duplicate inline-season recovery text | Extend `CampaignCreationServiceTests` to assert the conflict directs the caller to choose a different name |
| Durable creation-operation identity | Persist the optional advancement predecessor with a tenant-consistent composite FK; add command and PostgreSQL tests for operation-kind/predecessor collisions |
| Atomic currentness reads | Project the club pointer in the same season/setup SQL statement and use command-count regressions to prevent separate pointer reads |
| Strict ordering and advancement payloads | Add HTTP-client regressions for current-first/history ordering, campaign history ordering, page-2 current rows, and equal previous/current advancement IDs |

Implementation proceeds production-first, then the narrow unit and PostgreSQL/HTTP suites, followed by solution validation. Final assertion and pseudo-mutation reviews will be recorded in `.testagent/status.md`.
