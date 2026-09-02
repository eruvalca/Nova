# Player CSV import test research

## Bounded target inventory

- Shared player-import constraints, DTOs, interface, and route constants.
- Server CSV template/parser, signed-preview token protector, tenant-safe duplicate classifier, service, and endpoints.
- WebAssembly HTTP client.
- Unit tests for parser, service/security, and client contracts; Aspire HTTP integration tests for routing, authorization, multipart handling, and no persistence.

## Existing conventions

- SDK-style .NET 10 solution using xUnit v4, Microsoft.Testing.Platform, and Shouldly.
- `Nova.Unit.Tests` covers pure parsing/client behavior and SQLite tenancy service behavior.
- `Nova.Integration.Tests` uses `NovaAppHostFixture` for real authentication, HTTP, PostgreSQL, and tenant isolation.
- Existing player creation validation is `CreatePlayerInput` plus `InputValidator`; import must reuse it.
- Static source-to-test pairing found 448 source files, 249 tests, and established player service/client pairings. New sources will be paired under the existing Players test folders; this is a static heuristic, not line or branch coverage.

## Acceptance checklist

- Exact BOM-prefixed six-column CSV template and strict UTF-8/RFC 4180 parsing.
- File, row, field-size, header, encoding, structure, type, and formula-injection rejection.
- Locale-independent date/gender/numeric conversion and manual-create structural validation.
- Stable source rows and exact ready/invalid/duplicate reconciliation.
- One tenant-filtered existing-player lookup; deterministic active/archived/upload duplicate precedence.
- UUIDv7 plus one-hour signed identity bound to actor, club, byte length, and SHA-256 hash.
- Administrator-only template/preview HTTP endpoints with multipart bounds and trace-correlated problems.
- Strict WASM client request and response validation.
- Preview never persists players or assignments; campaign projection and commit stay out of scope.
