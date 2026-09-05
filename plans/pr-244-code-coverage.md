# PR 244 code hardening: transition and sibling coverage

This inventory records the code boundaries addressed by the hardening work. Test outcomes belong in the parent execution plan and verification artifacts; naming a test here is not a claim that it has run on the current changes.

## Feedback ownership and the validation pilot

| Owner and transition | Required effect | Executable coverage / disposition |
| --- | --- | --- |
| CampaignEntry: settled success or field failure, then another CampaignId | Clear previous campaign feedback; show the next campaign | `CampaignEntryTests.CampaignEntry_ClearsSettledFeedback_WhenCampaignChanges` (both rows reproduced against unchanged production code before the fix) |
| CampaignEntry: same-campaign refresh after successful metadata save | Preserve success feedback | The successful row above asserts both feedback and the second detail query before changing CampaignId |
| CampaignEntry: old detail completes after route change | Do not restore old detail | Retain `CampaignEntry_IgnoresStaleDetailCompletion_AfterCampaignChanges` |
| CampaignCreateForm: reject, correct, repeat unchanged parent snapshot, resubmit | Submit corrected input and retain operation ID | Retain `CampaignComponentsTests.CampaignCreateForm_ResubmitsCorrectedField_WithUnchangedParentErrorSnapshot` |
| CampaignMetadataForm: same correction transition with cross-field failures | Clear contextual failures and submit correction | `CampaignFormValidationTests.CampaignMetadataForm_ResubmitsCorrection_AfterUnchangedParentErrorRerender`; retain `CampaignEntry_ResubmitsMetadata_AfterCorrectingServerValidation` |
| Either form: new errors, null errors, model/context replacement, disposal | New errors apply; old snapshots/subscriptions cannot reappear | `ServerValidationMessagesTests` |
| Creation: nested inline-season field paths | Keep each error attached to its matching visible control | `CampaignCreateForm_MapsNestedServerErrors_ToTheirSeasonControls` |
| Preparation: cancel edit or switch to team creation | Discard edit-specific field failures | Retain `CampaignEntry_ClearsFieldValidation_WhenStartingAnotherEdit` |

The validation helper has exactly two consumers: CampaignCreateForm and CampaignMetadataForm. It owns only message-store/subscription/snapshot lifetime. Components retain local-model cloning, field mapping, DataAnnotations bridges, and public parameters. Other forms are not silently migrated.

## Identity and request ownership pilot

The first ownership consumers are NewCampaign and CampaignEntry. Campaigns, Players, and Teams are a subsequent explicitly gated cohort. Notification ordering is separate from applied identity ownership: unresolved or ultimately unchanged authentication preserves displayed state, unsaved input, and existing operations. A newly applied changed identity invalidates old effects and clears its state before asynchronous cleanup or replacement reads. Browser storage is never authorization evidence.

| Transition | Existing coverage to retain | Pilot additions / gate |
| --- | --- | --- |
| Newest notification overtakes startup or an older notification | `NewCampaign_IgnoresOvertakenAuthentication`; `CampaignEntry_IgnoresOvertakenAuthentication` | Shared controlled authentication provider; helper ownership tests |
| Unresolved notification resolves to the same identity | Preserve existing behavior | `NewCampaign_PreservesEdits_DuringPendingAndUnchangedAuthentication`, `NewCampaign_PreservesPendingCreation_DuringUnchangedAuthenticationRefresh`, `CampaignEntry_PreservesEditAndMutation_DuringPendingAndUnchangedAuthentication` assert unsaved input, result/finally publication, unchanged recovery, query counts, and exact creation retry payload |
| Changed identity applies while replacement read is pending | `NewCampaign_ClearsErrorsAndSnapshot_BeforeNewClubSetupCompletes` | `CampaignEntry_RejectsOldMutationEffects_AfterChangedIdentityApplies` asserts detail, snapshot, and editor clear before the replacement query completes |
| Old HTTP/JS completion after changed identity | `NewCampaign_IgnoresLateInputStorageFailure_AfterClubChanges` and retained campaign route/recovery tests | The Entry theory above holds old success, forbidden, or transport completion until a new save is pending, then checks old feedback/navigation/query effects and finally cannot affect new input/busy state |
| Parent render continuation resumes after old recovery finishes | `CampaignEntry_FocusesRecoveryContinuation_OnlyWhileItsIdentityStillOwnsPage` | Changed-identity row reproduced an extra stale focus before the guard; unchanged-identity neighbor preserves ordinary preparation and URL-backed review focus |
| Heading JS completes after changed identity while replacement read remains pending | `CampaignEntry_IgnoresOldHeadingFocusCompletion_AfterIdentityChanges` | A test subclass signals completion of the actual production render continuation; before the post-JS guard, the old continuation reproduced extra renders. The replacement identity retains ordinary heading and review focus |
| Disposal before authentication settles | `NewCampaign_IgnoresAuthenticationCompletion_AfterDisposal`; `CampaignEntry_IgnoresAuthenticationCompletion_AfterDisposal` | Helper leases also reject disposed owners |
| Creation input restored, same-role club changes, old cleanup fails | `NewCampaign_RestoresInput_AndInvalidatesItWhenClubChanges` | Preserve best-effort cleanup and fresh-scope loading |

