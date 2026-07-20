# Club Admin Review Fixes

Address the findings from the code review of the club-administration feature
(`ClubAdminService`, `ClubAdmin` page, and the promote/demote flow): claim-refresh
parity on promotion, input-validation hardening, a null-summary rendering nit, and
several documentation/comment cleanups.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

### Shared context (read once)

- **Repo layout:** server services in `Nova/Features/**`, shared contracts/DTOs/inputs
  in `Nova.Shared/**`, SSR pages in `Nova.UI/Features/**`, unit tests in
  `Nova.Unit.Tests/**` (xUnit v3 on Microsoft.Testing.Platform + Shouldly + NSubstitute).
- **Claim model:** ClubAdmin is an Identity role. Adding/removing a role does NOT
  update the user's live principal. The convention is to bump the target user's
  security stamp via `ClubMembershipClaimRefresher.MarkUserClaimsStaleAsync(user)`,
  which causes `IdentityRevalidatingAuthenticationStateProvider` to rebuild their
  principal at the next revalidation interval. `RefreshCurrentUserAsync` is only for
  the acting/current user (it also reissues the cookie). Precedent:
  `Nova/Features/Clubs/ClubJoinRequestService.cs` (`ApproveJoinRequestAsync`) and
  `Nova/Features/Clubs/ClubAdminService.cs` (`DemoteClubAdminAsync`).
- **Run commands (MTP — do NOT pass `--nologo`, `--collect`, `--logger`):**
    - Build: `dotnet build Nova.slnx -c Debug`
    - Unit tests (filter by class): `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*ClubMemberServiceTests"`

## Phase 1: Claim-refresh parity on promotion (Finding #1, Medium)

Status: Complete

`ClubMemberService.AssignClubAdminAsync` adds the ClubAdmin role but never bumps the
target's security stamp, so a newly promoted admin's role claim does not take effect
until they re-authenticate — even though the admin page reports success. Its sibling
`DemoteClubAdminAsync` already refreshes claims. Bring promotion to parity.

Suggested executor: orchestrator (cross-cutting DI + test changes, low ambiguity)

- [x] Inject `ClubMembershipClaimRefresher` into `ClubMemberService`'s primary
      constructor in [Nova/Features/Account/ClubMemberService.cs](../Nova/Features/Account/ClubMemberService.cs)
      (add `using Nova.Components.Account;`).
- [x] In `AssignClubAdminAsync`, after a successful `AddToRoleAsync` (and before the
      `LogAdminAssigned` call / `return true`), call
      `await clubMembershipClaimRefresher.MarkUserClaimsStaleAsync(targetUser);`.
      Leave the existing idempotent "already admin" early-return path unchanged.
- [x] Update the XML `<param>` doc on the constructor to describe the new parameter.
- [x] Confirm DI still resolves: `ClubMembershipClaimRefresher` is already registered
      in [Nova/Program.cs](../Nova/Program.cs) as scoped — no registration change needed;
      verify no change is required.
- [x] Update all `ClubMemberService` construction sites in
      [Nova.Unit.Tests/Account/ClubMemberServiceTests.cs](../Nova.Unit.Tests/Account/ClubMemberServiceTests.cs)
      (the `CreateService()` helper plus the 3 inline `new ClubMemberService(...)` calls)
      to pass a `ClubMembershipClaimRefresher`. Build the refresher with a substituted
      `SignInManager<NovaUserEntity>` exactly as
      [Nova.Unit.Tests/Clubs/ClubAdminServiceTests.cs](../Nova.Unit.Tests/Clubs/ClubAdminServiceTests.cs)
      does in its `CreateService`, and stub `userManager.UpdateSecurityStampAsync(...)`
      to return `IdentityResult.Success`.
- [x] Add a unit test `AssignClubAdminAsync_MarksTargetClaimsStale_WhenPromotionSucceeds`
      asserting `await userManager.Received().UpdateSecurityStampAsync(targetUser)` after
      a successful promotion (mirrors the demote coverage in `ClubAdminServiceTests`).

### Verification Plan

- `dotnet build Nova.slnx -c Debug` → `Build succeeded`, 0 errors.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*ClubMemberServiceTests"`
  → all tests pass, including the new `AssignClubAdminAsync_MarksTargetClaimsStale_*` test.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*ClubAdminServiceTests"`
  → still all pass (no regression from shared harness usage).

### Phase Summary

Injected `ClubMembershipClaimRefresher` into `ClubMemberService` and call
`MarkUserClaimsStaleAsync(targetUser)` after a successful `AddToRoleAsync`, giving
promotion the same security-stamp bump that demotion and join-request approval already
perform. DI needed no change (refresher already registered scoped). Updated the 4 test
construction sites via a new `CreateClaimRefresher(userManager)` helper (mirrors
`ClubAdminServiceTests`) and added `AssignClubAdminAsync_MarksTargetClaimsStale_WhenPromotionSucceeds`.
Build clean; `ClubMemberServiceTests` + `ClubAdminServiceTests` = 21 passed, 0 failed.

