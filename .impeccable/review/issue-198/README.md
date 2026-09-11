# Issue #198 — campaign evaluation evidence

Evaluate implements find player → verify identity → capture shared evidence → move on.
The user locked **Shared evidence notebook** within Fieldhouse Wayfinding; the approved
[comp](../../mocks/decision/issue-198-shared-notebook.png), provenance and [surface brief](../../surfaces/evaluation.md) remain tracked.

[Round twelve](review-round-12.md) is the current validation record for failed discard
and unreadable recovery protection across form, code and drawer navigation. It records
the tested source, commands and results. Earlier passing and failed runs
remain in the [historical evidence archive](evidence-archive.md). All three local
suites are required before merge.

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
[round seven](https://github.com/eruvalca/Nova/blob/fae6ed6753339174b6d58534b84157eb996fd480/.impeccable/review/issue-198/review-round-7.md) and its [separate review](https://github.com/eruvalca/Nova/blob/fae6ed6753339174b6d58534b84157eb996fd480/.impeccable/review/issue-198/review-round-7-local-review.md).
Parent #170 retains complete campaign-loop acceptance. Monitoring remains active as described
in [the watch protocol](pr-watch.md); no merge is authorized.
