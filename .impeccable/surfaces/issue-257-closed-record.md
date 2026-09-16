---
version: 1
primary_target: "Nova.UI/Features/Campaigns/Components/CampaignClosedRecord.razor"
related_targets: ["Nova.UI/Features/Campaigns/Components/CampaignClosedRecord.razor.cs", "Nova.UI/Features/Campaigns/Components/CampaignClosedRecord.razor.css"]
---

# Issue 257 Closed campaign record

Mode: Operate. Local extension of the existing Close board and campaign shell. Approved implementation plan: inline selected-participant history, search and outcome discovery, one PR. The #256 reference and its comparison disposition remain unchanged; this extension follows the established composition rather than introducing a new visual world or comp.

## Direction contract

THESIS: One attributable campaign record keeps final outcomes and their evidence together.

OWN-WORLD: Fieldhouse paper boards, sea-glass grouping, teal navigation, existing system typography and hairlines.

STORY: Identify the Closed campaign, read its final totals, find a participant, inspect campaign-only changes, and deliberately review an eligible reopen.

FIRST VIEWPORT: Preserve the actual shell and four Route Markers. Compact closure attribution and totals lead a full-width searchable, team-grouped roster. Inline participant history follows selection; bounded lifecycle history and the shared lifecycle checkpoint follow the record.

FORM: Extension of the approved #256 final roster board; seed issue-257-approved-plan. No new comp round for this scoped extension.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

## Interaction and evidence

- Native GET search/outcome filters and URL-backed 50-row paging; group counts describe the page only.
- Selected history is read-only, bounded to twenty events, and remains campaign-local. Evaluation uses the existing destination and Return to Close.
- Archived players/teams remain visible; stored actor attribution survives departure. Record integrity is checked before filtering.
- Loading, empty, filtered-empty, long content, page recovery, independent read failures, authority changes and reopen/re-close are required states.
- Desktop and 390px mobile retain the shell, full labels, keyboard focus and 44px targets. No export, print, new audit ledger or lifecycle command owner.
