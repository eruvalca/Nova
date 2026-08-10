# Address roster review feedback

Implement the remaining PR review feedback for the campaign participant roster/detail slice, including contract completeness, tenant-safe query behavior, endpoint metadata, client validation, and focused test coverage.

Status: Complete

## Phase 1: Align contracts and server behavior
- [x] Expand detail DTOs with campaign status and detail-specific tag applications
- [x] Fix roster/detail query behavior for campaign existence, pattern escaping, and paging before materialization
- [x] Add endpoint metadata and explicit capability derivation

## Phase 2: Fix client and test wiring
- [x] Correct namespace imports and global usings for compilation
- [x] Enforce bounded payload validation in the WASM client
- [x] Add/update unit, client, and HTTP tests for the new contract behavior

## Verification Plan
- dotnet test Nova.Unit.Tests/Nova.Unit.Tests.csproj
- dotnet test Nova.Integration.Tests/Nova.Integration.Tests.csproj
