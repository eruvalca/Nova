# Issue 256 independent local code review

Reviewer: separate `/root/backend_review` context. Inspected the full implementation diff and untracked source against the approved plan, applicable repository rules and reported validation. No source edits or tests performed by the reviewer. Final review: **no new material findings**.

Five earlier findings were resolved and reinspected:

1. Explicit empty/whitespace Close blocker input was not rejected consistently. Added shared `NotWhitespace` validation and query regressions.
2. A context disposal failure after attempted commit could escape the guarded region and trigger automatic replay. Moved context lifetime inside the commit-attempt guard; PostgreSQL lost-acknowledgement tests prove unknown result with no duplicate events even after an opposite transition.
3. Razor string parameters passed literal `Owner`/`Error` text. Bound actual values to preserve authority identity and truthful regional errors.
4. A parent refresh delegate could finish without rendering the new readiness evidence. Added explicit final render and composed delayed-preflight regression.
5. Required post-mutation detail refresh could unmount the Close subtree and erase attempt feedback. Retained the subtree on read failure, published feedback before refresh, and added composed success/unknown-result delayed/failing refresh tests.

Final inspected scope: lifecycle/query services, contracts and URL builders, HTTP clients, new Close components, workspace authority/readiness integration and Place/Evaluate correction navigation. Design review was separate. Subsequent documentation, encoding/format normalization, native Evaluate form context preservation and added validation tests are identified in the [validation record](../../../docs/issue-256-validation.md).

Final delta review found no production defects; it confirmed the native Evaluate form context and UTF-8 normalization. One browser-fixture finding required query-only history navigation to use `GoBackAsync` with `WaitUntilState.Commit`; corrected before the full browser run, with participant-count/URL assertions proving the restored state.

The new disposal regression initially disposed only bUnit's rendered wrapper. The reviewer inspected installed bUnit/ASP.NET implementation and required awaited renderer disposal with a captured cancellation-token assertion. That regression now passes and proves a late cancellation-ignoring service cannot trigger another read. No production change was warranted.

When legacy Close browser assertions were updated for the new flow, review identified two coverage gaps: confirmation-button absence alone did not prove non-admin Review controls were absent, and phone lifecycle target checks had been replaced by discovery-control checks. Restored explicit absence of both Review controls plus administrator text; retained the later-opening restriction and added eligible 480px review/commit/Cancel target measurements. Both were reinspected as resolved; browser execution is recorded in the validation record.
# Review round 1 — guidance reload and independent fix review

The original PR self-review is retained at https://github.com/eruvalca/Nova/pull/281#pullrequestreview-5218594527. Review-round dispositions and validation live in [the authoritative record](../../../docs/issue-256-validation.md).

An independent agent (`reload_backend_review`) reviewed the uncommitted round-one diff against the reloaded repository guidance, including HTTP response validation, persisted roster ownership, lifecycle confirmation and cancellation, and the new regression tests. It found one additional shared-component defect: an older failed refresh could disable actions after newer valid evidence arrived. The fix detects superseded refresh results across Review, Retry and post-command recovery.

The reviewer reinspected the fix, including the synchronous parent-render case: “The adjustment is sound. It permits a fresh synchronous callback result while the parent’s parameter update is queued, rejects replacement evidence observed during the read, and retains exact evidence identity at dispatch. The delayed-null protection remains intact. No additional defect found; confirm the composed preflight and stale-response tests pass.” The focused suite subsequently passed all 314 cases.

The reviewer also approved the narrow CA1812 exception on the test-only restored roster component: bUnit constructs it through reflection, consistent with the existing restored-state fixtures; no behavioral test or coverage is disabled. No other round-one defect was found in the reviewed diff. Full-suite results are recorded in the authoritative validation record, not inferred from this static review.
# Review round 2 — independent disposition and delta review

The final sibling check extended the rem correction to focus outline widths/offsets (`.125rem`/`.1875rem`), retaining permitted hairlines and established breakpoints. The reviewer independently confirmed this matches the custom-length rule and preserves default-root appearance. Source remained frozen during the preceding browser run; the stylesheet edit was followed by fresh browser validation.

The reviewer subsequently checked the method-length helper extraction and fixture literal as behavior-preserving. It inspected the browser failure snapshot, reload scenario and recovery component, approving passive bounded settlement after full reload: this waits for the exact recovery-ready state without clicks, mutation retries or storage changes, and preserves the payload/receipt/activity assertions. It explicitly limited the diagnosis to readiness not established within the default five seconds; the underlying startup-duration cause is unmeasured. Final test results remain in the validation record.

The independent reviewer (`reload_backend_review`) verified the two suppressed server ordering claims as inapplicable: both ascending boolean chains yield Assigned, Not selected, Withdrawn, Undecided before team/name ties. It also verified that the client did not compare concatenated text lexicographically, but did omit a numeric team-ID tie check for equal team names. These dispositions are explained with test evidence in [the validation record](../../../docs/issue-256-validation.md).

