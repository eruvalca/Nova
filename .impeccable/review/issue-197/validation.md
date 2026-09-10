# Issue 197 validation record

Status: implementation and code validation complete; PR publication authorized under the user's
score-threshold condition below. Automated comparison vetoes remain recorded, without a forced pass.

Baseline: `54c1da3abb16a6d02afbaaf14b59f9cbb870c8ff` on
`codex/issue-197-workspace-roster`. The initial checkout was clean and included #250 and #251.
Initial tested implementation: `49e8915bedbc431e1b0304fae2e87538af2a11df`. Tests ran before this
commit against the same production and test behavior; only the BOM/comment changes described below
followed test execution. The subsequent commit updates validation/review/PR-description evidence only.
Baseline solution build, format verification and 2,673 unit tests passed. The baseline build
reported the existing Sass warnings. No baseline integration/browser result is claimed.

## Initial behavior and evidence

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

The initial solution build passed with zero warnings/errors. Unit tests passed 2,783/2,783 and
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
remaining differences. The later publication instruction supersedes the earlier pending decision:
"If you've met the required image comparison score threshold and your work for this issue is complete,
create a PR using the repo's template." Both recorded overall scores exceed 72%, all prescribed
visual corrections are resolved, and implementation/behavioral checks are complete. The PR proceeds
under that score-based publication condition with the non-score vetoes disclosed. This is not an
assertion that the automated gate passed or that the reviewer changed its disposition from `fix`.
No gate was forced, and no failed gate is called a pass.

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

PR review identified two invalid auxiliary diagnostics: `eligibility-late-fixture.sql` and
`eligibility-late-lateral.sql` used outcome 22 instead of NotSelected (2). Their literals are now
corrected and the files explicitly identify themselves as unexecuted corrected diagnostic SQL.
The corresponding `*-plan.json` files have been withdrawn rather than edited into purported
execution evidence. Their former 3.312ms/2.392ms timings are not evidence for this change. The
separate early/late original plans, scalar/count proposals and actual EF SQL above use the correct
outcome and remain the supporting provider evidence. All SQL and plan outcome predicates were
checked for the same corruption. No production query contained that invalid literal.

The temporary 15-second settlement allowance used during diagnosis has been removed. The shipping
tests retain explicit URL/response synchronization and original five-second assertions, plus exact
counts, ordering, identity and empty/error expectations. The sort diagnostic records pointer events
on failure and asserts ascending-to-descending state; it never retries a toggle. No tests are skipped,
global timeouts/concurrency are unchanged, and no provider setting or migration was introduced.

## PR #252 — first review round

Review `5162991451` on `e12b886c23d5c006332b39717836ea0d8dc23a3e` contained four actionable
comments and no suppressed findings. All four are addressed in one review-round commit:

- Scripted narrow Route Markers retain the 36rem scrolling strip; the scripting-disabled fitted
  fallback remains separate. The existing browser journey now checks actual overflow and positive
  horizontal scroll as well as keyboard activation, full selected-marker visibility and preserved
  document scroll. Updated desktop/mobile captures use the corrected source.
- Direction-only sort inputs use display name with the requested direction in both SQL producers
  and WASM validators. Both omitted fields retain each read's original default. Ten new unit and
  six HTTP integration cases distinguish name/year ordering, player/assignment-ID ties, paging,
  omitted defaults, and portable client validation without emulating PostgreSQL text collation.
- The two invalid auxiliary SQL literals and their withdrawn plan claims are documented above.

The first full unit run also exposed bUnit's automatic timeout for an intentionally unconfigured
pending write in `NewCampaignIgnoresLateInputStorageFailureAfterClubChangesAsync`. Its controlled
handler now records the invocation through bUnit and owns the delayed task explicitly. The test
proves that the new club scope is enabled while the old write remains pending, then releases the
old failure and verifies that it cannot contaminate the new form. No production behavior, global
timeouts or existing error assertions changed. The separate local reviewer inspected this correction.

Round validation: solution build passed with zero warnings/errors; unit 2,793/2,793 passed in
35.912s; integration 586/586 passed in 3m20.355s, with no skips. Integration used the same production
source before the test-only pending-write harness rebuild. Browser 135/135 passed in 5m09.747s,
with no skips and all seven optional accessibility journeys enabled. Contrast and final format
verification passed. After the suites, only line-break formatting in two new test initializers and
documentation/evidence changed. The local code reviewer inspected all fixes and passing suites; the finish
reviewer marked the mobile route correction resolved and all seven fresh captures valid.
Round logs use the `round-1-` prefix. The live PR's Validation section records the resulting commit
SHA after the single round commit is created; no later source change is covered by these results.

