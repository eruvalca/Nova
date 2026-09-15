# Issue #254 — correction, reassignment, and recovery

The existing Fieldhouse Wayfinding Place board is extended with inline consequence
confirmation, optional history, prior-season Keep, and explicit uncertain-save recovery.
The approved queue rail and selected-player sheet remain in place.

- [Validation record](../../../docs/issue-254-validation.md)
- [Direction contract](../../surfaces/placement.md)
- [Approved comp](../../mocks/decision/issue-255-place-queue-rail.png)
- [Comp provenance](../../mocks/decision/issue-255-place-queue-rail.json)

## Captures

Final captures and geometry sidecars are recorded in `captures/`:

| State | Desktop | Mobile |
| --- | --- | --- |
| Initial assignment | [Desktop](captures/desktop.png) | [Mobile](captures/mobile.png) |
| Withdrawal confirmation | [Desktop](captures/confirmation-desktop.png) | [Mobile](captures/confirmation-mobile.png) |
| Outcome unknown | [Desktop](captures/outcome-unknown-desktop.png) | [Mobile](captures/outcome-unknown-mobile.png) |
| Expanded queue filters | [Desktop](captures/filters-desktop.png) | — |
| History paging (review round 2) | [Desktop](captures/history-paging-desktop.png) | [Mobile](captures/history-paging-mobile.png) |

The original `.json` sidecars record the viewport, document geometry, campaign ID, and board bounds.
Their `-board.png` companions isolate the same board from the same running page.
They are screenshots of the seeded running application, not generated visual assets.

The review-round-2 screenshots scroll to the history controls on the second bounded page.
Their sidecars identify the viewport, test, image checksum, and tested source fingerprint.
The independent finish review verified the 8px action gap, complete labels, 44px controls,
and mobile fit, with **ship** limited to that fix. The original composition remains unchanged.

## Review

Independent code findings and their fixes are recorded in the validation record.
The independent finish review requested three material fixes: stacked/readable rail
filters, truthful retained-request wording, and attribution in its corresponding decision
region. The final verification marked all three **resolved**, with no visible regressions
from that batch and **disposition: ship** for those scored fixes. It does not assert a
new whole-surface or quantitative comp approval. See [review dispositions](review.md).

The earlier comp measurement limitation remains unchanged: its raster did not faithfully
encode the locked rail-and-sheet composition. No passing comp-diff measurement is claimed.
