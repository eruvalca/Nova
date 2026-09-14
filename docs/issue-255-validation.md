# Issue #255 — Place queue, participant evidence, and decision recording

Validation record for the delivery of child slice
[#255](https://github.com/eruvalca/Nova/issues/255) of parent
[#199](https://github.com/eruvalca/Nova/issues/199), part of epic
[#170](https://github.com/eruvalca/Nova/issues/170).

## What changed

The `tab=place` destination was rebuilt as a player-first decision station and the replaced
`CampaignPlacementsPanel` row-per-participant composition was deleted, so no second mutation path
survives beside the new surface.

- **Composition** — locked to **Queue rail beside a fixed working sheet** from a three-comp surface
  round (the other two dealt comps were Programme strip and Waypoint stepper). The approved comp and
  its provenance are tracked at `.impeccable/mocks/decision/issue-255-place-queue-rail.*` and the
  direction contract is recorded in `.impeccable/surfaces/placement.md`.
- **URL state** — `CampaignWorkspacePlacementState` now carries the full Place discovery set
  (search, section, multi-year, tags, campaign-local outcome, campaign-local team, sort, page)
  serialized under `placement*` keys, so Place filters stay independent of Roster's while both
  reuse the canonical builders from #251/#252. `BuildPlaceWorkspaceUrl` also carries the Place
  selection and the Evaluate return affordance, and the closed-workspace normalization clears the
  Place section with the roster eligibility filter.
- **Surface** — new `CampaignPlacePanel` (queue / selection / mutations / display partials) over the
  delivered `GetCampaignEffectivePlacementsAsync` read and the existing `UpdatePlacementAsync`
  mutation. Active campaigns render the four unfiltered eligibility sections and the decision
  controls; Closed campaigns render the immutable `GetClosedCampaignRosterAsync` record plus the
  existing campaign-local outcome summary, read-only for every role.

## Boundary decisions

- **Concurrency conflict** — today's recovery ceiling is ported as-is: a conflict statement plus
  **Close and reload** that re-establishes authoritative state while preserving discovery context.
  The brief's richer stale-winner presentation ("Review latest placement", new changer/outcome) stays
  with sibling slice [#254](https://github.com/eruvalca/Nova/issues/254).
- **Evidence depth** — the selected player's evidence uses exactly what
  `CampaignEffectivePlacementItem` provides. The effective decision's source campaign/team stands in
  as prior-team context. Prior-**season** history, the "Keep on {Team}" fast path, and reassignment
  remain #254's; no foundation issue was needed because this slice required no missing contract.
- **Queue versus close outcomes** — Needs placement and "no campaign-local decision" are deliberately
  different sets. `BuildReviewUnresolvedUrl` now targets `placementEligibility=all&placementOutcome=undecided`
  rather than the Needs-placement queue, so a zero queue never substitutes for the explicit local
  outcomes a campaign needs before it can close.

## Deferred to #254 (not built here)

- Reassignment and cross-campaign supersession confirmation copy, including the brief's
  `Withdrawn` / replaced-resolved-decision confirmations. This slice records `Assigned`,
  `NotSelected`, and `Withdrawn` immediately, matching the capability the replaced panel had.
- Prior-season placement history and the `Keep on {Team}` fast path.
- The stale-winner presentation and the ambiguous-result matrix.

## Guidance read

`AGENTS.md`; `blazor-architecture`, `ui-design`, `bootstrap-theme`, `csharp-conventions`,
`service-layer`, `placement-decisions`, `season-lifecycle`, and `testing` instructions;
`.agents/skills/impeccable/reference/new-work.md`; `docs/placement-decision-foundation.md`;
`docs/campaign-workspace-roster.md`.

## Commands run and results

| Command | Result |
| --- | --- |
| `dotnet build Nova.slnx` | succeeded, 0 errors |
| `dotnet format Nova.slnx --verify-no-changes` | clean |
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | **3167 passed, 0 failed** (28 of them new `CampaignPlacePanelTests`) |

## Not yet run — outstanding before PR/merge

The local PR gate requires all three suites. The following were **not** executed in this session and
must pass locally before this branch is proposed for merge:

| Check | Status |
| --- | --- |
| `dotnet test --project Nova.Integration.Tests/... --no-build` | not run (requires the Aspire AppHost/PostgreSQL) |
| `dotnet test --project Nova.Browser.Tests/... --no-build` | not run (requires the Aspire AppHost + Chromium). `CampaignPlaceBrowserTests` is newly written against the new surface and is **unverified**. |
| `npm run check:contrast` (from `Nova/`) | not run; no `scss/` or `package.json` change was made |
| `comp-diff.mjs` against the approved comp | not run; requires a rendered capture |
| `impeccable-finish-reviewer` disposition | not run |
| Independent local review of the diff | not run |

The surface's own `razor.css` uses only semantic `--bs-*` tokens (no raw hex), so the contrast check
is expected to be unaffected, but it remains part of the gate.

## Test coverage added

`Nova.Unit.Tests/Campaigns/CampaignPlacePanelTests.cs` and `.Mutations.cs` (28 tests) cover:

- unfiltered section totals independent of the loaded page, and foundation labels
- the Needs-placement browsing default widening to every section once a search applies
- every applied discovery control reaching the authoritative read unchanged
- the queue never narrowing to the linked participant
- written empty state with a clear-filters action, and the region's own failure with retry
- keyboard-operable rows carrying tryout number, graduation year and written outcome
- the local decision staying separate from an inherited effective placement
- correction evidence for an invalid latest assignment with no historical fallback and no invented team
- applied tags and the Evaluate return affordance
- the Closed posture using the immutable read, offering no decision controls, and showing its
  campaign-local outcome totals
- a member without edit capability seeing evidence but no controls
- `Assigned` without a team blocking the submit with a written reason and no mutation call
- leaving `Assigned` dropping the team from the submission
- the local token being presented and the replacement token adopted
- a committed save re-reading queue, totals and selection authoritatively rather than patching
- replacing an existing local decision remaining available
- the conflict blocking editing until a confirmed reload, then recovering with discovery preserved
- validation refusals staying local with the controls available for a corrected retry
- a committed save that cannot be refreshed not being announced as success
- an unavailable saved team rendering disabled rather than substituted
- compatible team choices bounded to the selected graduation year, and their failure staying regional
