# Issue 196 implementation evidence

The user selected **Details and readiness** (`readiness-split`) from the three served comps. The approved comp and provenance are in `../../mocks/decision/issue-196-readiness-split.{png,json}`. The direction contract is `../../surfaces/campaign-spine.md`; Fieldhouse Wayfinding remains defined by the repository's DESIGN.md and PRODUCT.md.

## Surface and scope

Campaigns uses one season-grouped, role-filtered operational board with 20-row paging and contextual actions. Creation saves an administrator-only Draft in the authoritative current season. Preparation pairs campaign details and durable teams with readiness. The URL-backed opening review refreshes before commitment; retries retain the original operation and show the immutable receipt's enrollment count on Roster. Draft deletion preserves teams. Players/Teams correction routes retain a validated local return context.

The existing Active/Closed workspace supplies the minimal Roster destination. The Route Markers and workspace redesign remains #197. Eligibility/effective-placement semantics remain #213/#214. No schema migration or new lifecycle mutation was added.

## Captures

All images were captured from the real Aspire-hosted app by CampaignDraftBrowserTests. The `hero.png` frame is 1505×1045, matching the comp. `desktop.png` is 1440 wide. Mobile images are 390 wide. Warning and blocker desktop scenarios use 1280. Full-page mobile captures include the existing fixed bottom navigation at its viewport position.

| State | Desktop | Mobile |
|---|---|---|
| Draft opening review | [Comp viewport](hero.png), [1440](desktop.png) | [390](preparation-mobile.png) |
| Current-season/first-season creation | [Desktop](creation-desktop.png) | [Mobile](creation-mobile.png) |
| Campaign directory | [Desktop](directory-desktop.png) | [Mobile](directory-mobile.png) |
| No campaigns after deletion | [Feedback](directory-empty.png) | Covered by responsive directory layout |
| Ordinary member with no visible campaigns | [Member](directory-member-empty.png) | Covered by responsive directory layout |
| Zero-player blocker | [Blocked](preparation-blocked.png) | Shared readiness stack |
| No-team warning and long name | [Long/warning](preparation-warning-long.png) | [Long/warning mobile](preparation-warning-long-mobile.png) |
| Receipt and existing roster machinery | [Roster](roster-desktop.png) | [Roster mobile](roster-mobile.png) |

## Fidelity and review

The measured hero and responsive gates passed at approximately 89%. State/spec are in `../../build/`; region comparisons are in `../diff/hero/`, `../diff/desktop/`, and `../diff/final/`. The fresh review and bounded verdict are recorded in `finish-review.md`: the reviewer scored all four material fixes resolved and returned **ship** for that fix list.

The detector reported eight advisory type-ramp differences and no failures. The approved comp uses larger local type; desktop spacing/type uses a bounded fluid unit, while mobile retains rem sizing. The existing shell's dimensions, native focus rings, and semantic danger color are preserved. These differences are explicit; no force override was used.

## Verification

Validated for PR #244 review round ten on September 5, 2026, including the code in this commit. Solution build: passed with zero warnings. Unit suite: 2,521 passed. Full browser suite: 120 passed, seven existing optional screenshot-only checks skipped; the three Draft journey tests execute without flags, including malformed directory URL coverage. Format verification passed. Latest PostgreSQL/HTTP integration suite: 531 passed in round seven; rounds eight through ten change UI state/rendering only. Theme contrast/token checks passed in round five; theme inputs are unchanged.

Provider tests verify visibility before counts/paging, current-season ordering, closure ordering, bounded team preview and existing lifecycle/activity contracts. Component tests cover readiness freshness, replay and immutable counts, persisted creation recovery and identity invalidation, and late-route response suppression. Browser tests cover first-season creation, correction returns, opening by keyboard and focused Roster feedback, inline team creation, team-preserving deletion, long/mobile content, and member direct-link exclusion.

