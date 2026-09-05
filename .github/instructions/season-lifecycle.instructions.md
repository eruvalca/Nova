---
applyTo: "**/Features/Seasons/**/*.cs,**/Features/Campaigns/**/*.cs,**/Features/Campaigns/**/*.razor,Nova/Entities/ClubEntity.cs,Nova/Entities/SeasonEntity.cs,Nova/Data/Configurations/ClubEntityConfiguration.cs,Nova/Data/Configurations/SeasonEntityConfiguration.cs,Nova.Unit.Tests/Seasons/**/*.cs,Nova.Unit.Tests/Campaigns/**/*.cs,Nova.Integration.Tests/**/*Season*.cs,Nova.Integration.Tests/**/*Campaign*.cs,Nova.Browser.Tests/**/*Campaign*.cs"
description: "Season lifecycle invariants: authoritative currentness, campaign season selection, advancement, and historical-data preservation."
---

# Season Lifecycle Invariants

For campaign lifecycle product semantics, read `PRODUCT.md` → **Operating Context** and
**Capabilities**. This file records the persistence and concurrency invariants that implement
those product decisions.

- `ClubEntity.CurrentSeasonId` is the sole authority for a club's current season. Keep it as an
  optional, tenant-consistent relationship; null is the supported onboarding or recovery state.
- Season dates are metadata. They may produce warnings or bound linked campaign dates, but they
  never determine currentness or cause automatic lifecycle transitions.
- Creating a standalone season establishes the first current season only when the club has none.
  Campaign creation may select only the current season; inline season creation is allowed only in
  the no-current state and assigns the pointer atomically with the campaign.
- Starting the next season must compare the caller's expected current season with locked database
  truth and atomically insert the new season plus update the club pointer. Do not infer advancement
  from date ordering or make overlapping dates a hard blocker.
- Season metadata updates never accept or change currentness. Reject date changes that would place
  an existing linked campaign outside the season window.
- Advancement preserves durable teams and all previous seasons, campaigns, assignments,
  participation, placements, and history. It does not create a campaign or copy roster state into
  the new season.

Follow `.github/instructions/service-layer.instructions.md` for the club-season-first advisory-lock
order and retry-safe transaction pattern. Use the `add-domain-persistence` and `nova-testing` skills
for persistence changes and PostgreSQL race coverage.
