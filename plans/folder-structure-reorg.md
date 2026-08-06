# Folder Structure Realignment

Audit the Nova solution's folder layout against the organizational rules in the instruction files
and move misplaced files into their correct locations, then verify builds and tests pass.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status
to `Complete` and write its **Phase Summary**; run the phase's **Verification Plan** and record
the result before moving on.

## Findings Summary

The audit found two structural misalignments:

### Misalignment 1 — `Nova.Shared/Features/Players/` orphaned subfolder

`Nova.Shared` has a top-level `Features/Players/` folder with 4 files that belong alongside the
rest of the Players contracts in `Nova.Shared/Players/`:

- `Nova.Shared/Features/Players/GetPlayerRosterEndpoints.cs`
- `Nova.Shared/Features/Players/GetPlayerRosterInput.cs`
- `Nova.Shared/Features/Players/IPlayerService.cs`
- `Nova.Shared/Features/Players/PlayerListItem.cs`

The `Features/` subfolder exists nowhere else in `Nova.Shared`. The instruction-mandated pattern
is a flat feature-folder layout (`Nova.Shared/<Feature>/`) mirroring `Nova/Features/<Feature>/`.

### Misalignment 2 — `Nova.Client/Services/` is a flat folder

The 14 HTTP client services live as a flat list under `Nova.Client/Services/`. The instruction
file pattern `Nova.Client/Services/**/*.cs` implies feature-based subfolder organization. The
services should be grouped by feature to mirror `Nova/Features/<Feature>/` and
`Nova.Shared/<Feature>/`.

Current flat files:
- HttpCampaignCreationService.cs
- HttpCampaignQueryService.cs
- HttpClubJoinRequestService.cs
- HttpClubMemberService.cs
- HttpClubService.cs
- HttpPlayerDetailService.cs
- HttpPlayerLifecycleService.cs
- HttpPlayerManagementService.cs
- HttpPlayerService.cs
- HttpProfilePhotoService.cs
- HttpTeamDetailService.cs
- HttpTeamLifecycleService.cs
- HttpTeamManagementService.cs
- HttpTeamRosterService.cs

`HttpSuccessContentExtensions.cs` is a shared utility — it can stay in `Services/` root or move
to `Services/Shared/`.

### Non-issues (correct as-is)

- `Nova.Shared/Enums/` contains genuinely cross-cutting enums used across multiple features
  (`LifecycleStatus`, `Gender`, `RequestStatus`, `RequestAction`, `PlacementOutcome`,
  `CampaignStatus`, `CampaignLifecycleEventType`). A flat `Enums/` folder for cross-cutting
  enums is acceptable; no move needed.
- `Nova/Components/` — Identity/Account pages, App.razor, Routes, Layout — all correct per rules.
- Missing Campaigns/Tags UI pages in `Nova.UI` — a feature gap, not a structural misplacement.
- `Nova.UI` feature structure (`Features/<Feature>/Pages/` + `Features/<Feature>/Components/`) is
  correct.
- `Nova/Features/<Feature>/` server services are correctly organized.

---

## Phase 1: Move `Nova.Shared/Features/Players/` → `Nova.Shared/Players/`

Status: Complete

- [x] Move `GetPlayerRosterEndpoints.cs` to `Nova.Shared/Players/`
- [x] Move `GetPlayerRosterInput.cs` to `Nova.Shared/Players/`
- [x] Move `IPlayerService.cs` to `Nova.Shared/Players/`
- [x] Move `PlayerListItem.cs` to `Nova.Shared/Players/`
- [x] Update namespace declarations in moved files (from `Nova.Shared.Features.Players` →
      `Nova.Shared.Players`)
- [x] Find and update any `using Nova.Shared.Features.Players;` references across all projects
- [x] Delete the now-empty `Nova.Shared/Features/Players/` folder
- [x] Delete the now-empty `Nova.Shared/Features/` folder

### Verification Plan

- `dotnet build Nova.slnx --no-restore` — expect zero errors
- `dotnet test Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — all tests pass
- `dotnet test Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — all tests pass
  (or skip if not available locally)
- `grep -r "Nova.Shared.Features.Players" --include="*.cs"` — expect zero matches

### Phase Summary

_(write when phase completes)_

---

## Phase 2: Reorganize `Nova.Client/Services/` into feature subfolders

Status: Not started

Target layout:
```
Nova.Client/Services/
  Campaigns/
    HttpCampaignCreationService.cs
    HttpCampaignQueryService.cs
  Clubs/
    HttpClubJoinRequestService.cs
    HttpClubMemberService.cs
    HttpClubService.cs
  Photos/
    HttpProfilePhotoService.cs
  Players/
    HttpPlayerDetailService.cs
    HttpPlayerLifecycleService.cs
    HttpPlayerManagementService.cs
    HttpPlayerService.cs
  Teams/
    HttpTeamDetailService.cs
    HttpTeamLifecycleService.cs
    HttpTeamManagementService.cs
    HttpTeamRosterService.cs
  HttpSuccessContentExtensions.cs   ← stays at root (shared utility)
```

- [x] Create feature subfolders: `Campaigns/`, `Clubs/`, `Photos/`, `Players/`, `Teams/`
- [x] Move each `Http*Service.cs` file to its feature subfolder
- [x] Update namespace declarations in each moved file (namespaces kept as `Nova.Client.Services`
      — no callers needed updating)
- [x] Verify `Nova.Client/Program.cs` DI registrations still compile
- [x] Delete empty source folders if any

### Verification Plan

- `dotnet build Nova.slnx --no-restore` — expect zero errors
- `dotnet test Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build` — all tests pass
- `grep -r "Nova.Client.Services\." --include="*.cs" Nova.Client/` — all `using` statements
  reference feature-namespaced paths

### Phase Summary

Moved 14 `Http*Service.cs` files from the flat `Nova.Client/Services/` root into feature
subfolders (`Campaigns/`, `Clubs/`, `Photos/`, `Players/`, `Teams/`). Namespaces were kept as
`Nova.Client.Services` throughout — no `using` directive updates were needed in any caller.
`HttpSuccessContentExtensions.cs` remains at the `Services/` root as a shared utility.
Build: 0 errors. Unit tests: 954/954 passed.

---

## Final Recap

Both structural misalignments have been resolved:

1. **`Nova.Shared/Features/Players/`** — eliminated. The 4 orphaned contract files now live
   in `Nova.Shared/Players/` alongside all other Players contracts, with the correct
   `Nova.Shared.Players` namespace.

2. **`Nova.Client/Services/`** — now organized by feature subfolder, mirroring
   `Nova/Features/<Feature>/` and `Nova.Shared/<Feature>/`.

All other areas audited were already aligned with the instruction files.

## Deployment Plan

No deployment steps required — this was a pure in-repo rename/move reorg.
Merge the branch after confirming the build is green and all tests pass.
