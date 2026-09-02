# Player CSV import test plan

1. Parser/template tests cover exact bytes and metadata; UTF-8, RFC 4180, bounds, malformed structures, headers, row numbering, conversions, DataAnnotations, and formula-like cells.
2. Service tests cover authorization, mixed reconciliation, in-file and existing-player duplicate precedence, cross-tenant isolation, operation identity, and unchanged persistence counts.
3. Token tests cover round-trip identity plus tampered, expired, changed-user, changed-club, and changed-file rejection.
4. WASM client tests cover routes, multipart metadata/bytes, local bounds, ProblemDetails, and strict success-body invariants.
5. Aspire HTTP tests cover route registration, anonymous/member/admin policy behavior, template response headers/body, multipart success and validation failures, trace IDs, tenant duplicate isolation, maximum rows, and no persistence.
6. Run targeted unit and integration classes, then solution build, full unit/integration/browser suites, and format verification. Record assertion-quality and pseudo-mutation review in `.testagent/status.md`.
