# Independent review dispositions

## Code review

A separate read-only agent reviewed the complete implementation and its recovery,
authority, persistence, and API boundaries. It found the authentication subscription,
asynchronous ownership, Keep-intent lifetime, optional-history loading, expiry target,
closure feedback, and withdrawal-confirmation issues listed in the validation record.
Follow-up review confirmed the fixes and returned **no remaining actionable findings in
the reviewed paths**. The reviewer did not run builds or tests; suite results below are
from the implementation session.

## Finish review

Initial disposition: **fix**. Three material findings:

1. Desktop rail filters compressed five columns into the narrow rail.
2. Outcome-unknown wording incorrectly implied the original placement was unchanged.
3. Effective attribution repeated “Last changed” and sat outside its evidence region.

One fix batch made the Place shelf initially collapsed and stacked when expanded,
changed recovery wording to explicitly retain the save request, and moved attribution
inside its corresponding decision region.

Final verification reread the same desktop/mobile captures, plus the expanded-filter
capture. Each finding was **resolved**. No visible regressions were found in that batch.
Final disposition: **ship**, covering the three scored fixes. The comp measurement and
missing Place-specific quality-card/build-spec inputs remain documented limitations;
no synthetic measurement approval is claimed. PRODUCT.md, DESIGN.md, the direction
contract, approved comp/provenance, and captured application were reviewed.

## Other checks

The final detector inspected Place and the reused compact filter component. It reported
one existing advisory: `.discovery-status` uses `.8125rem`, outside its detected type ramp.
That declaration predates this change. No inline ignore, suppression, new token, or
check weakening was added. Earlier changed Place/Players/Teams markup scan had no findings.
The detector also reports an unrelated stale Evaluate hero build state; the Place contract
explicitly retains the prior comp measurement limitation, and this task does not rewrite
that unrelated state.

## PR review round 2 — history navigation

The independent finish reviewer first requested current desktop/mobile evidence for the new
**Latest changes** action. The captures then exposed touching button borders on both viewports.
Disposition: **fix**, with one material requirement: a wrapping flex row and an 8px gap.

The final implementation uses the existing `gap-2` spacing utility. The reviewer reopened
both final [history-paging captures](README.md#captures), verified complete unclipped labels,
44px control height, mobile fit, and the 8px separation, and reported no visible regression.
Final disposition: **ship**, limited to the scored history-control fix. This is a local
finish correction, with no material composition change. The missing Place-specific quality
card and existing comp-measurement limitation remain explicit; no new whole-surface approval
or quantitative comparison is claimed.

## PR review round 4 — invalid recovery data

Initial disposition: **recapture**, followed by **fix** after fresh desktop/mobile evidence.
The reviewer found that Retry storage stretched across the desktop field and was shorter
than the discard action. The controls now share a wrapping group with the existing 8px gap
and 44px minimum-height rule.

The reviewer reopened the final desktop and mobile captures, including a scrolled mobile
viewport that shows both controls above the fixed navigation bar. Content-sized desktop
widths, the gap, 44px height, complete labels, and clean mobile wrapping were verified.
The uncertainty warning remains explicit: discarding invalid data does not undo a save or
prove it failed. The deliberate action still requires authoritative refresh before editing.

Final disposition: **ship**, limited to the scored recovery-control fix; no visible regression
was found. This adds a regional state within the retained composition. Existing missing
quality-card/build-spec inputs and the comp-measurement limitation remain unchanged; no new
whole-surface or quantitative approval is claimed.

## PR review round 7 — administrator supersession copy

Initial disposition: **recapture**. Existing invalid-storage images did not show the newly corrected
conditional state. Four targeted desktop/mobile page and board captures now show the deliberate
supersession action, complete label, distinct historical/current campaign evidence, and absence of
the contradictory unavailable explanation. The mobile board capture keeps the action unobscured.

The independent reviewer inspected the refreshed captures and reported no remaining material fixes
or visible regression. Final disposition: **ship**, limited to this conditional-copy correction.
The shared composition, tokens, and styling are unchanged. This does not establish a new whole-surface
or quantitative comp approval; the documented measurement and missing-input limitations remain.
