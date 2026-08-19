# Aspire 13.5 Upgrade Review and Adoption

Review the repo's Aspire 13.4.6 → 13.5.0 upgrade (already committed in `a4aa744`) against the
[13.5 release notes](https://aspire.dev/whats-new/aspire-13-5/), remediate the one real breaking
change (removed `aspire ps` flags referenced by our agent skills), and adopt the agreed new
features: the CLI bundle and two custom resource commands (`reset-db`, `clear-profile-photos`).
Deliberately skip all experimental APIs.

Decisions made with the user (2026-08-18):

- **Adopt the CLI bundle**: set `AspireUseCliBundle=true` in `Nova.AppHost` so `dotnet run`
  delegates to `aspire run` via `dnx` (resolves the ASPIRE010 build warning).
- **Adopt custom resource commands**: `reset-db` on the postgres resource and
  `clear-profile-photos` on the Azurite storage resource, using the stable 13.5
  `WithCommand` + named-argument API.
- **Skip experimental APIs**: no `WithTerminal()`, no HTTPS-certificate config APIs, no
  `AddDotnetProject` (everything else is gated behind experimental diagnostics).
- **Verification depth**: integration tests **and** browser tests (both AppHost-based,
  local-only), plus format check.
- **Not adopted** (recorded so nobody re-litigates): persistent volumes for K8s/AKS (no K8s
  deployment in repo), cross-scope Azure `AsExisting*` refs (no Azure deployment target),
  GitHub Models (deprecated upstream), Radius/Foundry Local/Redis modules (unused),
  Blazor-gateway-on-Compose (unused), TypeScript AppHost features (C# AppHost only),
  dashboard/VS Code changes (free, nothing to do), `aspire stop --force` (documented only).

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its
status to `Complete` and write its **Phase Summary** (what was done, key decisions, anything
needed to continue with zero context); run the phase's **Verification Plan** and record the
result before moving on. When all phases are done, fill in **Final Recap** and
**Deployment Plan**.

## Phase 1: Breaking-change audit (analysis only)

Status: Complete

- [x] Read the 13.5 release notes (whats-new page + upgrade-aspire guidance)
- [x] Grep the repo for every obsoleted/removed API surface from the 13.5 breaking-changes list
- [x] Check CI workflows, AppHost, ServiceDefaults, and agent skills for CLI-flag usage

### Verification Plan

- Release notes: https://aspire.dev/whats-new/aspire-13-5/ (breaking changes 1–12)
- Greps: `ServiceProvider|PublishAsConnectionString|DevTunnelRegion|TerminalOptions|OrleansProvider|DotnetProjectResource|WithTerminal|PromptInput|WithCommand|WithHttps|AddPersistentVolume` (production code) and `aspire ps` (repo-wide)

### Phase Summary

All 12 breaking changes audited; only **#3 (`aspire ps --resources` / `--include-hidden`
removed)** affects us, and only in `.agents/skills/**` (agent workflows, 42 references). No
production code uses any removed/renamed API:

1. `ServiceProvider` → `Services` rename: no `ExecuteCommandContext`/hosting-context usage in
   `Nova.AppHost/AppHost.cs` (no `WithCommand` callbacks today). New commands in Phase 3 must
   use `ctx.Services`. ✓ N/A
2. `PublishAsConnectionString` obsolete: not used. ✓
3. `aspire ps --resources`/`--include-hidden` removed: `.github/workflows/ci.yml` never invokes
   the Aspire CLI; `.github/instructions/testing.instructions.md` already uses
   `aspire describe --format Json` (still valid). `.agents/skills/**` uses
   `aspire ps --include-hidden --format Json` in 10 files → Phase 4. **REAL IMPACT (skills only)**
4. GitHub Models deprecated: not used. ✓
5. Proxyless endpoint port timing: no proxyless endpoints configured; Postgres/Azurite use
   default container networking. Watch only if ports ever change. ✓
6. Go polyglot options DTO: no Go AppHost. ✓
7. `TerminalOptions.Shell` removed: new-in-13.5 API, never used. ✓
8. `DevTunnelRegion` enum normalization: not used. ✓
9. Dashboard AI Assistant removed: nothing to do. ✓
10. VS Code dashboard auto-launch removed: dev-workflow note → documented in Phase 5. ✓
11. Orleans annotations internal: not used. ✓
12. `DotnetProjectResource` moved to `Aspire.Hosting.Dotnet`: not used. ✓

Baseline facts: packages already at 13.5.0 (`Directory.Packages.props`), AppHost SDK 13.5.0,
local Aspire CLI 13.5.0, `dnx` available via SDK (`dotnet dnx`), build + 1507 unit tests already
green on 13.5.0 (see `plans/xunit-v4-upgrade-review.md` Phase 1) with one ASPIRE010 warning
(CLI bundle not enabled).

## Phase 2: Adopt the CLI bundle

Status: Complete

Suggested executor: sub-agent w/ smaller model (single-property change + run/verify loop)

- [x] Add `<AspireUseCliBundle>true</AspireUseCliBundle>` to the `Nova.AppHost.csproj`
      `PropertyGroup` (the only required change; do NOT set `AspireCliInvocationMode`)
- [x] Build and confirm the ASPIRE010 warning is gone and no new Aspire diagnostics appear
- [x] Smoke-run the AppHost with `dotnet run --project Nova.AppHost` and confirm it delegates
      to the CLI bundle (dnx acquisition log line), `/health` and `/alive` return 200
- [x] Record the final run command behavior in the Phase Summary (any prompts/delays on first
      `dotnet run` after the change matter for team docs)

### Verification Plan

- `dotnet build Nova.slnx` — expect success, **0 warnings** (previously 1 × ASPIRE010)
- `dotnet run --project Nova.AppHost` (background), then
  `curl http://localhost:<port>/health` → `Healthy`; `/alive` → `Healthy`; then stop
- Contingency: if ASPIRE009 (bundle can't resolve) or ASPIRE011 (dnx missing) appears, fall
  back to pinning the CLI via a local tool manifest (`dotnet tool install Aspire.Cli`) and
  note it here + in Phase 5 docs; do not silently downgrade to opt-out.

### Phase Summary

Added `AspireUseCliBundle=true` to `Nova.AppHost.csproj`; no
`AspireCliInvocationMode` override or local tool manifest was needed. `dotnet build Nova.slnx`
completed with 0 warnings and 0 errors, removing ASPIRE010 without introducing ASPIRE009/011.
`dotnet run --project Nova.AppHost --no-build` delegated to the bundled Aspire CLI, printed the
CLI's `Connecting to AppHost...` / `Starting dashboard...` flow, and exposed the dashboard.
There was no dnx acquisition prompt or delay on this machine because the dependency was already
available. After `aspire wait` reported the `nova` project healthy, both `/health` and `/alive`
returned HTTP 200 with `Healthy`.

## Phase 3: Custom resource commands (reset-db, clear-profile-photos)

Status: Complete

Suggested executor: orchestrator (API-surface decisions: connection-string resolution, restart)

- [x] Add `.WithCommand("reset-db", "Reset nova database", …)` to the `postgres` resource in
      `Nova.AppHost/AppHost.cs`:
  - Declare one named argument via `CommandOptions.Arguments`: name `confirm`,
    `InputType.Choice` with a single `yes` option, `Required = true`, descriptive help text
    (surfaces as `--confirm yes` on the CLI, an input dialog in the dashboard)
  - In the callback: read `ctx.Arguments["confirm"]` (use `ctx.Services`, **not**
    `ServiceProvider` — 13.5 rename); if the value isn't `yes`, return
    `CommandResults.Failure(...)`
  - Resolve the postgres connection string at runtime: primary approach —
    `novaDatabase.Resource.ConnectionStringExpression.GetValueAsync(ctx.CancellationToken)`;
    verify that overload exists on 13.5 (`aspire docs api search` / package assembly).
    Fallback if unavailable: set a stable dev password via `.WithPassword(...)` on the
    postgres resource and build the connection string from the container endpoint obtained
    via `ResourceNotificationService`
  - Execute `DROP DATABASE "nova" WITH (FORCE);` then `CREATE DATABASE "nova";` over Npgsql
    (connect to the `postgres` maintenance DB); then restart the `nova` project resource so
    `StartupDatabaseInitializer` re-applies migrations on next start — use
    `IDistributedApplicationLifecycle.RestartAsync(...)` if that service exists in 13.5,
    otherwise instruct a dashboard Stop/Start in the command result message
- [x] Add `.WithCommand("clear-profile-photos", "Clear profile photos", …)` to the `storage`
      resource: same `confirm` argument pattern; resolve the Azurite connection string
      (same `GetValueAsync` approach), `BlobServiceClient.GetBlobContainerClient("profile-photos")`,
      delete all blobs, report the count in the result message
- [x] Keep commands CLI-safe: no unguarded `PromptInputsAsync` calls (CLI runs with
      `NonInteractive = true` and prompts throw — the named-argument confirm covers CLI usage)
- [x] Build, then smoke-verify both commands against a running AppHost:
      `aspire resource postgres reset-db --confirm yes` and
      `aspire resource storage clear-profile-photos --confirm yes`; confirm the database is
      emptied and re-created and the blob container is emptied (check via `aspire describe`)
- [x] Manual dashboard check considered: the dashboard was not open, so the optional UI check was
      skipped; `aspire describe` confirmed both commands and their required choice argument metadata

### Verification Plan

- `dotnet build Nova.slnx` — expect success, 0 warnings
- With the AppHost running: `aspire resource postgres reset-db --confirm yes` → success result;
  then GET `/health` on the restarted `nova` app → `Healthy` (proves migrations re-applied)
- `aspire resource storage clear-profile-photos --confirm yes` → success result with blob count
- Negative check: `aspire resource postgres reset-db` (no `--confirm`) → error mentioning the
  missing required option
- Integration suite re-run deferred to Phase 6 (it exercises the same AppHost)

### Phase Summary

Added both guarded commands through a new `AppHostCommands` helper and wired them to the
`postgres` and `storage` resources. `CommandOptions.Arguments` exposes a required `confirm`
choice with only the `yes` value; the callbacks independently reject any other value and never
prompt, so non-interactive CLI use is safe. Added direct, centrally versioned
`Npgsql`/`Azure.Storage.Blobs` package references rather than relying on transitive compile assets.

The 13.5 `ReferenceExpression.GetValueAsync(CancellationToken)` overload exists and is used at
execution time. The blob-container expression appends `ContainerName` and is not a raw Azure SDK
connection string, so the storage command correctly evaluates the parent blob-service expression
instead. `IDistributedApplicationLifecycle` is not present in 13.5; the database command uses
`ctx.Services` to resolve `ResourceCommandService` and execute the built-in restart command for
the Nova project resource.

`dotnet build Nova.slnx` passed with 0 warnings. The missing-confirmation check failed before
callback execution with `Required option '--confirm' was not provided.` Both positive CLI commands
completed successfully. After `reset-db`, `aspire wait nova` reported healthy and `/health`
returned HTTP 200 `Healthy`, proving the project restarted and migrations reapplied.

Code review follow-up: the database name now flows from
`PostgresDatabaseResource.DatabaseName` (quoted for SQL identifier safety) instead of a hardcoded
literal, the confirmation guard reads through the null-tolerant `GetString` accessor, and the
private consts carry XML docs per the C# conventions. All three hardening changes were re-verified
with a clean build, a clean format gate, and a second live `reset-db` / `clear-profile-photos`
smoke pass.

## Phase 4: Audit agent skills for the removed `aspire ps` flags

Status: Skipped (Aspire CLI-managed files retained)

Suggested executor: sub-agent w/ smaller model (mechanical find/replace, but must verify the
13.5 flag surface first)

- [x] Establish the replacement command surface on the installed 13.5 CLI:
      run `aspire ps --help`, `aspire describe --help`, `aspire resource --help` against a
      running AppHost and record exactly which of `--format Json` / `--include-hidden` each
      supports (release notes say `--include-hidden` now lives on `aspire resource`)
- [x] Confirm `.agents/skills/**` is provided and managed by Aspire CLI tooling
- [x] Leave the generated skills unchanged per the user's ownership decision

### Verification Plan

- `git diff --exit-code -- .agents/skills` → no changes
- Keep the verified CLI behavior below as evidence for a future upstream/tooling update

### Phase Summary

Verified against the installed 13.5 CLI while Nova was running: `aspire ps` lists running
AppHosts and supports `--format Json`, but no longer accepts `--include-hidden`; `aspire describe`
lists resources and supports both `--format Json` and `--include-hidden`; `aspire resource`
supports `--include-hidden` for command target selection but has no JSON output option.

This means some generated skill examples are stale for the installed CLI. They were initially
corrected locally, then reverted at the user's request because `.agents/skills` is owned by Aspire
CLI tooling and local edits could be overwritten by regeneration. The repository intentionally
keeps those generated files unchanged; the discrepancy belongs in the upstream/tooling update
path rather than this application change.

## Phase 5: Docs update (dev workflow)

Status: Complete

Suggested executor: sub-agent w/ smaller model (mechanical doc edits from the Phase 2–4 facts)

- [x] Update `.github/instructions/testing.instructions.md` (the Aspire + Playwright
      validation section and any run instructions):
  - `dotnet run --project Nova.AppHost` now delegates to the Aspire CLI bundle (behavior
    parity with `aspire run`); note the first-run dnx acquisition if observed in Phase 2
  - Document the two new dev commands: dashboard usage and
    `aspire resource postgres reset-db --confirm yes` /
    `aspire resource storage clear-profile-photos --confirm yes`
  - Document `aspire stop --force` as the destructive alternative (permanently deletes
    Postgres/Azurite volume data — the manual equivalent of the new commands)
  - Note the VS Code Aspire extension no longer auto-opens the dashboard (opt in via
    `dashboardBrowser` setting / `launch.json`), per breaking change #10
- [x] Confirm no other repo docs (`.github/instructions/**`, `plans/**`) contain stale
      `aspire ps` flag references (Phase 4 handles `.agents/skills`)

### Verification Plan

- `grep -rn "aspire ps --" .github` → 0 matches
- Read-through of the edited sections for consistency with the Phase 2/3 results

### Phase Summary

Added a local Aspire workflow section to the testing instructions covering CLI-bundle delegation
and possible first-run acquisition, agent/automation startup, dashboard and CLI use of both
destructive commands, the broader `aspire stop --force` reset, and the VS Code dashboard launch
opt-in. Updated the repository run summary to note the Aspire 13.5 CLI bundle. No
`aspire ps --...` references remain under `.github`.

## Phase 6: Full verification (integration + browser)

Status: Complete

Suggested executor: sub-agent / task runner (long-running commands, summarize results only)

- [x] Run the Aspire integration suite (boots the real AppHost with PostgreSQL 18 via
      Aspire.Hosting.Testing 13.5): `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj`
- [x] Run the browser suite (Playwright against the Aspire-hosted app):
      `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj`
      (install Chromium first if needed:
      `Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1 install chromium`)
- [x] Format check: `dotnet format Nova.slnx --verify-no-changes` (repository-wide baseline
      failure recorded below; the new AppHost file passes a targeted verification)
- [x] Final full build: `dotnet build Nova.slnx` — expect success, 0 warnings

### Verification Plan

- Integration tests: all pass (they also re-validate the AppHost after Phases 2–3 edits)
- Browser tests: all pass
- `dotnet format Nova.slnx --verify-no-changes` — clean

### Phase Summary

The Aspire integration suite passed 269/269 tests. The browser suite passed 21 tests with the
existing environment-gated accessibility evidence test skipped as designed (22 total, 0 failed).
The final solution build succeeded with 0 warnings and 0 errors.

The new `Nova.AppHost/AppHostCommands.cs` and 24 pre-existing C# files were normalized to the
repository-required UTF-8 BOM encoding. Once those masked failures were cleared, the formatter
surfaced three migration files using block-scoped namespaces; they were converted mechanically to
the configured file-scoped namespace style. The repository-wide
`dotnet format Nova.slnx --verify-no-changes` gate now passes with 0 files requiring changes.

## Final Recap

Completed the Aspire 13.5 review and adoption:

1. Enabled the Aspire CLI bundle, eliminating ASPIRE010 while preserving the existing manual
   `dotnet run --project Nova.AppHost` entry point.
2. Added confirmed `reset-db` and `clear-profile-photos` resource commands using stable 13.5
   command arguments, runtime connection-string evaluation, and a Nova resource restart after
   database recreation.
3. Audited the generated Aspire skills against the 13.5 CLI, documented the stale command
   behavior in this plan, and left the CLI-managed files unchanged.
4. Documented the new developer workflow and destructive reset options.
5. Verified the commands against a live isolated AppHost, then passed the full integration and
   browser suites, repository-wide format gate, and a warning-free solution build.

## Deployment Plan

No production infrastructure or database migration is required; these changes affect the local
AppHost and developer workflow.

1. Merge the change through the normal PR process.
2. Developers restore/build normally; `dotnet run --project Nova.AppHost` may acquire the bundled
   Aspire CLI through `dnx` on first use, then opens the usual dashboard.
3. Use the dashboard resource menus or the documented `aspire resource ... --confirm yes`
   commands for targeted local resets. Existing Postgres/Azurite volumes remain intact unless a
   reset command or `aspire stop --force` is intentionally invoked.
