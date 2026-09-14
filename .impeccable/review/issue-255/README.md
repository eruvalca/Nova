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
| Desktop and mobile captures with geometry sidecars | `captures/` |
| Comp-diff side-by-side, heatmap, region crops, report | `diff/final/` |
| Validation record | `../../../docs/issue-255-validation.md` |

Unchosen comps from the same round (Programme strip, Waypoint stepper) remain in
`.impeccable/mocks/decision/` as the round's spent hand. They carry no approval and imply none, and
stay out of source control.

## Review outcome

The finish review returned **disposition: fix** with six material findings, and an independent code
review returned one HIGH defect and four lower findings. Both sets are fixed in this slice except
where the validation record notes a deliberate deferral. The measured comp-diff score (56%,
contradicted) is recorded as measured and is **not** treated as an approval.

`.impeccable/build/state.json` and `.impeccable/build/spec.json` are the earlier #198 Evaluate build
record, not this surface's, so the comp-diff for Place ran without a spec and derived its regions
from the comp's own horizontal bands.
