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
| The "selection cleared while a read is in flight" coverage gap | **closed.** Two ordering tests now cover the superseded-read invariant using distinguishable participant names and sheet-scoped assertions, after the original test was found to be passing vacuously (every row shared the name "Avery Chen", so the assertion matched the queue rather than the sheet). Chasing that produced a further real fix: the working sheet no longer keeps rendering the previous participant with active controls while a new selection resolves. |
| Place-content-scoped comp comparison | not done; the whole-frame measurement is not an approval. See the blocker below. |

### Open defect: typing in the Place search field does not apply the search

Reproduced deterministically. Typing into the shared search field on the Place destination never
applies the search: after 23 seconds of retrying with `FillAsync` and then with real key events
(`ClickAsync` + `Keyboard.TypeAsync`), the URL was still `/campaigns/{id}?tab=place` with no
`placementSearch`. Every other Place control is interactive in the same session — rows, the outcome
and team selects, Save, the section buttons, and the pager — so this is not a hydration-window
artifact and not a general interactivity failure. The `Clear filters` control also works, so the
applied-search path itself is sound; it is the keystroke-to-applied path through the shared
`CampaignRosterFilters` field that does not fire.

The browser test therefore drives the applied search through the URL (which proves the
section-widening, filter truth, and unfiltered-total behavior) and names this defect inline. This is
recorded as an **open defect in a control this slice renders**, not as an untested path. The likely
area is the panel-owned `_searchDraft` draft and 350 ms debounce added in response to the code
review; the pre-existing `CampaignRosterFilters` wiring was reused unchanged.

## Blocker: the comp comparison cannot pass for this surface

The comp comparison is at 57% (contradicted) and I am recording that it **cannot be brought above
the 72% threshold inside this slice**, with evidence rather than assertion.

A pixel census of the settled build capture (1440×953) and the approved comp gives the reason:

| Region | Build | Comp |
| --- | --- | --- |
| Sea glass (`--bs-primary-bg-subtle`, the named rule for active fields) | 20.0% overall; bands 0–2 (the campaign shell) run 15–24% sea glass with **0%** paper white | `#c4d1d2` at 2%, `#9ea9aa` at 1% — the same fields rendered **grey**, not teal-tinted |
| Paper white | 38.1% overall; the board region runs 43–73% | `#f7f8f8` at 93% — the generator flattened board and field into one near-white |

So the build's largest palette divergence from the comp is that the build **correctly** uses the
theme's sea-glass token for active and tinted fields, while the generated comp painted those fields a
neutral grey. Matching the comp's palette would mean replacing `--bs-primary-bg-subtle` with a grey
the design system forbids, in the shell that #255 does not own. The remaining gap — `detail 21%` — is
glyph-level anti-aliasing between a generated raster and a real render, and cannot be closed by build
quality at all.

Two further reasons the number is not a build signal here:

- The build's `documentHeight` is 953px against the comp's 1024px frame, but the composition is
  distributed differently: the incumbent shell (campaign sign, four route markers, the readiness
  region) occupies roughly the top third of the real page, whereas the comp compresses that chrome
  into a thin strip. Scaling the build to the comp's width therefore stretches the shell against a
  comp that renders it small.
- The spec is absent — `.impeccable/build/spec.json` is the #198 Evaluate record — so regions come
  from the comp's own horizontal bands and land on different content than the same bands in the build.

Before the bounding fix the whole-frame number was 56% and *most* of the low bands measured empty
board rather than content; that part is fixed and proven (`documentHeight` 4285 → 953). What remains
is a comparison between a correct build and a comp that contradicts the design system.

**What would actually settle it** (each needs a decision this slice cannot make on its own):
re-generate the comp against the real rendered surface so the comparison has a faithful reference;
or agree a content-scoped measurement boundary, as #198 did for Evaluate, with the shell reviewed
separately; or retire the whole-frame score for this surface and rely on the design-system checks,
the finish review, and the curated captures instead. The measured numbers are retained as measured
rather than re-derived to look better.

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
