# First-Run Developer Experience Hardening

Harden Nova's "press F5 and go" experience so the npm/Sass build is friendly, self-healing, and visible in the Aspire dashboard, and give teammates a human-facing onboarding doc. Based on the 2026-08-23 research audit (session artifact `files/research/can-you-use-the-latest-aspire-docs-mcp-t.md`).

**Scope decision (user unavailable at planning time; recommended option chosen):** R1 (Node preflight) + R2 (lockfile-aware `npm ci`) + R3 (theme workflow as Aspire dashboard commands) + R5 (README). **Deferred:** R4 (`WithBrowserLogs`, requires the `Aspire.Hosting.Browsers` *preview* package and experimental `ASPIREBROWSERLOGS001` opt-in) and R6 (devcontainer) — both can be added later as follow-up phases if desired.
**Delivery:** commit to the session branch as phases complete; open a single PR against `main` when all phases are done.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to `Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue with zero context); run the phase's **Verification Plan** and record the result before moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

## Phase 1: Harden the npm/Sass build target (R1 + R2)

Status: Not started <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator (MSBuild incrementality is subtle; a smaller model risked breaking the static-web-asset ordering this target depends on)

<!-- Context recap: `Nova/Nova.csproj` has a `BuildBootstrapTheme` target (BeforeTargets="ResolveProjectStaticWebAssets") that runs `npm ci` only when `node_modules` does not exist, then `npm run build:css`, then copies + registers the bootstrap-icons fonts. Research findings: (1) a teammate without Node ≥ 20 gets a cryptic MSB3073 error because nothing checks Node; (2) the `!Exists(node_modules)` guard misses a stale tree when `package.json`/`package-lock.json` change (exactly what PR #136's bootstrap-icons addition would trigger for existing contributors); (3) `Nova/scripts/check-contrast.mjs` shows the repo convention for small Node helper scripts. -->

- [ ] Add `Nova/scripts/check-node.mjs`: exits 0 when `process.versions.node` major ≥ 20 (per `Nova/package.json` `engines.node`), prints a friendly one-line message (version found / too old), exits 1 otherwise. Follow the style of `Nova/scripts/check-contrast.mjs`.
- [ ] Add a `CheckNodePrerequisite` target in `Nova/Nova.csproj` that runs `node scripts/check-node.mjs` from `$(MSBuildProjectDirectory)` with `IgnoreExitCode="true"`, captures the exit code into a property, and emits a clear `<Error>` (e.g. "Node.js 20+ is required to build the Nova theme (npm ci / npm run build:css). Install from https://nodejs.org and rebuild.") when the exit code is non-zero. Wire it as `DependsOnTargets` of `BuildBootstrapTheme` so it runs every time the theme target is evaluated (dependencies run even when the parent target is up-to-date and skipped) — a Node-less machine can never build Nova anyway because `wwwroot/css/bootstrap-theme.css` is gitignored and must compile on every clean clone.
- [ ] Extract the `npm ci` call into a new incremental `RestoreNpmPackages` target: `Inputs="$(MSBuildProjectDirectory)\package.json;$(MSBuildProjectDirectory)\package-lock.json"`, `Outputs="$(MSBuildProjectDirectory)\obj\npm-ci.stamp"`; run `npm ci`, then `Touch`/`WriteLinesToFile` the stamp and register it with `FileWrites`. Make `BuildBootstrapTheme` depend on it (`DependsOnTargets="CheckNodePrerequisite;RestoreNpmPackages"`) and **remove** the old `!Exists(node_modules)` condition so `npm ci` re-runs whenever the manifest or lockfile changes even when `node_modules` exists.
- [ ] Add `$(MSBuildProjectDirectory)\package-lock.json` to `BuildBootstrapTheme`'s `Inputs` so the theme also recompiles when the lockfile changes.
- [ ] Preserve the existing font `Copy` + `@(Content)`/`FileWrites` registration exactly as-is; verify the target's `BeforeTargets="ResolveProjectStaticWebAssets"` hook and `Outputs` list stay intact (static-web-asset discovery must still see the compiled CSS and fonts).

### Verification Plan

- `dotnet build Nova.slnx` succeeds from a clean-ish state: delete `Nova\wwwroot\css` (and optionally `Nova\obj\npm-ci.stamp`) first, then build — CSS + fonts must be regenerated and registered (no 404-prone missing assets).
- Stale-tree fix: `(Get-Item Nova\package-lock.json).LastWriteTime = Get-Date` then `dotnet build Nova.slnx` → `npm ci` output appears (packages re-installed); build again → no npm output (stamp up-to-date, target skipped).
- Missing-Node fix: run the theme target in a shell with Node removed from PATH (`$env:Path` filtered) via `dotnet build Nova\Nova.csproj` and assert the build fails with the new friendly `Error` text, not MSB3073.
- `npm run build:css` and `npm run check:contrast` (from `Nova/`) still pass; `dotnet format Nova.slnx --verify-no-changes` passes.
- CI: push the branch → Build and Unit Tests jobs green (CI runs its own `npm ci` first, so the new target's stamp path must not break the CI flow — confirm no double-install error).

### Phase Summary

_(write when phase completes)_

## Phase 2: Expose the theme workflow as Aspire dashboard commands (R3)

Status: Not started <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator (AppHost wiring touches the experimental process-command API; judgment needed on the fallback)

<!-- Context recap: `Nova.AppHost/AppHost.cs` already defines custom resource commands (`reset-db` on postgres, `clear-profile-photos` on storage) via `WithCommand` + `AppHostCommands` with a confirmation-argument pattern. Research finding: Aspire 13.5's `WithProcessCommand` (experimental, gated by ASPIREPROCESSCOMMAND001) runs a local process on the AppHost machine, streams stdout/stderr to the resource's dashboard console logs, supports a `WorkingDirectory`, bounded output, and exit-code mapping — ideal for `npm` tasks. `check:contrast` is currently CI-only, and teammates have no discoverable way to rebuild the theme. -->

- [ ] In `Nova.AppHost/AppHost.cs`, add three process-backed commands to the `nova` resource using `WithProcessCommand` (add `#pragma warning disable ASPIREPROCESSCOMMAND001` at the top of the file; the repo already opts into preview features via `AspireUseCliBundle`):
  - `install-npm-deps` → `npm ci`, display name "Install npm packages" — visible remedy for stale/missing `node_modules`.
  - `rebuild-theme` → `npm run build:css`, display name "Rebuild Bootstrap theme".
  - `check-contrast` → `npm run check:contrast`, display name "Run WCAG contrast check" — turns the CI-only check into a one-click local action.
  - Use the `createProcessSpec` factory overload to set `WorkingDirectory = Path.Combine(builder.AppHostDirectory, "..", "Nova")` (commands must run from `Nova/`, where `package.json` lives), plus short `Description`s noting they run on the dev machine.
