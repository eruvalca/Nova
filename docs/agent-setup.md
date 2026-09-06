# Agent setup and verification

[AGENTS.md](../AGENTS.md) owns repository rules, routing, and completion requirements.
This page explains provider discovery and how to verify it. It does not replace the
task skills or the application's build/test gate.

## Shared sources and provider copies

| Concern | Source of truth | Provider behavior |
| --- | --- | --- |
| Repository rules | Root AGENTS.md | Both tools read it. Keep one repo-wide file. |
| Scoped rules | .github/instructions/*.instructions.md | Copilot CLI matches applyTo; Codex follows the explicit root instruction to read matching files. |
| Task recipes | .agents/skills/*/SKILL.md | Both CLIs support this location. Selection must be followed by reading the recipe and applicable references. |
| Impeccable agents | .agents/skills/impeccable/agents/*.toml | Same-format copies in .codex/agents; equivalent Markdown profiles in .github/agents. |
| Impeccable hooks | Both copies of scripts/hook-admin.mjs in the Impeccable skill | Produces .codex/hooks.json and .github/hooks/impeccable.json; changing only generated entries is insufficient. |

Retain the existing .github/skills/impeccable mirror. Provider paths and native image-tool
instructions intentionally differ; shared behavior must not. When changing an agent, edit
its canonical TOML, both provider definitions, and run the parity check. When changing hook
installation, update both installer copies and the generated entries.

Run from the repository root (PowerShell 7 is available on the CI runner):

~~~powershell
pwsh -NoProfile -File scripts/Test-AgentGuidance.ps1
pwsh -NoProfile -File scripts/Test-AgentGuidance.ps1 -SelfTest
node --test scripts/AgentHooks.Tests.mjs
~~~

The PowerShell check has no package dependencies and never repairs files. It compares the
four maintained agents and hook installers, with only explicit provider substitutions.
Hook tests use the existing Node runtime and temporary fixtures. Neither command proves
that an agent loaded the files or that a user's tool trusted the hooks.

## Verify a fresh session

After changing instructions, start a fresh session. Repeat from the repository root and
a representative subdirectory; do not assume a root-only check covers nested launches.

1. Inspect versions with codex --version, copilot --version, and node --version. Record
   these with the verification result; an observed version is not a minimum requirement.
   Confirm repository-configuration trust in the native UI before interpreting behavioral
   exercises. Permission to run a tool in a disposable copy does not prove its instructions
   or hooks were trusted; do not grant or persist trust merely to make a probe pass.
2. In Copilot CLI, inspect /env, /instructions, /skills list, /skills info, and /agent. The
   non-interactive inventory command below also checks repository discovery. Path-specific
   instructions may be loaded only when a matching file is worked on.
3. In Codex, ask it to identify active instruction sources, inspect /skills and available
   custom-agent roles, and verify the sources it actually reads when working on a named file.
4. Give a small representative task without spelling out the expected defect. Inspect the
   session's tool trace: the relevant scoped instructions and skill references must actually
   be read, followed by the expected behavioral analysis or test. A catalog listing or an
   agent's claim that it followed instructions is insufficient.
5. Verify the selected custom-agent definition. Report personal/project name collisions;
   do not silently edit global skills, model preferences, or personal configuration.

~~~powershell
copilot plugins list --kind skill --kind instruction --scope repository --json
~~~

Use existing task recipes for both additions and modifications. Keep shared references as
ordinary Markdown links plus explicit read instructions. Do not rely on provider-specific
import syntax, undocumented precedence, or a fallback-filename list to load every scoped
rule. Codex's documented startup discovery is based on root-to-working-directory ancestry,
not arbitrary edited-file globs.

## Hooks: execution, trust, and fallback

The design hooks are advisory. Their event adapters support different output envelopes in
Codex and Copilot; do not copy one provider's JSON output into another provider's manifest.
Shell edits outside the matched edit tools may not trigger a hook.

- Codex commands resolve the repository root and have a Windows override. Project and hook
  trust are separate from a committed manifest; inspect /hooks and its diagnostics. Changed
  definitions can require renewed native trust. Do not bypass that process.
- Copilot has Bash and PowerShell commands. Verify execution in the local checkout and keep
  cloud-agent default-branch activation separate. Do not infer GitHub PR-review hook execution
  from CLI or cloud-agent support.
- Test a harmless edit in a temporary fixture with a known detector finding, from root and a
  nested directory. Confirm the correct command runs and its advisory reaches the model.
  Also confirm a clean fixture and a non-UI C# file behave as expected.
- The hook passes the repository root separately from the event cwd: configuration/cache
  belongs to the repository, while relative edited paths belong to the tool's working directory.
- A missing runtime, disabled/untrusted hook, detector failure, or unsupported host is
  **unverified/skipped**, not a clean design result. Exit zero preserves the editing session;
  it does not prove scanning succeeded.
- Verify Codex desktop execution separately from its bundled CLI version. If native trust
  or host access prevents that check, report the limitation without changing user settings.

For a manual pass, follow the loaded Impeccable skill's hooks reference and use its local
detector directly; no package installation is needed:

~~~powershell
node .agents/skills/impeccable/scripts/detect.mjs <source-file>
~~~

Use the .github skill path when that is the loaded provider copy. Review actual detector
output and stderr. A `DEGRADED` message means only the documented fallback ran; absent
optional HTML parsers leave custom properties, selector matching, and computed contrast
unverified. A mechanical pass does not replace browser checks or functional regression tests.

## Official references and maintenance

Consult current documentation when changing a provider manifest or discovery behavior.
These guides can evolve independently of an installed version; verify material behavior
locally and preserve uncertainty where sources disagree.

- OpenAI: [AGENTS.md discovery](https://learn.chatgpt.com/docs/agent-configuration/agents-md),
  [skills](https://learn.chatgpt.com/docs/build-skills),
  [custom agents](https://learn.chatgpt.com/docs/agent-configuration/subagents),
  [hooks](https://learn.chatgpt.com/docs/hooks).
- GitHub: [CLI instructions](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-custom-instructions),
  [CLI skills](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-skills),
  [agent profiles](https://docs.github.com/en/copilot/reference/custom-agents-configuration),
  [hook schemas](https://docs.github.com/en/copilot/reference/hooks-reference),
  [PR review](https://docs.github.com/en/copilot/how-tos/use-copilot-agents/request-a-code-review/use-code-review).

Current PR-review documentation describes head-branch instructions/skills. Do not repeat
older base-branch or instruction-size claims without checking the current applicable guide.
Avoid depending on disputed instruction-import or duplicate-agent precedence behavior.

The implementation plan records verification evidence. Future changes should keep one PR
validation record identifying the tested revision, commands/results, independent review when
required, sources actually read, behavioral transitions/siblings checked, and unavailable checks.
Read the listed sources and test assertions when reviewing that record; a claimed skill read is
not proof of applying it. Recheck discovery and hook execution after relevant tooling
upgrades; do not accumulate stale version-specific workarounds.
