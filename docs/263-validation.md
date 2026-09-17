# Issue 263 validation

## Scope and tested revision

Base: `b8bc5532a8ddc9daafa3853660ab52bae5e8e279` (#283), plus this working-tree implementation.
The change replaces the first-100 screen with a 20-row directory, adds the tenant-scoped summary
read, routes the existing form under the same host, exposes member manual actions and preserves
correction returns. No schema or command contract changes. CSV stays hidden until #218; form
redesign and durable reload recovery remain #264; record/history replacement remains #216/#272.

The [application-input manifest](../.impeccable/review/issue-263/application-inputs.txt) records
SHA-256 values for every changed or new application/test file, relative to the base commit.
Current manifest SHA-256: `87D32690D3CFB30EB1769A878FBA1326DA71F92DC1F8AAA6B9298B0B485EAB19`
(38 files). The review-fix runs used opening revision `eb5031ff7aa2fafacb8e67ade8801f77db827513`
plus uncommitted changes to `Players.razor.cs`, `PlayerComponentsTests.cs` and the new
`PlayerComponentsTests.ReadFailures.cs`. Those exact inputs are included with this record.
The [opening validation record](https://github.com/eruvalca/Nova/blob/eb5031ff7aa2fafacb8e67ade8801f77db827513/docs/263-validation.md)
preserves the prior manifest `78A15BBD11FD203334FD934B53CEAFED1F285DA382FE848E348239FF72879746`
and its earlier unit/integration evidence-reuse comparison. All full suites were rerun for the fix;
only documentation/evidence bookkeeping changed afterward.
Runtime: .NET SDK 10.0.401, Aspire 13.5.4 and PostgreSQL 18.
Later design documentation, validation and evidence-manifest edits do not change runtime inputs.

## Guidance actually read

- `AGENTS.md`, `.github/PULL_REQUEST_TEMPLATE.md`, PRODUCT.md and DESIGN.md.
- Scoped instructions: C# conventions, service layer, validation, API endpoints, EF tenancy,
  Blazor architecture, UI design, Bootstrap theme, observability and testing.
- Feature recipes: add-feature-slice (input, result and WASM references), add-api-endpoint
  (routes, handlers, metadata, validation), add-blazor-ui (placement, render mode, lifecycle,
  parameters, forms), nova-testing (transition, component, SQLite, integration and browser evidence).
- Impeccable context, shape, new-work, visualize, craft-floor and document; imagegen;
  Aspire orchestration, Playwright CLI and manual validation workflow.
- Test generation, source pairing, run-tests, assertion-quality, test-analysis-extensions/.NET
  and test-gap-analysis. Source-pairing discovery ran once; no production mutations were executed
  for the static final test review.
- Player-intake brief, #163 delivery rules, #263 and successor issues, and #279's validation handoff.

## Contracts and decisions

- Roster ordering and literal name/exact Active-campaign tryout search are retained. Filters
  combine with Active-campaign tag application semantics. Offset arithmetic uses `long`; excessive
  positive pages return an empty page with their actual requested page number.
- Summary counts and ordered distinct years are club-wide bounded SQL reads; tag choices use the
  existing complete active-definition query. No choices are inferred from visible rows.
- Summary counts, graduation years and roster rows are separate reads and may observe different
  concurrent revisions. Client validation checks required fields and structural bounds, never
  count relationships between those reads. Failed evidence is unavailable, never synthetic zero.
- Discovery is URL-backed, defaults to Active/page 1, and normalizes malformed optional numeric
  values. Searches over 200 characters remain visible with feedback and dispatch no invalid query.
  Search uses a 350ms debounce and replaces history; explicit changes create history entries.
- `/players`, `/players/new` and `/players/{id}/edit` share the route host. Pending manual creation
  retains its exact operation and payload for the same actor/club, including administrator demotion.
  Actor/club changes clear prior rows, counts, choices, feedback, snapshots and correction context.
- All approved members can use directory Add/Edit/Archive/Restore. Draft return is separately
  administrator-only. The future CSV entry is hidden for every role until #218 supplies its destination.

## Behavioral evidence

| Boundary | Named evidence |
| --- | --- |
| Tenant/member summary, accurate active/archive counts, empty club and complete years | [PlayerServiceTests.Directory](../Nova.Unit.Tests/Players/PlayerServiceTests.Directory.cs): `SummaryCountsBothViewsAndAllYearsForOrdinaryMemberAsync`, `EmptyClubSummaryReturnsRealZeroCountsAndNoYearsAsync`, `SummaryDoesNotDeriveYearsFromFirstHundredPlayersAsync`, denial cases; [HTTP summary tests](../Nova.Integration.Tests/Http/PlayerRosterHttpTests.Directory.cs) |
| Combined filters, duplicate-name ordering and extreme page | `DirectoryCombinesLiteralNameYearAndActiveCampaignTagAsync`, `ExtremePageDoesNotWrapOffsetAsync`, complete-year paging case; retained [roster tests](../Nova.Unit.Tests/Players/PlayerServiceTests.cs) |
| Literal `%`, `_` and backslash on PostgreSQL | [PlayerSearchEscapingPostgresTests](../Nova.Integration.Tests/Data/PlayerSearchEscapingPostgresTests.cs) |
| Required JSON, null/malformed responses and structural limits | [HttpPlayerServiceTests.Directory](../Nova.Unit.Tests/Players/HttpPlayerServiceTests.Directory.cs), retained roster payload tests |
| Safe numeric/return parsing and complete nested context | [PlayersUrlStateTests](../Nova.Unit.Tests/Players/PlayersUrlStateTests.cs): directory/form/record/Draft composition, direct Place wrapping, unsafe local fallback, overlong search and query fingerprints |
| Empty, archived-only, filtered-empty, failed and unavailable-page states | [PlayerComponentsTests.Directory](../Nova.Unit.Tests/Players/PlayerComponentsTests.Directory.cs): `DirectoryDistinguishesEmptyResultsAsync`, regional retry and overlong-search cases |
| Independent completion, stale requests, matching snapshots and identity changes | Same file: held tag neighbor, old summary after club change, changed actor inside same club, mismatched snapshot query; retained [component tests](../Nova.Unit.Tests/Players/PlayerComponentsTests.cs) for startup overtaking, forbidden/transport completions and immediate club reset |
| Same-route changes and complete metadata | `MountedDirectoryAppliesChangedUrlAndRetainsSavedTagAsync`, `TagChoicesIncludeDefinitionsAbsentFromVisiblePlayersAsync` |
| Routed form ownership and exact pending commands | `LateUpdateCannotReplaceANewerRoutedFormAsync`, `SuccessfulEditRetryClearsObsoleteLoadErrorAsync`; [creation recovery tests](../Nova.Unit.Tests/Players/PlayerComponentsTests.CreationRecovery.cs) cover reopened pending success/duplicate/uncertainty, role demotion, denial, expiry and stale actor completion |
| 121 players, paging/history, mobile long content and 44px targets | [PlayersDirectoryBrowserTests](../Nova.Browser.Tests/PlayersDirectoryBrowserTests.cs): `DirectoryPagesBeyondOneHundredAndRestoresHistoryAsync`, `MobileDirectoryKeepsLongContentAndKeyboardTargetsWithinItsRegionAsync` |
| Native GET, modified links and member manual actions | Same class: `NativeDiscoveryWorksWithoutJavaScriptAsync`, `ModifiedAddLinkOpensANewTabWithoutMovingTheDirectoryAsync`, `OrdinaryMemberCreatesEditsArchivesAndRestoresThroughRoutedFormAsync` |
| Real nested Draft/Place and direct Place round trips | `DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync`, plus adapted [CampaignDraftBrowserTests](../Nova.Browser.Tests/CampaignDraftBrowserTests.cs) and `PlayerDetailReturnContextFollowsInteractiveQueryNavigationAndBrowserHistoryAsync` in [Place history scenarios](../Nova.Browser.Tests/CampaignPlaceInvalidStorageBrowserTests.cs) |
| Existing validation, receipt recovery and duplicate returns | Retained/adapted [PlayerFormBrowserTests](../Nova.Browser.Tests/PlayerFormBrowserTests.cs) and [recovery scenarios](../Nova.Browser.Tests/PlayerFormBrowserTests.Recovery.cs) |
| Delayed enhanced response preserves the mounted form draft | [PlayersDirectoryBrowserTests.Navigation](../Nova.Browser.Tests/PlayersDirectoryBrowserTests.Navigation.cs): `EnhancedFormResponsePreservesTheMountedFormAndItsDraftAsync` holds the successful destination response, types into the attached form, releases the response and verifies the retained value after navigation completion |

## Commands and current results

Aspire integration and browser runs are serial. Each suite starts its own AppHost; no extra host
was started for these runs. All tests use the compiled solution with `--no-build`.

| Check | Command / result |
| --- | --- |
| Full build | `dotnet build Nova.slnx --no-restore --no-incremental` — passed, 0 warnings/errors, 1m09.2s, current manifest. |
| Format | `dotnet format Nova.slnx --no-restore`; `dotnet format Nova.slnx --verify-no-changes --no-restore` — both passed for the review fix. |
| Focused read-failure regression | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build --filter-method '*ThrownDirectoryReadFailure*' --filter-method '*ObsoleteReadException*'` — **11 passed**, 2.8s, current manifest. |
| Opening component/URL/detail selection | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build --filter-class '*PlayerComponentsTests' --filter-class '*PlayerDetailComponentsTests' --filter-class '*PlayersUrlStateTests'` — 100 passed before final additional actor/tag cases; superseded by current full unit evidence. |
| Opening service/client selection | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build --filter-class '*PlayerServiceTests'` — 54 passed after empty-club fixture correction; superseded by current full unit evidence. |
| Full unit | `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — **3774 passed**, 45.3s, current manifest. |
| Full integration | `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — **674 passed**, 3m11.9s, current manifest. |
| Selected browser | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-class '*PlayersDirectoryBrowserTests' --filter-class '*PlayerFormBrowserTests' --filter-class '*CampaignDraftBrowserTests'` — **20 passed, 1 optional capture skipped**, 59.8s. Includes final runtime/markup/CSS; superseded by the full browser evidence for the final inputs. |
| Navigation confirmation | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-class '*PlayersDirectoryBrowserTests' --filter-class '*CampaignEvaluationCaptureBrowserTests'` — **26 passed**, 63.2s, before the final Cancel probe/diagnostic wrapper adjustment. Final full suite covers that adjustment. |
| Full browser | `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build`, with `NOVA_PLAYERS_EVIDENCE=.impeccable/work/issue-263/review-round-1-final` (resolved absolute path) — **217 passed, 0 failed, 8 optional captures skipped**, 7m26.7s, current manifest. |
| Static diff | `git diff --check` — passed. |

The selected browser scope covers directory/form contracts and the Draft consumer; the final full
suite also covers Place and evaluation siblings. Its eight existing optional capture skips are
`CampaignFormA11yEvidenceCapturesScreenshotsAsync`, `TeamDetailA11yEvidenceCapturesScreenshotsAsync`,
`DashboardA11yEvidenceCapturesScreenshotsAsync`, `CaptureSettledPlaceSurfaceForEvidenceAsync`,
`CloseoutA11yEvidenceCapturesScreenshotsAsync`, `PlayerDetailA11yEvidenceCapturesScreenshotsAsync`,
`LandingA11yEvidenceCapturesScreenshotsAsync` and
`A11yManualChecklistCapturesContrastAndTouchTargetEvidenceAsync`. Those require their existing
`NOVA_A11Y_SCREENSHOTS`/`NOVA_PLACE_EVIDENCE` flags. Directory captures were enabled; its responsive,
keyboard and 44px assertions ran. No behavioral scenario was skipped or weakened.
After navigation diagnostics, full solution format verification passed again, followed by formatting
and `--verify-no-changes --no-restore --include Nova.Browser.Tests/InteractionHelpers.cs
Nova.Browser.Tests/PlayersDirectoryBrowserTests.cs` for the final wrapper/Cancel delta; both passed.

## Material failure dispositions

- Early component fixture setup resolved NavigationManager before all services were registered;
  defer navigation until render. The component suite then passed.
- Early browser assertions used a pre-routing Add button selector, read Edit before the record
  destination rendered, or accepted a page-local row count before search settled. Updated semantic
  anchors and explicit destination/exact-result assertions prove the intended transitions.
- Search attachment probes retried Fill every 250ms, continually resetting the 350ms debounce.
  Attachment now uses a non-debounced local control, followed by one fill and exact URL wait.
  Mobile geometry waits for attached controls/CSS; the nested fixture correctly includes its historical
  campaign players (four directory pages). All affected selected browser scenarios now pass.
- The empty-club SQLite fixture omitted its explicit generated identifier and reached input validation
  instead of the empty aggregate. Seed club 102 consistently with the harness's other clubs; all 54
  selected service/client tests pass. No production behavior changed for this correction.
- PostgreSQL duplicate-query index selection: the unchanged query returned the correct duplicate,
  but the full-suite plan estimated 1,100 matches, used the tenant-only index and filtered 5,000
  rows. The unchanged recovery class passed 25/25 in isolation. Shared fixtures commonly use
  `2012-01-01`; a table-wide frequency/selectivity distortion is the supported inference (exact
  statistics were not captured), consistent with [PostgreSQL planner statistics](https://www.postgresql.org/docs/18/planner-stats.html).
  The query-plan fixture now uses `1951-05-12` for both tenants' matching targets and explicitly
  passes that date to both 5,000-row distractor seeders. Production SQL, `ANALYZE`, tenant identity,
  expected composite index, both index conditions, no residual filter and actual-row assertions
  are unchanged. The independent reviewer accepted this bounded fixture correction with no
  validation weakened; the final **674/674 full integration pass** confirms the correction in the
  suite that previously failed.
- The first full browser run passed 215 scenarios and skipped eight optional captures, but the
  existing Place record/history case still selected an inline Edit button that is now a native
  routed link. Its attachment probe now opens Archive and cancels without mutation or navigation,
  verifies the panel closes, and retains every same-record query and Back/Forward assertion.
  Other Edit button consumers were checked: they target unchanged team, campaign or note controls.
  Independent review found no coverage loss; this case passed in the next full run.
- That next full browser run passed 215 scenarios and skipped eight optional captures, but the
  unchanged 1,000-participant evaluation case expired its default five-second page-two assertion.
  The ARIA snapshot had `evalPage=2`, search `Player`, 1,000 campaign participants and the loading
  status “Finding players…”, with no finder alert. The unchanged responsive class passed 2/2
  in isolation (`dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build
  --filter-class '*CampaignEvaluationResponsiveBrowserTests'`, 30.9s). This does not prove contention
  or identify the underlying latency source. Both reads use the same unchanged finder/service;
  the fixture already declares a 15-second functional settlement bound and explicitly disclaims a
  five-second performance target. Page two now uses that same one-shot success-or-alert bound,
  requires zero alerts, retains its exact page assertion and additionally requires 20 results.
  Search/count assertions and benchmark recording are unchanged; navigation is not retried and no
  global timeout changed. Separate quality-control review accepted the inconsistent-bound correction
  without findings. The final full suite passes this case.
- The next full run passed 212 scenarios, skipped eight optional captures and failed four navigation
  checks. The new-tab evaluation case had already created the correct new page but asserted its
  heading before the initial document finished loading. Both new-tab sibling tests now wait for
  `DOMContentLoaded`, retaining the destination and original-tab assertions, without a timeout override.
- Three directory failures exposed incomplete navigation evidence: correction captured `page.Url`
  immediately after Next, history observed page six after Forward, and the member form disappeared
  between a visibility check and typing. The unchanged directory/evaluation-capture classes passed
  25/25 in isolation (63.8s), which was diagnostic only. The framework
  [notifies interactive runtimes before requesting destination HTML](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Components/Web.JS/src/Services/NavigationEnhancement.ts#L181-L205),
  so an early location-driven render is not completed enhanced navigation. The new shared helper
  observes a fresh navigation start and end; exact page, URL and context assertions remain. The
  three scenarios capture URL/history/events/forms/links/ARIA on later failures too, and diagnostic
  failures cannot replace the original exception. Handler attachment remains separately proved by
  Archive/Cancel or Cancel/reopen; a reviewed interim blank-submit probe was replaced because it
  could submit natively before attachment. The no-JavaScript scenario remains unchanged.
  The held-response regression passed and the revised 26-case selection passed. The original
  pre-attachment disappearance's precise internal ordering was not captured; no application defect
  was established, and the correction addresses the demonstrated missing document/attachment
  boundaries. All three scenarios and the held-response regression pass in the final full suite.
- Two diagnostic invocations selected zero tests (an intersecting class/method filter, then a new
  method before a successful rebuild). They provide no evidence. Corrected filters/builds ran the
  named scenarios. The intermediate method-length build error was fixed by extracting the correction
  navigation helper; no analyzer suppression was added.

## Independent reviews

Fresh local code reviewer `/root/directory_code_review` inspected the complete diff, authorization,
summary SQL, HTTP/JSON boundaries, asynchronous ownership and command retention. Final verdict:
**no remaining findings** after these eight fixes. A separate follow-up accepted the query-plan
fixture correction above; its failure is closed by the final full integration result. The reviewer
also accepted the Place history attachment correction without findings: it changes local state only,
confirms cleanup and retains all mounted-record query/history assertions.
The evaluation scale adjustment also passed separate quality-control review: its existing functional
bound is applied consistently, errors still fail immediately and result assertions are stronger.
The final popup/navigation/helper review has **no remaining findings** after the safe Cancel probe
and best-effort scenario diagnostics. It distinguishes document completion from interactive attachment
and does not claim to reconstruct the missing original pre-attachment trace.

| Finding | Disposition and evidence |
| --- | --- |
| Late update could erase a newer form | Route generation owns update/archive/restore presentation; controlled success/failure regression passes. |
| Direct Place → record → Edit lost context | `FromReturnDestination` wraps safe non-directory destinations; helper and real browser round trips pass. |
| Completed regions waited for unrelated reads | Each owned read requests rendering; held-tag regression proves independent roster/summary presentation. |
| Link interception broke modified clicks | Ordinary anchors restored; native no-JS and Ctrl-click browser cases pass. |
| Edit retry retained old failure | Retry clears error, cancels prior work and advances ownership; retry regression passes. |
| Approved comp artifacts remained ignored | Narrow exceptions moved below broad ignore rule; approved image and provenance are visible to Git. |
| Reopened pending Add hid its result | Feedback follows the exact operation ID on the visible create form; success/duplicate/uncertain cases pass. |
| Debounce retry probes could never settle | Non-debounced attachment and one-fill exact-URL checks; selected browser suite passes. |

Final test review is static, not a claimed mutation run: assertions cover values, collections,
ordering, protocol problems, negative side effects and state transitions. Removing tenant guards,
offset widening, query ownership, saved-tag retention or command identity would change named
assertions. No new behavioral test is assertion-free or limited to a presence check. The optional
capture fixture intentionally produces images in addition to its visible-content assertion.

## Design evidence

User approved **A — Compact controls** on 2026-09-17. The [brief](../.impeccable/surfaces/player-intake.md)
records its actual-shell frame, composition check, direction contract and staged CSV handoff.
[Approved comp](../.impeccable/mocks/issue-263-a.png) and [exact prompt/provenance](../.impeccable/mocks/issue-263-a.png.json)
are retained. No shipping raster asset is required by this semantic directory.

The isolated phase workspace preserves unrelated root #197 state. Hero gate passed **78.67%**;
responsive/final comparison passed **78%**, with no missing/contradicted final region. Detector
returned `[]`; its root-state advisory belongs to #197, while isolated #263 gates passed unforced.
The [finish review and verdict](../.impeccable/review/issue-263/finish-review.md) records all five review
sections and resolves the two documentation findings. Final **ship** is scoped to the scored fixes.
The fresh documenter recorded the built behavior in DESIGN.md and `.impeccable/design.json` without
changing tokens or extending unrelated incumbent guidance.

Curated captures: [desktop 1440](../.impeccable/review/issue-263/captures/desktop.png),
[desktop 1280](../.impeccable/review/issue-263/captures/desktop-1280.png),
[mobile 390](../.impeccable/review/issue-263/captures/mobile.png), with
[capture manifest](../.impeccable/review/issue-263/capture-manifest.json) and
[comparison report](../.impeccable/review/issue-263/comp-report.json).
The opening full-suite mobile capture is byte-identical to the reviewed mobile capture
(`5A6AA880F1CA3451E0CD7CD12F9D78235711366B7F97A99B978D5A123418BE68`). No markup or CSS changed
after the reviewed captures. Before opening, later application-input changes were confined to browser
tests. The review fix changes only read exception handling and its tests, so the design inputs and
reviewed composition remain applicable; the full browser suite was rerun for the changed behavior.

## Instruction and skill retrospective

The user requested this review against Microsoft's
[instructions-hygiene article](https://devblogs.microsoft.com/dotnet/instructions-hygiene-what-frontier-models-still-need-you-to-say/).
The review applied its keep/remove/move/verify lens: retain consequential local constraints, place
procedures in the existing recipe, and avoid turning every implementation mistake into a new rule.

| Lesson from this change | Guidance decision |
| --- | --- |
| An independently completed region stayed hidden behind other reads | Correct the scoped Blazor `StateHasChanged` rule to permit intermediate rendering. It previously contradicted the existing lifecycle recipe; no new workflow is needed. |
| Visible form/URL state could precede enhanced document completion, which itself does not prove attachment | Refine the scoped testing rule and existing nova-testing browser reference. Document the shared navigation/diagnostic helpers, reversible non-submitting probes, and new-tab document wait with their limits. |
| Late command results, pending payload retention, correction context, native links, separate-read counts and debounce retries | Keep the existing ownership, navigation, HTTP and test guidance. These were application/review findings, not missing instructions; the regression tests above supply enforcement. |
| Query-plan fixture selectivity, ignored comp artifacts and unsuccessful diagnostic invocations | Keep their specific failure dispositions in this record and the corrected fixtures/artifacts. Do not add an unproven general workaround or a repo-wide execution ritual. |

No new skill, top-level router or `AGENTS.md` expansion is warranted. The additions belong to the
existing browser procedure; its skill entry point already routes readers there. The scoped rule
files and canonical `.agents/skills/nova-testing` are shared by Codex and Copilot. There is no
`.github/skills/nova-testing` duplicate to synchronize.

Guidance reread for this retrospective: `AGENTS.md`; Blazor/testing instructions; add-blazor-ui
and its lifecycle reference; nova-testing and its browser/integration references; the WASM client
reference; and the installed `skill-creator/SKILL.md`. The article was retrieved through web search
after its direct page returned 403.

Validation: `python C:/Users/eruva/.codex/skills/.system/skill-creator/scripts/quick_validate.py
.agents/skills/nova-testing` passed basic skill validation. Helper behavior and the held-response,
ordinary-member, history and modified-click scenarios were inspected against the revised procedure.
This is documentation verification, not a new agent-performance experiment or application test run.
Whitespace checks passed for all four guidance/evidence files; all 33 local Markdown link targets
resolve. All 37 application/test hashes still match the final manifest, so the recorded suite
results remain applicable; this documentation-only follow-up did not rerun the suites or alter gates.

Independent guidance reviewer `/root/directory_code_review` found one overgeneralization:
attachment probes must apply only to behavior dependent on Blazor handlers, and drawer-row probes
must not be described as buttons. Both scoped instructions and the recipe were corrected, preserving
native no-JavaScript coverage and requiring `type="button"` only for button probes. The final
three-file guidance review has **no remaining findings**. This was a documentation/code consistency
review; no stronger claim about the original missing pre-attachment trace is made.

## PR review loop

The user requested thorough `code-review` rounds, one fix commit per round, and fresh review after
each push. The installed skill, its PR procedure, doctrine and checklist were read; its pending-review
then Comment submission workflow was used on [draft PR #284](https://github.com/eruvalca/Nova/pull/284).
Review includes the full source/test/guidance diff and related callers. Binary captures and generated
comparison output are excluded from code review; their separate design review remains above.

[Round 1](https://github.com/eruvalca/Nova/pull/284#pullrequestreview-5239996524) at `eb5031ff` found one
Medium, Verified defect, independently corroborated by `/root/pr284_review_round1`: direct server
database exceptions escaped regional recovery and could fail the page. The fix uses a deferred,
logged read boundary for roster, summary and tags, covering direct database/transport failures and
database retry wrappers. Owned cancellation remains cancellation, stale results cannot publish,
and active unrelated programming exceptions still propagate. Mutation settlement is unchanged.

| Requirement | Evidence |
| --- | --- |
| Thrown reads leave successful regions visible, conceal provider details, log failures and retry only the failed region | `PlayerComponentsTests.ThrownDirectoryReadFailurePreservesNeighborsAndRetriesOnlyItsRegionAsync`: nine cases for three regions, synchronous/faulted-task provider failures, and EF retry-exhaustion wrappers. |
| Obsolete failures and cancellation cannot replace the new club's data | `PlayerComponentsTests.ObsoleteReadExceptionCannotReplaceNewClubSummaryAsync`: two cases retain the replacement summary and snapshot ownership without an alert. |

Both tests are in [PlayerComponentsTests.ReadFailures.cs](../Nova.Unit.Tests/Players/PlayerComponentsTests.ReadFailures.cs).
Before the production fix, the initial eight-case regression run produced **seven failures and one
passing cancellation control**, with uncaught `NpgsqlException` stacks through the original loader.
After the fix, all eight passed; the final eleven-case run adds real EF retry wrappers and passes.
An initial test compile error was corrected by explicitly constructing the summary DTO. The first
overbroad catch failed CA1031; the catch was narrowed without a suppression. Separate fix review
accepted the final filter, ownership behavior and assertions with **no findings**.

The current validation table records all rerun gates. Full browser scope was selected because the
read boundary participates in startup, navigation and refresh; suites ran serially. Fresh reviews
after the fix push are submitted to the PR against their exact head revision. Any new findings must
be resolved in the next single fix commit before the review loop is complete.

## Delivery gates

No migration is required. Build, format, full unit/integration and final full-browser gates pass;
the code, quality-control and design reviews are recorded above. All 38 manifest inputs were rechecked
after the review-fix browser run; only documentation/evidence bookkeeping changed afterward.
Draft PR #284 is open on `codex/263-players-directory`; no merge has been performed. Later application/browser
input changes require fresh evidence, and unit/integration suites must rerun before merge under the
repository stage gate.
