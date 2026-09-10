## Summary

Closes #197. Active campaigns now combine the existing Roster discovery controls with effective season placement and authoritative Close readiness. Closed campaigns read immutable campaign-local evidence, including archived participants and teams. Participant context retains note/tag editing, direct links, keyboard navigation and return state.

The shared reads support SQL-side multi-year/tag filters, explicit campaign-local outcome/team filters, eligibility, deterministic sorting and bounded participant lookup. Unfiltered campaign scale and integrity checks remain independent of discovery. A correlated latest-assignment lookup also fixes an observed PostgreSQL plan that repeatedly scanned the full decision set.

The selected **Roster beside context** composition preserves Fieldhouse navigation and supplies desktop, tablet and phone behavior. Shared-shell and query handoffs for #198–#200 are documented in [the contract](../../../docs/campaign-workspace-roster.md). The integrated #170 campaign-loop acceptance remains separate.

## Validation

- Tested revision: pending local commit; baseline `54c1da3abb16a6d02afbaaf14b59f9cbb870c8ff`.
- Guidance and behavior: [validation record](validation.md), including sources actually read and lifecycle, ownership, recovery, discovery and history evidence.
- Commands and results: solution build passed with zero warnings/errors; 2,783 unit, 580 integration and 135 browser tests passed with zero skips. All seven optional accessibility journeys ran, and all three always-running Draft journeys remain unchanged. Contrast and final format verification passed.
- Separate local review: [all seven code findings resolved](local-code-review.md). [The finish reviewer](finish-review.md) scored every prescribed visual correction resolved after the user-funded third round.
- Unavailable checks or remaining limitations: **do not publish this draft while the comparison gate is unresolved**. The hero scores 79.71% and the 1440px comparison scores 75.46%; the hero still flags retained tag controls and additional content. User adjudication is pending, and no exception or whole-surface approval is claimed.

## Checklist

- [x] Format check passes: `dotnet format Nova.slnx --verify-no-changes`
- [x] Unit tests pass: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build`
- [x] Integration tests pass: `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` (local, requires the Aspire AppHost)
- [x] Browser tests pass: `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` (local-only, Playwright)
- [x] Applicable guidance and related implementations were checked; substantial changes received a separate local review and findings were resolved by behavior/evidence, including suppressed review-body findings.
- [x] If `Nova/scss/` or `Nova/package.json` changed: `npm run build:css` and `npm run check:contrast` pass (run from `Nova/`). Neither file changed; contrast passed independently.
