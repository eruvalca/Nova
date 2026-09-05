# PR 244 process changes: native compatibility evidence

Observed on Windows on 2026-09-05. Shared skill discovery and the native checkpoint/block/defer lifecycle pass in both installed local clients with `.github/skills/impeccable` absent and the required host trust enabled. Copilot's initial activation gap was resolved by explicitly trusting the export in isolated configuration; `--add-dir` alone was insufficient. Copilot cloud and VS Code native execution have not been tested here. Retain the real legacy skill copy until the separate cloud discovery proof is complete.

## Scope and client settings

The isolated Git export is `artifacts/compatibility/2026-09-05T21-38-27-565Z/repo`. It contains candidate guidance, adapters and engineering scripts, but no `.github/skills` directory. The export has its own `main` branch and local commits; no real source checkout changes, external tasks, pushes or review comments were made by the native clients. No .NET, application builds, application tests or design workflow were run. Clients could read the discovered skill and execute only the explicit checkpoint/defer commands; write, .NET and publication tools were denied. Logs and temporary diagnostic handlers stay under ignored `artifacts/compatibility`.

| Client | Installed version | Model settings used | Shared skill discovery | Native hooks |
| --- | --- | --- | --- | --- |
| Codex CLI | `0.153.4` | Existing `gpt-6-astra`, reasoning `ultra`; no override | Pass: runtime-supplied `.agents/skills/impeccable/SKILL.md`, metadata `4.1.3` | Pass with invocation-scoped hook trust: SessionStart, UserPromptSubmit, PostToolUse, Stop, one block and defer |
| Copilot CLI | `1.0.83` | Existing `settings.json` selects `claude-opus-4.8`; no model/effort override | Pass: native `skill` invocation and `skill list --json` resolve the canonical project skill, metadata `4.1.3` | Pass with explicit folder trust: native session/prompt/post-tool callbacks, one block, defer and Stop guard |
| Copilot cloud | Not run | Not applicable | Pending publication and cloud smoke | Pending |
| VS Code | Not run | Not applicable | Not evaluated here | Payload/manifest fixtures pass; native activation pending |

Node for repository scripts was `24.15.0`. No global user configuration or permission settings were changed. Codex reported its stable `hooks` feature enabled. The probe supplied a trusted project layer and `--dangerously-bypass-hook-trust` for that invocation only. This is test authorization, not a recommendation to bypass normal hook review in daily work. A repeat with no `.codex/config.toml` in the export still received SessionStart context. Copilot's `disableAllHooks` was unset, whose installed help documents a default of `false`.

## Native Codex lifecycle

The full probe used native session `01a07387-eb34-7ba3-927e-56d23d640dea`:

1. SessionStart supplied the real session ID, and UserPromptSubmit initialized its request epoch.
2. The agent read the canonical skill path supplied by its native catalog. It ran `node eng/verify.mjs expect --session 01a07387-eb34-7ba3-927e-56d23d640dea --profile quick --base HEAD` and attempted `FIRST_COMPLETION_PROBE`.
3. Stop at `21:45:08.269Z` emitted top-level JSON `decision: block`, naming missing evidence and the explicit defer option. No tests ran inside the hook.
4. The agent executed the authorized defer with reason `Compatibility smoke intentionally excludes .NET`.
5. The second Stop at `21:45:22.474Z` carried native `stop_hook_active: true` and emitted no block. Persisted state has `continuations: 1`, a recorded deferral and no active checkpoint. The agent ended with `DEFERRED_AFTER_NATIVE_STOP`, explicitly reporting verification as unverified.

A second native session, `01a0738a-389b-7fd3-837e-6daec9560468`, confirmed SessionStart loading without the temporary local config file. It declared no intent and stopped without a block.

The initial Windows adapter used a `cmd` FOR expression; an isolated shell probe reproduced a quoting failure (`delims` was unexpected). The generated Codex `commandWindows` now explicitly invokes `powershell.exe -NoProfile -NonInteractive -EncodedCommand` with a UTF-16LE encoding of the same root-resolving PowerShell script used by Copilot. The successful native runs exercise that generated adapter. The Unix command remains a root-resolving shell command. Regeneration still preserves unrelated hook entries.

## Copilot CLI activation and folder trust

The first four native runs discovered the shared skill but received no SessionStart context. Diagnostics next to the candidate handlers did not run. Neither adding `--add-dir` nor an isolated repository `settings.local.json` with `disableAllHooks: false` and inline handlers established hook activation. During the fourth probe the agent attempted its session-folder UUID; the checkpoint CLI correctly rejected it with exit code 1 because no native request was registered. No checkpoint or pass was manufactured.

Installed help distinguishes `--add-dir` (file access and trusted skills/agents) from the `trustedFolders` setting. The user's configured trusted folder did not include this export. The fifth probe supplied an invocation-only `COPILOT_HOME` under the ignored evidence directory with `trustedFolders` set to the exact export and `disableAllHooks: false`. Only existing nonsecret model/effort settings were copied; no credentials or global settings were changed. Native hooks then activated. The sixth probe removed the temporary inline repository hooks and confirmed that the repository manifest files alone work with folder trust.

The manifests-only native session `94f679bf-650d-4a66-9431-7731c07e84aa`:

1. Received the real SessionStart ID and invoked the canonical shared skill, version `4.1.3`.
2. Armed `quick` for `HEAD` and attempted `FIRST_COMPLETION_PROBE`.
3. Produced one top-level Stop block at `21:59:48.699Z` for missing evidence, forcing continuation.
4. Recorded the explicit deferral `Compatibility smoke intentionally excludes .NET`.
5. Stopped at `21:59:58.617Z` with native `stop_hook_active: true` and no block, then reported `DEFERRED_AFTER_NATIVE_STOP` and unverified status.