After inspecting the complete round-two working diff and tests, the reviewer returned: “Round-two delta reviewed clean. Normalized role lookup now matches command enforcement. Numeric team ties are validated without reproducing database string collation; other discovery sorts retain their previous behavior. Search normalization, readable inherited-outcome copy, rem targets, and documentation changes are coherent.” The reviewer checked the Active/Closed client cases, server paging, persisted role identity and URL boundaries. It did not run tests or inspect refreshed browser captures; the main agent owns that final evidence.

# Review round 3 — independent disposition and delta review

The independent reviewer (`reload_backend_review`) checked the complete round-three source/test diff against the reloaded guidance. It verified that the alleged cross-tenant reopen conflict is inapplicable: `NovaDbContext` retains the current-club campaign query filter for administrators, and the command never bypasses it. The strengthened reopen/history regression leaves another club Active and preserves all prior outcome/activity assertions.

Final read-only verdict: “No material findings in the current 14-file round 3 delta. Verified loading/confirmation ownership, page bounds, reason-specific links, unknown-commit HTTP mapping, and tenant-scoped reopen behavior. Existing history/outcome assertions remain intact; no coverage was weakened.” The reviewer specifically checked the null-evidence detail-refresh interval, settled failure retry, parent-loading confirmation invalidation, all three handler guards, and both URL consumption and generation. It did not run suites; results and the analyzer-failure disposition are in the authoritative validation record.

The reviewer also confirmed the corrected helper XML and Activity lifetime, then reinspected the unit-failure fixes: explicit ARIA true/false strings across the three Close regions and a missing-opening fixture arranged after seed normalization. The log and interceptor implementation support both causes; assertions remain intact and no checks were weakened.

The next run established that the missing-opening database fixture also violates CK_Campaigns_StatusLifecycleMetadata. The reviewer acknowledged that its earlier fixture check missed this second constraint and approved removing only the impossible new query-theory row. Three valid persisted link/reason cases remain, and defensive MissingOpening policy coverage now includes another-Active overlap. No database constraint was bypassed and no existing test was removed.

# Review round 4 — independent disposition and delta review

The independent reviewer (`reload_backend_review`) verified the actual server eligibility filter rather than inferring it from the lossy client correction-reason projection. The server predicate already matches the closure policy; broader “ineligible” wording is appropriate, without a query change.

The reviewer inspected the feedback separation, four recovery permutations, new PostgreSQL snapshot gates and fixture authority. Two findings were corrected before execution: four browser label assertions/locators, and the Active fixture's initially incorrect assumption that local Not selected needs placement. Final reinspection: “Both findings are fixed. The four browser label updates preserve their assertions and correction journeys. The Active PostgreSQL fixture now proves NeedsPlacement changes from 1 to 0 alongside readiness and CanClose, while the suspended read retains the original snapshot. No remaining findings in this delta.”

The one-line CA5394 exception is restricted to a non-secret fixture identity and follows existing isolated-actor tests. It disables no behavioral check or security validation. This was a read-only review; build and suite evidence remain in the authoritative validation record.

# Review round 5 — persistence and URL-state disposition

The independent reviewer (`reload_backend_review`) confirmed that successful Close evidence alone could not preserve a failed prerender read. It inspected the complete persistence delta and its attachment, retry and owner/detail mismatch cases. The separate failure snapshot restores only under exact authority and value-equal detail, retains its generation and maps to the new local read key; invalidation/new reads clear it. Existing generation/key/cancellation checks reject obsolete responses.

The suppressed URL suggestion was independently found inapplicable: dropping namespaced Place context contradicts the approved preservation contract. Close requests consume only Close state. One test predicate finding was corrected to use LocalOutcome and check LocalTeamId/ParticipantId alongside effective team, years and tags. Final reinspection: “No remaining findings in the reviewed round 5 delta.” This was a read-only review; actual build and suite results belong to the validation record.
The first build also identified the test harness disposal API and ordinal-comparison analyzer violations. The reviewer reinspected the corrections: renderer-level disposal removes the original component while preserving serialized payload/services; ordinal string comparisons preserve the exact isolation assertion. No suppression or evidence weakening was introduced.

# Review round 6 — stale-page recovery and contract disposition

The independent reviewer (`reload_backend_review`) confirmed the positive-total/empty-page defect and inspected the explicit native recovery link, safe last-page arithmetic, retained context and unchanged zero-results/error siblings. It independently found the URL-normalization suggestion inapplicable: validation precedes URL construction and invalid values must not silently broaden discovery. The new client cases prove this boundary. Parsed provenance prompt equality with the sibling text was verified.

The browser fixture resets exactly 51 of 60 local decisions; its selected participant has no inherited decision and the first Not selected outcome dispatches without confirmation. The selection wait now requires enabled Save to prove the value bound after attachment; absence of a team field alone was insufficient. No production issues or other findings remained. This was a read-only review; actual suite and visual results belong to the validation record.
The first build also required an explicit non-null assertion on validation errors. The reviewer verified that assertion and the enabled-Save settlement correction; final round-six code verdict is clean, without suppressions or weakened checks.

The first full browser run exposed an ambiguous status locator: normal conflict feedback and refresh progress were both present. The reviewer inspected the failure trace, requested the same correction for the fifth sibling, and reinspected all five expected-text filters. Final read-only verdict: clean; actions and behavioral assertions remain unchanged, while concurrent progress messages no longer create ambiguity. Test results remain in the validation record.
