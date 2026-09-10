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

## Effective placement query foundation (#214)

`IEffectivePlacementQueryService` supplies three approved-member reads. Each applies the same input
validation at its service and HTTP boundaries, uses the authenticated tenant, and returns
`NotFound` for inaccessible identifiers. Working/Closed reads return `Conflict` when a visible
campaign has the wrong lifecycle; members cannot discover Drafts.

| Read | Route | Meaning |
| --- | --- | --- |
| `GetCurrentSeasonRosterAsync` | `/api/seasons/current/roster` | Unique active players assigned to valid teams by the latest saved current-season decision. An absent current season is explicit, with an empty page. Closed campaigns remain valid sources during an idle season. |
| `GetCampaignEffectivePlacementsAsync` | `/api/campaigns/{campaignId}/effective-placements` | Current-season Active participation, local decision/token, effective decision/source/team, eligibility, correction reason, and unfiltered section counts. |
| `GetClosedCampaignRosterAsync` | `/api/campaigns/{campaignId}/closed-roster` | Only this Closed campaign's participants and saved outcomes, including archived players/teams and original decision attribution. Incomplete decision records produce an integrity conflict. |

Pages default to 50 rows, with a maximum of 100. Working pages order by graduation year, last name,
first name, then player ID; season and Closed pages order by last name, first name, then player ID.
Season/working reads support literal name search and exact graduation-year/team filters. Working
search also accepts an exact numeric tryout number and an optional eligibility filter. Filters do
not change working section totals. Missing/unknown eligibility selects all states.

`EffectivePlacementQueries` selects saved same-season decisions using a SQL anti-newer predicate:
opening sequence descending, participation ID descending. Validity and team filters occur **after**
selection. All filters, counts, ordering, and bounds stay in SQL; no per-player history reads are
needed. A later enrollment never supersedes, and an invalid/teamless latest decision never
reactivates an older assignment. Assigned membership requires an active player and an active,
graduation-compatible, tenant-visible team.

Needs placement matches `CampaignPlacementPolicy.GetEligibility`: no saved decision, an earlier
`NotSelected`, or an invalid latest assignment requires a decision. Valid Assigned is optional
reassignment; local NotSelected is resolved; Withdrawn and archived players are unavailable.
Administrator withdrawal-override authority does not inflate the ordinary queue. Invalid saved
assignments retain their source evidence and correction reason; inaccessible team identifiers and
names are not disclosed. In that case the query's decision snapshot has a null team ID rather than
leaking the invalid cross-tenant reference.

Campaign list/dashboard unresolved counts and the attention region use this same predicate. The
attention count and target are a single SQL aggregate, and regional failures remain independent.
Team directory/detail add `EffectiveCurrentSeasonPlacementCount` and
`CurrentCampaignPlacementContribution`; Active-campaign impact/history fields retain their own
meaning. Current-campaign contribution counts only effective memberships sourced from the Active
campaign, not every local historical assignment.

Closeout adds `NeedsPlacementCount` without changing the existing campaign-local outcome summary
or closure policy. **Zero Needs placement does not imply ready to close.** Every participant still
needs an explicit local Assigned, NotSelected, or Withdrawn outcome. This was confirmed for #214:
inherited assignments remain optional placement work but do not satisfy the local close record.

Each of these three query responses uses a repeatable-read snapshot on PostgreSQL (serializable on
SQLite), inside the execution strategy. Current-season identity and roster membership therefore
agree even if the season advances between statements; Active campaign identity, eligibility counts,
and rows agree even if the campaign closes. Closed lifecycle, integrity validation, count, and page
share the same guarantee. Other list consumers retain their ordinary eventually consistent totals.
Clients still validate portable ordering without reproducing database collation. A snapshot covers
one response, not separate page requests; #221 must maintain a consistent read for an entire generated
export. Neither receipts nor effective-season queries determine Closed rows.

The existing campaign-local placement roster/summary remains campaign-local. #169/#199/#200/#217
own UI consumption and #221 owns CSV generation. No UI, mutation controls, or schema migration
were introduced by this query slice.

#197 extends the two campaign reads with shared discovery, campaign-applied tag enrichment and
explicit local-team evidence. See [Campaign workspace and Roster](campaign-workspace-roster.md)
for the extended contract and downstream UI handoffs.

### Guidance and validation

Implementation guidance read: `AGENTS.md`; C#, service, validation, API, EF/tenancy, placement,
season, functional-core, observability, and testing instructions; `add-domain-persistence`
(query construction and functional-core references), `add-api-endpoint` (all four required
references), `nova-testing` (SQLite and Aspire integration references), and the WASM contract-check
reference. Test work also uses the .NET test-generation, static-pairing, and runner skills.

Query/consumer regression evidence is in `EffectivePlacementQueryServiceTests` and
`EffectivePlacementConsumerTests`; production HTTP and PostgreSQL evidence is in
`EffectivePlacementHttpTests` and `EffectivePlacementPostgresTests`; strict response handling is
covered by `HttpEffectivePlacementQueryServiceTests`. The [validation record](issue-214-validation.md)
records the tested revision, actual commands/results, and separate review disposition.

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
