# Review-watch visual confirmation

Source: the PR #269 review-round commit containing this record, based on `33cdfd42`.
Captured by `CampaignPlaceBrowserTests.CaptureSettledPlaceSurfaceForEvidenceAsync` with
`NOVA_PLACE_EVIDENCE` enabled, using the real Aspire application and Playwright Chromium.
These are screenshots of seeded test data, not generated design comps.

The first batched desktop/mobile inspection showed native select text clipping team counts after a long
team name. One correction added a wrapping selected-team count line. The second batched inspection confirms
both observed counts remain readable on desktop and mobile. It does not claim a new whole-surface design
approval or change the previous comp measurement; the incumbent queue/filter and shell geometry remain
outside this count-label correction.

- [Desktop, 1440 × 900 CSS viewport, DPR 1](review-watch/desktop.png): full-page height 968; board 1144 × 557.
- [Mobile, 390 × 844 CSS viewport, DPR 1](review-watch/mobile.png): full-page height 1387; board 358 × 782.

The new summary uses existing `place-decision-note` and semantic text styles, adds no CSS tokens, keeps
Save placement reachable, and labels observed counts without introducing capacities or availability claims.
Behavioral checks and the independent code review are recorded in
[the validation record](../../../docs/issue-255-validation.md#review-watch-round-outstanding-findings-through-2026-09-14-1838-utc).

## Capture checksums (SHA-256)
- desktop.png: 8AD3093BCFDD81856FE9F96A0D41A871004495F0A0015A90D40E1CC605C10F4B
- mobile.png: A2AF96FD7EF1F79D6C5A9E41F0D5367DA07E67141D9683D52275E6B0C8025401
