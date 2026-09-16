# Browser test policy validation

## Scope and revision

Reviewed revision: `8e89f7d3d6889f7a9779cd0efe96a1c2c1a2123e` plus the working-tree documentation
changes listed below, including the instruction-hygiene review corrections and this validation
record. No application, test, dependency, build, runtime configuration, or generated-asset changes
are included.

- [AGENTS.md](../AGENTS.md#pull-request-test-gate): authoritative browser selection, applicability,
  evidence reuse, and failure handling.
- [PR template](../.github/pull_request_template.md): separate current-stage and final browser gates.
- [Testing recipe](../.agents/skills/nova-testing/SKILL.md#run-tests) and
  [browser reference](../.agents/skills/nova-testing/references/browser-suite.md#run-commands): route
  to that policy without maintaining separate gate definitions.

## Rationale and enforcement review

The user approved reducing redundant browser runs while retaining the suite's regression coverage.
Focused changes can open a PR after relevant browser scenarios pass, with the full suite explicitly
pending. Full-suite evidence remains required before merging application or browser-execution
changes; broad or uncertain impact requires it earlier. Reusing a pass requires a recorded input
comparison, including incoming changes after a rebase or merge. This reduces duplicate executions
and moves some broad regression detection later in review for focused changes. It does not reduce
the browser cases required by the final gate.

Unit and integration gates, machine-wide Aspire serialization, fixed application inputs during a
browser run, and local-only integration/browser execution remain in place. No smoke suite, test
exclusions, retry changes, CI changes, or harness changes were introduced.

Focused self-review of the complete diff checked the following cases:

| Case | Policy outcome |
| --- | --- |
| Focused application change ready for review | Relevant named browser scenarios pass; full suite can remain pending at opening; final checkbox stays unchecked. |
| Shared authentication, recovery, navigation/layout, rendering, or HTTP-contract impact that is broad or uncertain | Full suite required at opening and affected intermediate pushes. Backend changes are included in selection. |
| Targeted pass before merge | Insufficient for an applicable final browser gate. |
| Earlier full pass followed only by prose edits | Reusable with tested revision and input comparison recorded. |
| Application/browser-test source, dependencies, configuration, or generated assets change after a pass | Previous full pass cannot fulfill the final gate; obtain a new full pass. |
| Rebase or merge introduces changed execution inputs | Assess incoming changes and obtain new evidence, even if the PR's own patch is unchanged. |
| Documentation-only change | Browser N/A requires rationale; changes to guidance or policy still need appropriate validation and coverage/enforcement review. |
| Unrelated unit-test source change | Browser N/A is available if application and browser-suite inputs are unchanged; unit/integration gates still apply. |
| Shared integration fixture changes | Browser evidence is required: `BrowserSuiteFixture` uses `Nova.Integration.Tests.Data.NovaAppHostFixture`. Folder names do not determine applicability. |
| Browser discovery or execution changes | Require browser evidence; documentation-file extensions cannot supply an automatic exemption. |
| Failed run followed by a green rerun | Failure disposition remains required; the green rerun alone does not establish a cause or resolution. |
| Pending or infrastructure-blocked required check | Cannot be represented as passing or satisfy the merge gate. |

Initial review removed the recipe/reference statements that conflicted with targeted evidence at
opening. The follow-up correctness and instruction-hygiene review found and fixed these issues in
that working-tree draft:

| Finding | Evidence and disposition |
| --- | --- |
| Medium, verified: incomplete N/A boundary | The draft's `AGENTS.md:51–55` required evidence for application/browser changes but allowed N/A only for “Documentation-only changes.” An unrelated unit-test edit fit neither branch, while the template demanded full evidence or N/A. Fixed by defining applicability from application/browser inputs, including shared helpers. The two new cases above check both sides of that boundary. |
| Low, verified: repeated policy | The draft's `AGENTS.md:49,63` repeated the final full-suite requirement, and its failure paragraph repeated the existing validation/review rules. “Do not assume a generic smoke suite exists” added a discoverable inventory fact. Removed repetition, kept the specific green-rerun rule beside failure dispositions, and shortened recipe/template pointers. Reverted the unnecessary testing-instructions edit; its existing link already routes to the authority. |

Applied the [Microsoft instruction-hygiene article](https://devblogs.microsoft.com/dotnet/instructions-hygiene-what-frontier-models-still-need-you-to-say/)
as follows: keep Nova's local gate and shared-machine constraints in `AGENTS.md`; remove repeated
rules; keep runner mechanics in the existing recipe and review history in this record; verify CI and
the shared fixture dependency against their definitions. The common definition of done belongs at
repository scope. No model-specific rules, new instruction copies, or procedural scaffolding were
added.

No unresolved finding remains in this diff. This is a self-review of policy and documentation,
not an independent application review or an empirical comparison of agent performance.

## Guidance read

- [AGENTS.md](../AGENTS.md), the [PR template](../.github/pull_request_template.md), and
  [testing instructions](../.github/instructions/testing.instructions.md).
- [nova-testing](../.agents/skills/nova-testing/SKILL.md), its
  [browser reference](../.agents/skills/nova-testing/references/browser-suite.md), and
  [transition evidence guide](../.agents/skills/nova-testing/references/transition-evidence.md).
- Local `skill-creator/SKILL.md` at
  `C:/Users/eruva/.codex/skills/.system/skill-creator/SKILL.md` for the recipe edit and validator.
- Local `code-review/SKILL.md` and its local-review, review-doctrine, and review-checklist references
  at `C:/Users/eruva/.agents/skills/code-review/`, plus the Microsoft article linked above.
- Inspected the instruction `applyTo` declarations: no path-specific rule matches the Markdown
  edits; testing rules were read for the behavior under review.

The testing recipe is shared by Codex and Copilot under `.agents/skills/nova-testing`; there is no
separate `.github/skills/nova-testing` copy to update. Historical validation records retain the policy
and results applicable to their own revisions.

## Checks and limitations

Checks apply to the working-tree revision described above:

| Check | Result |
| --- | --- |
| `git diff --check` | Pass. |
| `python C:/Users/eruva/.codex/skills/.system/skill-creator/scripts/quick_validate.py .agents/skills/nova-testing` | Pass: `Skill is valid!` |
| `pwsh -NoProfile -File scripts/Test-AgentGuidance.ps1` | Pass: four agent families and hook-installer mirrors. This checks existing ecosystem parity, not policy semantics. |
| `rg -n --glob '*.md' --glob '*.toml' --glob '*.json' 'all three suites\|all three before\|before opening\|before merge\|targeted pass\|PR.stage\|local PR gate' AGENTS.md .github .agents .codex` | Reviewed active guidance matches; no conflicting opening/merge requirement remains. |
| `Test-Path .github/skills/nova-testing` | `False`; no mirrored recipe exists. |
| Relative Markdown links and heading anchors in all five changed documents (inline Python check of target existence, heading slugs, and whitespace) | Pass after review edits: 21 local links and anchors; whitespace valid. |
| CI and browser fixture source inspection | Confirmed `.github/workflows/ci.yml` runs unit tests only and `BrowserSuiteFixture` uses the integration project's AppHost fixture. |

Browser execution is N/A for this documentation change: no application or browser-execution inputs
changed. The policy's effect on selection and enforcement was reviewed above. Build, format, unit,
integration, and browser suites were not run for this edit; no commit or PR is being created, and
these checks are not claimed as passes. The existing unit/integration PR-stage requirements still
apply if this change is subsequently opened or merged as a PR. Automated branch protection and a
machine-wide suite lock remain outside this change; the documented gates require contributors to
follow them.
