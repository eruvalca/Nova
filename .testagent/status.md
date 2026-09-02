# Player CSV import test status

## Result

Strong: the generated suite protects the import contract's security denials, file and row boundaries, parser classifications, duplicate precedence, tenant isolation, signed identity, client response invariants, and no-persistence guarantee.

## Pseudo-mutation gap review

- A change that selected an archived player before an active match, or a higher active player ID before the lowest ID, was initially unprotected. `PreviewAsync_PrefersActiveExistingMatch_ThenLowestPlayerId` now pins both observable outcomes.
- Header comparison, UTF-8 rejection, maximum rows, malformed structure, formula defense, row status/count reconciliation, cross-club filtering, token bindings, and endpoint authorization all have assertions that would observe their caller-visible outcomes.
- No remaining high-risk caller-visible survivor was identified in the issue scope. Campaign projection, commit behavior, UI behavior, and durable receipts were intentionally excluded by the issue boundary.

## Assertion-quality review

- Reviewed 57 test methods across the parser, service/token, endpoint metadata, WASM client, and Aspire HTTP files.
- No assertion-free, always-true, self-referential, or null-only tests were found.
- Assertions cover equality/boolean results, nullability, strings, collections and ordering, exceptions/cancellation, comparisons/bounds, negative behavior, database side effects, HTTP request metadata, and nested response structure.
- Smoke-style authorization tests intentionally assert only their exact HTTP status; broader response-shape assertions are exercised by the validation tests.

## Validation evidence

- Focused player-import service and client review tests: 40 passed.
- Focused Aspire player-import HTTP tests: 9 passed.
- `dotnet build Nova.slnx --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj`: 2,083 passed.
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build`: 412 passed.
- `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build`: 111 passed, 7 expected opt-in accessibility evidence tests skipped.
- `dotnet format Nova.slnx --verify-no-changes --verbosity diagnostic`: formatted 0 of 778 files.

## PR review follow-up

- Strict clients now validate the exact template bytes/charset/filename, consecutive source rows, and every ready or duplicate candidate without trusting the browser clock for token expiry.
- The client never inserts an untrusted original filename into multipart headers; it validates the `.csv` extension and transmits the fixed safe template filename.
- CSV parsing now rejects numeric gender spellings and all-empty wrong-width rows.
- Token `Try*` methods return safe null out values for unsupported versions, malformed hashes, and every binding mismatch.
- Both routes advertise 401 responses, and the transport-level request limit has an end-to-end 413 ProblemDetails regression.
- Suppressed-review follow-up validates UUIDv7 response IDs, rejects null nested row strings and null token hashes, and names the operation input `PlayerImportUploadInput` in its dedicated contract file.
- Later suppressed-review follow-up closes mixed space/control formula-prefix bypasses and documents every public positional import contract parameter.
- The upload operation input now uses explicit required init-only properties so reflection-based validation can inspect the contract consistently.
- Missing filenames and null or blank protected tokens now return structured validation or safe `Try*` failure results instead of throwing.
- Invalid rows intentionally do not reserve duplicate identities; `PreviewAsync_DoesNotLetInvalidRowReserveDuplicateIdentity` pins the issue contract that duplicate keys are built from valid rows only.
