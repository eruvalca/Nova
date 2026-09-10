## Summary

Closes #197. Active campaigns now combine the existing Roster discovery controls with effective season placement and authoritative Close readiness. Closed campaigns read immutable campaign-local evidence, including archived participants and teams. Participant context retains note/tag editing, direct links, keyboard navigation and return state.

The shared reads support SQL-side multi-year/tag filters, explicit campaign-local outcome/team filters, eligibility, deterministic sorting and bounded participant lookup. Unfiltered campaign scale and integrity checks remain independent of discovery. A correlated latest-assignment lookup also fixes an observed PostgreSQL plan that repeatedly scanned the full decision set.

The selected **Roster beside context** composition preserves Fieldhouse navigation and supplies desktop, tablet and phone behavior. Shared-shell and query handoffs for #198–#200 are documented in [the contract](https://github.com/eruvalca/Nova/blob/codex/issue-197-workspace-roster/docs/campaign-workspace-roster.md). The integrated #170 campaign-loop acceptance remains separate.

## Validation

- Tested revision: the first review-round commit containing this record, based on `e12b886c23d5c006332b39717836ea0d8dc23a3e`; the live PR pins the resulting commit SHA. All four initial review findings are included in that single commit.
- Guidance and behavior: [validation record](https://github.com/eruvalca/Nova/blob/codex/issue-197-workspace-roster/.impeccable/review/issue-197/validation.md), including sources actually read and lifecycle, ownership, recovery, discovery and history evidence.
- Commands and results: solution build passed with zero warnings/errors; 2,793 unit, 586 integration and 135 browser tests passed with zero skips. All seven optional accessibility journeys ran, and all three always-running Draft journeys remain unchanged. Contrast and final format verification passed. Only test-initializer line breaks and documentation/evidence changed after the suites.
- Separate local review: [initial code findings and first PR review round resolved](https://github.com/eruvalca/Nova/blob/codex/issue-197-workspace-roster/.impeccable/review/issue-197/local-code-review.md). [The finish reviewer](https://github.com/eruvalca/Nova/blob/codex/issue-197-workspace-roster/.impeccable/review/issue-197/finish-review.md) scored every prescribed visual correction and the restored scripted mobile route resolved, with all seven refreshed captures valid.
- Unavailable checks or remaining limitations: refreshed image comparison scores are **79.68%** at the approved viewport and **75.47%** at 1440px, both above 72%. Publication follows the user's instruction to create the PR when the required score threshold and implementation are complete. The automated hero gate still flags selector-plus-Apply chrome and five additional-content cells; those results and the reviewer's `fix` disposition remain recorded. No mechanical pass or whole-surface approval is claimed. [Approved comp](https://github.com/eruvalca/Nova/blob/codex/issue-197-workspace-roster/.impeccable/mocks/decision/issue-197-side-context.png), [desktop](https://github.com/eruvalca/Nova/blob/codex/issue-197-workspace-roster/.impeccable/review/issue-197/hero-repro.png), [mobile](https://github.com/eruvalca/Nova/blob/codex/issue-197-workspace-roster/.impeccable/review/issue-197/mobile.png), and [mobile participant context](https://github.com/eruvalca/Nova/blob/codex/issue-197-workspace-roster/.impeccable/review/issue-197/mobile-context.png).

## Checklist

- [x] Format check passes: `dotnet format Nova.slnx --verify-no-changes`
- [x] Unit tests pass: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build`
- [x] Integration tests pass: `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` (local, requires the Aspire AppHost)
- [x] Browser tests pass: `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` (local-only, Playwright)
- [x] Applicable guidance and related implementations were checked; substantial changes received a separate local review and findings were resolved by behavior/evidence, including suppressed review-body findings.
- [x] If `Nova/scss/` or `Nova/package.json` changed: `npm run build:css` and `npm run check:contrast` pass (run from `Nova/`). Neither file changed; contrast passed independently.
