# WASM Client Response Consistency

Audit the new agent guidance and align every Nova WebAssembly HTTP client so valid empty
collections remain valid while invalid successful responses are surfaced consistently.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

## Phase 1: Audit Guidance and Client Contracts

Status: Complete <!-- Not started | In progress | Complete -->

- [x] Review the changed instruction and skill text for accuracy, concision, and consistency.
- [x] Inventory every WASM HTTP success payload and classify valid empty, required body, and obvious
  DTO invariants.
- [x] Identify existing client tests and the smallest coverage additions needed.

### Verification Plan

- Search every `Nova.Client\Services\Http*Service.cs` success-deserialization path and account for it
  in the implementation scope.
- Run `git diff --check` with no whitespace errors.

### Phase Summary

The guidance correctly distinguishes a valid deserialized `[]` from an empty/null/malformed body and
now covers input validation, portable invariants, eventual totals, and focused tests. The audit found
required DTO clients with null-forgiving or incomplete malformed-JSON handling, collection clients
that collapse JSON `null` to `[]`, and one photo client that treats an invalid 2xx body as NotFound.
No-body mutation clients are already correct.

## Phase 2: Align Clients and Tests

Status: Complete

- [x] Preserve valid deserialized empty collections.
- [x] Map empty bodies, JSON `null`, malformed JSON, and obvious invalid DTOs to
  `ServiceProblem.ServerError`.
- [x] Add focused regression tests for affected clients without duplicating deep campaign-query
  contract coverage.

### Verification Plan

- Run targeted client test classes with
  `dotnet test --project Nova.Unit.Tests --filter-class "*Http*ServiceTests"` and confirm all
  discovered tests pass.
- Run `dotnet build Nova.Client\Nova.Client.csproj` successfully.

### Phase Summary

Added a shared required-success-body deserializer and aligned every DTO-returning WASM HTTP client
with it. Client validators now preserve valid empty collections while rejecting null/malformed
payloads and portable contract violations. Focused regression coverage includes nested nulls,
relationships, bounds, ordering, identifier correlation, and validation before lossy URL building.

## Phase 3: Cross-Surface Verification

Status: Complete

- [x] Re-scan all WASM HTTP clients for permissive null substitution and null-forgiven required
  success bodies.
- [x] Confirm guidance matches the implemented client behavior.
- [x] Run the complete unit suite and inspect the final diff.

### Verification Plan

- Run `dotnet test --project Nova.Unit.Tests` successfully.
- Run `git diff --check` with no whitespace errors.

### Phase Summary

All required successful JSON bodies now flow through `ReadRequiredJsonAsync`; the only direct
`ReadFromJsonAsync` call is inside that helper. The guidance matches the implementation. Final
verification passed with 907 unit tests, a zero-error solution build, and a clean `git diff --check`.

## Final Recap

Clarified that JSON `[]` is valid when allowed by the contract, centralized strict success-body
handling, aligned all WASM HTTP clients, shared response bounds where needed, strengthened input
contracts, and added focused regression coverage for invalid successful responses.

## Deployment Plan

No special deployment steps or data migration are required. Review and merge the code and guidance
changes, then deploy through the normal application pipeline.
