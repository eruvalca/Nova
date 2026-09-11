# Review round nine — deferred selection and relevance coverage

Base `fae6ed6753339174b6d58534b84157eb996fd480`; CI Build and Unit Tests passed.
Review `5181303665` says “Needs a closer look” but contains two actionable findings,
so it does not satisfy the user's clean-review stopping exception. Session
`5607c1c4-aaae-4e67-8801-8f74a13bdb2f` and workflow `34623578632` agree on two distinct
moderate findings; the raw log also contains revised wording for the same Place
finding. All 27 reviews, seven issue comments and 19 threads were inspected; two
new threads remain unresolved, with no further nested-comment page.

- `PRRT_kwDOSz2VcM6hj1U3`: while a placement save is in flight, retain requested
  filter/page state and selected participant together. Apply both plus linked-focus
  intent through the same helper used by immediate navigation, before deferred
  reload. Preserve latest-request wins and cancellation when returning to the
  already applied destination. The old completion path left applied selection stale,
  omitted focus and caused a redundant reload on identical parameters.
- `PRRT_kwDOSz2VcM6hj1Vf`: add service and PostgreSQL evidence that exact-number
  relevance is ordered before paging in Active and Closed paths. Include sufficient
  matches to put the exact number beyond the first default name-sorted page, preserve
  default Roster ordering, and assert complete ordered identities across pages.
- The review body also identifies the outstanding full-browser gate. Run the complete
  suite after the correction; do not replace a failed full run with focused evidence.

The implementer reused `add-blazor-ui`, lifecycle/state and JS interop references,
current Blazor/validation/C# rules and placement-decision semantics. The testing agent
applies `nova-testing`, its component/provider references and the installed test
pipeline. No design-system change or new instruction/skill is needed. The separate
reviewer inspects the actual diff and sibling paths; [its record](review-round-9-local-review.md)
identifies its own evidence. Raw collection and command logs remain ignored locally.

Final local validation passes. This record accompanies the single combined commit;
fresh CI and automatic review follow its push. No review will be requested manually
and no merge is authorized.

Source is `fae6ed67` plus six application/test files, manifest SHA-256
`08F105C3BBC22A6BBC5F0AC824749077F396FB587DD8E962D6E86DA973D22135`
(`.impeccable/archive/round9-source-manifest.log`). Added six unit and three provider
cases; no query implementation change was necessary. Build passed with zero warnings
or errors in 2m 43.71s. Full unit suite: 3,132 passed, zero failed/skipped, 40.943s.
Full PostgreSQL integration suite: 608 passed, zero failed/skipped, 1m 32.204s.
Commands: `dotnet build Nova.slnx`, then `dotnet test --project` the respective unit
and integration csproj with `--no-build`.

The first full browser run failed: 174/176 passed, two failed, none skipped, 10m 32.208s
(`--no-build --output Detailed --long-running 90 --xunit-diagnostics on`, with
`NOVA_A11Y_SCREENSHOTS=1`). Closed Roster timed out waiting for reload; the lost-response
case asserted its pending receipt while the new document still displayed “Restoring the
draft before editing…”. The latter is not evidence of erased pending state. Investigation
identified a repeated-refresh path: a persistent Closed history conflict can request parent
lifecycle reconciliation, which removes and recreates the requesting evidence component.
The correction retains mounted regions and shares the in-flight reconciliation guard.
The recovery test must wait without typing into or clearing the pending draft. Original
reload, exact replay-payload and single-note assertions remain required. Final evidence is
pending; this failed full run is retained, not replaced by a focused-pass claim.

The new async component regression bounds detail reads, retains the same selected
drawer through persistent conflict and verifies explicit Retry recovers; existing
Active/Closed/reopened cases remain. Separate review verified the shared guard,
stable child ownership and non-mutating restoration wait. The wait uses the existing
bounded functional-readiness policy, longer than the former five-second assertion;
it is not a latency SLO. No global timeout, final assertion, integrity response or
test-parallelism setting changed.

To retain room below Copilot's 20,000-line cap, four superseded round-seven/eight
records were untracked with local copies preserved and immutable `fae6ed67` links,
Git blobs and SHA-256 values in the [archive index](evidence-archive.md). Independent
verification matched all four. Current-round evidence, approved inputs and tests
remain tracked. The user requires a PR disposition comment before the single push.

Initial format verification found new initializer whitespace; the formatter applied
fixes but could not fix MA0051. The following build failed at 6m 30s because the
lost-response test reached 41 statements against a 40-statement limit. Grouping
reload and its non-mutating readiness wait into one helper fixes that diagnostic
without suppression or removing an assertion. Final build and verification follow.
Both review threads received pre-push replies (`3991807648`, `3991807934`).

Final source is `fae6ed67` plus ten C# files, SHA-256 manifest
`9D939814A56387A137D82EA78CBAD57ED509A3BEA4A0BEFC7023A0AC9F388BE3`
(`round9-final-source-manifest.log`), independently verified. The rebuilt solution
passed with zero warnings/errors in 1m 1.65s. Full unit: 3,133 passed, zero failed or
skipped, 1m 41.140s. Full PostgreSQL: 608 passed, zero failed or skipped, 6m 16.355s.
The same build-first / `--no-build` commands were used. Full browser: 176 passed,
zero failed or skipped, 9m 30.361s, with accessibility captures enabled and the same
detailed diagnostic flags as the failed attempt. Closed-history reload passed in
8.648s; lost-response reload passed in 49.195s, retaining original-payload replay
and exactly-one-note assertions. Source/assets were fixed and Aspire suites serial.
Full format verification (`dotnet format Nova.slnx --verify-no-changes --verbosity diagnostic`)
passed, exit zero, 0/946 files changed, 7m 25.650s. Raw `round9-final-*` logs are retained
locally. The ten source hashes remained unchanged after all checks. Final pre-push
collection found 29 reviews (two added records are our thread replies), seven issue
comments and 19 threads; only the two addressed threads remain open until the push.
The [complete validation summary](https://github.com/eruvalca/Nova/pull/253#issuecomment-5638793814)
was posted before committing or pushing, as requested.
