# Issue #198 — campaign evaluation evidence

Evaluate implements find player → verify identity → capture shared evidence → move on.
The user locked **Shared evidence notebook** within Fieldhouse Wayfinding; the approved
[comp](../../mocks/decision/issue-198-shared-notebook.png), provenance and [surface brief](../../surfaces/evaluation.md) remain tracked.

[Round seven](review-round-7.md) identifies the current application validation: build and
format passed, 3,126 unit tests passed, and the unchanged provider boundary retains 605
passing integration tests at `f76ae557`. Full browser runs were 171/176 and 175/176; after
the final test-readiness correction, the changed class passed 20/20. This is not a clean
full-browser gate. All three local suites and a clean full browser run remain required
before merge. [Round eight](review-round-8.md) records the review-size correction.

The [finish disposition](finish-review.md) retains explicit user approval for sheet-relative
measurement: **76.11% against 72%**, with a separate [shell review](sheet-relative/shell-review.md).
Original failing comparisons, approval history and intermediate measurements are preserved,
not relabeled as passes. Representative final captures and their metadata remain under `captures/`.

[Historical evidence archive](evidence-archive.md) links every relocated record to immutable
commit `b577001319b3302da723d93de60356852b3ba00b`, with Git blobs and SHA-256 values.
It includes full original validation, independent code review, rounds one through six,
and sheet-relative measurement inputs/results. The older artifact manifest in that archive
in turn points to commit `1a9cb8be`, preserving rejected concepts and failed attempts.
Local files/ZIPs remain available for resumption; the pinned Git commits provide shared retention.
Previously tracked generated build/diff files are restored to the base revision and do not
represent current Evaluate evidence. No application code, tests or checks were removed.

Guidance read and its source examples are recorded in [instructions hygiene](instructions-hygiene-review.md),
[round seven](review-round-7.md) and its [separate review](review-round-7-local-review.md).
Parent #170 retains complete campaign-loop acceptance. Monitoring remains active as described
in [the watch protocol](pr-watch.md); no merge is authorized.