## Phase 2: Input-validation hardening (Finding #4, Low)

Status: Complete

`[Required]` on a non-nullable `long` is a no-op, so `TargetUserId = 0` currently
passes `InputValidator.Validate(...)`. Add a positive-range constraint so invalid
ids are rejected at the validation boundary. Apply to both admin inputs for symmetry.

Suggested executor: sub-agent w/ smaller model (mechanical, well-specified)

- [x] Add `[Range(1, long.MaxValue)]` to `TargetUserId` in
      [Nova.Shared/Clubs/DemoteAdminInput.cs](../Nova.Shared/Clubs/DemoteAdminInput.cs)
      (keep `[Required]`; `System.ComponentModel.DataAnnotations` is already imported).
- [x] Apply the same `[Range(1, long.MaxValue)]` to `TargetUserId` in
      [Nova.Shared/Account/AssignAdminInput.cs](../Nova.Shared/Account/AssignAdminInput.cs)
      for consistency.
- [x] Add a unit test in `ClubAdminServiceTests` asserting
      `DemoteClubAdminAsync(new DemoteAdminInput { TargetUserId = 0 }, ...)` returns a
      `ServiceProblemKind.Validation` problem (and does not reach `FindByIdAsync`).

### Verification Plan

- `dotnet build Nova.slnx -c Debug` → `Build succeeded`, 0 errors.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*ClubAdminServiceTests"`
  → passes, including the new `TargetUserId = 0` validation test.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*ClubMemberServiceTests"`
  → still passes (confirm no existing test relied on `TargetUserId = 0`).

### Phase Summary

Added `[Range(1, long.MaxValue)]` to `TargetUserId` on both `DemoteAdminInput` and
`AssignAdminInput`, so `InputValidator.Validate(...)` now rejects a zero/negative id at
the validation boundary instead of falling through to a `NotFound`. Added
`DemoteClubAdminAsync_ReturnsValidation_WhenTargetUserIdIsZero` asserting a `Validation`
problem and that `FindByIdAsync` is never reached. `ClubAdminServiceTests` +
`ClubMemberServiceTests` = 22 passed, 0 failed. No existing test depended on id 0.

## Phase 3: Null-summary rendering nit (Finding #3, Low)

Status: Complete

When `_summary` is null the header renders a lone `", "` from
`@(_summary?.City), @(_summary?.State)`. Only render the location line when a summary
is present (a null-summary fallback message already exists lower in the card).

Suggested executor: sub-agent w/ smaller model (single-line markup change)

- [x] In [Nova.UI/Features/Clubs/Pages/ClubAdmin.razor](../Nova.UI/Features/Clubs/Pages/ClubAdmin.razor),
      wrap the `<p class="text-muted mb-0">@(_summary?.City), @(_summary?.State)</p>`
      location line so it renders only when `_summary is not null` (e.g. guard with
      `@if (_summary is not null)`), leaving the `@(_summary?.Name ?? "Club")` heading
      and the existing `_summary is null` fallback block intact.

### Verification Plan

- `dotnet build Nova.slnx -c Debug` → `Build succeeded`, 0 errors.
- Manual markup review: confirm no bare `", "` can render when `_summary` is null and
  the populated case still shows `City, State`.
- (Provider-agnostic; no new automated test required. The happy-path rendering remains
  covered by the existing SSR HTTP assertions in
  [Nova.Integration.Tests/Http/ClubDetailAdminHttpTests.cs](../Nova.Integration.Tests/Http/ClubDetailAdminHttpTests.cs).)

### Phase Summary

Guarded the location line with `@if (_summary is not null)` so the `City, State`
paragraph renders only when a summary exists (using non-null `@_summary.City` /
`@_summary.State`). The `@(_summary?.Name ?? "Club")` heading and the lower
`_summary is null` fallback are unchanged, so the null case no longer emits a lone
`", "`. Build clean; `ClubComponentsTests` still pass.

## Phase 4: Documentation & comment cleanups (Findings #2 and #5, Low/Nits)

Status: Complete

Non-functional clarity fixes. No behavior changes.

Suggested executor: sub-agent w/ smaller model (doc/comment edits only)

