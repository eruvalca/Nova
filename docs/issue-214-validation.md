# Issue #214 validation record

This change supplies effective current-season roster and Active Needs-placement reads, separate
Closed campaign records, and shared counts for existing consumers. The contract and downstream
handoff are documented in [placement-decision-foundation.md](placement-decision-foundation.md).

## Tested revision

Base commit: `be8f5b8c0ab58cafdfcab95c046b1dc41071cd0c`. Validation was run against the working-tree
implementation on that base. The 42 added/modified C# source and test files at test time had manifest SHA-256
`5bf599fb5eb1ddb15b85bc4b8ebbe0a12d5eb98e4e1f5804a296a50e313ee545`.
The manifest is UTF-8 without BOM, sorted by repository path, with one `path lowercase-file-SHA256`
line per file and LF terminators including the final line. Documentation is excluded from the
fingerprint so this record can be completed after execution.

PR preparation removed one extra trailing blank line from each of
`Nova.Integration.Tests/Data/EffectivePlacementPostgresTests.cs` and
`Nova.Integration.Tests/Http/EffectivePlacementHttpTests.cs`. Byte comparison verified that only those
trailing newline bytes changed. The resulting 42-file delivery manifest is
`a0e67a2ecde55bed76eb5ff08011de5ee019eded48f6db3aed708e4bc83b8170`.
All other changes after the test runs were Markdown-only; the application suites were not repeated
for this whitespace cleanup.

## Behavioral evidence

| Requirement | Evidence |
| --- | --- |
| First enrollment, supplemental enrollment, local versus prior NotSelected | `SupplementalEnrollmentPreservesEffectiveAssignmentAndLocalTokenAsync`, `LocalNotSelectedResolvesWhilePriorNotSelectedAndFirstEnrollmentNeedPlacementAsync` |
| Withdrawals remain unavailable for both roles; archived players unavailable | `WithdrawnAndArchivedPlayersRemainUnavailableRegardlessOfOverrideAuthorityAsync` |
| Latest saved opening sequence wins; technical enrollment does not supersede | `LatestOpeningSequenceWinsOverCampaignIdTimestampAndTechnicalEnrollmentAsync`, `DraftSavedRowsCannotSupersedeOpenedDecisionsEvenForAdministratorsAsync` |
| Teamless/invalid latest decisions suppress older assignments and retain correction evidence | `LatestTeamlessDecisionSuppressesOlderAssignmentAsync`, `InvalidLatestAssignmentRetainsEvidenceAndNeedsPlacementWithoutFallbackAsync` |
| Authoritative current season, absent/advanced season, idle rosters | `CurrentSeasonPointerControlsRosterRatherThanMostRecentCampaignAsync`, `AdvancingSeasonResetsPriorWithdrawalToNeedsPlacementAsync`, `IdleCurrentSeasonStillHasEffectiveRosterAsync`, `CurrentRosterWithoutSeasonReturnsExplicitNullAndEmptyPageAsync` |
| Closed campaign-local history and original token/actor/team remain independent of later decisions | `ClosedRosterPreservesLocalOutcomeTeamAttributionAndTokenAfterSupersessionAsync`, `ClosedHistoryRetainsArchivedParticipantsAndTeamlessSavedOutcomesAsync` |
| Closed lifecycle and rows share one snapshot under concurrent reopening | `ClosedRosterSnapshotRetainsClosedLifecycleAndDecisionWhenReopenedBetweenReadsAsync` (PostgreSQL interceptor suspends the read after lifecycle observation, commits a reopen/replacement independently, then verifies the original response) |
| Zero Needs placement does not waive explicit local close outcomes | `ZeroNeedsPlacementDoesNotWaiveMissingCampaignLocalCloseOutcomeAsync` |
| Shared dashboard/list/attention count and distinct team contribution | `CampaignDashboardAndAttentionAgreeOnEffectiveNeedsPlacementAsync`, `TeamCountsSeparateEffectiveRosterFromActiveCampaignContributionAsync` |
| Stable duplicate-name pages, filters, unfiltered totals, bounded reader growth | `WorkingFiltersPreserveUnfilteredCountsAndStablePageBoundariesAsync`, `TeamAndTryoutFiltersUseEffectiveAssignmentAndLocalEnrollmentAsync`, `DuplicateNamesPageByPlayerIdWithUnfilteredWorkingTotalsAndConstantReaderCountAsync` |
| Literal PostgreSQL percent, underscore, and backslash search | `PostgreSqlSearchTreatsWildcardAndEscapeCharactersAsLiteralAsync` |
| Tenant isolation, ordinary-member access, Draft invisibility, lifecycle conflicts | `CrossTenantIdentifiersRemainNonDisclosingAsync`, `CrossTenantPlayerReferencesCannotLeakThroughTenantOwnedParticipationsAsync`, `OrdinaryMemberReadsPopulatedPlacementBodyWithOmittedOptionalQueriesAsync`, `CampaignReadHidesForeignAndDraftCampaignsAndReportsVisibleLifecycleConflictAsync` |
| Anonymous/nonmember rejection, validation and trace-bearing problems | `PlacementReadRejectsAnonymousCallerAsync`, `PlacementReadRejectsAuthenticatedCallerWithoutApprovedMembershipAsync`, `PlacementReadsRejectInvalidExplicitPagingWithTraceBearingValidationAsync` |
| Strict WASM bodies without client collation assumptions or cross-statement total assumptions | `RequiredSuccessBodyRejectsEmptyNullMalformedAndUnexpectedJsonAsync`, `PageAcceptsDatabaseNameCollationAndEventuallyConsistentTotalsAsync`, `WorkingRequiresExplicitCampaignLifecycleStatusAsync`, `SameCampaignEffectiveDecisionCannotHideItsLocalDecisionAsync`, `TeamUnavailableCorrectionCannotClaimVisibleSavedTeamAsync` |

