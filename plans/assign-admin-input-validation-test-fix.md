# Fix AssignAdminInput Validation Test Mismatch

Update the failing `AssignAdminInput_ValidationPasses_WhenTargetUserIdIsZero_BecauseRequiredIsContractMarkerOnly` test and audit related tests so they match the current `[Range(1, long.MaxValue)]` validation rule on `AssignAdminInput.TargetUserId`.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to `Complete` and write its **Phase Summary**; run the phase's **Verification Plan** and record the result before moving on. When all phases are done, fill in **Final Recap**.

## Phase 1: Audit Related Tests

Status: Complete

Suggested executor: orchestrator

- [x] Read `Nova.Unit.Tests/Account/AccountDeletionTests.cs` and identify all `AssignAdminInput` tests.
- [x] Read `Nova.Unit.Tests/Account/ClubMemberServiceTests.cs` and identify tests that exercise `AssignClubAdminAsync` with boundary values.
- [x] Confirm whether any other test expects `TargetUserId = 0` to be valid or otherwise conflicts with `[Range(1, long.MaxValue)]`.
- [x] Record findings in this plan's Phase Summary.

### Verification Plan

- `grep_search` for `TargetUserId = 0` and `AssignAdminInput` in `Nova.Unit.Tests/` returns no unexpected conflicts.
- All identified related tests are listed in Phase Summary with a note on whether they need changes.

### Phase Summary

Only one test expected `TargetUserId = 0` to be valid: `AssignAdminInput_ValidationPasses_WhenTargetUserIdIsZero_BecauseRequiredIsContractMarkerOnly` in `AccountDeletionTests.cs`. The related `DemoteAdminInput` already has a matching `[Range(1, long.MaxValue)]` rule and a corresponding zero-validation test in `ClubAdminServiceTests.cs`. `ClubMemberServiceTests` had no zero-value validation test for `AssignClubAdminAsync`.

## Phase 2: Update Tests and Add Missing Coverage

Status: Complete

Suggested executor: orchestrator

- [x] Update `AssignAdminInput_ValidationPasses_WhenTargetUserIdIsZero_BecauseRequiredIsContractMarkerOnly` in `Nova.Unit.Tests/Account/AccountDeletionTests.cs` to assert that validation fails for `TargetUserId = 0`.
- [x] Rename the test to reflect the new expectation (e.g., `AssignAdminInput_ValidationFails_WhenTargetUserIdIsZero`).
- [x] Update or remove the outdated comment about `[Required]` being a contract marker only.
- [x] If missing, add/update service-level test in `ClubMemberServiceTests.cs` verifying that `AssignClubAdminAsync(new AssignAdminInput { TargetUserId = 0 })` returns a validation `ServiceProblem`.
- [x] Run the affected unit tests and confirm they pass.

### Verification Plan

- Run `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "Nova.Unit.Tests.Account.AccountDeletionTests" --filter-class "Nova.Unit.Tests.Account.ClubMemberServiceTests"`.
- Expected result: all matching tests pass.
- **Result:** Passed — 37 tests, 0 failed.

### Phase Summary

Updated `AccountDeletionTests.cs`:
- Renamed test to `AssignAdminInput_ValidationFails_WhenTargetUserIdIsZero` and inverted assertions to expect validation failure.
- Added `AssignAdminInput_HasRangeAttribute_OnTargetUserId` to lock in the `[Range(1, long.MaxValue)]` contract.

Updated `ClubMemberServiceTests.cs`:
- Added `AssignClubAdminAsync_ReturnsValidation_WhenTargetUserIdIsZero` to verify the service returns `ServiceProblemKind.Validation` for zero input.

Verification passed: 37 tests succeeded with no failures.

## Final Recap

The failing test `AssignAdminInput_ValidationPasses_WhenTargetUserIdIsZero_BecauseRequiredIsContractMarkerOnly` was outdated: it predated the `[Range(1, long.MaxValue)]` attribute on `AssignAdminInput.TargetUserId`. The fix aligned the test suite with the current validation contract by asserting that `0` is invalid, adding a Range-attribute contract test, and adding a service-level validation test. All 37 affected tests pass.

## Deployment Plan

No deployment steps required. The change is test-only and safe to merge once the full CI test suite passes.

## Phase 2: Update Tests and Add Missing Coverage

Status: Not started

Suggested executor: orchestrator

- [ ] Update `AssignAdminInput_ValidationPasses_WhenTargetUserIdIsZero_BecauseRequiredIsContractMarkerOnly` in `Nova.Unit.Tests/Account/AccountDeletionTests.cs` to assert that validation fails for `TargetUserId = 0`.
- [ ] Rename the test to reflect the new expectation (e.g., `AssignAdminInput_ValidationFails_WhenTargetUserIdIsZero`).
- [ ] Update or remove the outdated comment about `[Required]` being a contract marker only.
- [ ] If missing, add/update service-level test in `ClubMemberServiceTests.cs` verifying that `AssignClubAdminAsync(new AssignAdminInput { TargetUserId = 0 })` returns a validation `ServiceProblem`.
- [ ] Run the affected unit tests and confirm they pass.

### Verification Plan

- Run `dotnet test Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter "FullyQualifiedName~AssignAdminInput|FullyQualifiedName~AssignClubAdmin"`.
- Expected result: all matching tests pass.

### Phase Summary

_(write when phase completes)_

## Final Recap

_(write when all phases complete: summary of the entire piece of work)_
