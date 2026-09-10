# Campaign workspace and Roster (#197)

The Active/Closed workspace shares campaign orientation, native Route Markers and an independently
loaded close-readiness region. `CampaignEntry` owns authorized entry and supplies its detail snapshot;
`CampaignWorkspace` owns Roster discovery and return context. The selected Fieldhouse composition is
**Roster beside context**: discovery and participant rows occupy the main board, with participant
context alongside on desktop and a focus-trapped dialog below 1200px. See the approved comp and
direction contract in `.impeccable/surfaces/campaign-spine.md`.

## Read contract

The existing `IEffectivePlacementQueryService` endpoints supply both rows and placement evidence:

| State | Read | Whole-campaign scale |
| --- | --- | --- |
| Active | `GetCampaignEffectivePlacementsAsync` | Sum of the four unfiltered eligibility counts |
| Closed | `GetClosedCampaignRosterAsync` | `ParticipantCount`, independent of discovery |

Both campaign reads accept these discovery parameters in addition to paging:

| Parameter | Meaning |
| --- | --- |
| `search` | Literal name substring, or an exact numeric tryout number; maximum 200 characters |
| Repeated `graduationYears` | OR within the selected positive years |
| Repeated `tagDefinitionIds` | OR within campaign-applied tags, including archived definitions |
| `localOutcome` | Campaign-local `undecided`, `assigned`, `notselected` or `withdrawn` |
| `localTeamId` | Campaign-local team; tenant visibility is checked before reading |
| `participantId` | Exact participant-assignment identifier, still bounded by campaign and tenant |
| `sortBy` | `displayName`, `graduationYear`, `tryoutNumber`, `assignmentId`, `outcome` or `teamName` |
| `sortDirection` | `asc` or `desc`; deterministic assignment-ID ties remain ascending |

Filter groups combine with AND. Active's existing single `graduationYear` and effective `teamId`
filters retain their meanings and combine with the new groups. Active `eligibility` selects Needs
placement, optional reassignment, resolved or unavailable without changing whole-campaign counts.
Supplying a direction without a sort field orders by display name in that direction, consistently
across the SQL producers and strict client validation. Omitting both ordering fields retains the existing read defaults documented in
[the placement foundation](placement-decision-foundation.md). The UI explicitly requests name
ascending and 50 rows; the API maximum remains 100. Null tryout numbers sort last in either direction.

Filtering, sorting and paging execute in SQL. Local-team evidence is projected with each row; tags
are loaded once for the page's assignment IDs. Identity, integrity validation, unfiltered counts,
filtered count, page and tag enrichment share the existing repeatable-read transaction. Closed
integrity is checked before discovery, so even an exact participant link cannot hide another
incomplete decision. No per-row history requests or browser joins of independently paged responses
are required.

The working-set query first selects each participant's latest saved assignment ID, ordered by
opening sequence and assignment ID, then joins its evidence. Validity is evaluated after this
selection. The correlated scalar lookup keeps PostgreSQL from repeating the whole latest-decision
anti-join per participant and uses the same SQL construction in the SQLite test harness. Discovery
never filters the saved-decision source used to determine that latest assignment.

WASM validates required evidence, row identities, tenant-safe local/effective relationships, applied
tag uniqueness, requested filters and exact page length against the response's snapshot count.
Ordering validation follows numeric primary order and deterministic ties where portable; it does
not attempt to reproduce PostgreSQL text collation. Other eventually consistent list contracts are
unchanged. These are contract changes in the existing endpoint family, with no schema migration.

## Ownership and behavior

The legacy Roster `outcome` and `teamId` URL parameters remain campaign-local and map to
`LocalOutcome` and `LocalTeamId`. Shared URL builders preserve discovery, selected participant and
page when following Evaluate, Place and Close anchors. `/campaigns/{id}/roster` remains canonical for
Roster; explicit route precedence and native anchor behavior are retained.

Active rows separately label campaign outcome, effective season placement/source, eligibility and
correction reason. A missing local decision is **No campaign decision**, including when a valid
inherited assignment makes reassignment optional. Closed rows use only local saved attribution and
include archived participants/teams. Close readiness comes from the closeout service: zero Needs
placement does not establish that all participants have the explicit local outcomes required to close.

Participant context reuses a loaded Active row. A directly linked participant outside the page uses
one exact, one-row placement read; Closed context reads the immutable local record the same way.
Notes and existing tag capture retain their mutation contracts and ownership rules. Successful
changes refresh the relevant participant, Roster and tag context. Readiness, filter choices, Roster
and participant details have regional retry paths that preserve successful neighboring content.
Team choices search bounded active and archived results and explicitly disclose truncation.

Persisted data is scoped to user/club authority, campaign, lifecycle and query. Request generations
and mutation leases reject obsolete successes, errors and cleanup. Entry authorization changes
dispose the workspace. A lifecycle conflict refreshes authorized detail and switches both the
authoritative Roster read and child capabilities. Closed detail immediately disables capture;
reopening restores it only after authoritative refresh. Opening receipt feedback uses its original
validated operation and matching acknowledgment; only that receipt requests Roster-heading focus.

## Handoffs

- **#198 Evaluate:** replace destination content within the existing workspace. Reuse authorized
  detail and canonical return-context URLs; do not take ownership of Roster state. New evaluation
  workflows and inline tag creation remain separate.
- **#199 Place:** consume the Active effective-placement read and its explicit local/effective
  meanings. Reuse eligibility counts without inferring readiness. Placement mutations remain owned
  by the existing placement service and operation receipts.
- **#200 Close:** use closeout readiness and immutable Closed reads. Close/reopen workflows remain
  in their current panels; print/export remain deferred, and CSV still depends on #221.

The parent #170 integrated campaign-loop acceptance remains separate from this surface delivery.
