# Season foundation test research

## Scope and targets

- Persistence: `ClubEntity` current-season pointer, `SeasonEntity` concurrency token, EF configuration, advisory lock, and migration.
- Boundary services: first-class season create/update/start-next commands and list/detail queries.
- HTTP/WASM: `/api/seasons` route family, ProblemDetails mapping, `201 Location`, strict client payload handling.
- Integration: campaign creation/setup must use only the current season and inline creation may establish currentness only when none exists.

## Existing conventions and pairing

- SDK-style .NET 10 solution; xUnit v4 on Microsoft.Testing.Platform with Shouldly.
- Unit/service tests use `TenancyTestHarness` with in-memory SQLite; provider, constraint, retry, and HTTP tests use `NovaAppHostFixture` with PostgreSQL.
- The mandatory Roslyn static pairing pass found 437 production files, 242 test files, 275 paired files, and 162 unpaired files. `SeasonEntity`, `CampaignCreationService`, `CampaignQueryService`, and the existing `SeasonMetadataService` are already referenced by focused unit/integration tests; new first-class season services require new focused pairings.
- Static pairing is a source-reference heuristic, not line or branch coverage evidence.

## Acceptance checklist

- One tenant-consistent current-season pointer per club, deterministic migration backfill, and no date-derived lifecycle.
- First-season creation is admin-only, idempotent, atomic, and conflicts when currentness already exists.
- List/detail reads are member-authorized, tenant-safe, bounded, deterministically ordered, and do not expose roster placeholders.
- Metadata updates are admin-only, concurrency-checked, trim names, reject exact duplicates, and preserve campaign date containment.
- Start-next is admin-only, retry/ambiguous-commit safe, blocks stale/no-current/non-Closed work/name conflicts, and preserves all historical/team/placement data.
- Campaign creation accepts only current seasons; inline seasons establish currentness only from no-current state; setup exposes one nullable current season.
- Routes, OpenAPI metadata, authorization, ProblemDetails, `201 Location`, and strict WASM payload validation follow existing patterns.
