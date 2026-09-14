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
| `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` | **3172 passed, 0 failed** |
| `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` | **608 passed, 0 failed** |
| `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` | **179 total, 0 failed, 172 succeeded** (7 env-gated a11y captures skipped) |
| `npm run check:contrast` (from `Nova/`) | **PASS** — every documented pair met its threshold (minimum 4.67:1 against 4.5) and no Bootstrap-blue literal was found |

The Aspire-backed suites provision their own AppHost through
`DistributedApplicationTestingBuilder.CreateAsync<Projects.Nova_AppHost>`, so no separately running
AppHost is required; the suites were run serially.

## Comp fidelity — measured, and not a pass

`comp-diff` against the approved comp, run without a spec so regions come from the comp's own
horizontal bands:

`node .agents/skills/impeccable/scripts/comp-diff.mjs --comp .impeccable/mocks/decision/issue-255-place-queue-rail.png --build .impeccable/review/issue-255/captures/desktop.png --out-dir .impeccable/review/issue-255/diff/final`

Measured: **overall 57% (contradicted)** — structure 48%, color 89%, detail 21%, bands 81%, with
seven region rows in `diff/final/report.json` plus the side-by-side, heatmap, and paired crops.

This is **not** an approval and is not recorded as one. Two things are worth separating:

- The finish review's first finding — an unbounded queue rail that stretched the board to roughly
  4 300 px and pushed the working sheet away from the queue — is **fixed**, and the capture proves
  it: the settled page's `documentHeight` went from **4285 to 953**, so the whole surface now fits
  one viewport with the rail beside the sheet.
- The score itself barely moved (56% → 57%) *because* the earlier low bands were not measuring the
  board at all. With the page now one screen tall, the comparison is apples-to-apples in extent and
  still reports structure 48% and detail 21%. That is the comparison, not the build: the approved
  comp is a **generated** image whose type, text, and micro-layout are the generator's invention, so
  glyph-level detail and fine structure cannot match a real render even when the composition is
  right.

The honest conclusion is that this whole-frame diff is not a usable fidelity signal for this surface
and is not treated as one. A Place-content-scoped comparison of the kind #198 used for Evaluate, or a
human comparison against the comp, remains outstanding. The measured numbers are retained as
measured rather than re-derived to look better.

## Independent review

**Code review** (fresh context, full diff vs `71fcd89b`), disposition *not blocking*. It found one
HIGH defect and four lower findings, and separately confirmed invariants 1–9 as correct or
acceptable, including that no client-side eligibility re-derivation, no patching from the mutation
response, closed-posture immutability, and the token/conflict rules all hold.

**Finish review** (`impeccable-finish-reviewer`), disposition **fix**, six material findings.

Both reviews' findings were fixed in this slice except where noted:

| Finding | Source | Disposition |
| --- | --- | --- |
| An obsolete selection read could adopt a participant the URL no longer selects, so "Save placement" could record a decision against the wrong participant (HIGH) | code review | **Fixed.** Every entry to `RefreshSelectionAsync` now invalidates the in-flight read, and `AdoptSelectionAsync` refuses a row that no longer owns the selection. Covered by a new ordering regression test that holds the exact read open across a selection change. |
| The Closed working sheet asserted "No effective season placement" for every participant, an unsupported negative claim about data the Closed read never supplied (MEDIUM) | code review | **Fixed.** The effective-placement definition now renders only for the Active posture. |
| The Closed campaign-local summary read was the only unwrapped read, so a transport failure escaped as an unhandled renderer error (MEDIUM) | code review | **Fixed.** Wrapped like its neighbours; the existing success check is retained. |
| Search raised a navigation and an authoritative read on every keystroke, and bound the input to the applied value so a completed round trip could rewrite the field mid-word (MEDIUM) | code review | **Fixed.** The field is bound to a panel-owned draft and applied after a 350 ms debounce matching the Roster destination. |
| A failing first load left the applied-lifecycle marker behind, repeating the same failing read on the next parameter pass (LOW) | code review | **Fixed.** The posture marker advances when the posture is decided. |
| The queue rail was unbounded and the working sheet did not hold position, so the board stretched and the THESIS ("one decision never costs you the queue") was broken in behavior (fix 1) | finish review | **Fixed.** The rail is now a bounded scroll region matching the shell's `.roster-scroll-region`, so the board is one screen tall and the sheet stays beside the queue. |
| The rail head read as four equal rows rather than "large count plus quieter totals" (fix 3) | finish review | **Fixed.** The lead count keeps the Title step in amber; the other three drop to the Label step. |
| An unearned eyebrow kicker sat above the player's name, against the comp and the craft floor (fix 2) | finish review | **Fixed.** The tryout number now sits in the identity line beneath the name. |
| `Close and reload`, the `btn-sm` Retry affordances, and "Clear filters" sat below the 2.75rem target on phones (fix 4) | finish review | **Fixed.** Every control the panel owns meets the target at phone widths. |
| The save statement named no movement (fix 5) | finish review | **Fixed.** It now reports the recorded outcome and the resulting Needs-placement count. |

## Outstanding before merge

| Check | Status |
| --- | --- |
| Browser suite | **179 total, 0 failed, 172 succeeded** (7 env-gated a11y captures skipped) |
| Place-content-scoped comp comparison | not done; the whole-frame measurement above is not an approval |
| A separately covered "selection cleared while a read is in flight" case | not done. The branch is guarded by the same invalidation as the covered case, but a dedicated test was attempted and withdrawn: driving a nullable parameter back to `null` through bUnit's re-render did not produce the expected branch, and the cause was not resolved within budget. Recorded as a known coverage gap rather than left as a failing test. |
| A browser-level keystroke-to-URL assertion for the Place search field | not done. Filling the shared search input did not change the URL in the browser harness even before the debounce was added, and the cause was not resolved within budget; the browser test instead drives the applied search through the URL, which proves the section-widening and filter-truth behavior. The keystroke path itself is the same shared `CampaignRosterFilters` wiring the Roster destination uses, and it is covered there. Worth confirming by hand before merge. |

## Deferred to #254 (recorded, not built)

- Reassignment and cross-campaign supersession confirmation copy, including the brief's `Withdrawn`
  and replaced-resolved-decision confirmations. This slice records the three outcomes immediately,
  matching the capability the replaced panel had.
- Prior-season placement history and the `Keep on {Team}` fast path.
- The stale-winner presentation and the ambiguous-result matrix.

One finding from the code review is deliberately left as a follow-up rather than fixed here:
conflict recovery resumes editing even when its reload failed. The consequence is bounded — the stale
token still makes the next save conflict rather than overwrite — and the richer recovery matrix
belongs to #254.

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