- [x] **TOCTOU note (#2):** Add a short comment above the `adminCount <= 1` last-admin
      check in `DemoteClubAdminAsync`
      ([Nova/Features/Clubs/ClubAdminService.cs](../Nova/Features/Clubs/ClubAdminService.cs))
      documenting the accepted read-then-write race (concurrent self-demotions are
      possible but out of scope; guard is best-effort). Do NOT add locking/transactions.
- [x] **Test doc (#5):** Correct the class-level XML summary on
      [Nova.Unit.Tests/Clubs/ClubAdminServiceTests.cs](../Nova.Unit.Tests/Clubs/ClubAdminServiceTests.cs)
      to reflect that it covers `GetClubAdminSummaryAsync`, `GetClubRosterAsync`, and
      `DemoteClubAdminAsync` (not only the roster method).
- [x] **Misleading comment (#5):** Fix the comment in `GetClubRosterAsync`
      ([Nova/Features/Clubs/ClubAdminService.cs](../Nova/Features/Clubs/ClubAdminService.cs))
      that implies the guard dictates the `NovaReadDbContext` choice; reword to state
      the guard authorizes access while the read context provides tenant-scoped reads.
- [x] **Interface intent (#5):** Add a `<remarks>` note to
      [Nova.Shared/Clubs/IClubAdminService.cs](../Nova.Shared/Clubs/IClubAdminService.cs)
      recording that this service is consumed only by the server-rendered `ClubAdmin`
      page (static SSR), so — unlike `IClubMemberService` — no WASM `HttpClient` client
      implementation exists by design.

### Verification Plan

- `dotnet build Nova.slnx -c Debug` → `Build succeeded`, 0 errors (comment/doc-only
  changes must not break the build).
- `git diff --stat` → only the four files above changed; no source-logic lines altered
  (diff limited to comments/XML docs).

### Phase Summary

Applied four non-functional edits: (1) a best-effort/accepted-race comment above the
last-admin guard in `DemoteClubAdminAsync`; (2) corrected the `ClubAdminServiceTests`
class summary to list all three covered methods; (3) reworded the `GetClubRosterAsync`
read-context comment to separate authorization (guard) from tenant-scoped reads
(context); (4) added a `<remarks>` note on `IClubAdminService` documenting it as
SSR-only with no WASM client by design. No behavior changed; build clean, all tests pass.

## Final Recap

All four review findings are resolved:

- **#1 (Medium) — Promote/demote claim-refresh parity:** `ClubMemberService.AssignClubAdminAsync`
  now bumps the promoted member's security stamp via `ClubMembershipClaimRefresher.MarkUserClaimsStaleAsync`,
  matching `DemoteClubAdminAsync` and `ApproveJoinRequestAsync`. DI unchanged (already registered).
- **#4 (Low) — Input validation:** `[Range(1, long.MaxValue)]` added to `TargetUserId` on
  `DemoteAdminInput` and `AssignAdminInput`; zero/negative ids now fail validation.
- **#3 (Low) — Null-summary rendering:** the `City, State` line in `ClubAdmin.razor` is guarded so
  it no longer emits a bare `", "` when the summary fails to load.
- **#2 / #5 (Low/Nits):** best-effort TOCTOU comment on the last-admin guard; corrected
  `ClubAdminServiceTests` class doc; reworded the `GetClubRosterAsync` read-context comment; added
  an SSR-only `<remarks>` note to `IClubAdminService`.

New tests: `AssignClubAdminAsync_MarksTargetClaimsStale_WhenPromotionSucceeds` and
`DemoteClubAdminAsync_ReturnsValidation_WhenTargetUserIdIsZero`. Full solution builds with 0 errors;
`ClubAdminServiceTests` + `ClubMemberServiceTests` + `ClubComponentsTests` = 62 passed, 0 failed.

Files touched: `Nova/Features/Account/ClubMemberService.cs`, `Nova/Features/Clubs/ClubAdminService.cs`,
`Nova.Shared/Clubs/DemoteAdminInput.cs`, `Nova.Shared/Account/AssignAdminInput.cs`,
`Nova.Shared/Clubs/IClubAdminService.cs`, `Nova.UI/Features/Clubs/Pages/ClubAdmin.razor`,
`Nova.Unit.Tests/Account/ClubMemberServiceTests.cs`, `Nova.Unit.Tests/Clubs/ClubAdminServiceTests.cs`.

## Deployment Plan

No migrations, config, or infrastructure changes. Standard code deployment:

1. Run the full unit suite: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj`.
2. (Recommended) Run integration tests: `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj`
   (starts the Aspire Postgres AppHost) to confirm the SSR admin-page flow still renders.
3. Commit and merge to `main`; deploy via the normal pipeline — no special steps.
4. Post-deploy smoke check: as a club admin, open `/Clubs/{id}/admin`, promote a member, and confirm
   their ClubAdmin capabilities take effect after the next auth revalidation interval (the promoted
   user's principal is rebuilt from the bumped security stamp — same mechanism as demotion/approval).