Pilot adoption must preserve operation IDs, payloads, storage keys, routes, API contracts, same-identity preservation, and each page's existing reset decisions. Cancellation still stops cooperative work; ownership checks reject late noncooperative completions. Separate operation lanes cannot clear each other's busy state. Do not add pending-auth quarantine or a generic workflow framework.

The two-page implementation uses `UiIdentityScope` for ordered authentication snapshots and `UiRequestOwner` for the pages' existing operation lanes. `UiOwnershipTests` covers first empty identity, overtaken startup, pending and unchanged identity preservation, user/club/authority changes, independent request lanes, foreign/stale leases, and disposal. Page-specific resets, callbacks, cancellation, persistence, navigation, and payload construction remain in the pages. Root recorded the completed pilot gate as 158 passing tests with independent review, then authorized the following bounded cohort.

| Consumer | Go/no-go decision and preserved local differences | Coverage |
| --- | --- | --- |
| Campaigns | Go: full identity ownership with independent list-load, edit-selection, and metadata-mutation lanes. Cancellation and view checks remain; list reload cannot clear the mutation's progress ownership | Retain stale mutation, auth, list, edit-choice, and role-loss tests. Add `Campaigns_PreservesEdit_DuringPendingAndUnchangedAuthentication` and `Campaigns_RedirectsForbiddenSeasonChoices_OnlyForCurrentEditOwner`: root reproduced stale AccessDenied navigation after same-club user replacement while the current-owner neighbor passed; the ownership check now precedes that redirect |
| Players | Go: identity scope replaces identity generations; a separate request owner handles roster loads. Mutation/edit operations retain their existing identity-only lease. Numeric club claim comparison and persisted scope formatting remain local | Retain old-club result/error/archive/edit, empty startup, role revocation, and snapshots. Add `Players_PreservesConfirmation_DuringPendingAndUnchangedAuthentication` (42/042 equivalent claims) and `Players_ClearsConfirmation_WhenUserChangesWithinSameClub` |
| Teams | No-go for production composition: existing mutations and roster requests are club-scoped; role loss closes management forms without replacing roster/request ownership, and user ID is not part of this page's current comparison. Full identity leases would silently introduce different behavior, so production code remains local | Shared controlled provider replaces duplicated auth test doubles. `Teams_PreservesEdit_DuringPendingAndUnchangedAuthentication` preserves edits; existing `Teams_DiscardsDraftReturnContext_WhenScopeChanges` additionally asserts role loss/regrant preserves roster/filter context without another query. Existing club-reset cancellation and stalled-mutation tests remain |

`ControlledAuthenticationStateProvider` now replaces asynchronous auth doubles across all five targeted page suites. It accepts either a held task or an immediately resolved principal, leaving Nova-specific claim construction in each suite. Cohort filters are `*CampaignComponentsTests`, `*PlayerComponentsTests`, `*TeamComponentsTests`, plus the pilot's `*CampaignEntryTests`, `*NewCampaignRecoveryTests`, `*CampaignFormValidationTests`, `*ServerValidationMessagesTests`, and `*UiOwnershipTests`.

The cohort review also found Campaigns retained its populated season-choice cache after changing identity. `Campaigns_ReusesSeasonChoiceCache_OnlyForUnchangedIdentity` reproduced the old club's season remaining selectable alongside the new club's season (`artifacts/verification/campaigns-cache-red-exact`); the unchanged-auth cache-reuse neighbor passed. The changed-identity reset now clears `_seasonChoices` and its total count. Its per-edit projection was already cleared by `CancelMutationForm`. NewCampaign and CampaignEntry already clear their setup snapshots on identity changes; Players clears its roster-derived filter caches; Teams clears club-derived roster/filter caches in its existing club-reset path. No additional cache abstraction is introduced.

## Recovery: critical versus optional storage