The earlier trusted run, session `04f6075a-4532-4959-9e93-c786e4ebc83f`, also passed with duplicate file/inline callbacks. The consumed counter allowed only one block. These are native observations, separate from the shell-level and schema fixtures.

Copilot emits our corrective reason as another `userPromptSubmitted` event. The observed payload contains only session ID, timestamp, cwd and prompt, with no origin flag; the final Stop includes the documented native continuation guard. A focused follow-up now keeps the spent allowance when the prompt exactly matches the hash of our last emitted reason in the same session/checkout/branch. Every prompt still creates a new epoch and expires intent. This exception never carries over an old checkpoint or raw prompt text; an exact human copy is treated conservatively the same way. Native-shaped fixtures prove that re-arming and cosmetic edits cannot cause another block even with the native Stop guard false, while other prompts reset normally. This preserves the bounded contract without claiming that Copilot supplies origin metadata.

## Reproduction and retained local evidence

The bounded drivers are `artifacts/compatibility/run-native.mjs`, `run-native-recheck.mjs` and `run-native-trusted.mjs`. The last supplies only the isolated `COPILOT_HOME` in addition to the existing bounded invocation. They record timestamps, exit codes and outputs and impose a four-minute child timeout. The export and its logs are pointed to by `artifacts/compatibility/latest.json`. Native inference/authentication was permitted; remote export, updates, external tool calls and application execution were excluded. Subsequent probes disabled configured MCP startup using the installed `--disable-mcp-server` option.

Codex invocation shape (the model and reasoning settings are deliberately omitted):

```text
codex -a never exec --json --sandbox workspace-write --dangerously-bypass-hook-trust --ephemeral -C <export> -c 'projects."<export>".trust_level="trusted"' <bounded-prompt>
```

Copilot invocation shape:

```text
copilot -p <bounded-prompt> --add-dir <export> --no-ask-user --no-auto-update --no-remote-export --disable-builtin-mcps --available-tools=skill,view,powershell,read_powershell --allow-tool=shell(node:*) --allow-tool=shell(Get-Content:*) --allow-tool=shell(Get-ChildItem:*) --allow-tool=shell(git rev-parse:*) --deny-tool=write --deny-tool=shell(dotnet:*) --deny-tool=shell(gh:*) --deny-tool=shell(git push:*) --output-format json --log-level debug --log-dir <logs>
```

Within the timestamped evidence directory:

- `copilot-skills.json`: native project discovery listing with the canonical skill path.
- `codex-attempt1.jsonl`, `copilot-attempt1.jsonl`: initial discovery and missing-context observations.
- `codex-attempt2.jsonl`: full native block/deferral transcript; `copilot-attempt2.jsonl`: diagnostic-handler activation probe.
- `codex.jsonl`, `copilot-attempt3.jsonl`, `copilot-attempt4.jsonl`: config/path-access/settings rechecks. `copilot-attempt5.jsonl` records explicit folder trust with inline hooks; `copilot.jsonl` is the sixth manifests-only passing probe. Corresponding `*-result.json` files record client exit, not application verification.
- `repo/artifacts/codex-hooks.jsonl` and `repo/artifacts/copilot-hooks.jsonl`: actual adapter event/output traces, including each native session and its sole block.
- `repo/artifacts/native-diagnostic/`: raw native Codex and Copilot input payloads. Copilot files appeared only after explicit folder trust.
- `repo/artifacts/verification/checkpoints/`: isolated native epoch and deferral state.
- `copilot-logs/`, `copilot-ps-shell.json`, `codex-cmd-shell.json`: runtime logs and the separate shell-level probes.
- `copilot-trusted-home/config.json`, `settings.json`: isolated explicit trust and unchanged model settings. `copilot-attempt5-settings.local.json` was removed from the export before the final manifests-only probe.

Raw transcripts are local diagnostic evidence and should not be committed wholesale. This summary is the curated record. The engineering validation after the Windows fix passed `node eng/guidance.mjs check` (no drift or errors) and all 18 focused guidance/hook/artifact tests. The later corrective-echo refinement passed all 12 isolated hook tests, including two new tests. These tests cover provider output fixtures, unrelated-hook preservation, session isolation, stale evidence, branch/request changes, one-continuation bounds, Razor scanning and historical-artifact protection.

## Remaining acceptance

- Run a Copilot cloud task on the published candidate with only `.agents/skills/impeccable`, and record its native discovery path before deleting the real legacy copy. Local discovery alone does not satisfy that accepted condition.
- Require the normal host trust setup for each actual checkout and confirm SessionStart context. Both local clients now have native activation proof; direct verification remains available when hooks are disabled or unavailable.
- Exercise the VS Code adapter natively before claiming that host's completion workflow verified. Existing fixtures use the documented nested Stop response and native guard.
- Keep normal host trust controls and the direct verification workflow available. Missing hooks cannot establish passing evidence and must not prevent read-only work.

Schema sources checked during implementation: [Codex hooks](https://developers.openai.com/codex/hooks), [GitHub Copilot hook reference](https://docs.github.com/en/copilot/reference/hooks-reference), and [VS Code hook reference](https://code.visualstudio.com/docs/agents/reference/hooks-reference). The implementation uses separate documented output adapters; the native observations above determine what is actually proven on this machine.
