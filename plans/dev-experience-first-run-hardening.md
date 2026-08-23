# First-Run Developer Experience Hardening

Harden Nova's "press F5 and go" experience so the npm/Sass build is friendly, self-healing, and visible in the Aspire dashboard, and keep the agent-facing instructions accurate and high-signal. Based on the 2026-08-23 research audit (session artifact `files/research/can-you-use-the-latest-aspire-docs-mcp-t.md`).

**Scope decision (updated 2026-08-23 by the user):** R1 (Node preflight) + R2 (lockfile-aware `npm ci`) + R3 (theme workflow as Aspire dashboard commands) + an **agent instructions hygiene pass** (update/add guidance in `.github/instructions/*.md`, following [Instructions hygiene: what frontier models still need you to say](https://devblogs.microsoft.com/dotnet/instructions-hygiene-what-frontier-models-still-need-you-to-say/)). **Dropped by the user:** R5 (human-facing README — do not add one). **Deferred:** R4 (`WithBrowserLogs`, requires the `Aspire.Hosting.Browsers` *preview* package and experimental `ASPIREBROWSERLOGS001` opt-in) and R6 (devcontainer) — both can be added later as follow-up phases if desired.
**Delivery:** commit to the session branch as phases complete; open a single PR against `main` when all phases are done.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to `Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue with zero context); run the phase's **Verification Plan** and record the result before moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

## Phase 1: Harden the npm/Sass build target (R1 + R2)

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator (MSBuild incrementality is subtle; a smaller model risked breaking the static-web-asset ordering this target depends on)

<!-- Context recap: `Nova/Nova.csproj` has a `BuildBootstrapTheme` target (BeforeTargets="ResolveProjectStaticWebAssets") that runs `npm ci` only when `node_modules` does not exist, then `npm run build:css`, then copies + registers the bootstrap-icons fonts. Research findings: (1) a teammate without Node ≥ 20 gets a cryptic MSB3073 error because nothing checks Node; (2) the `!Exists(node_modules)` guard misses a stale tree when `package.json`/`package-lock.json` change (exactly what PR #136's bootstrap-icons addition would trigger for existing contributors); (3) `Nova/scripts/check-contrast.mjs` shows the repo convention for small Node helper scripts. -->

<!-- Decision note (user asked 2026-08-23, answered here for future agents): what happens if a teammate has no Node installed, and does Aspire account for it? `dotnet run --project Nova.AppHost` builds Nova transitively; on a clean clone `wwwroot/css/bootstrap-theme.css` is gitignored/absent, so `BuildBootstrapTheme` executes `npm ci` and fails with MSB3073 ("exited with code 9009" on Windows) BEFORE the AppHost ever starts — no DCP, no dashboard, just a failed build. Aspire does NOT account for it: `aspire doctor` checks CLI version/OS/.NET SDK/dev certs/Docker/DCP/VS Code extension but has no Node check, and the npm step is repo-local MSBuild logic invisible to Aspire's resource model (the JavaScript hosting integration's npm install only applies to runtime Node resources, which Nova does not use). The `AspireUseCliBundle` opt-in only self-provisions Aspire's own dependencies (DCP/dashboard), not the repo's npm toolchain. Mitigation = this phase's `check-node.mjs` preflight with a friendly MSBuild Error. -->

- [x] Add `Nova/scripts/check-node.mjs`: exits 0 when `process.versions.node` major ≥ 20 (per `Nova/package.json` `engines.node`), prints a friendly one-line message (version found / too old), exits 1 otherwise. Follow the style of `Nova/scripts/check-contrast.mjs`.
- [x] Add a `CheckNodePrerequisite` target in `Nova/Nova.csproj` that runs `node scripts/check-node.mjs` from `$(MSBuildProjectDirectory)` with `IgnoreExitCode="true"`, captures the exit code into a property, and emits a clear `<Error>` (e.g. "Node.js 20+ is required to build the Nova theme (npm ci / npm run build:css). Install from https://nodejs.org and rebuild.") when the exit code is non-zero. Wire it as `DependsOnTargets` of `BuildBootstrapTheme` so it runs every time the theme target is evaluated (dependencies run even when the parent target is up-to-date and skipped) — a Node-less machine can never build Nova anyway because `wwwroot/css/bootstrap-theme.css` is gitignored and must compile on every clean clone.
- [x] Extract the `npm ci` call into a new incremental `RestoreNpmPackages` target: `Inputs="$(MSBuildProjectDirectory)\package.json;$(MSBuildProjectDirectory)\package-lock.json"`, `Outputs="$(MSBuildProjectDirectory)\obj\npm-ci.stamp"`; run `npm ci`, then `Touch`/`WriteLinesToFile` the stamp and register it with `FileWrites`. Make `BuildBootstrapTheme` depend on it (`DependsOnTargets="CheckNodePrerequisite;RestoreNpmPackages"`) and **remove** the old `!Exists(node_modules)` condition so `npm ci` re-runs whenever the manifest or lockfile changes even when `node_modules` exists.
- [x] Add `$(MSBuildProjectDirectory)\package-lock.json` to `BuildBootstrapTheme`'s `Inputs` so the theme also recompiles when the lockfile changes.
- [x] Preserve the existing font `Copy` + `@(Content)`/`FileWrites` registration exactly as-is; verify the target's `BeforeTargets="ResolveProjectStaticWebAssets"` hook and `Outputs` list stay intact (static-web-asset discovery must still see the compiled CSS and fonts).

### Verification Plan

- `dotnet build Nova.slnx` succeeds from a clean-ish state: delete `Nova\wwwroot\css` (and optionally `Nova\obj\npm-ci.stamp`) first, then build — CSS + fonts must be regenerated and registered (no 404-prone missing assets).
- Stale-tree fix: `(Get-Item Nova\package-lock.json).LastWriteTime = Get-Date` then `dotnet build Nova.slnx` → `npm ci` output appears (packages re-installed); build again → no npm output (stamp up-to-date, target skipped).
- Missing-Node fix: run the theme target in a shell with Node removed from PATH (`$env:Path` filtered) via `dotnet build Nova\Nova.csproj` and assert the build fails with the new friendly `Error` text, not MSB3073.
- `npm run build:css` and `npm run check:contrast` (from `Nova/`) still pass; `dotnet format Nova.slnx --verify-no-changes` passes.
- CI: push the branch → Build and Unit Tests jobs green (CI runs its own `npm ci` first, so the new target's stamp path must not break the CI flow — confirm no double-install error).

### Phase Summary

**(Implementation)**: `Nova/scripts/check-node.mjs` added — reads `engines.node` from `Nova/package.json` (currently `>= 20`), prints one friendly line (`Node.js 24.15.0 found (major 24 >= 20).` or a "too old" message naming the required major), exits 1 when the major is below the requirement. `Nova/Nova.csproj` now has two new targets:

- `CheckNodePrerequisite`: `Exec Command="node scripts/check-node.mjs"` from `$(MSBuildProjectDirectory)` with `IgnoreExitCode="true"`, captures `ExitCode` into `NodeCheckExitCode`, then `<Error>` with a friendly message ("Node.js 20+ is required to build the Nova theme (npm ci / npm run build:css). Install from https://nodejs.org and rebuild.") when non-zero. Wired as `DependsOnTargets` of `BuildBootstrapTheme` so it runs on every build even when the parent is skipped as up-to-date.
- `RestoreNpmPackages`: incremental `npm ci` with `Inputs=package.json;package-lock.json`, `Outputs=obj\npm-ci.stamp`; `MakeDir obj`, `npm ci`, `WriteLinesToFile` stamp, stamp registered with `FileWrites` (clean removes it). `BuildBootstrapTheme` now has `DependsOnTargets="CheckNodePrerequisite;RestoreNpmPackages"`; the old `!Exists(node_modules)` condition and inline `npm ci` Exec were removed, so a manifest/lockfile change re-installs even when `node_modules` exists. `package-lock.json` added to `BuildBootstrapTheme`'s `Inputs`. `BeforeTargets="ResolveProjectStaticWebAssets"`, the `Outputs` list, and the font `Copy` + `@(Content)`/`FileWrites` registration are preserved verbatim.

**Verification**: clean `dotnet build Nova.slnx` succeeds; stale-tree test (touched `package-lock.json` then built) shows `npm ci` output and re-installs, second build shows no npm output (stamp up-to-date); missing-Node test (filtered `$env:Path` to remove Node) fails with the friendly `Error` text, not MSB3073; `npm run build:css` + `npm run check:contrast` pass; `dotnet format Nova.slnx --verify-no-changes` passes. CI note: CI runs its own `npm ci` before `dotnet build`, so the stamp is absent in CI → `RestoreNpmPackages` re-runs `npm ci` during the build; this is a second install (fast, cache-backed), not an error, and does not break the CI flow.

## Phase 2: Expose the theme workflow as Aspire dashboard commands (R3)

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator (AppHost wiring touches the experimental process-command API; judgment needed on the fallback)

<!-- Context recap: `Nova.AppHost/AppHost.cs` already defines custom resource commands (`reset-db` on postgres, `clear-profile-photos` on storage) via `WithCommand` + `AppHostCommands` with a confirmation-argument pattern. Research finding: Aspire 13.5's `WithProcessCommand` (experimental, gated by ASPIREPROCESSCOMMAND001) runs a local process on the AppHost machine, streams stdout/stderr to the resource's dashboard console logs, supports a `WorkingDirectory`, bounded output, and exit-code mapping — ideal for `npm` tasks. `check:contrast` is currently CI-only, and teammates have no discoverable way to rebuild the theme. -->

- [x] In `Nova.AppHost/AppHost.cs`, add three process-backed commands to the `nova` resource using `WithProcessCommand` (add `#pragma warning disable ASPIREPROCESSCOMMAND001` at the top of the file; the repo already opts into preview features via `AspireUseCliBundle`):
  - `install-npm-deps` → `npm ci`, display name "Install npm packages" — visible remedy for stale/missing `node_modules`.
  - `rebuild-theme` → `npm run build:css`, display name "Rebuild Bootstrap theme".
  - `check-contrast` → `npm run check:contrast`, display name "Run WCAG contrast check" — turns the CI-only check into a one-click local action.
  - Use the `createProcessSpec` factory overload to set `WorkingDirectory = Path.Combine(builder.AppHostDirectory, "..", "Nova")` (commands must run from `Nova/`, where `package.json` lives), plus short `Description`s noting they run on the dev machine.
- [x] Keep `reset-db` and `clear-profile-photos` unchanged.
- [x] Fallback (only if the experimental API misbehaves in practice): implement the same commands with the established `WithCommand` + `AppHostCommands` pattern, adding a small `ProcessRunner` helper (start process, capture stdout/stderr, return `CommandResults.Success(output)` / `Failure(...)` on non-zero exit). Decide during implementation and record the choice in the Phase Summary.
- [x] Do not edit `.github/instructions/*.md` in this phase — instruction-file updates (including documenting these three commands as the sanctioned theme workflow) are owned by Phase 3.

### Verification Plan

- `dotnet build Nova.slnx` succeeds with the pragma in place (no ASPIREPROCESSCOMMAND001 error).
- Start the AppHost (`dotnet run --project Nova.AppHost`); confirm the dashboard's `nova` resource exposes the three new commands (also visible via MCP `list_resources` → resource `commands`).
- Execute each command: `check-contrast` completes with exit 0 (output contains no contrast violations); `rebuild-theme` completes and `Nova\wwwroot\css\bootstrap-theme.css` mtime updates; `install-npm-deps` completes with npm output streamed to the resource's console logs.
- `dotnet format Nova.slnx --verify-no-changes` passes.

### Phase Summary
**(Decision — API choice)**: the experimental `WithProcessCommand` API was used, not the `WithCommand` + `AppHostCommands` + `ProcessRunner` fallback. The API worked end to end, including runtime command execution. Details worth recording: in Aspire.Hosting 13.5.2 the factory overload parameter is named `processSpecFactory` (the docs say `createProcessSpec` — a docs mismatch that initially caused CS1739; the parameter name in the assembly was confirmed via reflection of `Aspire.Hosting.dll`). `new ProcessCommandSpec("npm")` + `Arguments = ["ci"]` + `WorkingDirectory` works; short executable names resolve from the AppHost PATH with PATHEXT on Windows.

**(Implementation)**: `Nova.AppHost/AppHost.cs` — added `#pragma warning disable ASPIREPROCESSCOMMAND001` at the top of the file; `novaDirectory = Path.Combine(builder.AppHostDirectory, "..", "Nova")`; three `WithProcessCommand` calls on the `nova` resource: `install-npm-deps` → `npm ci` ("Install npm packages"), `rebuild-theme` → `npm run build:css` ("Rebuild Bootstrap theme"), `check-contrast` → `npm run check:contrast` ("Run WCAG contrast check"); each sets `WorkingDirectory = novaDirectory` and a short `Description` noting the command runs `npm` on the dev machine. `reset-db` (postgres) and `clear-profile-photos` (storage) are unchanged. No `.github/instructions/*.md` edits in this phase (owned by Phase 3).

**Verification**: `dotnet build Nova.slnx` succeeds with the pragma; `dotnet format Nova.slnx --verify-no-changes` passes. Runtime: AppHost started (`dotnet run --project Nova.AppHost`; aspire-doctor green, Docker running) → `aspire-list_resources` showed all three commands enabled on `nova`; executed each: `check-contrast` PASS with full output, `rebuild-theme` completed and `Nova\wwwroot\css\bootstrap-theme.css` mtime updated, `install-npm-deps` completed with npm output; `aspire-list_console_logs` confirmed output streams to the resource console logs. AppHost stopped afterward; `apphost.log` removed.

## Phase 3: Agent instructions hygiene pass (replaces the dropped README)

Status: Complete <!-- Not started | In progress | Complete -->

Suggested executor: orchestrator (keep/remove/move/verify decisions require judgment; a smaller-model sub-agent may draft the audit findings for the orchestrator to decide on)

<!-- Context recap: the user explicitly dropped the human-facing README (R5). Instead, agent guidance lives in `.github/instructions/*.md` (path-scoped, e.g. `bootstrap-theme.instructions.md`, `blazor-architecture.instructions.md`) plus the repo-wide Copilot overview. This phase applies the keep/remove/move/verify review from https://devblogs.microsoft.com/dotnet/instructions-hygiene-what-frontier-models-still-need-you-to-say/ so instructions stay high-signal and accurate after Phases 1–2. Known staleness to fix: `bootstrap-theme.instructions.md` lines ~29–31 describe `npm ci` as running "only when node_modules is absent" (Phase 1 makes it lockfile-aware), and no instruction file yet mentions the new dashboard commands or the Node preflight. `blazor-architecture.instructions.md` line ~121 (Bootstrap-native navbar markup) is still accurate post-PR #136 but should be verified. -->

- [x] Apply the keep/remove/move/verify review from the blog post to every file in `.github/instructions/`: **keep** info that is true, consequential, and hard to infer; **remove** generic coaching ("write clean code" etc.), rules already enforced by tools (`dotnet format`, analyzers), prompt folklore, and stale workarounds; **move** rules that belong at a different scope; **verify** every command, version, and file path cited.
- [x] Update `bootstrap-theme.instructions.md` for the Phase 1–2 changes: replace the now-wrong "`npm ci` runs only when `node_modules` is absent" description with the lockfile-aware `RestoreNpmPackages` stamp behavior; document the `check-node.mjs` preflight (build fails with a friendly error naming Node 20+ when Node is missing or too old); document the three dashboard commands (`install-npm-deps`, `rebuild-theme`, `check-contrast` on the `nova` resource) as the sanctioned theme workflow alongside the existing npm scripts.
- [x] Verify the other nine instruction files against recent code changes (PR #133 theme overhaul, PR #136 navbar redesign) and fix only genuine staleness or contradictions; do not expand files with generic advice.
- [x] Follow the blog's progressive-disclosure rules: point to sources of truth instead of duplicating (palette → `Nova/scss/_variables.scss`, versions → `Nova/package.json`/`Directory.Packages.props`, SDK → `global.json`), keep authoritative commands as the shortest reliable validation path, and reserve strong language (never/always/must) for genuinely absolute rules (e.g. never edit/commit the compiled CSS).
- [x] If Phase 1–2 changed anything else agent-relevant (e.g. build failure modes, working-directory requirements for npm scripts), fold that into the appropriate path-scoped file rather than adding a new file — only create a new instruction file if a clear gap exists that no existing file's scope covers.

### Verification Plan

- Every command cited in the edited files is runnable as written; run the cheap ones to prove it: `dotnet build Nova.slnx`, `dotnet format Nova.slnx --verify-no-changes`, and (from `Nova/`) `npm run check:contrast`.
- No instruction file contradicts the implemented behavior: grep `.github/instructions/` for `npm ci`, `node_modules`, `build:css`, `dashboard`, `check-contrast` and compare against `Nova/Nova.csproj` targets and `Nova.AppHost/AppHost.cs`.
- The dropped-README decision is honored: confirm no `README.md` was created at the repo root.
- Docs-only phase: `dotnet build Nova.slnx` and format check unaffected.

### Phase Summary
**(Review performed)**: keep/remove/move/verify pass over all 10 files in `.github/instructions/`. `bootstrap-theme.instructions.md` was rewritten for Phases 1–2: frontmatter `applyTo` extended with `Nova/package-lock.json` and `Nova/scripts/**`; body now documents the Node preflight (friendly build error, do not bypass), the lockfile-aware `RestoreNpmPackages` stamp behavior (re-install on manifest/lockfile change even when `node_modules` exists, replacing the stale "only when node_modules is absent" text), the 3-step `BuildBootstrapTheme` chain, and a new **Aspire dashboard commands (theme workflow)** section naming `install-npm-deps` / `rebuild-theme` / `check-contrast` on the `nova` resource as the sanctioned workflow alongside the npm scripts. Existing rules (never re-add vendored CSS, never hardcode Bootstrap-blue literals, never edit the generated CSS) preserved with "never" reserved for those absolutes.

The other nine files were checked against PR #133/#136 and the implemented behavior: no genuine staleness found. `blazor-architecture.instructions.md` (Bootstrap-native navbar markup) verified accurate against `Nova/Components/Layout/NavMenu.razor` post-#136; `testing.instructions.md` dashboard section (reset-db on postgres, clear-profile-photos on storage) still accurate and correctly scoped — the new `nova` commands are theme-workflow scope and live in the theme file only; `copilot-instructions.md` overview (npm ci + build:css bullets) still accurate. Progressive disclosure kept: palette → `Nova/scss/_variables.scss`, versions → `Nova/package.json` / `Directory.Packages.props`, SDK → `global.json` pointers preserved; no new instruction files created; no `README.md` created (user dropped R5).

**Verification**: `dotnet build Nova.slnx` and `dotnet format Nova.slnx --verify-no-changes` pass; `npm run check:contrast` (from `Nova/`) passes; grep of `.github/instructions/` for `npm ci` / `node_modules` / `build:css` / `dashboard` / `check-contrast` is consistent with `Nova/Nova.csproj` targets and `Nova.AppHost/AppHost.cs`; no README.md at repo root.

## Final Recap

R1 (Node preflight) + R2 (lockfile-aware `npm ci`) + R3 (theme workflow as Aspire dashboard commands) + the agent-instructions hygiene pass are all implemented, validated, and committed as three phase commits (`0189217`, `f55bc3a`, `d2fe349`). The human-facing README (R5) was intentionally dropped per the user. The theme build is now self-healing (manifest/lockfile change re-installs via the stamp; Node missing/too-old fails fast with a friendly error instead of MSB3073), the three npm tasks are one-click operations in the Aspire dashboard on the `nova` resource, and `.github/instructions/bootstrap-theme.instructions.md` documents the lockfile-aware behavior, the preflight, and the dashboard commands as the sanctioned workflow. All validation passed: builds (clean + stale-tree + missing-Node), `npm run build:css`, `npm run check:contrast`, `dotnet format --verify-no-changes`, and a full AppHost runtime pass executing all three dashboard commands.

## Deployment Plan

1. Review and merge the single PR against `main` (the one opened from the session branch; context: merged PR #136).
2. No runtime deployment steps: this change is developer-experience only (MSBuild targets in `Nova/Nova.csproj`, a small Node helper, AppHost commands). Ship it as part of the next AppHost/`Nova` deployment as usual — `dotnet publish` / the AppHost path rebuild the theme via `BuildBootstrapTheme` as before.
3. Post-merge, existing contributors with a stale `node_modules` get an automatic, transparent `npm ci` on their next `dotnet build` (stamp-based), and a friendly error naming Node 20+ if their toolchain is too old. Contributors without Node 20+ must install it from https://nodejs.org.
4. If anything regresses around static-web-asset discovery (404 on `bootstrap-theme.css` or fonts), the symptom is the `BeforeTargets="ResolveProjectStaticWebAssets"` hook or the `@(Content)` registration in `BuildBootstrapTheme` — see the comment block in `Nova/Nova.csproj`.