| Storage boundary | Required behavior | Retained coverage |
| --- | --- | --- |
| Creation form/pending request incompatible or unreadable | Block creation, preserve original recovery, allow corrected retry | `NewCampaign_PreservesIncompatibleRecovery_UntilCorrectedRetry`; `NewCampaign_RetriesSessionStorage_AfterReadFailure` |
| Creation write fails before initial/confirmation dispatch | No HTTP dispatch until exact request is persisted | `NewCampaign_RepersistsRequest_BeforeRetryingFailedStorageWrite` |
| Current edits fail persistence | Retry current edits without restoring stale saved form | `NewCampaign_RetryRecoveryStorage_PreservesCurrentEditsUntilWriteSucceeds` |
| Successful creation cleanup fails partway | Pending request remains until editable form cleanup succeeds | `NewCampaign_RetainsPendingRequest_WhenSuccessfulFormCleanupFails` |
| Opening initial/retry persistence fails | No opening dispatch; later retry uses original operation | `CampaignEntry_RetriesStorageBeforeOpening_WithTheSameOperation` |
| Opening committed but response is ambiguous / page is Active | Replay exact operation; immutable receipt owns reported count | `CampaignEntry_ReusesOpeningOperation_WhenResponseIsAmbiguous`; `CampaignEntry_ReplaysPersistedOpening_AfterCampaignAlreadyOpened` |
| Deleted Draft is already NotFound | Pending deletion can replay against durable tombstone | `CampaignEntry_ReplaysPendingDeletion_WhenDetailIsAlreadyNotFound` |
| Critical typed opening/deletion marker incompatible | Preserve marker and block recovery command, even when detail is Active | `CampaignEntry_PreservesIncompatibleRecovery_UntilCorrectedRetry` |
| Optional workspace receipt missing, unreadable, malformed, unsupported, or invalid | Roster remains usable; no opening claim, focus handoff, or acknowledgement | `CampaignWorkspace_DoesNotAcknowledgeUnusableReceipt` |
| Valid optional receipt | Apply immutable count, focus, then acknowledge its own operation | `CampaignWorkspace_AcknowledgesValidOpeningReceipt_AfterApplyingCount`; actual JavaScript assertions in `CampaignDraftBrowserTests.Draft_OpensIntoRoster_AfterCreationAndCorrectionRoundTrips` |

The optional receipt path must never inherit critical-storage blocking behavior. The actual browser JavaScript coverage is retained because bUnit mocks cannot prove read/remove/acknowledgement storage effects.

## Producer/client contracts and navigation

- Retain `HttpCampaignQueryServiceTests` raw JSON cases for malformed/omitted/null/nested payloads, invalid IDs/counts and duplicate teams, requested page/limit agreement, required Draft enrollment previews, and complete `min(5, count)` readiness previews. Invalid fixtures must continue isolating their intended condition instead of all failing an unrelated new check.
- Team count and bounded preview use one PostgreSQL statement; directory count/rows intentionally tolerate independent-read drift. Provider-test changes are owned separately and do not change these contracts.
- `CampaignEntry_RequiresNewConfirmation_WhenEnrollmentCountChanges` protects a second confirmation after a changed preview while preserving authoritative receipt counts that can change after refresh. No expected-enrollment-count command field is introduced.
- Retain workspace initial-snapshot reuse/rejection tests, dedicated-roster conflicting-tab tests, receipt focus/no-focus tests, and the real browser keyboard/receipt journey. Browser history coverage is assessed by the browser-test owner separately.

## Sibling dispositions and rollout gate

| Family | Disposition |
| --- | --- |
| Campaign creation and metadata validation | Both migrate together to the one message-lifetime helper |
| Draft creation and preparation ownership | Pilot together; retain feature-specific reset/recovery behavior |
| Directory and Players ownership | Equivalent shared composition adopted after root-recorded pilot tests and independent review; cohort validation pending |
| Teams ownership | Deliberate no-go for full production composition; preserve its different club-scoped behavior with common test techniques and explicit preservation assertions |
| Workspace receipt/navigation and HTTP client validation | Existing separate boundary coverage retained; no generic persistence or payload framework |
| All other forms/pages | Outside these explicit consumers; additional discovered defects require their own evidence and disposition |

Before advancing the ownership cohort, the pilot's existing regression meanings and new unchanged/changed-identity cases must pass; independent review must inspect result, exception, post-JS, navigation, and finally ownership at every replaced generation boundary. Full pre-PR test gates remain required. Root alone runs builds/tests and owns verification evidence.
