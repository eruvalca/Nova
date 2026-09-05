# Engineering verification

Use Node 24+, the SDK in `global.json`, a working Docker engine, and PowerShell 7. The .NET build restores the Sass package; engineering scripts have no npm dependencies. After the first build, install local Chromium with `pwsh -File Nova.Browser.Tests/bin/Debug/net10.0/playwright.ps1 install chromium` (Linux: add `--with-deps`). Aspire packages and Playwright versions come from `Directory.Packages.props`; tests start their own AppHost.

```text
node eng/verify.mjs plan --profile push --base origin/main
node eng/verify.mjs run --profile quick --suite unit --filter-class "*CampaignEntryTests"
node eng/verify.mjs run --profile push --base origin/main
node eng/verify.mjs run --profile pre-pr
node eng/verify.mjs run --profile pre-merge
node eng/verify.mjs run --profile ci --install-browser
node eng/verify.mjs status --profile pre-pr --json
```

`quick` runs a fresh build and selected tests. `push` adds engineering checks, formatting, unit tests, conservatively affected suites, and contrast for theme changes. Full profiles always build once and execute engineering checks, formatting, contrast, and all three suites serially. No suite result is reused. A failing step stops the run; investigate it before starting another run. Filters and explicit suite selection are accepted only for `quick`.

`push` requires an explicit base. Full profiles default to `origin/main`; fetch it before checking readiness. The run records the resolved commit and original reference, and status checks both current source and that reference. Committed, staged, unstaged, nonignored untracked, and both sides of renames participate in selection. The checkout lock is acquired before selecting inputs.

Run evidence lives in ignored `artifacts/verification/<run-id>/`: manifest, command logs, native xUnit CTRF JSON, TRX, and browser diagnostics. It records exact commands, tools, source/head/base, timing, skips, and failures. Missing/malformed reports, zero executed tests, unexpected skips, cancellation, timeout, failed teardown, or edits during the run prevent a pass. Full readiness requires one current full run. Fingerprints identify the inputs checked; they do not prove adequate behavioral coverage or secure attestation.

Only the seven existing named `A11yEvidence`/`A11yManualChecklist` screenshot tests in `eng/lib/policy.mjs` may skip by default. Set `NOVA_A11Y_SCREENSHOTS=1` to require them. Keyboard, focus, receipt, and interaction regressions are ordinary required tests.

The runner's checkout lock serializes build-capable commands. An interrupted owner leaves a diagnostic file: inspect its PID and child build processes before manually removing an abandoned lock. The shared AppHost fixture also holds an OS-backed capacity lock, including direct test invocations. `NOVA_TEST_LOCK_DIRECTORY` must be one absolute shared path for every checkout if overridden; `NOVA_TEST_LOCK_TIMEOUT_SECONDS` bounds waiting (default 900). Do not use per-run paths for that lock.

Browser artifacts are run/test-specific. `NOVA_TEST_ARTIFACTS` is an absolute output root, and `NOVA_BROWSER_TRACE=1` retains native Playwright traces; the runner sets both. Fixture teardown attempts every resource cleanup and reports failures. There is no automatic orphan deletion. CI retains diagnostics for 30 days.

## Guidance and completion checkpoints

```text
node eng/guidance.mjs check
node eng/guidance.mjs explain Nova.UI/Features/Campaigns/Pages/NewCampaign.razor.cs
node eng/guidance.mjs sync
node eng/verify.mjs expect --session <native-id> --profile pre-pr --base origin/main
node eng/verify.mjs defer --session <native-id> --reason "Waiting for the requested decision"
```

Adapters publish the native session ID when enabled. `expect` arms explicit workflow intent for that session/request/worktree/branch; running tests alone never arms it. `defer` records a blocker without passing verification. New user requests expire earlier checkpoints. An armed completion hook can direct the agent to the common runner once, honors native stop guards, and cannot run suites or model reviews itself. Read-only work and ambiguous intent remain advisory. Disabled/untrusted/broken hooks do not establish verification success; CI enforces the merge checks.

### Enable hooks for the actual checkout

Review the repository hook commands, then enable them through the host's normal folder and hook trust controls. Trust applies to the actual checkout/worktree path; trusting another saved checkout does not establish trust for a new worktree.

- **Copilot CLI:** the checkout must be included in the host's `trustedFolders` setting. `--add-dir` permits file access and trusted skill/agent discovery, but did not activate repository hooks in the Windows smoke. Start a fresh session after reviewing and trusting the checkout. `disableAllHooks` must not be enabled. For isolated diagnostics, `COPILOT_HOME` can point to a temporary configuration directory; do not copy credentials or overwrite the user's configuration.
- **Codex:** the project and its hook manifest must be trusted, and hooks enabled. Use the normal review/approval flow. The compatibility smoke's invocation-only hook-trust bypass is not a daily workflow requirement.
- **Confirm activation:** a new session should receive `Nova verification session: <native-id>` from SessionStart. Skill discovery alone does not prove hooks ran. If this context is missing, report the activation gap and run verification directly; never infer a usable checkpoint ID from a client session-folder name.

Every prompt expires the preceding checkpoint. Copilot also delivers a hook's corrective reason as a prompt: an exact hash match retains only the spent continuation allowance, never the old checkpoint. A real user copying that entire reason is indistinguishable and also gets advisory behavior until new intent is declared. Other prompts reset the allowance normally. No raw prompt text is stored in checkpoint state.

The canonical skill source is `.agents/skills`. The temporary legacy Impeccable copy remains until recorded Codex, Copilot CLI, and Copilot cloud discovery proof permits removal. `guidance sync` repairs only owned agent/hook adapters and preserves unrelated hook entries. Discovery, host capabilities, instruction scope, and behavioral parity are separate checks.

Official references checked for this implementation: [Codex hooks](https://learn.chatgpt.com/docs/hooks), [Codex skills](https://learn.chatgpt.com/docs/build-skills), [Copilot hooks](https://docs.github.com/en/copilot/reference/hooks-reference), [Copilot skills](https://docs.github.com/en/copilot/how-tos/copilot-on-github/customize-copilot/customize-cloud-agent/add-skills), [VS Code hook schemas](https://code.visualstudio.com/docs/agents/reference/hooks-reference), [Aspire CI](https://aspire.dev/testing/testing-in-ci/), and [Playwright .NET CI](https://playwright.dev/dotnet/docs/ci). The instruction revisions follow [Microsoft's instruction-hygiene guidance](https://devblogs.microsoft.com/dotnet/instructions-hygiene-what-frontier-models-still-need-you-to-say/), retaining repository facts and demonstrated traps while keeping recipes conditional.
