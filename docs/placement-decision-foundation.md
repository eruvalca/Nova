# Placement decision foundation (#213)

## Participation and saved decisions

`PlayerCampaignAssignmentEntity` remains the campaign participation row. Opening and player intake
create `Undecided` rows with no decision attribution or placement activity. The placement mutation
accepts only `Assigned`, `NotSelected`, or `Withdrawn`. `DecisionRecordedAt`, `DecisionRecordedById`,
and `DecisionActorDisplayName` describe an explicit save independently of enrollment audit columns.

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
| Complete eligibility/outcome matrix and resolved versus optional work | `Evaluate_AllowsEverySavedOutcome_ForEligibleDecisionHistory`, `Evaluate_EnforcesWithdrawalMatrix_ForEveryRequestedOutcome`, `GetEligibility_ClassifiesLatestDecision` |
| New-season reset and non-current rejection | `UpdatePlacementAsync_ResetsEligibility_WhenWithdrawalBelongsToPreviousSeason`, `UpdatePlacementAsync_RejectsNonCurrentSeason_WithoutWrites` |
| Immutable Closed source and no historical fallback | `UpdatePlacementAsync_SupersedesPriorDecision_AndPreservesClosedHistory`, `UpdatePlacementAsync_UsesLatestDecisionBeforeTeamValidity_WithoutHistoricalFallback` |
| Participation versus decision attribution and response contracts | `UpdatePlacementAsync_RecordsDecisionAttribution_WithoutReplacingEnrollmentAuthor`, `GetPlacementRosterAsync_RejectsMalformedSavedDecision` |
| No-op, stale token, and terminal withdrawal | `UpdatePlacement_IdenticalSavePreservesDecision_AndStaleIdenticalSaveConflicts`, `UpdatePlacementAsync_RejectsReplacementOfLocalWithdrawal_WithoutWrites` |
| Withdrawal override authorization | `UpdatePlacementAsync_ForbidsMemberPriorWithdrawalOverride_WithoutWrites` |
| Tenant isolation and immutable receipt integrity | `PlacementMutationReceipts_FilterByOwningTenant`, `PlacementMutationReceipts_RejectCrossTenantWrites`, `PlacementMutationReceipts_RejectChangesToCommittedReceipt`, `PlacementReceipt_EnforcesTenantScopedOperationUniqueness` |
| Receipt retention, including deleted tenants | `PlacementMutationReceipts_PruneExpiredReceipts_WithinCurrentTenantOnly`, `PlacementMutationReceipts_GlobalCleanupRemovesExpiredDeletedClubEvidence` |
| Atomic writes and retry proof despite later mutations | `UpdatePlacement_RetriesFailedCommit_AndPersistsReplacementToken`, `UpdatePlacement_RecoversOriginalToken_WhenLaterSavePrecedesCommitVerification`, `UpdatePlacement_RecoversOriginalSuccess_WhenClubDeletionPrecedesCommitVerification` |
| Contended season/opening/team locks | `UpdatePlacement_RejectsNonCurrentSeason_AfterWaitingForSeasonLock`, `UpdatePlacement_SerializesCompetingOpening_WithoutCreatingAnotherDecision`, `UpdatePlacement_RejectsArchivedTarget_AfterWaitingForTeamLock`, `UpdatePlacement_LocksPriorTeam_AndSupersedesItsDecisionAfterArchival` |
