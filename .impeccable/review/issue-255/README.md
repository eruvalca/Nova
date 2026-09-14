# Issue #255 — Place queue, participant evidence, and decision recording

Place implements find the next teamless player → verify their authoritative evidence → commit one
attributable decision → watch the count move. The user locked **Queue rail beside a fixed working
sheet** within Fieldhouse Wayfinding from a three-comp surface round; the approved
[comp](../../mocks/decision/issue-255-place-queue-rail.png), its provenance and the
[surface brief](../../surfaces/placement.md) direction contract are tracked.

The [validation record](../../../docs/issue-255-validation.md) carries the tested revision, the exact
commands and results, the boundary decisions taken with the user, and the checks that remain
outstanding before this branch may be proposed for merge.

## Scope of this packet

This is the first of the two Place slices. It replaces the row-per-participant
`CampaignPlacementsPanel` with the player-first queue, selected-player evidence, and the decision
loop over the delivered eligibility/supersession and effective-read foundations, leaving no second
mutation path beside the surface it replaced.

Reassignment, prior-season history the `Keep on {Team}` fast path, supersession confirmation copy,
and the stale/ambiguous recovery matrix belong to sibling slice
[#254](https://github.com/eruvalca/Nova/issues/254) and are not pre-built here.

## Evidence

| Artifact | Location |
| --- | --- |
| Approved comp | `../../mocks/decision/issue-255-place-queue-rail.png` |
| Comp provenance and embedded prompt | `../../mocks/decision/issue-255-place-queue-rail.json` |
| Comp prompt sidecar | `../../mocks/decision/issue-255-place-queue-rail.prompt.txt` |
| Direction contract and comp frame | `../../surfaces/placement.md` |
| Validation record | `../../../docs/issue-255-validation.md` |

Unchosen comps from the same round (Programme strip, Waypoint stepper) remain in
`.impeccable/mocks/decision/` as the round's spent hand. They carry no approval and imply none, and
stay out of source control.

## Outstanding before merge

Captures, the `comp-diff` comparison, the `impeccable-finish-reviewer` disposition, the integration
and browser suites, and an independent local review have not been produced or run yet. They are
recorded as outstanding in the validation record rather than claimed as passing.
