---
version: 1
slug: "i-features-campaigns-pages-campaignworkspace-razor"
primary_target: "Nova.UI/Features/Campaigns/Pages/CampaignWorkspace.razor"
related_targets: ["Nova.UI/Features/Campaigns/Components/CampaignRosterFilters.razor","Nova.UI/Features/Campaigns/Components/CampaignRosterTable.razor","Nova.UI/Features/Campaigns/Components/CampaignRosterCards.razor","Nova.UI/Features/Campaigns/Components/CampaignPlacementsPanel.razor"]
---

# Campaign workspace

**Mode:** Operate

## User success

Administrators and evaluators should locate the campaign's current state, the players needing attention, and the next valid action within seconds during a live tryout. Dense information stays visible and conventional controls stay familiar.

## Fieldhouse Wayfinding

The workspace behaves like a calm venue directory rather than a generic SaaS dashboard. A factual campaign route line anchors the page, then a scan-ready working field carries evaluation, placement, overview, and closeout tasks. Matte enamel sign surfaces, printed roster strips, and punched markers supply the visual grammar without simulating sport or turning state into decoration.

## Hierarchy

1. Campaign identity, lifecycle state, dates, and participant count.
2. A real four-stop phase route mapped to the existing workspace tabs.
3. The active task region, with filters adjacent to its results.
4. Row-level evaluation or placement actions.
5. Supporting overview and closeout information.

## Responsive behavior

Desktop uses a stable left navigation rail and broad working field. Mobile uses the existing compact navigation and keeps the phase route horizontally scrollable; roster tables continue to switch to cards. Information order and actions remain unchanged across breakpoints.

## Interaction and accessibility

Preserve InteractiveAuto, authorization, URL-backed tab state, filtering, sorting, paging, drawers, and placement persistence. Route markers are buttons with the existing tab semantics and an unambiguous active state. Keep visible focus, minimum touch targets, native controls, semantic regions, and reduced-motion-safe behavior.

## Visual constraints

Use the existing kelp palette. Primary teal is reserved for active wayfinding and primary actions; gold marks attention only. Avoid gradients, glass, decorative scoreboards, sports imagery, ornamental progress, thick colored side borders, and card grids that obscure operational hierarchy.
