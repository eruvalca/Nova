# Seasons directory

Status: Confirmed

## Direction contract

THESIS: One vertical season route; the current season is the lit stop, and past seasons descend beneath it.

OWN-WORLD: Fieldhouse Wayfinding flat boards, one hairline spine, circular route stops, compact ruled rows,
teal for the live stop and sea glass for its field.

STORY: Confirm the current season, walk bounded history, and open any season's record.

FIRST VIEWPORT: The Seasons heading and the lit current-season stop carrying its name, its date window, and
its Current chip.

FORM: Two independent regions on one spine. The current stop always leads from its own bounded read, so
currentness never depends on which history page is loaded; the paged history stops follow it.

Issues: [#258](https://github.com/eruvalca/Nova/issues/258), child of [#204](https://github.com/eruvalca/Nova/issues/204),
part of [#171](https://github.com/eruvalca/Nova/issues/171) under epic [#163](https://github.com/eruvalca/Nova/issues/163)

Visitor mode: Operate

## Job and audience

Nova must let any approved club member answer one question before starting or continuing club work: which
season is current, and what seasons came before it. A coach or evaluator arrives here from the Club area to
confirm that the season they are working in is the season the club is actually on, and to reach a specific
season's record. An administrator arrives here to confirm the same thing and, when the club is ready, to
begin the next season.

The directory is a directory, not a season manager: it presents authoritative currentness, a bounded and
deterministically ordered record of past seasons, and the entries into those records. It does not create,
edit, or advance seasons.

## Outcome and proof

- The current season leads the directory as one lit stop with its name, date window, and an explicit
  **Current** chip. Currentness comes only from the club's authoritative pointer: season dates are metadata
  and never decide it.
- Past seasons follow as ruled route stops in the delivered contract's order (current first, then start date
  and identifier descending), each carrying only what the contract supplies: name, date window, and
  currentness. Nothing is recomputed, fabricated, or cloned to fill a row, and no roster from a different
  season is consulted.
- The current season appears exactly once. Its own bounded read drives the leading stop, and the history list
  excludes it on every page.
- Absent seasons are stated, never inferred from an empty list. A club with no season at all is the
  first-season state; a club with recorded seasons but no current one is the recovery state, and the two read
  differently.
- Every approved member reaches the directory and every season record; only administrators see the
  **Start next season** entry.

## Composition and behaviour

- One route spine: a single hairline with circular stops. The current stop carries the sea-glass field and the
  teal-filled marker; history stops are hollow markers on ruled rows.
- History is URL-backed paging (`?page=`), with Previous/Next anchors and a "Page N of M · recorded seasons"
  caption. Page one is canonical and carries no query string. Direct links, refresh, keyboard activation, and
  scripting-disabled navigation all keep working because every destination is a real anchor.
- Paging is arithmetic over the contract's `TotalCount`; the directory never silently renders only the first
  page. A page beyond the recorded history states that plainly and offers a route back to page one.
- Each region owns its own loading, error, and retry, so one unavailable region leaves the other intact.
- Long history, empty history, a first season, a missing current season, partial failure, and stale results
  after a club or authority change are all handled; obsolete responses can never repopulate the directory.

## Role-correct affordances

The directory is readable by every approved club member. The read requires club membership, not administrator
scope, and member reads therefore never depend on administrator authority. Advancement is the one
administrator-only entry, and it leads to the destination that issue #260 owns.

## Recorded decision

The surface decision ran at `--scope surface --mode operate` (surface seed `c157253d`), which dealt three
compositions of equal salience: **Season route** (the lead), *Stop strip over register*, and *Pinned register*.
The served decision page was not answered before the round closed, and the session was explicitly unattended:
the build proceeded with the roll's assigned lead, **Season route**, which also carries the directory
composition already settled with the user (a persistent current band above a paged history list that never
repeats the current row).

This round was **code-led**: no image-generation tool and no `OPENAI_API_KEY` exist on this machine, so no
comp could be produced and the cards carried wireframe schematics instead. `.impeccable/config.local.json`
records `buildPath: code` for this machine only; the committed default in `.impeccable/config.json` is
unchanged, and no comp-fidelity gate was claimed. The substituted process is disclosed in the validation
record for this change.

Recorded downgrade, in the user's own words from the live session: *"The user is not available to respond and
will review your work later. Work autonomously and make good decisions."*

## Finish review

The shipped `impeccable-finish-reviewer` reviewed this surface at disposition **fix** (no approved comp
exists, so the fidelity matrix was judged against this contract's OWN-WORLD and DESIGN.md with colours
sampled from the captures; both captures were validated as valid evidence).

Resolved:

- The page title now carries DESIGN's documented page-title step (`2rem`/700/`-0.025em`/1.2) instead of
  Bootstrap's viewport-fluid `h1`, so the directory identity no longer drifts with width.
- The `Current` chip is a labeled status treatment (emphasis ink, subtle border, `0.875rem` label) rather
  than a solid primary badge sharing the CTA fill. It keeps the paper field because the lit stop's own field
  is the same sea glass.
- The advancement entry is demoted to an outline control and both it and the pager anchors now meet the
  documented `2.75rem` control height (measured 38px before).
- The admin first-season sentence no longer implies a single path ("Establish the club's first season,
  including when you create its first campaign").
- The `#101010` rectangle in the first viewport was identified as the **Club shell's** post-navigation
  heading focus (`ClubShell.razor.js` focuses the hall's `h1`). It is shell behaviour, not this surface's, so
  it was not suppressed: the evidence pass now captures with nothing focused, and the ring measures 0 pixels
  across all six captures (768 and 1314 before).
- State evidence now exists for the member view, the first season, the recovery state, and a middle page of
  a long history.

Raised as a foundation divergence rather than declined: the club-setup brief explicitly requires that a
creator establish "exactly one club and its first season" and that "club and first-season creation" commit
atomically (`club-setup.md:17`, `:147`), and the shipped flow does not do that —
`Nova.UI/Features/Clubs/Pages/ClubOnboarding.razor.cs` has no season step and `ClubEndpoints.Complete` is the
post-creation cookie-refresh hop, with `CampaignCreationService` the only inline season creator. The directory
states the first-season state accurately for what ships; the divergence belongs to the club-setup slice and is
raised against #163 on issue #258.

Open, disclosed: no comp exists, so the comp-fidelity promise is unmet for this surface rather than waived by
a comp round.

