# Issue #258 validation record

This change builds the Seasons directory: a member-readable `/club/seasons` surface that presents the current
season first with explicit currentness, then bounded, deterministically ordered past seasons with URL-backed
paging, plus the route vocabulary and entry points the season detail (#259) and advancement (#260) siblings
use. It adds no entities, EF configuration, migrations, endpoints, service contracts, or WASM clients.

## Tested revision

Base commit: `71fcd89b`. Validation ran against the working-tree implementation on that base. The 23
added/modified C#/Razor/CSS source and test files use the repository manifest format (UTF-8 without BOM,
sorted by repository path, one `path lowercase-file-SHA256` line per file, LF terminators including the final
line) with SHA-256 `3a013cff9623b0e311a60dec7033dc43265d4a3695c458567d3dc325166eba35`. Documentation
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

Divergence raised, not declined. The finding is correct about the authoritative source: the club-setup surface
brief states both "A creator establishes exactly one club and its first season" (`club-setup.md:17`) and
"Commit club and first-season creation atomically from the creator's perspective" (`club-setup.md:147`). The
shipped flow does not implement that: `Nova.UI/Features/Clubs/Pages/ClubOnboarding.razor.cs` contains no season
step and `ClubEndpoints.Complete` is documented as the post-creation cookie-refresh hop, while the only inline
season creator is `CampaignCreationService` — which satisfies
`.github/instructions/season-lifecycle.instructions.md` ("inline season creation is allowed only in the
no-current state") but not the brief. The directory therefore states the first-season state accurately for
what ships, and the brief-versus-implementation divergence is recorded as an open foundation gap against #163
owned by the club-setup slice rather than by this directory; it is also raised on issue #258. This slice must
not resolve it by inventing a season.

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

## Review round 2 — pull-request review

A pull-request review raised four threads; each was evaluated on its merits. Two were real defects in this
change and are fixed:

- **Persisted history state was not honored across prerender/attach.** The restore branch returned only when a
  successful history payload existed, so after a server-side history failure the interactive attach pass
  immediately re-issued the same request — the duplicate startup fetch `[PersistentState]` exists to prevent.
  The requested page is now persisted independently of the payload, and a recorded failure for the requested
  page counts as initialized exactly like a successful payload. Covered by
  `RenderDoesNotRefetchARestoredHistoryFailure`, which asserts the season service is not called at all: the
  test fails against the pre-fix guard and passes after it.
- **The authentication reconciliation was only tested for a same-role club change.** Two controlled
  delayed-response tests now cover the missing permission transitions:
  `RenderRemovesTheAdvancementEntryBeforeReplacementReadsCompleteAsync` (administrator → member withdraws the
  advancement entry while the replacement reads are still outstanding) and
  `RenderClearsThePreviousClubRowsAndRoutesWhenMembershipDisappearsAsync` (a clubless notification clears the
  previous club's rows before routing to access-denied).

One thread corrected the record rather than the code: the club-setup brief **explicitly** requires atomic
club-plus-first-season creation (`club-setup.md:17` and `:147`), so describing that finding as the reviewer's
inference misstated its source. The record now states the divergence plainly and attributes it to the
club-setup slice as an open foundation gap. The final thread's point — that the missing comp leaves an
acceptance criterion unmet — is accepted, and is now stated explicitly. A later review round caught that the
pull request body still carried a `Fixes: #258` closing reference that the host had appended at creation,
contradicting this record; that reference was removed so merging cannot close the issue as satisfied.

## Review round 3 — pull-request review

A second pull-request review raised three further threads; all were evaluated on their merits and are fixed.

- **A page change could not supersede an in-flight batch history read.** The reload batch and the
  identity-change batch loaded history under the batch token while `BeginRegionRetry` cancelled only the
  region source, and both requests shared the same `_reloadVersion`. A URL page change during a batch
  therefore left the older page-1 response still authoritative, so it could overwrite the newer page's rows
  while the URL and caption said page two. The history region now always loads under its own source, so a
  newer request supersedes an older one by cancellation while the batch version still supersedes both regions
  at once. `RenderKeepsTheNewerPageWhenABatchHistoryResponseArrivesLateAsync` reproduces the interleaving with
  a delayed batch response and fails against the previous ownership. The persisted-restore guard was tightened
  at the same time to require the restored payload's own page to match the requested page, so a snapshot
  written while a supersession was in flight can no longer skip a needed reload.
- **The reserved destinations' control was below the documented height.** "Back to Seasons" inherited the
  theme's shorter `.btn` (measured 38px), which is under Nova's 44px phone control contract. It is now a
  centered inline-flex control with the documented minimum, and
  `DirectoryAdministratorReachesTheReservedAdvancementDestinationAsync` asserts the measured height is at
  least 44px — the assertion fails when that rule is removed.
- **The reserved pages did not use the page-title typography.** The notice heading now carries DESIGN's
  page-title token (weight 700, line-height 1.2, `-.025em`) rather than Bootstrap's heading weight and the
  `-.035em` inherited from the incumbent reserved-section stylesheet, so the reserved destinations do not drift
  from the directory. The incumbent `ClubReservedSection` styling is deliberately left untouched as
  pre-existing.

## Review round 4 — pull-request review

A further review raised three suppressed findings. All three were valid and are fixed; the description
corrections it raised were applied to the pull request body.

- **A non-interactive retry left the directory.** The shared `RegionFailure` hardcoded its no-circuit
  fallback to the club overview. That is correct on the overview's own route but wrong here: during prerender
  or with scripting disabled, "Retry this section" navigated away to `/club` instead of retrying the
  directory, defeating this page's scripting-disabled recovery path. The component now exposes a
  `FallbackRoute` parameter — defaulting to the overview, so its existing caller is unchanged — and this page
  passes its own page URL, preserving `?page=` for the history region.
  `RenderKeepsNonInteractiveRetriesOnTheDirectory` and `RenderKeepsTheRequestedPageInTheHistoryRetryFallback`
  assert the rendered fallback, and both fail against the previous hardcoded route. The shared component's
  other caller was re-verified through `ClubOverviewBrowserTests` (5/5).
- **A partial failure could claim the current season was shown.** The history caption said "the current
  season is shown above" whenever a current row had been filtered out of the history page — including when the
  current region had failed and was showing its error instead. The caption is now gated on a successfully
  loaded current season with no error, and `RenderDoesNotClaimTheCurrentSeasonIsShownWhenItsRegionFailed`
  fails against the previous condition.

The same round flagged the pull-request description: it carried the host-appended `Fixes: #258` reference and
test totals that no longer matched this record. Both were corrected in the description rather than argued.

## Review round 5 — pull-request review

Three further suppressed findings, all valid and fixed.

- **Concurrent reads could render one season twice.** The current and history reads are independent and
  eventually consistent, and the history list originally removed only whichever row *its own* snapshot marked
  current. A club that advanced between the two reads could therefore mark a different season current in each,
  and the season shown as the current stop reappeared as a past row while the newer season was omitted
  entirely. The visible rows are now derived from the stored payload and rebuilt whenever either region
  publishes, excluding both the snapshot's own current row and the identifier actually displayed in the
  current band, so the reconciliation holds whichever response arrives last.
  `RenderReconcilesMismatchedCurrentIdentityBetweenRegionsAsync` reproduces the mixed snapshot with a delayed
  current read and fails against the previous derivation. Residual, deliberate: the current band keeps the
  current read's identity until the next load rather than letting the history snapshot overrule it, so a
  genuine advancement may need a refresh — but no season is ever misrepresented as past.
- **The phone target contract was enforced only vertically.** Season names accept any non-whitespace value, so
  a short name such as "A" produced a link far narrower than the documented `2.75rem`. The season link (and
  the reserved destination's control) now carry a minimum width and width; the new
  `DirectoryPhoneSeasonLinkMeetsBothTargetDimensionsForAShortNameAsync` seeds a one-character season name and
  fails on the width when the rule is removed. The previous long generated fixture name could not catch this.
- **The record and the pull-request description disagreed** on the unit count. Both now report the same
  verified number.

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

**This leaves one acceptance criterion unmet.** Issue #258 requires a comp-led decision and approved comp
evidence, and neither exists here. The disclosure above records the substitution; it does not discharge the
criterion. This pull request therefore must not close issue #258. The host appended a `Fixes: #258` closing
reference to this description when the pull request was created; review caught it and it has been removed, so
merging cannot close the issue as satisfied. Closing it requires either supplying the required comp and
re-reviewing this surface against it, or formally amending the issue's acceptance criteria to accept a
code-led build with wireframe-card evidence. That choice belongs to the repository owner, and the same
statement is recorded on the issue.

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
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | Passed: 3,198/3,198, zero skips. |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | Passed: 608/608, zero skips. |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | Passed: 190 total; 182 succeeded, 8 skipped (existing `NOVA_A11Y_SCREENSHOTS` opt-in captures), zero failures. |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build --filter-class "*SeasonDirectoryBrowserTests"` with `NOVA_A11Y_SCREENSHOTS=1` | Passed: 6/6, including the contrast, touch-target and state-capture evidence pass. |
| `node .agents/skills/impeccable/scripts/detect.mjs --json <seasons surface files>` | Zero findings. |
| `npm run check:contrast` | Not applicable: `Nova/scss/**` and `Nova/package.json` are unchanged by this slice. |

### The only browser failures seen were pre-existing and flaky

The final full browser run is green (190 total, zero failures). Two unrelated failures appeared in three
earlier runs and not in two others: `CampaignEvaluationCaptureBrowserTests.UnreadableCaptureCanLeaveExplicitlyWithoutErasingRecoveryDataAsync_002`
and `_004`, the two `wasm: True` theory cases, failing in the WebAssembly attachment probe. They are not
caused by this change: a clean `git worktree` at the unmodified base commit `71fcd89b`, built and run with the
same command, fails the identical two cases with the same probe failure, and they passed in a full run made
before the finish-review fixes. They were not suppressed, skipped, or weakened.

One further transient failure was observed once — `ClubOverviewBrowserTests.OverviewMobileSheetOpensCompleteDirectoryAndNoScriptShowsRoutesAsync`
— and that class passes 5/5 in isolation on the unchanged build and in the final full run.

### Limitations

- The evidence captures were produced by the opt-in `NOVA_A11Y_SCREENSHOTS=1` pass, not by a comp comparison.
- No comp exists, so no regional fidelity score is reported (see the substitution note).
- A region-failure capture was not produced: the failure states are proven by the bUnit retry and
  region-preservation cases rather than by a browser capture.