- [ ] Keep `reset-db` and `clear-profile-photos` unchanged.
- [ ] Fallback (only if the experimental API misbehaves in practice): implement the same commands with the established `WithCommand` + `AppHostCommands` pattern, adding a small `ProcessRunner` helper (start process, capture stdout/stderr, return `CommandResults.Success(output)` / `Failure(...)` on non-zero exit). Decide during implementation and record the choice in the Phase Summary.
- [ ] Update `.github/instructions/bootstrap-theme.instructions.md` to document the three dashboard commands as the sanctioned way to install npm deps, rebuild, and contrast-check the theme locally (alongside the existing `npm run build:css` / `npm run check:contrast` wording).

### Verification Plan

- `dotnet build Nova.slnx` succeeds with the pragma in place (no ASPIREPROCESSCOMMAND001 error).
- Start the AppHost (`dotnet run --project Nova.AppHost`); confirm the dashboard's `nova` resource exposes the three new commands (also visible via MCP `list_resources` → resource `commands`).
- Execute each command: `check-contrast` completes with exit 0 (output contains no contrast violations); `rebuild-theme` completes and `Nova\wwwroot\css\bootstrap-theme.css` mtime updates; `install-npm-deps` completes with npm output streamed to the resource's console logs.
- `dotnet format Nova.slnx --verify-no-changes` passes.

### Phase Summary

_(write when phase completes)_

## Phase 3: Human-facing README (R5)

Status: Not started <!-- Not started | In progress | Complete -->

Suggested executor: sub-agent w/ smaller model (well-scoped documentation task; Phase 1–2 decisions are the only inputs it needs)

<!-- Context recap: the repo has no root README (verified by glob). Onboarding knowledge currently lives in agent-facing `.github/instructions/*.md`. Research finding: a teammate needs .NET 10 SDK, Node ≥ 20, and Docker; `aspire doctor` verifies everything except Node; `Nova.AppHost` F5 is the single entry point; browser tests need a one-time Playwright browser download. -->

- [ ] Create `README.md` at the repo root with, in order:
  1. One-paragraph project description (Blazor Web App: club management, kelp-forest theme).
  2. **Prerequisites**: .NET 10 SDK, Node.js ≥ 20, Docker (running) — with verify commands (`dotnet --version`, `node --version`, `aspire doctor`).
  3. **First run**: `dotnet run --project Nova.AppHost` (or F5 on `Nova.AppHost` in Visual Studio / VS Code with the Aspire extension / JetBrains Rider), what it provisions (PostgreSQL 18 + `novadb`, Azurite blob emulator with `profile-photos`), dashboard URL, `/health` + `/alive` endpoints, and the note that the Bootstrap theme compiles automatically on first build (first build takes longer due to `npm ci`).
  4. **Development commands**: dashboard custom commands (`reset-db`, `clear-profile-photos`, plus the three new ones from Phase 2 — `install-npm-deps`, `rebuild-theme`, `check-contrast`), theme workflow (`npm ci`, `npm run build:css`, `npm run check:contrast` from `Nova/`), watch mode (`aspire config set features.defaultWatchEnabled true` then `aspire run`), and keeping the Aspire CLI current (`aspire update --self`).
  5. **Testing**: unit tests command; integration tests (require AppHost); browser tests (local-only, one-time `Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1 install chromium`).
  6. Link to `.github/instructions/` for the detailed conventions (theme rules, tenancy, testing, service layer, etc.).
- [ ] Keep the README human-facing (not agent-facing); do not duplicate the instruction files' content.
- [ ] Write the README **after** Phase 2 so the documented dashboard commands match reality (dependency: Phase 3 runs after Phase 2).

### Verification Plan

- All relative links/commands in the README reference paths that exist in the repo (`Nova/package.json`, `Nova.AppHost/`, `.github/instructions/*`, test project paths).
- `dotnet build Nova.slnx` and `dotnet format Nova.slnx --verify-no-changes` unaffected (no code changes expected in this phase).
- No CI changes required.

### Phase Summary

_(write when phase completes)_

## Final Recap

_(write when all phases complete: summary of the entire piece of work)_

## Deployment Plan

_(write when all phases complete: step-by-step deployment instructions)_
