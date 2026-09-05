# PR 244 guidance evaluation

This small diagnostic compares baseline and revised guidance on three real Nova defect families.
Twelve initial repairs (three tasks × two clients × two guidance conditions) can expose useful
differences and regressions; they cannot establish a reliable reduction in future PR findings.

## Handoff and current state

Prepared on 2026-09-05. Root subsequently completed all six serial control builds and test runs;
no evaluation client has been launched. Root coordinates .NET validation and model execution. Raw fixtures/output are ignored under
artifacts/evaluation; this document retains the procedure and final compact results.

- [x] Select historical starting code and focused unit acceptance source.
- [x] Prepare fresh repositories without original Git history or evaluator acceptance tests.
- [x] Keep application/tests and shared tooling byte-identical across each task's four agent runs.
- [ ] Freeze final candidate guidance after implementation settles; refresh if it changed.
- [x] Prove negative/positive controls and inspect their exact assertion failures.
- [ ] Run both clients under both conditions, then apply the frozen acceptance files separately.
- [ ] Record results and retain, simplify, or revise guidance based on observed behavior.

## Three bounded tasks

Frozen prompts ask for outcomes, regression tests, and inspection of a related path without suggesting
a helper or algorithm. Their common instruction limits work to the isolated repository and prohibits
commits, pushes, external tasks, and publication.

| Case | Starting code | Known historical repair | Shared acceptance |
| --- | --- | --- | --- |
| V — validation recovery | 71a9a9da894cf95392d003d07fb6ff08346cd3c3 | 01fb169966598a9f305d45d8c1caecdd1d2070f7 | A corrected rejected name resubmits despite an unchanged parent error snapshot, preserving its operation ID; ordinary invalid input remains rejected. Inspect metadata correction as the sibling. |
| A — asynchronous identity | dcafe85673746108f8d239a55b751318d4df2051 | 97bae360454ed3ce29ffbf22d5e707a4885269d3 | Overtaken startup/notification completions cannot replace the newer club on both Draft pages; disposal rejects late completion; ordinary opening and restored-input identity change remain usable. |
| D — durable commands | fd85a44ef35c96146d2c55492b08272af71129b6 | 9ae0487ce6487bbe8befe0b69f3b7d2edded75d5 | Required persistence precedes retry dispatch, failed form cleanup retains the pending command, and replay uses the original payload/operation ID. Inspect Draft opening as the sibling. |

Acceptance comes from the subsequent regression files, extracted into uniquely named evaluator
classes retaining only these behaviors and existing arrangement helpers. Apply the same frozen
files after each agent finishes. Existing task-era tests remain visible. No particular refactor
earns credit. The real browser opening/history journey is a separately validated repository regression,
not an extra browser run in every model evaluation.

## Fixtures and controls

The one-off artifacts/evaluation/prepare.mjs exports historical code into fresh local repositories
with no remotes and one local fixture commit. Baseline guidance comes from
6f943d5177a537bbe4a89a7b797321911ff0414b; candidate guidance is a hashed snapshot of current root/scoped
instructions, skills, agent adapters, and hook files. Both conditions receive identical frozen
eng tooling and .node-version, and their task's original SDK/dependencies. Only guidance differs.

Preparation p2 contains 12 agent workdirs and six controls. Use a new label to refresh after final
guidance edits; existing preparations are never overwritten:

    node artifacts/evaluation/prepare.mjs p3

The manifest records application, guidance, shared-tool, prompt, and acceptance hashes and refuses
an application mismatch. Short names use case v/a/d, client x (Codex) or p (Copilot), and condition
b/c. Controls use v-n/v-p, etc. Evaluator files and prompts are outside agent workdirs.

Root runs these commands one at a time, substituting the chosen preparation:

    pwsh -File artifacts/evaluation/run-control.ps1 -Preparation p2 -Case v -Control negative
    pwsh -File artifacts/evaluation/run-control.ps1 -Preparation p2 -Case v -Control positive
    pwsh -File artifacts/evaluation/run-control.ps1 -Preparation p2 -Case a -Control negative
    pwsh -File artifacts/evaluation/run-control.ps1 -Preparation p2 -Case a -Control positive
    pwsh -File artifacts/evaluation/run-control.ps1 -Preparation p2 -Case d -Control negative
    pwsh -File artifacts/evaluation/run-control.ps1 -Preparation p2 -Case d -Control positive

The wrapper builds Nova.Unit.Tests once and uses MTP --no-build, explicit --filter-class, CTRF JSON,
and TRX. Build/discovery/report/teardown failures are incomplete controls. A nonzero negative exit
is insufficient: inspect that the intended assertion failed and its ordinary neighbor passed.
The known repair must pass the full acceptance set before the case is eligible.

All three cases are eligible from the root-run controls recorded on 2026-09-05. Each run discovered
the expected acceptance cases, had no skips or infrastructure errors, and preserved passing neighbors:

