# Season foundation test research

## Scope and targets

- Persistence: `ClubEntity` current-season pointer, `SeasonEntity` concurrency token, EF configuration, advisory lock, and migration.
- Boundary services: first-class season create/update/start-next commands and list/detail queries.
- HTTP/WASM: `/api/seasons` route family, ProblemDetails mapping, `201 Location`, strict client payload handling.
- Integration: campaign creation/setup must use only the current season and inline creation may establish currentness only when none exists.

## Existing conventions and pairing

- SDK-style .NET 10 solution; xUnit v4 on Microsoft.Testing.Platform with Shouldly.
- Unit/service tests use `TenancyTestHarness` with in-memory SQLite; provider, constraint, retry, and HTTP tests use `NovaAppHostFixture` with PostgreSQL.
- The replacement-PR Roslyn static pairing pass found 442 production files, 247 test files, 278 paired files, and 164 unpaired files. `SeasonEntity`, `CampaignCreationService`, `CampaignQueryService`, `CampaignLifecycleService`, `CampaignMetadataService`, `SeasonCommandService`, `SeasonQueryService`, and both season HTTP clients already have focused unit or integration pairings.
- Static pairing is a source-reference heuristic, not line or branch coverage evidence.

## Acceptance checklist

- One tenant-consistent current-season pointer per club, deterministic migration backfill, and no date-derived lifecycle.
- First-season creation is admin-only, idempotent, atomic, and conflicts when currentness already exists.
- List/detail reads are member-authorized, tenant-safe, bounded, deterministically ordered, and do not expose roster placeholders.
- Metadata updates are admin-only, concurrency-checked, trim names, reject exact duplicates, and preserve campaign date containment.
- Start-next is admin-only, retry/ambiguous-commit safe, blocks stale/no-current/non-Closed work/name conflicts, and preserves all historical/team/placement data.
- Campaign creation accepts only current seasons; inline seasons establish currentness only from no-current state; setup exposes one nullable current season.
- Routes, OpenAPI metadata, authorization, ProblemDetails, `201 Location`, and strict WASM payload validation follow existing patterns.
- Review carry-forward: campaign metadata and historical-campaign reopen writers participate in the club-season lock invariant; advancing cannot miss an Active campaign moved or reopened concurrently.
- Review carry-forward: huge valid page numbers cannot overflow SQL offsets, eventually-consistent totals remain valid under concurrent inserts, query DTO annotations run at the HTTP boundary, and endpoint metadata advertises the reachable problem responses.
- Approved exclusions remain unchanged: season detail has no effective-roster projection (#214 owns it), and season mutations emit no new activity-event family.