The approved hero checkpoint is also persisted at `../hero-repro.png`: the 1505×1045 `hero.png` capture associated with the passed 89% hero gate in `.impeccable/build/state.json`. The final packet refresh preserves this checkpoint association.

## Code-review corrections

Metadata edits clear contextual server validation on input changes without reintroducing the unchanged error snapshot. Opening confirmation now persists the retained operation ID before every submission, so a failed storage write cannot be bypassed by recovery. These are behavior corrections; the approved composition is unchanged.

| Requirement | Regression evidence in `CampaignEntryTests` |
|---|---|
| Correct a server-rejected date and submit again, including after a parent render | `CampaignEntry_ResubmitsMetadata_AfterCorrectingServerValidation` |
| Block initial and recovery opening calls while storage fails; then submit the same operation once | `CampaignEntry_RetriesStorageBeforeOpening_WithTheSameOperation` |

Subsequent PR review rounds added scoped deletion replay, durable creation retries, identity cleanup recovery, URL paging synchronization, strict paging and closure validation, single-statement team previews, and accessible mobile table headers. The workspace reuses its router's authorized detail snapshot, preserves the focused Roster route during participant navigation, and ignores conflicting workspace tab values on that route.

Opening receipts remain stored until .NET validates and applies the immutable count and acknowledges the matching operation. Only that validated receipt handoff moves focus to the Roster heading; ordinary visits retain normal navigation focus. Abandoned metadata edits discard server validation errors. JSON responses must contain both paging fields, Active/Closed directory requests omit the unused Draft player-count query, and creation links back to the root-relative directory path.

Regression coverage includes `CampaignEntryTests`, `CampaignWorkspaceTests`, `HttpCampaignQueryServiceTests`, and `CampaignQueryServiceTests`, plus the real-browser Draft journey. The query tests assert the actual reader count for views without Drafts. The browser checks receipt retention and acknowledgment using the real JavaScript modules, along with Roster routing and mobile header semantics. The verification totals above describe the complete suites, not the original focused test subset.

The fifth review round adds an accessible recovery-storage retry that writes the current in-memory creation form before enabling submission. Identity changes clear form and persisted errors before loading new setup; late writes cannot restore an old identity's error. Draft return links require current administrator rights, including live role changes on Players and Teams. A Draft list row now requires its enrollment preview, with zero remaining valid.

| Requirement | Regression evidence |
|---|---|
| Recover from repeated input-storage failures without replacing current edits or the operation ID | `NewCampaign_RetryRecoveryStorage_PreservesCurrentEditsUntilWriteSucceeds` |
| Clear old form and persisted errors before another club's setup completes | `NewCampaign_ClearsErrorsAndSnapshot_BeforeNewClubSetupCompletes` |
| Ignore a previous club's late storage failure | `NewCampaign_IgnoresLateInputStorageFailure_AfterClubChanges` |
| Reject missing/null Draft previews while accepting zero | `GetCampaignListAsync_RequiresPreviewForDraftRows` |
| Hide Draft return links for members and after role loss | `Players_ReturnToDraft_RequiresCurrentAdministratorRole`, `Teams_ReturnToDraft_RequiresCurrentAdministratorRole` |

The sixth review round completes Players identity rebinding: user, club, or role changes discard roster rows, derived filters, management forms, archive confirmation, and persisted snapshots before loading fresh data. Late query, edit, and mutation responses cannot publish into another identity's page. Prerender restoration checks the same scope, and club/user changes discard old directory return/filter context.

| Requirement | Regression evidence in `PlayerComponentsTests` |
|---|---|
| Discard archive confirmation on role loss and require fresh confirmation when access returns | `Players_DiscardsArchiveConfirmation_WhenAdministratorRoleIsLost` |
| Clear previous-club state immediately and query the new club | `Players_ClearsPreviousClubState_BeforeNewRosterCompletes` |
| Ignore old roster successes, authorization failures, and transport failures | `Players_IgnoresPreviousClubRosterCompletion`, `Players_IgnoresPreviousClubTransportFailure` |
| Ignore old edit and archive completions | `Players_IgnoresPreviousClubEditCompletion`, `Players_IgnoresPreviousClubArchiveCompletion` |
| Restore only a matching user/club/role snapshot | `Players_RestoresOnlyMatchingPrerenderSnapshot` |

