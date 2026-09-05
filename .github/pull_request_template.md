## Behavior

<!-- Explain the concrete trigger and resulting behavior. Link the feature contract or
transition/test mapping for stateful work; ordinary changes do not need a new table. -->

## Validation

<!-- Summarize the fresh `node eng/verify.mjs run --profile pre-pr` run: revision,
run ID, executed suite counts, explicit optional skips, and any limits.
Use the push profile with a current base for follow-ups and a fresh pre-merge run.
Do not combine evidence from older runs or treat passing tests as coverage proof. -->

- [ ] Fresh local pre-PR verification passed; hosted checks are passing for this revision.
- [ ] For behavioral fixes, the regression and a neighboring scenario pass; relevant siblings have evidence-backed dispositions.
- [ ] For consequential stateful changes, an independent reviewer examined the diff, behavioral contract, and test mapping; supported findings are resolved.

<!-- Review preparation: include every review body (including suppressed entries),
inline thread, and reply in a compact findings ledger. Batch coherent fixes before
requesting another review, and avoid overlapping reviews of changing revisions.
For design work, retain approved/final evidence and its source manifest in Git. -->
