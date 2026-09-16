# Select transition evidence

Before implementation, choose the cases affected by the change and the mechanisms the feature
actually uses or is explicitly adding. Record the starting state, action or interruption, expected
outcome, and test or observation that proves it. Select meaningful cases, not every combination of
every axis.

Reuse this list as the behavioral evidence in the single validation record defined by
[AGENTS.md](../../../../AGENTS.md#completion-and-review). Link named tests or curated captures and
state the observed result or missing proof. Link required original reviews and design artifacts
from that record. The full suite gates still apply.

| Changed behavior | Evidence to select when affected |
| --- | --- |
| Decision rules | Allowed/rejected cases and exact boundary values at the owning boundary. Test a pure policy directly when one owns the rule; keep authorization and persistence evidence at the service boundary. |
| Input validation | Valid and invalid inputs through the consuming service and, when exposed over HTTP, the endpoint. |
| Form validation and corrected retry | Submit through the rendered form → contextual error → edit → unchanged parent rerender → successful second submission with the corrected payload. |
| Authorization and identity | Least-privileged allowed role and denial; for mounted UI, same-role club change, role-only change, and first clubless notification. Clear data, confirmation, and feedback owned by the previous identity before replacement work finishes. |
| Lifecycle changes in mounted UI | Re-evaluate editability and invalidate stale confirmations. Within the same authorized recovery scope, retain confirmed receipt feedback through a closure refresh and explain the current read-only state separately. |
| Async UI ownership | Old success, failure, and cleanup complete after newer work or disposal; none can publish data/feedback, navigate, or clear the newer operation's busy state. |
| Versioned UI confirmation and conflict | Confirm or cancel the reviewed subject/version/proposal; changed authority or evidence invalidates confirmation. Failed or obsolete required-evidence refresh keeps editing blocked; optional regions fail independently. |
| Browser commands retained across reloads | Storage failure blocks every dispatch path; uncertain result → retained ID/payload → reload/replay. Partial cleanup cannot conceal committed effects or authorize stale context. Current-state agreement is not proof of a commit. |
| Persistence/concurrency | Use PostgreSQL for provider/lock/transaction guarantees. For retry/replay contracts, cover failure before commit, lost acknowledgement, competing writers, and the original operation's result without duplicate effects. |
| URL-backed state | Rendered state and query agree after reset, permission change, reload/history, and correction return navigation. For cross-destination handoffs, exercise the composed workspace with nondefault sibling state through subsequent forms/paging/selection and return; verify both retained values and intentional resets. A mocked URL-builder callback does not prove that composition. |
| HTTP contracts | Real producer serialization, required fields, nested relationships/bounds, client validation, and any rendered consumer agree; preserve legitimate zero/empty values. Use the [contract check](../../add-feature-slice/references/wasm-client.md#producer-to-ui-contract-check). |
| Composed UI | Interactivity, semantics, focus, applicable touch targets, and responsive behavior hold in the actual browser DOM. Capture the changed states required by the surface's design checks. |

Choose the boundary that proves the claim: bUnit callback success does not establish deployed
interactivity; a service test does not establish HTTP serialization; SQLite does not establish
PostgreSQL lock behavior. Use [component-test mechanics](blazor-component-tests.md),
[HTTP/provider mechanics](aspire-integration-harness.md), and [browser mechanics](browser-suite.md)
as applicable.

Use controlled tasks for delayed completions, not timing sleeps. Reproduce the defect before the
fix when practical. Record a missing boundary check as a limitation; do not infer success from a
matching current value, a mocked callback, or an unrelated green suite.
