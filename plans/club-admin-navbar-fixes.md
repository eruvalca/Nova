# Club Admin Demote Guard & Navbar Club Link

Disable the Demote button when the current user is the only club member or the sole admin, and add a left-aligned navbar link to the current user's club detail page using the club's name.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to `Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue with zero context); run the phase's **Verification Plan** and record the result before moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

## Phase 1: Add club name claim

Status: Complete

Suggested executor: orchestrator

- [x] Add `ClubName` constant to `Nova.Shared/Security/NovaClaimTypes.cs`.
- [x] Update `Nova/Components/Account/NovaUserClaimsPrincipalFactory.cs` to query the admin DbContext for the club name when `user.ClubId.HasValue` and emit `NovaClaimTypes.ClubName`.
- [x] Update the factory's XML doc comment to mention the new claim and the refresh requirement when a club name changes.
- [x] Add/update unit tests in `Nova.Unit.Tests/Security/NovaUserClaimsPrincipalFactoryTests.cs` to assert the club name claim is present (and absent when the user has no club).

### Verification Plan

- Run `dotnet test --project Nova.Unit.Tests --filter-class "Nova.Unit.Tests.Security.NovaUserClaimsPrincipalFactoryTests"` and confirm all tests pass.
- Build the solution: `dotnet build Nova.slnx` with no errors.

### Phase Summary

Added `NovaClaimTypes.ClubName` and emitted it from `NovaUserClaimsPrincipalFactory` by looking up the club name in the admin DbContext. Updated the factory docs to note that a security-stamp refresh is needed when a club name changes. Added unit tests covering presence and absence of the new claim.

## Phase 2: Disable Demote button for last member / sole admin

Status: Complete

Suggested executor: orchestrator

- [x] In `Nova.UI/Features/Clubs/Pages/ClubAdmin.razor`, compute a local `bool` inside the member loop that is `true` when the member is the current user and (`_summary?.MemberCount <= 1` or `_summary?.IsCurrentUserSoleAdmin` is `true`).
- [x] Apply `disabled` attribute and a `title` attribute to the Demote button using the same wording already shown in the warning alert: "You are the only admin for this club. Consider promoting another member before you demote yourself or leave the club."
- [x] Keep the existing form and antiforgery token intact; only the submit button should be disabled.
- [x] Existing backend guard in `ClubAdminService.DemoteClubAdminAsync` remains the source of truth and already has unit coverage for the last-admin conflict case.

### Verification Plan

- Run `dotnet test --project Nova.Unit.Tests --filter-class "Nova.Unit.Tests.Clubs.ClubAdminServiceTests"` and confirm all tests pass.
- Build the solution: `dotnet build Nova.slnx` with no errors.

### Phase Summary

Updated `ClubAdmin.razor` to disable the Demote button and show an explanatory title when the current user is the last member or the sole admin. The form and antiforgery token remain intact; only the submit button is disabled. The backend conflict guard is unchanged and still covered by tests.

## Phase 3: Add navbar link to current user's club

Status: Complete

Suggested executor: orchestrator

- [x] In `Nova/Components/Layout/NavMenu.razor.cs`, add computed properties `ClubDetailUrl` and `ClubName` derived from `ICurrentUserProvider.ClubId` and the principal's `NovaClaimTypes.ClubName` claim.
- [x] In `Nova/Components/Layout/NavMenu.razor`, add a new left-aligned `<ul class="navbar-nav me-auto">` containing the club link, placed before the existing right-aligned auth links.
- [x] The link uses `NavLinkMatch.Prefix` so it stays active on child routes such as `/Clubs/{id}/admin`.
- [x] Add tests in `Nova.Unit.Tests/Components/NavMenuTests.cs` to verify the link renders with the club name and routes to the correct URL when the claim is present, and is absent when the user has no club.

### Verification Plan

- Run `dotnet test --project Nova.Unit.Tests --filter-class "Nova.Unit.Tests.Components.NavMenuTests"` and confirm all tests pass.
- Build the solution: `dotnet build Nova.slnx` with no errors.
- Playwright/browser check: sign in as a club member, verify the navbar shows the club name to the right of the Nova brand and navigates to `/Clubs/{id}` on click.

### Phase Summary

Added a left-aligned navbar link that shows the current user's club name and routes to `/Clubs/{clubId}`. The link is gated on the user having both a `ClubId` and a `ClubName` claim. Added bUnit tests with a fake `IAuthorizationService` and `CascadingAuthenticationState` wrapper.

## Final Recap

Implemented both requested UX improvements:

1. The Demote button is now disabled with an explanatory tooltip when the current user is the only club member or the sole admin.
2. Authenticated club members now see a left-aligned navbar link labeled with their club name that routes to the club detail page.

A new `nova:club_name` claim was introduced so the navbar can display the club name without extra DB queries. The claim is emitted at sign-in and refreshed via the existing security-stamp/cookie-refresh mechanism when club membership or name changes.

## Deployment Plan

1. Ensure the app is rebuilt and deployed as usual (`dotnet build Nova.slnx` / `dotnet publish`).
2. No database migration is required; the club name is read from the existing `Clubs.Name` column at sign-in.
3. Existing signed-in users will not see the new navbar label until their cookie is refreshed (next sign-in or security-stamp revalidation). To force the new claim immediately, sign out and sign back in.
4. Run the full test suite before merging; note that `Nova.Unit.Tests.Account.AccountDeletionTests.AssignAdminInput_ValidationPasses_WhenTargetUserIdIsZero_BecauseRequiredIsContractMarkerOnly` currently fails due to an unrelated pre-existing `[Range(1, long.MaxValue)]` attribute on `AssignAdminInput.TargetUserId` that was already present in the working tree before this change.