Fresh captures score **79.68%** at the approved viewport and **75.47%** at 1440px. Both exceed 72%.
The two previously disclosed hero vetoes remain unchanged and unforced. The score-based publication
instruction above still applies; these numbers do not relabel the mechanical comparison as passed.

## PR #252 — second review round

Review `5163181715` on `c487608d27e93565604de7f7cc768966eee8e541` contained one actionable
comment (`3975931609`) and no suppressed findings. Authorized Closed detail now clears Active-only
eligibility and resets Roster paging before its read. The canonical URL replaces the current history
entry while retaining the pathname, participant, destination and other discovery/placement context.
The same rule handles initial entry, history/parameter refresh, lifecycle-conflict reconciliation and
persisted-state restoration. Reopening does not resurrect the removed filter. Wrong-scope or
wrong-campaign entry snapshots cannot clear an authorized Active filter.

Twelve new component cases cover these paths. The existing Closed browser journey now starts with
stale eligibility/page-two URL state, checks canonical navigation and the first 50 rows, reloads, and
continues its immutable archived-evidence and integrity-error checks. The local reviewer identified
and resolved an initial explicit-page-one URL mismatch before validation. Initial unit runs exposed
new fixture/assertion mismatches: bounded participant context legitimately omits the default page,
the test DOM may represent an unbound select as null, and the Closed workspace fixture must supply
Closed participant detail to avoid intentionally triggering lifecycle reconciliation. The corrected
tests preserve their paging/filter and cleared-selection assertions, including strict null eligibility
in the API call and absence from the URL. The existing dedicated initial-snapshot regression retains
the no-duplicate-startup-read assertion. No production exceptions, timeouts, skips or validation rules
were weakened.

The first browser run passed 134/135; an existing single-click route-history journey did not leave
Evaluate. A subsequent focused failure recorded the correct Place pointer/click target and browser
URL, while the rendered panel and active marker still showed Evaluate, with no page errors. This is
an observed URL/view divergence, not a missed click. Source review of the pinned .NET 10.0.12 runtime
identified a startup window between the circuit URI snapshot and renderer attachment in which
enhanced-navigation notifications have no attached recipient. The exact runtime URI at the original
failure was not captured; this remains a source-supported explanation of the observed divergence.

A once-per-workspace check now compares the runtime startup URI with the live browser location.
It dispatches the current browser URL synchronously through public enhanced GET navigation with
history replacement, constrained to a connected component owner and the exact campaign path.
Post-render effects wait for parameter delivery to acknowledge the new location, including unrelated
renders while dispatch is complete but notification is pending. A delivery latch handles rapid Back
navigation; pending scroll restoration carries navigation generation and authority ownership.
Five component cases cover overlapping renders, pending notification, later Back, obsolete queued
scroll/receipt effects and disposal. A controlled browser journey delays the fingerprinted workspace
module and models the missed notification with a browser-only URL update. Its first diagnostic run
timed out on an incorrect module interception glob; that run is not behavioral red evidence.

The second full run passed 133/135: an existing note keyboard flow did not await the drawer's
asynchronous initial-focus handoff, and a one-shot route measurement observed no overflow. Later
diagnostics proved the measured route element was detached (`isConnected=false`, all dimensions
zero). Geometry now resolves the current DOM node and measures it synchronously, including after
the awaited JS import. The keyboard test asserts the required Close focus before selecting and
verifying Add note focus; the exact original focus-event ordering was not captured. The separate
reviewer verified that single-click actions, strict overflow, positive horizontal scrolling, document
scroll preservation, assertion timeouts and all three Draft journeys remain intact. Diagnostic logs
remain local under the `round-2-` prefix; failed runs are not represented as passing validation.

Final round validation on the frozen source passed: solution build, zero warnings/errors (36.75s);
unit **2,810/2,810** (57.993s); PostgreSQL integration **586/586** (3m07.555s); browser
**136/136** (6m30.622s), all with zero skips. `NOVA_A11Y_SCREENSHOTS=1` enabled all seven optional
accessibility journeys, and all three Draft journeys remain unchanged. The browser pass includes
the modeled startup repair and real-module fragment/owner/path no-dispatch probes. Final format
verification exited 0 and contrast passed. An intermediate build/format check caught the new
browser method exceeding the existing size rule; extracting its independent boundary assertions
resolved that diagnostic without suppression. The separate local reviewer verified the frozen diff
and final evidence, resolving the completed-dispatch and fragment-only findings. Only documentation
changed after validation. All second-round work ships in one commit based on
`c487608d27e93565604de7f7cc768966eee8e541`; the live PR pins its resulting SHA.

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
The initial implementation and its evidence update precede the review-round corrections recorded
above. The live PR identifies the latest tested review-round commit.