The seventh review round requires exactly `min(5, ActiveTeamCount)` readiness preview entries in the WASM client, matching the server's atomic team snapshot. Omitted/empty previews with active teams and undersized capped previews are rejected. Existing malformed-payload fixtures carry valid previews when testing other invariants, so the new check does not mask those assertions.

| Requirement | Regression evidence in `HttpCampaignQueryServiceTests` |
|---|---|
| Reject omitted and empty previews when active teams exist | `GetOpeningReadinessAsync_ReturnsServerError_ForInvalidPayload` |
| Accept zero/singleton/capped previews and reject a short capped preview | `GetOpeningReadinessAsync_RequiresCompleteBoundedPreview` |

The eighth review round orders campaign-directory authentication notifications before publishing their results, including startup and disposal. Raw page/deletion query values are parsed and normalized instead of throwing during binding. Optional opening receipts tolerate JSON/schema incompatibility, use the singular noun for one player, and have an accurate one-time-read comment.

| Requirement | Regression evidence |
|---|---|
| Ignore older authentication notifications without cancelling the newest member query | `Campaigns_IgnoresOlderAuthenticationCompletion_WhileNewMemberListLoads` |
| Ignore notifications completed after component disposal | `Campaigns_IgnoresPendingAuthentication_AfterDisposal` |
| Normalize malformed page/deletion values | `Campaigns_DefaultsMalformedOptionalQueryValues`; real-browser `Draft_IsUnavailableToOrdinaryMember_AndWarningDoesNotBlockAdministrator` |
| Ignore incompatible receipt data without focus or acknowledgment | `CampaignWorkspace_DoesNotAcknowledgeUnusableReceipt` |
| Use singular/plural receipt wording while preserving immutable counts and acknowledgment | `CampaignWorkspace_AcknowledgesValidOpeningReceipt_AfterApplyingCount` |

The ninth review round resets the Players URL on role changes as well as user/club changes. Replacing the URL with `/players` removes stale filter and Draft-return parameters when the in-memory roster filters reset, so refresh cannot reapply the previous authority's context.

| Requirement | Regression evidence in `PlayerComponentsTests` |
|---|---|
| Reset the URL and query lifecycle/search/year/tag values when administrator access is revoked | `Players_ReturnToDraft_RequiresCurrentAdministratorRole` |

The tenth review round orders authentication completions in Draft creation and preparation, including startup and disposal. Stale work cannot replace the latest identity or resume recovery from its previous scope. Typed recovery schema failures leave commands disabled and preserve the original markers; Retry can resume the original operation once compatible data is available. The preparation error remains visible for an already Active campaign as well.

| Requirement | Regression evidence |
|---|---|
| Ignore older startup/notification authentication completions | `NewCampaign_IgnoresOvertakenAuthentication`, `CampaignEntry_IgnoresOvertakenAuthentication` |
| Ignore authentication finishing after component disposal | `NewCampaign_IgnoresAuthenticationCompletion_AfterDisposal`, `CampaignEntry_IgnoresAuthenticationCompletion_AfterDisposal` |
| Preserve incompatible form/pending/open/delete data, block commands, and retry the original operation | `NewCampaign_PreservesIncompatibleRecovery_UntilCorrectedRetry`, `CampaignEntry_PreservesIncompatibleRecovery_UntilCorrectedRetry` |

Focused validation passed: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build --filter-class '*CampaignEntryTests'` (27 tests) and the equivalent `'*NewCampaignRecoveryTests'` filter (16 tests).
