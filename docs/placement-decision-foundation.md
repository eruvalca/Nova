# Placement decision foundation (#213)

## Participation and saved decisions

`PlayerCampaignAssignmentEntity` remains the campaign participation row. Opening and player intake
create `Undecided` rows with no decision attribution or placement activity. The placement mutation
accepts only `Assigned`, `NotSelected`, or `Withdrawn`. `DecisionRecordedAt`, `DecisionRecordedById`,
and `DecisionActorDisplayName` describe an explicit save independently of enrollment audit columns.
The database requires all three for saved outcomes and none for `Undecided`. Saved-decision DTOs
require these attribution fields in JSON and expose non-nullable values; clients reject missing,
null, default, or invalid actor metadata.

`CampaignSavedPlacementDecision` identifies the player, source participation/campaign/season,
opening sequence, outcome/team, decision attribution, and source concurrency token. The placement
roster's `SavedDecision` is null for enrollment and otherwise describes that campaign's own decision;
its existing outcome and summary fields remain campaign-local. It is not an effective-roster DTO.

## Same-season selection and mutation

For one tenant and player, filter to saved decisions in the target season and opened campaigns.
Order by `Campaign.SeasonOpeningSequence` descending, then participation ID descending as a stable
tie-breaker. Campaign opening sequence is authoritative; IDs and attribution timestamps are not
chronology. Select the latest decision before considering team validity. Never filter to Assigned
or active teams first: NotSelected, Withdrawn, or an invalid latest assignment must prevent fallback
to older roster membership. Technical participation in a later campaign does not supersede anything.

The pure `CampaignPlacementPolicy` accepts fresh immutable facts. Assigned allows optional
reassignment and a valid effective team is not unresolved. NotSelected is resolved in its source
campaign and eligible for another attempt later. Withdrawn cannot be changed in its owning campaign,
including after reopen; only a club administrator can supersede it in a later Active campaign.
Previous-season decisions do not restrict active players in a new season.

The service locks club-season, club-roster, campaign, player, then affected teams in identifier order,
and reloads guarded state. Only the target Active/current-season participation changes. Earlier
Closed outcomes and tokens are untouched; no superseded flag or delete/reset operation is needed.
An identical local save checks the expected token and preserves attribution/token/activity. An
identical later-campaign decision is an explicit supersession and receives its own attribution/event.

## Activity and commit evidence

Every meaningful save writes the placement, a `PlacementMutationReceiptEntity`, and one event through
`ActivityEventWriter` in the same transaction. Later-campaign decisions emit `PlacementSuperseded`
with both source identities and old/new outcome/team snapshots; their owning campaign is the later
Active campaign. The event actor/name/time remain durable history. Enrollment emits no event.

The stable replacement token also serves as the internal logical operation ID. Retry verification
uses the immutable receipt rather than the participation's mutable token, so a later save cannot
erase proof of an acknowledged-lost commit. This is internal execution-strategy recovery, not an
HTTP idempotency-key API. Receipt uniqueness is tenant-scoped; one-day retention prunes only the
current tenant during later meaningful mutations. The FK-less tenant snapshot survives club deletion;
existing global membership-mutation retention also prunes expired placement receipts across tenants,
including deleted clubs, using a CreatedAt-leading index. Receipts are
not history and are never used to determine effective placement or attribution.

## Handoff to #214

Reuse the decision snapshot, opening-sequence rule, and pure eligibility facts when adding bounded
effective rosters and Needs-placement projections. Count a player once, retain invalid latest-team
truth for correction, and preserve Closed campaign-local records independently of current truth.
Existing roster counts, attention counts, close-readiness projections, paging, and team/export
queries are not converted by #213; #214 owns those consumers and their query/provider tests.

## Regression evidence

| Requirement | Tests |
| --- | --- |
| Complete eligibility/outcome matrix and resolved versus optional work | `EvaluateAllowsEverySavedOutcomeForEligibleDecisionHistory`, `EvaluateEnforcesWithdrawalMatrixForEveryRequestedOutcome`, `GetEligibilityClassifiesLatestDecision` |
| New-season reset and non-current rejection | `UpdatePlacementAsyncResetsEligibilityWhenWithdrawalBelongsToPreviousSeasonAsync`, `UpdatePlacementAsyncRejectsNonCurrentSeasonWithoutWritesAsync` |
| Immutable Closed source and no historical fallback | `UpdatePlacementAsyncSupersedesPriorDecisionAndPreservesClosedHistoryAsync`, `UpdatePlacementAsyncUsesLatestDecisionBeforeTeamValidityWithoutHistoricalFallbackAsync` |
| Participation versus decision attribution and response contracts | `UpdatePlacementAsyncRecordsDecisionAttributionWithoutReplacingEnrollmentAuthorAsync`, `GetPlacementRosterAsyncRejectsMalformedSavedDecisionAsync` |
| No-op, stale token, and terminal withdrawal | `UpdatePlacementIdenticalSavePreservesDecisionAndStaleIdenticalSaveConflictsAsync`, `UpdatePlacementAsyncRejectsReplacementOfLocalWithdrawalWithoutWritesAsync` |
| Withdrawal override authorization | `UpdatePlacementAsyncForbidsMemberPriorWithdrawalOverrideWithoutWritesAsync` |
| Tenant isolation and immutable receipt integrity | `PlacementMutationReceiptsFilterByOwningTenant`, `PlacementMutationReceiptsRejectCrossTenantWrites`, `PlacementMutationReceiptsRejectChangesToCommittedReceiptAsync`, `PlacementReceiptEnforcesTenantScopedOperationUniquenessAsync` |
| Receipt retention, including deleted tenants | `PlacementMutationReceiptsPruneExpiredReceiptsWithinCurrentTenantOnlyAsync`, `PlacementMutationReceiptsGlobalCleanupRemovesExpiredDeletedClubEvidenceAsync` |
| Atomic writes and retry proof despite later mutations | `UpdatePlacementRetriesFailedCommitAndPersistsReplacementTokenAsync`, `UpdatePlacementRecoversOriginalTokenWhenLaterSavePrecedesCommitVerificationAsync`, `UpdatePlacementRecoversOriginalSuccessWhenClubDeletionPrecedesCommitVerificationAsync` |
| Contended season/opening/team locks | `UpdatePlacementRejectsNonCurrentSeasonAfterWaitingForSeasonLockAsync`, `UpdatePlacementSerializesCompetingOpeningWithoutCreatingAnotherDecisionAsync`, `UpdatePlacementRejectsArchivedTargetAfterWaitingForTeamLockAsync`, `UpdatePlacementLocksPriorTeamAndSupersedesItsDecisionAfterArchivalAsync` |
| WASM no-op success | `UpdatePlacementAsyncReturnsSuccessWhenNoOpPreservesSubmittedTokenAsync` |
| Enrollment display without a clearing action | `OutcomeOptionsDisableUndecidedForEnrollmentAndSavedDecision`, `OutcomeChangeClearsTeamAndDisablesTeamSelectWhenLeavingAssigned` |
| Strict supersession feed contract | `GetClubActivityAsyncAcceptsSupersessionForSavedOutcomesAsync`, `GetClubActivityAsyncRejectsSupersessionForMalformedDecisionAsync` |
| Required saved-decision attribution in JSON and persistence | `GetPlacementRosterAsyncRejectsMissingDecisionAttributionAsync`, `GetPlacementRosterAsyncRejectsMalformedSavedDecisionAsync`, `PlacementDecisionRejectsInvalidAttributionAtDatabaseBoundaryAsync` |
