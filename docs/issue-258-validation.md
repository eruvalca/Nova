# Issue #258 validation record

This change builds the Seasons directory: a member-readable `/club/seasons` surface that presents the current
season first with explicit currentness, then bounded, deterministically ordered past seasons with URL-backed
paging, plus the route vocabulary and entry points the season detail (#259) and advancement (#260) siblings
use. It adds no entities, EF configuration, migrations, endpoints, service contracts, or WASM clients.

## Tested revision

Base commit: `71fcd89b`. Validation ran against the working-tree implementation on that base. The 21
added/modified C#/Razor/CSS source and test files use the repository manifest format (UTF-8 without BOM,
sorted by repository path, one `path lowercase-file-SHA256` line per file, LF terminators including the final
line) with SHA-256 `39ddb4ac154c32804a02c6c2e66397aa63eda20e14a4a46d364d16e530aa1bdd`. Documentation
(`docs/`, `.impeccable/`), `.gitignore`, and the curated evidence captures are excluded from the fingerprint
so this record can be completed after execution.

## Behavioral evidence

| Requirement | Evidence |
| --- | --- |
| Current season leads with explicit current identity; facts come only from the delivered contract | `RenderLeadsWithTheCurrentSeasonAndShapesAdvancementByRole` asserts the `Current season` heading, the `Current` status chip, the name, the culture-formatted date window, and the season-detail link |
| The current season appears exactly once and never repeats in history | `RenderShowsTheCurrentSeasonOnceSoItNeverRepeatsInHistory`, `DirectoryPagesLongHistoryAndKeepsTheCurrentSeasonOffEveryPageAsync` (26-season page one renders 19 history rows and one current band) |
| First-season and no-current-season states stated explicitly, not inferred from an empty list | `RenderStatesTheFirstSeasonStateWhenNoSeasonIsRecorded`, `RenderStatesTheRecoveryStateWhenRecordedSeasonsHaveNoCurrentOne`, `DirectoryStatesTheFirstSeasonStateWhenTheClubHasNoSeasonAsync` |
| Bounded, deterministically ordered paging from the URL | `RenderPagesHistoryFromTheUrlAndKeepsTheCurrentSeasonOffEveryPage`, `RenderOffersRecoveryToTheFirstPageWhenTheRequestedPageIsBeyondHistory`, `DirectoryPagesLongHistoryAndKeepsTheCurrentSeasonOffEveryPageAsync`, plus the seeded page-2/paging evidence capture |
| Paging and authority races: stale results cannot repopulate the directory | `RenderDiscardsStaleResultsWhenTheClubChangesAsync` (a superseded club's slower response is discarded after an identity change) |
| Per-region loading, error, and retry | `RenderPreservesTheLoadedRegionWhenTheOtherFails`, `RetryCurrentReloadsOnlyTheCurrentRegion` (the current-region retry leaves the history region's single load intact) |
| Role-correct affordances: members read, only administrators advance | `RenderLeadsWithTheCurrentSeasonAndShapesAdvancementByRole`, `DirectoryMemberReadsCurrentSeasonAndHistoryWithoutAdministratorScopeAsync`, `DirectoryAdministratorReachesTheReservedAdvancementDestinationAsync` |
| URL canonicalization and scripting-disabled navigation | `RenderCanonicalizesAMalformedSeasonPageInTheUrl`, `RenderKeepsTheFirstSeasonPageFreeOfAQueryString`, the no-script anchors in `DirectoryMobileKeepsTouchTargetsKeyboardFocusAndNoScriptLinksAsync` |
| Phone layout, touch targets, and keyboard focus | `DirectoryMobileKeepsTouchTargetsKeyboardFocusAndNoScriptLinksAsync` (44px link, keyboard focus, phone-width horizontal-overflow guard), `AssertTouchTargetAsync` in the evidence pass |
| Authorization route classification stays aligned | `ClubShellContractTests.CanonicalRoutesAreStableAndAdministratorRoutesAreRecognized`, `RedirectToLoginOrAccessDeniedTests.OnInitializedAsyncNavigatesDemotedMemberToClubNoticeOnAdministratorRoute`, `ClubOverviewBrowserTests.OverviewMemberDirectoryShowsMemberRoutesAndDeniedAdministratorRouteShowsPermissionNoticeAsync` |
| Interactive render mode is declared for the page | `SeasonDirectoryComponentTests.RouteDeclaresInteractiveAutoAndKeepsLogicInCodeBehind`, `ClubShellContractTests.ClubRouteComponentsKeepLogicInCodeBehind` |
| Contrast on the lit stop's tinted field | `AssertDirectoryContrastAsync` composites the stacked backgrounds instead of assuming white. The guard was demonstrated to fail before the fix: with plain link teal the assertion fails, and with the theme's on-subtle emphasis token it passes |

`SeasonDirectoryComponentTests` holds the bUnit cases; `SeasonDirectoryBrowserTests` holds the browser cases.
The browser evidence pass also produced the curated captures in `.impeccable/review/issue-258/`.

## Separate review

The shipped `impeccable-finish-reviewer` reviewed the built surface, with disposition **fix**, and validated
both submitted captures as valid evidence. It reported one persistence finding, six material fixes, and one
craft observation. Resolved: the missing page-title step, the solid `Current` badge, the primary advancement
button below the control height, the first-season copy, and the first-viewport focus rectangle. The focus
rectangle was identified as the Club shell's own post-navigation heading focus (`ClubShell.razor.js` focuses
the hall's `h1`), so it was not suppressed; the evidence pass now captures unfocused and the ring measures 0
pixels across all six captures (768 and 1314 before).

Declined with evidence: the reviewer inferred from the club-setup surface brief that club creation
establishes the first season. The shipped flow does not:
`Nova.UI/Features/Clubs/Pages/ClubOnboarding.razor.cs` contains no season step and `ClubEndpoints.Complete`
is the post-creation cookie-refresh hop; the only inline season creator is `CampaignCreationService`, matching
`.github/instructions/season-lifecycle.instructions.md`. The browser suite proves a freshly created club
renders the first-season state.

Open and disclosed: no approved comp exists for this surface (see the substitution note below), so the
comp-fidelity promise is unmet rather than waived by a comp round.

A separate local review of the complete diff was run for the authorization and asynchronous-state-ownership
changes (a fresh read-only reviewer, given the diff, the intended behavior, the constraints, and the test
evidence). Disposition: two findings, both resolved.

1. **Coverage gap (fixed).** `ClubOverviewComponentTests` asserted the new member-visible directory entry
   with a whole-page `href="/club/seasons"` check, which the Club shell's own nav link also satisfies now
   that Seasons is member-visible — so the assertion could not fail. Both affected assertions are now scoped
   to the current-season waypoint. Verified by regression: deleting the waypoint entry now fails two tests,
   where before the fix the suite stayed green.
2. **Over-claimed evidence (fixed).** The record said the committed evidence included the static detector
   output, but `detector.json` was still covered by the `.impeccable/review/**` ignore rule. A narrow
   exception was added, matching the issue-198 precedent, so the committed claim is now true.

The reviewer also confirmed no material findings in four areas it examined independently: the authorization
classifier across both consumers (including that the member read does not 403 at the API layer, which requires
`RequireClubMember` while only the mutations require administrator scope); asynchronous state ownership
against the shipped `ClubOverview`/`Campaigns` precedents (it could not construct an interleaving that applies
a superseded response, strands a region loading, or republishes a previous club's data); paging suppression
(it confirmed by arithmetic that no past season is skipped or duplicated at any boundary, and that
`?page=2147483647` is clamped); and route/link integrity (three `/club/seasons`-prefixed routes with no
duplicate, `start-next` not shadowed by the `:long` constraint, and every destination a real anchor). It also
noted, as a non-defect with no reachable consumer, that the URL-driven history reload does not call
`PersistState()`; the persisted snapshot is only read on fresh instantiation, which always re-primes it.

## Comp-round substitution (disclosed)

The issue requires one comp-led surface decision, an approved comp, and a curated evidence packet containing
it. This round is **code-led and has no comp**: the machine provides no image-generation tool and no
`OPENAI_API_KEY`, so no comp could be produced. The surface decision was authored and served at
`--scope surface --mode operate` (surface seed `c157253d`, wireframe cards for *Season route*, *Stop strip over
register*, and *Pinned register*), and the page went unanswered. The session was explicitly unattended —
the user's words: *"The user is not available to respond and will review your work later. Work autonomously
and make good decisions."* — so the build took the roll's assigned lead, **Season route**, which also carries
the two-region directory composition the user approved before implementation. `.impeccable/config.local.json`
records `buildPath: code` for this machine only; the committed default in `.impeccable/config.json` is
unchanged. No comp-fidelity gate is claimed, and no comp-diff score is reported.

Curated evidence committed: six state captures (administrator desktop and mobile, member, first season,
recovery, and a middle page of a long history) in `.impeccable/review/issue-258/`, the static detector output,
and the surface brief with the direction contract and finish-review record. The stale
`.impeccable/build/state.json` in this tree belongs to issue #197's surface and is not this surface's record.

## Guidance actually read

- `AGENTS.md`; `.github/instructions/` — blazor architecture, ui design, csharp conventions, service layer,
  API endpoints, validation, ef-core tenancy, season lifecycle, testing, bootstrap theme.
- `.agents/skills/` — `add-blazor-ui`, `nova-testing`, and `impeccable` with the new-work, craft-floor and
  finish-review paths; the surface and product sources `DESIGN.md`, `PRODUCT.md`,
  `.impeccable/surfaces/club-setup.md`, and `.impeccable/surfaces/seasons-directory.md`.

## Execution results

| Command | Result |
| --- | --- |
| `dotnet build Nova.slnx` | Passed; zero warnings, zero errors. |
| `dotnet format Nova.slnx --no-restore --verify-no-changes` | Passed (exit 0, no output). |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | Passed: 3,190/3,190, zero skips. |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | Passed: 608/608, zero skips. |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | 189 total: 179 succeeded, 8 skipped (existing `NOVA_A11Y_SCREENSHOTS` opt-in captures), 2 failed. |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-class "*SeasonDirectoryBrowserTests"` with `NOVA_A11Y_SCREENSHOTS=1` | Passed: 6/6, including the contrast, touch-target and state-capture evidence pass. |
| `node .agents/skills/impeccable/scripts/detect.mjs --json <seasons surface files>` | Zero findings. |
| `npm run check:contrast` | Not applicable: `Nova/scss/**` and `Nova/package.json` are unchanged by this slice. |

### The two browser failures are pre-existing

Both are `CampaignEvaluationCaptureBrowserTests.UnreadableCaptureCanLeaveExplicitlyWithoutErasingRecoveryDataAsync_002`
and `_004` — the two `wasm: True` theory cases — failing in the WebAssembly attachment probe. They are not
caused by this change: a clean `git worktree` at the unmodified base commit `71fcd89b`, built and run with the
same command, fails the identical two cases with the same probe failure. They also failed before the
finish-review fixes were applied. The same two cases pass in some runs and fail in others on both revisions,
which is the documented hydration/probe sensitivity of that class; they were not suppressed, skipped, or
weakened.

One additional transient failure was observed once in a full run —
`ClubOverviewBrowserTests.OverviewMobileSheetOpensCompleteDirectoryAndNoScriptShowsRoutesAsync` — and that
class passes 5/5 in isolation on the unchanged build.

### Limitations

- The evidence captures were produced by the opt-in `NOVA_A11Y_SCREENSHOTS=1` pass, not by a comp comparison.
- No comp exists, so no regional fidelity score is reported (see the substitution note).
- A region-failure capture was not produced: the failure states are proven by the bUnit retry and
  region-preservation cases rather than by a browser capture.
