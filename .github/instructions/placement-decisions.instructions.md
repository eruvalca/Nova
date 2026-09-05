---
applyTo: "**/Features/Campaigns/**/*.cs,**/Features/Campaigns/**/*.razor,**/*Placement*.cs,**/*PlayerCampaignAssignment*.cs,Nova/Features/Players/**/*.cs,Nova/Features/Teams/**/*.cs,Nova/Features/Attention/**/*.cs,Nova.Unit.Tests/Campaigns/**/*.cs,Nova.Integration.Tests/**/*Campaign*.cs,Nova.Browser.Tests/**/*Campaign*.cs"
description: "Placement decision semantics: participation, same-season precedence, withdrawal authority, immutable history, and no-op saves."
---

# Placement Decisions

The [placement foundation](../../docs/placement-decision-foundation.md) records the implemented
contracts and regression evidence; [PRODUCT.md](../../PRODUCT.md) owns product intent.

- `Undecided` is technical participation without a saved decision. Enrollment creates neither
  decision attribution nor placement activity. Keep campaign-local saved outcomes separate from
  effective-season truth in contracts and queries.
- Select the latest saved same-season decision by `SeasonOpeningSequence` before checking team
  validity. Enrollment rows do not supersede; NotSelected, Withdrawn, or an invalid latest team
  must not cause fallback to an older Assigned row. Valid Assigned players permit optional
  reassignment and are not unresolved; a new season resets eligibility for active players.
- Placement mutations belong to the current season's Active campaign. Closed outcomes remain
  unchanged. Withdrawn is terminal in its owning campaign, including after reopen; only an
  administrator may supersede it in a later Active campaign.
- An identical local save still checks the expected token and preserves attribution, token, and
  activity. The same outcome/team recorded in a later campaign is a new superseding decision
  with its own attribution and atomic activity.

Use the existing domain-persistence and testing skills. The foundation document identifies which
read-model consumers still need integration; do not assume existing counts implement these rules.
