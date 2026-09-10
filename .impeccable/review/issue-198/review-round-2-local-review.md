# PR #253 — review round 2 local review

## Scope and disposition

Separate local review by `round1_local_review` of the working-tree changes against `af99924213e2cbf455bbfca12cef5551f5bc36db`. The scope is five cancellation filters, three extracted evidence-read handlers, their direct callers and recovery/ownership siblings, and the new cancellation regressions. No remaining actionable source or test findings. This is a bounded local disposition, not GitHub approval or a new review of the complete implementation PR.

The reviewer inspected actual diffs and implementations, made no source/test edits, ran no builds or tests, and did not contact GitHub or use a browser. Only this review record was written. Validation below was run by the implementer; the reviewer inspected the named result logs.

## Source conclusions

- Evaluate identity, finder and mutation-dispatch catches, plus drawer replay and mutation-wrapper catches, now exclude a canceled component token. Component-owned cancellation therefore escapes these production operations instead of being converted into unavailable/ambiguous-result feedback. Existing request/owner guards still prevent obsolete errors and `finally` cleanup from changing replacement state.
- Finder replacement cancellation remains deliberately different from component disposal: a new generation cancels the old query, and the old generation cannot render an error or clear the replacement's loading state. The debounce and disposal cleanup catches intentionally absorb teardown without publishing failure feedback; they are outside the corrected transport-error branches.
- Pending note payloads and operation IDs are stored before dispatch. An exception from submission bypasses success/rejection cleanup, and the changed catches do not clear the pending record. The inspected JavaScript detach/close paths remove UI listeners and focus traps without deleting recovery storage. Reload recovery retains the original request identity and content.
- Notes, applications and trait-choice GET routes now call named private static handlers. Their `[AsParameters]` inputs, injected service, cancellation token, `ToHttpResult` conversion, shared route constants, route names, authorization group and response metadata are unchanged. No authorization, validation, serialization or recovery contract was broadened.
- Evidence-region cancellation filters from round 1 remain consistent with the new filters. No new sibling defect was identified in this round.

## Regression review

The 13 new cases in `CampaignEvaluationPanelTests.Cancellation.cs` and `CampaignParticipantDrawerTests.Cancellation.cs` exercise:

- Identity/finder lifecycle cancellation escaping the actual base lifecycle method with the same canceled token.
- Cancellation escaping the production mutation or replay callback, observed through a test-only event wrapper before the framework can consume it.
- Exact pending payload retention across disposal, restoration into a new component, and successful replay with an input equal to the original, including its operation ID.
- Unrelated transport cancellation remaining recoverable through rendered retry controls.
- Finder supersession while the replacement request remains pending: the old operation completes without publishing an error, and the new loading state and search survive.

One transient test-construction issue was reported during drafting: a shared cancellation helper was called with reversed arguments. The final source fixes it; the successful build and full unit run confirm no remaining compilation diagnostics. The reviewed tests use controlled tasks rather than timing sleeps and observe production callbacks rather than inferring cancellation from disposal alone. Two narrowly scoped, test-only `CA1812` suppressions document that bUnit constructs the lifecycle/event observer subclasses through reflection. They suppress an unused-type diagnostic for the two test observers, not behavior or test execution; their successful execution provides the corresponding construction evidence.

## Validation evidence

| Log | Inspected result |
| --- | --- |
| `round2-build-final.log` | Solution build succeeded; 0 warnings, 0 errors. |
| `round2-unit.log` | 2,992 passed; 0 failed, 0 skipped, including all 13 new cases. |
| `round2-integration.log` | 600 passed; 0 failed, 0 skipped. |
| `round2-browser.log` | 148 passed; 0 failed, 0 skipped. The implementer enabled `NOVA_A11Y_SCREENSHOTS=1` for the full run. |
| `round2-format-final.log` | Empty success log; implementer confirmed final verification exit status 0. |

Application source was unchanged between the successful build and suite runs. No schema change was made in this round. The reviewer inspected log summaries and test code, not the rendered browser captures; this record does not grant a new visual-design approval.

## Guidance and inspection

Applicable guidance already read in this same reviewer context was reused: root `AGENTS.md`; C#, Blazor architecture, API endpoints, validation, service, tenancy, testing, campaign lifecycle, placement and functional-core instruction files; the `add-blazor-ui`, `add-api-endpoint`, `add-domain-persistence`, `nova-testing` and local `code-review` skills with the references listed in [round 1](review-round-1-local-review.md). The relevant procedures were lifecycle/ownership and recovery, producer-to-client contract preservation, named static handlers and metadata, and bUnit transition evidence. The `add-blazor-ui/references/js-interop.md` reference was additionally read for disposal and storage inspection.

Inspection used scoped `git diff`, `git status`, `git rev-parse HEAD`, `rg` symbol/catch/storage searches and `Get-Content` source/log reads. No behavioral assertions or validation gates were disabled or weakened. The two justified diagnostic exceptions are recorded above.
