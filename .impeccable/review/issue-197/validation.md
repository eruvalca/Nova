# Issue 197 validation record

Status: implementation and code validation complete; visual gate awaiting user adjudication. No PR or full completion claim.

Baseline: `54c1da3abb16a6d02afbaaf14b59f9cbb870c8ff` on
`codex/issue-197-workspace-roster`. The initial checkout was clean and included #250 and #251.
Tested implementation: `49e8915bedbc431e1b0304fae2e87538af2a11df`. Tests ran before this
commit against the same production and test behavior; only the BOM/comment changes described below
followed test execution. The subsequent commit updates validation/review/PR-description evidence only.
Baseline solution build, format verification and 2,673 unit tests passed. The baseline build
reported the existing Sass warnings. No baseline integration/browser result is claimed.

## Behavior and evidence

- Active and Closed discovery: service, strict HTTP, PostgreSQL and HTTP integration cases cover
  combined years/tags/local outcome/local team/effective eligibility, sorting, bounded exact
  participant reads, tenant identifiers, literal wildcard search, unfiltered counts and tags.
- Existing inherited-assignment, invalid-latest-assignment, withdrawal and immutable Closed-history
  cases remain in the full suites. Closed integrity precedes every discovery filter.
- Component and URL cases cover lifecycle source switching, persisted ownership, delayed reads,
  same-query refresh preservation versus changed-query failures, regional retries, off-page detail,
  boundary navigation, receipt acknowledgment and search/history ownership.
- Browser acceptance retains the three always-running `CampaignDraftBrowserTests` journeys and the
  evaluation/note/tag workflows. New workspace journeys capture desktop/mobile, inherited context,
  zero Needs placement with an authoritative Close blocker, archived Closed evidence, no matches
  and a Closed integrity failure hidden by neither search nor an exact participant link.

The current solution build passes with zero warnings/errors. Unit tests pass 2,783/2,783 and
PostgreSQL integration passes 580/580, with no skips, including the bounded latest-decision query
and cancellation regressions. The final strict browser run passed 135/135 with no failures or skips
in 5m01.670s, using the original five-second assertion allowance. An earlier Name-sort navigation
timeout did not reproduce in either its focused diagnostic (1/1) or this full run. All seven optional
accessibility journeys were enabled with `NOVA_A11Y_SCREENSHOTS=1`; the three Draft journeys remain
unchanged. The final contrast check passed every ratio and forbidden-token assertion.
Final format verification passed after correcting the required UTF-8 BOM in two C# test files.
Those encoding-only changes and a browser helper comment were the only source-file changes after
the final tests.

| Command | Final result |
| --- | --- |
| `dotnet build Nova.slnx` | Passed; zero warnings/errors |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | 2,783 passed, zero failed/skipped; 36.908s |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | 580 passed, zero failed/skipped; 3m08.262s |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` with `NOVA_A11Y_SCREENSHOTS=1` | 135 passed, zero failed/skipped; 5m01.670s |
| `npm run check:contrast` from `Nova/` | Passed all contrast ratios and forbidden-token assertions |
| `dotnet format Nova.slnx --verify-no-changes` | Passed, exit code 0 |

Build-capable commands and the Aspire-backed suites ran serially. Final browser execution preserved
the reviewed workspace captures rather than overwriting them: the capture and final full test runs
used the same production source, with later browser changes limited to stricter synchronization,
single-click sort diagnostics and an explanatory comment.

## Reviews and design

The user selected **Roster beside context**, seed `16fb136e`, before production UI edits.
See the approved comp sidecar, [surface contract](../../surfaces/campaign-spine.md),
[local code review](local-code-review.md) and [finish review](finish-review.md).

The separate code reviewer resolved snapshot ownership, regional startup persistence, drawer
teardown ownership, collocated code, and abandoned debounce text. Later browser evidence identified
canceled JavaScript disposal and a provider join-plan defect; both are corrected and covered by
behavioral tests. Pending imports, canceled/healthy cleanup exactly once, and unrelated JavaScript
errors are tested without diagnostic suppressions. All seven code findings are resolved; the final
review evidence identifies the implementation revision above separately from the pending visual gate.

The user funded a third visual correction round after the earlier 69.22% result. The final refined
capture scores **79.71%** at the approved viewport and **75.46%** at 1440px. The finish reviewer
scored all prescribed visual refinements resolved. The mechanical gate remains open for the retained
selector-plus-Apply interaction and additional ink in five cells. Exact cell attribution is unproven;
the captures show required facts, controls and feedback. The user has been asked to adjudicate these
remaining differences. No exception is assumed, no gate was forced, and no failed gate is called a pass.

The documenter recorded the local surface in `campaign-spine.md`; the incumbent `DESIGN.md` and
sidecar remain unchanged. Detector output was collected once. Its local type-size advisories do not
establish new system tokens; operational filter labels now meet the existing 0.875rem floor. The
stale thesis comment and inherited local back-arrow glyph were also corrected. Captures contain
synthetic test data; there are no new shipping raster assets.

## Provider diagnostic and assertion integrity

A PostgreSQL plan repeated the latest-decision anti-join for every participant, performing 216,000
inner scans for 60 players and taking 4.7 seconds, with no JIT or disk reads. Statistics changes made
the bad plan intermittent. `WorkingSet` now selects a nullable latest assignment ID per participant,
ordered by opening sequence and assignment ID, then joins its saved evidence. Eligibility and
correction policy are unchanged; discovery never narrows the source used to choose the latest record.

The scalar SQL prototype reduced maximum inner loops to 120. Actual EF aggregate and sibling
Needs-placement SQL were captured from DCP stdout and confirm the correlated ordered `LIMIT 1`
shape; see `provider-actual-ef-aggregate.sql` and the separate review. Prototype plan timings are
diagnostic evidence, not a guaranteed product latency budget. SQLite and PostgreSQL behavioral
suites pass the implemented construction.

The temporary 15-second settlement allowance used during diagnosis has been removed. The shipping
tests retain explicit URL/response synchronization and original five-second assertions, plus exact
counts, ordering, identity and empty/error expectations. The sort diagnostic records pointer events
on failure and asserts ascending-to-descending state; it never retries a toggle. No tests are skipped,
global timeouts/concurrency are unchanged, and no provider setting or migration was introduced.

## Guidance actually read

- `AGENTS.md`; matching `.github/instructions/` sources for C#, Blazor, UI/theme, services,
  validation, API contracts, EF tenancy, lifecycle, placement, functional core, testing and observability.
- Nova `add-blazor-ui`, `add-api-endpoint`, `add-feature-slice`, `add-domain-persistence` query
  construction, and their applicable lifecycle/HTTP/WASM references; `nova-testing` and its references.
- `impeccable` with new-work, shape, Operate, visualization and craft-floor references; the selected
  surface brief, `PRODUCT.md`, `DESIGN.md`; image generation and the separate reviewer instructions.
- Aspire orchestration, browser validation and Playwright recipes; .NET run-tests/filter syntax,
  code-testing-agent and Roslyn source-pairing workflow; code-review and local-review references.
- EF query optimization recipe for the observed loading failures, followed by read-only early/late
  plan analysis, scalar-query comparison and inspection of actual EF SQL. The initial tiny-fixture
  plans alone did not expose the eventual defect; the later plan evidence supersedes them.

`source-pairing.json` is the Roslyn inventory taken at the start of test work, not coverage or proof
that every behavior is exercised. Logs are retained locally under this directory and ignored by Git.
The implementation commit above is followed only by the documentation/evidence update that records it.