These cases live in the five `EffectivePlacement*`/`HttpEffectivePlacement*` test classes in
`Nova.Unit.Tests/Campaigns`, `Nova.Integration.Tests/Data`, and `Nova.Integration.Tests/Http`.
Reader-growth assertions use `CountingCommandInterceptor`; they describe asynchronous relational
reader commands, not all database commands. SQLite verifies service behavior; PostgreSQL verifies
provider translation, literal search, and the snapshot race.

## Separate review

A fresh read-only reviewer inspected the complete production/test diff and applicable guidance.
Two client defects were found and fixed: an omitted Active campaign lifecycle defaulted to Active,
and same-campaign effective evidence could incorrectly coexist with a null local decision. Both
have regression tests. The reviewer also identified the need for a contended Closed snapshot test;
the deterministic PostgreSQL test above fills that gap. Final source review found no remaining
actionable findings. A follow-up review also accepted the fixture corrections and the existing
membership-lock test's synchronization correction described below. No empirical mutation run is claimed.

The only new analyzer suppression is scoped to the SQLite search expression: SQL `UPPER` has no
culture or `StringComparison` overload. PostgreSQL uses escaped `ILIKE`. No checks were disabled,
new test skips introduced, or validation weakened. Existing fixtures were corrected to set current-season
identity/opening sequence explicitly and to classify local NotSelected as resolved.

## Guidance actually read

- `AGENTS.md`; `.github/instructions/` C# conventions, service layer, API endpoints, validation,
  EF tenancy, season lifecycle, placement decisions, functional core, testing, observability;
  reviewer also read Blazor composition-root guidance.
- `add-domain-persistence` with query-construction and functional-core references;
  `add-api-endpoint` with all four references; `add-feature-slice` WASM client reference.
- `nova-testing` with SQLite tenancy, Aspire integration, and browser-suite references;
  .NET test-generation, static source/test pairing, runner, assertion-quality, and test-gap skills.
- For the guidance follow-up: system `skill-creator` and the .NET Blog's
  [Instructions Hygiene](https://devblogs.microsoft.com/dotnet/instructions-hygiene-what-frontier-models-still-need-you-to-say/),
  plus the existing instructions and skill references changed in that follow-up.

The guidance-only follow-up passed `quick_validate.py` for all three affected skills, local-link
checks (11 links), instruction frontmatter checks, and `git diff --check`. A fresh read-only reviewer
found no material findings in the eight-file guidance diff. No duplicate ecosystem skill copies
needed synchronization. The C# manifest above remained unchanged; the application suites were not
rerun for these Markdown-only edits.

## Execution results

| Command | Result |
| --- | --- |
| `dotnet build Nova.slnx --no-restore` | Passed; zero warnings/errors. Restore completed in the earlier full build. |
| `dotnet format Nova.slnx --no-restore --verify-no-changes` | Passed; reverified during PR preparation. |
| `dotnet format Nova.slnx --no-restore --verify-no-changes --include Nova.Integration.Tests/Http/DashboardHttpTests.cs Nova.Integration.Tests/Data/ClubAttentionPostgresTests.cs` | Passed after the final two fixture edits; all other source remained unchanged since the full formatting check. |
| `dotnet format Nova.slnx --no-restore --verify-no-changes --include Nova.Integration.Tests/Data/ClubJoinRequestRetryTests.cs` | Passed after the final test synchronization edit. |
| `dotnet format Nova.slnx --no-restore --verify-no-changes --include Nova.Integration.Tests/Data/EffectivePlacementPostgresTests.cs Nova.Integration.Tests/Http/EffectivePlacementHttpTests.cs` | Passed after the trailing-blank-line cleanup during PR preparation. |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | Passed: 2,668/2,668, zero skips. No unit or production source changed afterward. |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | Passed: 559/559, zero skips. |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | Passed: 120 automated tests, zero failures; seven existing opt-in accessibility screenshot checks skipped. |
| `git diff --check` and `git diff --cached --check` | Passed, including all new files in the staged check before commit. |

The first full integration run passed 556/559. Two attention fixtures omitted the authoritative
current-season pointer; these now set the pointer and opening order explicitly while retaining
their expected counts. The isolated rerun passed 558/559; a club-creation/join-approval advisory-lock
poll timed out again. That existing race test started polling before crest encoding and four blob
uploads finished. It now observes the actual lock attempt through the existing interceptor before
starting the unchanged exact-key waiter checks. Early creator completion is surfaced directly;
timeout bounds, both waiter-count assertions, and winner/persistence assertions remain intact.
The new placement HTTP/provider cases, including the contended Closed read, passed in both runs.

The first browser run passed 117 tests and failed three existing interaction/navigation cases;
seven opt-in accessibility screenshot checks were skipped by their existing environment gates.
The two affected classes then passed all 12 automated tests on the unchanged build (one opt-in
screenshot check skipped). No browser tests, UI behavior, or retry limits were changed. Final full
integration and browser reruns passed after the test synchronization correction, serially without
a competing formatter workload. The seven screenshot checks require the existing
`NOVA_A11Y_SCREENSHOTS=1` opt-in and were not enabled for this read-foundation change.
