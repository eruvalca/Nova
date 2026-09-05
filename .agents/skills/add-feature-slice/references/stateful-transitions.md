# Stateful transitions and proving tests

Use this before the first implementation of a command recovered across reloads, a form receiving
contextual server validation, or asynchronous UI state whose user, club, route, or permissions may
change while work is pending. Ordinary CRUD does not need this procedure.

Record a compact mapping in the existing task plan or feature brief. Do not create another document
solely to satisfy this recipe. Name the actual entry points and test cases, not generic assurances.

| State owner | Entry point and transition | HTTP/JS await boundary | Permitted effects after the transition | Coverage disposition |
| --- | --- | --- | --- | --- |
| Actual user/club/authority/resource or form | Initial, confirmation, retry, recovery, or reconciliation path | Work that may outlive that owner | Visible state, original ID/payload, persistence, dispatch, navigation, focus, cleanup | Covered: named test; Missing: scenario to add; or Not applicable: reason |

Add a row for each consequential scenario that actually exists. Inventory named tests first;
classify every selected scenario as covered, missing coverage, or not applicable with a reason.
Add missing tests and a neighboring valid case; do not multiply every possible failure combination.

Select only the cases applicable to the flow:

- **Contextual validation:** submit, display the server field error, change the input, allow a parent
  rerender with the old error snapshot, and submit the corrected payload. Expire the form's
  server-owned snapshot as fields change so contextual/cross-field rules can run again; preserve
  independently owned DataAnnotations messages. Clearing in `OnValidSubmit` alone cannot recover a
  form whose errors prevent that callback.
- **Recoverable commands:** persist the original ID and payload before every dispatch path. Deny
  storage on initial and confirmation/retry paths; assert no command is sent. After an ambiguous
  result, retain the original command and replay it rather than generating another ID. Reconcile
  current authorization/lifecycle, and report the operation's immutable receipt rather than a
  preview or later aggregate count.
- **Ownership changes:** hold a service or JS completion, change the owning user/club/route or
  required permission, and assert old visible data disappears before cleanup finishes. Complete the
  old work after replacement work starts; it must not restore data, navigate, submit, or clear the
  replacement request's busy/error state. Cover same-role club changes as well as role changes.
- **Identity transitions:** order startup and authentication notifications independently from the
  currently applied identity. Pending or unchanged authentication must preserve legitimate work,
  edits, and recovery identities. Applying the newest changed user/club/authority invalidates old
  effects; disposal rejects late completions. Keep server authorization authoritative.

Use an explicit request identity/generation for ownership and cancellation for cooperative work;
neither browser storage nor cancellation alone proves ownership after an await. Keep the mechanism
feature-local unless existing equivalent flows justify reuse.

Use bUnit for rendered form correction and controlled async completions, real HTTP tests for
middleware/authorization/contracts, PostgreSQL for transaction/receipt races, and browser tests for
attachment, navigation, focus, and actual browser storage. See
[component tests](../../nova-testing/references/blazor-component-tests.md) and
[integration tests](../../nova-testing/references/aspire-integration-harness.md).

Before implementation, name any uncovered consequential transition. During implementation, turn it
into a behavioral test. At handoff, report coverage separately from which commands passed; a green
pre-existing suite is not proof that the new transition was exercised.
