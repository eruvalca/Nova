# PR 244 guidance evaluation

This small diagnostic compares baseline and revised guidance on three real Nova defect families.
Twelve initial repairs (three tasks × two clients × two guidance conditions) can expose useful
differences and regressions; they cannot establish a reliable reduction in future PR findings.

## Handoff and current state

Prepared on 2026-09-05. Root completed all six serial control builds and test runs; native model
evaluation is now running. Root grants one serial .NET grading lane while the clients implement
without running builds/tests. Raw fixtures/output are ignored under
artifacts/evaluation; this document retains the procedure and final compact results.

- [x] Select historical starting code and focused unit acceptance source.
- [x] Prepare fresh repositories without original Git history or evaluator acceptance tests.
- [x] Keep application/tests and shared tooling byte-identical across each task's four agent runs.
- [x] Freeze candidate guidance and shared tooling for the paired runs (p3 snapshot below).
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

Actual runs use p3, frozen at 2026-09-05T21:54:55.977Z. All acceptance hashes match the proven p2
controls. Task-relevant reference audit found all 11 entry documents and 25 direct relative Markdown
links per candidate tree; the shared scope resolver succeeded. Frozen SHA256 identities:

- Candidate guidance: b9893a0a8c4b0ea0123947d78c72967208b8c8ed84deda2f8315d0b70fd692cb
- Baseline guidance: e357c9d988127fb4913ed35c80e55c09c2b568d165f564923c4ef64de6e5af86
- Shared tooling: 0d895c2c3becba096c65b94d9d7217ec4d7f678800e845deeedfa303c9cf9e67

Later repository/tooling edits do not alter these frozen comparisons. The p3 prompt explicitly
reserves build/test execution for the controller, prohibits other agents and outside-workdir reads,
and asks for implementation plus regression tests followed by a handoff. Both clients receive the
same restriction. PATH shims block ordinary build-tool invocations; Copilot also denies those tools
through native permissions. This measures first-pass implementation quality with execution withheld,
not the effectiveness of a complete local red/green development loop or full verification gate.

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
Root requested each client's configured default model, with no model/effort override. Codex's
configured default is gpt-6-astra/ultra; Copilot native events resolve to claude-opus-4.8. Record
native model/usage events where available; configured settings alone do not reveal hidden server
selection. Alternate condition order. Use fresh sessions and the same 30-minute
limit per pair. Preserve native exit/timeout status and events. Never replace an initial result
with an automatic retry; one optional correction turn is scored separately.

The actual controller runs one sequential lane per client; the two clients may overlap while doing
implementation-only work. The same concurrency and restrictions apply to both conditions:

    node artifacts/evaluation/run-sequence.mjs p3 codex
    node artifacts/evaluation/run-sequence.mjs p3 copilot

run-model.mjs preserves exact native arguments, prompt, timings, exit status, events, and initial
tracked/untracked changes under p3/_results. Codex uses workspace-write, never approvals, ephemeral
JSONL, and disables the host node_repl MCP. Copilot permits local read/write/shell work, disables
built-in MCPs and remote exports, and explicitly denies publication and build-tool commands.
These permissions and private-test separation limit what this comparison establishes.

The controller imposes the wall-clock limit, preserves the patch before grading, and cleans only
that run's resources. A working directory is not a security sandbox. Keep evaluator files, other
outputs, retrospective, and this document outside it. After completion, copy that case's _acceptance
files into Nova.Unit.Tests/Evaluation, build, and run the exact manifest classes plus relevant
existing and agent-authored classes. Agent-authored tests do not replace evaluator assertions.
Root grants the .NET lane before invoking:

    pwsh -File artifacts/evaluation/grade-model.ps1 -Preparation p3 -RunName v-x-b

Do not change a model output to make grading pass. The grader checks the expected acceptance count
(V: 2, A: 9, D: 3), neighbors, skips, and infrastructure errors. Preserve every initial result.

Options were checked against installed help and the official
[Codex noninteractive documentation](https://learn.chatgpt.com/docs/non-interactive-mode) and
[Copilot programmatic reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-programmatic-reference).

## Results and decisions

| Case/client/condition | Acceptance passed/total | New neighboring failures | New coverage and sibling disposition | Correction turns / repeated family | Time / usage | Discovery evidence / limits |
| --- | --- | --- | --- | --- | --- | --- |
| V / Codex / baseline | 2/2 | One newly authored disposal test fails; existing neighbors pass | Creation repaired, metadata inspected and retained; three test classes extended/added | 0 / none yet | 527 seconds; usage in native events | Explicit scoped-instruction/skill reads; full context injection not observable |
| Remaining 11 runs | Pending | Pending | Pending | 0 | Pending | Native events retained |

The first grading wrapper concatenated a scalar neighbor-class name and discovered only the two
private tests. It refused completion because neighbors were absent. The original grading artifacts
remain; a separate supplemental class run on unchanged model source executed 127 tests (126 passed,
one new disposal test failed). That run also repeated the already-passing private cases because of
a broad suffix filter. The wrapper's array handling was corrected; this harness defect is separate
from the model-authored test failure. See p3/_results/v-x-b/grading and grading-neighbors.

Discovery evidence means actual file reads, scope explanation, skill invocation, hook output, or
named sibling inspection tied to a decision. Self-report does not prove discovery; absent read
events do not prove omission of automatically injected instructions. Record client, model/settings,
input hashes, host versions, and validation infrastructure separately from behavioral scores.

Judge behavior before condition labels where practical. Count missed invariants and repeated
families, not comment volume or added prose. Separate infrastructure failures and inconsistent
unchanged-input runs; retain every outcome. If both conditions pass, record no measured benefit.
Record which rules changed useful decisions and which added only work before revising guidance.

Final recap: definitions, frozen fixtures, and all six discriminating controls are complete. Paired
model execution and independent grading are in progress; no overall benefit conclusion is drawn yet.
This exercise publishes nothing and creates no repository rollout or CI requirement.
