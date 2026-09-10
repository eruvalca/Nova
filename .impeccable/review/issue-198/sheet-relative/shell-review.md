# Preserved shell and comparison coverage

The user approved measuring Evaluate relative to its sheet and reviewing the preserved campaign shell separately. This is a measurement-scope change, not a reduction of the 72% overall threshold. The original approved comp, full-frame captures, 73.43% report and all regional failures remain archived unchanged.

## Registration

`measure-sheet.mjs` reads the original comp and final hero capture at native resolution. A long-border scan across physical x=100..800 confirms top-border rows 475 and 588 in their preidentified neighborhoods. Both crops begin at x=59 and retain width 780; neither image is resized. The single translation is 113 physical pixels (75⅓ CSS pixels). The bottom boundary is physical y=1667, before the fixed navigation, giving 1079 pixels of unobscured sheet coverage.

`report.json` retains the shared scoring functions, thresholds, original region IDs and minimum 48-pixel regional sampling rule. It omits the comparator's additional best-shift search to honor the single-translation method. The unmodified comparator output, including its extra regional shift, is separately preserved under `standard-tool/`. No per-element position, font, scale or image content was altered.

## Shell review

The final hero and responsive captures retain the existing campaign back link, campaign name, season and dates, participant count, complete Route Marker names and captions, independently loaded readiness, and responsive app navigation. These are governed by Fieldhouse Wayfinding and the existing workspace. Their required captions and metadata explain the sheet-origin difference from the generated comp. The same independent finish reviewer accepted those adaptations in the bounded correction round; they do not require production shell changes to recreate the generated reference's shorter masthead.

## Coverage

Nineteen Evaluate regions are measured, from Back to results through Find another player. Raw regional `missing`/`drift` labels are retained for independent inspection; the overall score alone is not a complete regional disposition.

The translated older-history box intersects fixed bottom navigation. It is explicitly outside unobscured hero measurement, rather than labelled missing, matched or waived. The authentic `../captures/mobile.png` and `../captures/desktop.png` full-page captures show Show older notes following Find another player. Those captures support responsive visual review and the passing browser suite verifies history behavior; no same-scale quantitative score is claimed for older history.

Final source remains `381d501950d064d426ddce7c772fe449c485cfde`. All changes for this scope decision are analytical images, measurement scripts and documentation; no application source or test expectations changed.