| Case | Original control | Known repair control | Observed original failures | Evidence directories under artifacts/evaluation/p2 |
| --- | --- | --- | --- | --- |
| V | 1 failed, 1 passed | 2 passed | Corrected input did not produce the second submission | v-n/artifacts/control-20260905-214344-674; v-p/artifacts/control-20260905-214540-693 |
| A | 6 failed, 3 passed | 9 passed | Four overtaken authentication cases restored the older identity; two disposal cases started extra queries | a-n/artifacts/control-20260905-214558-474; a-p/artifacts/control-20260905-214615-918 |
| D | 2 failed, 1 passed | 3 passed | Retry reached dispatch with only one form write where at least two were required; failed form cleanup had already removed the pending request | d-n/artifacts/control-20260905-214635-642; d-p/artifacts/control-20260905-214654-548 |

Each directory contains control.json, native results.json/TRX, and build/test logs. These controls
prove the fixtures discriminate the intended defects; they are not model-evaluation results.

Before scoring, confirm candidate recipe links exist in each historical tree and the shared quick
runner works. Baseline guidance predates the new engineering guidance checks: use focused development
checks in this implementation exercise, not full PR/merge profiles. Exercise adapter/hook compatibility
separately using engineering tests and native host smoke checks. Record native hook trust/enabled
state and any host-injected personal guidance identically within pairs.

## Client execution

Installed help checked: Codex CLI 0.153.4 and Copilot CLI 1.0.83. Recheck help if versions change.
Choose an explicit available model and reasoning setting per client and keep them fixed within
pairs. Alternate condition order. Use fresh sessions, serial execution, and the same 30-minute
limit per pair. Preserve native exit/timeout status and events. Never replace an initial result
with an automatic retry; one optional correction turn is scored separately.

Set $evaluationWorkdir, $evaluationOutput, $evaluationPrompt, and $evaluationModel to the manifest
directory, a separate private output directory, frozen prompt text, and recorded model. Create
the output directory first. These PowerShell invocations preserve native event output:

    $evaluationPrompt | codex exec --cd $evaluationWorkdir --model $evaluationModel --sandbox workspace-write --config 'approval_policy="never"' --ephemeral --json --color never --output-last-message (Join-Path $evaluationOutput 'final.md') - 1> (Join-Path $evaluationOutput 'events.jsonl') 2> (Join-Path $evaluationOutput 'stderr.log')
    $evaluationExitCode = $LASTEXITCODE

    copilot -C $evaluationWorkdir --model $evaluationModel --effort high --prompt $evaluationPrompt --allow-tool 'write,shell' --deny-tool 'shell(git push),shell(gh:*)' --disable-builtin-mcps --no-ask-user --no-auto-update --no-remote-export --output-format json --no-color --log-dir (Join-Path $evaluationOutput 'client-logs') --usage-output-file (Join-Path $evaluationOutput 'usage.json') 1> (Join-Path $evaluationOutput 'events.jsonl') 2> (Join-Path $evaluationOutput 'stderr.log')
    $evaluationExitCode = $LASTEXITCODE

The controller imposes the wall-clock limit, preserves the patch before grading, and cleans only
that run's resources. A working directory is not a security sandbox. Keep evaluator files, other
outputs, retrospective, and this document outside it. After completion, copy that case's _acceptance
files into Nova.Unit.Tests/Evaluation, build, and run the exact manifest classes plus relevant
existing regressions. Agent-authored tests do not replace evaluator assertions.

Options were checked against installed help and the official
[Codex noninteractive documentation](https://learn.chatgpt.com/docs/non-interactive-mode) and
[Copilot programmatic reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-programmatic-reference).

## Results and decisions

| Case/client/condition | Acceptance passed/total | New neighboring failures | New coverage and sibling disposition | Correction turns / repeated family | Time / usage | Discovery evidence / limits |
| --- | --- | --- | --- | --- | --- | --- |
| Pending all 12 runs | Unrun | Unrun | Unrun | Unrun | Unrun | Unrun |

Discovery evidence means actual file reads, scope explanation, skill invocation, hook output, or
named sibling inspection tied to a decision. Self-report does not prove discovery; absent read
events do not prove omission of automatically injected instructions. Record client, model/settings,
input hashes, host versions, and validation infrastructure separately from behavioral scores.

Judge behavior before condition labels where practical. Count missed invariants and repeated
families, not comment volume or added prose. Separate infrastructure failures and inconsistent
unchanged-input runs; retain every outcome. If both conditions pass, record no measured benefit.
Record which rules changed useful decisions and which added only work before revising guidance.

Final recap: definitions, fixtures, and all six discriminating controls are complete; final guidance
freeze and model runs remain pending.
This exercise publishes nothing and creates no repository rollout or CI requirement.
