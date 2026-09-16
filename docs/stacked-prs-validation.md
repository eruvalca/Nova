# Stacked PRs integration validation

Date: 2026-09-15 (America/Chicago).

## Scope and revision

Implemented local repository guidance for experimenting with native stacked PRs.
Base revision: `3486196375e01fc870ce4fba0ac918f3a317773d`; implementation branch:
`codex/stacked-pr-workflow`. Evidence below covers the uncommitted AGENTS routing,
PR template, shared Nova skill, setup guide, runbook, decision note,
and this record. No application, browser-suite, build/runtime, CI, or GitHub
settings changed. No commit, remote branch, PR, or native stack was created.

The [decision note](../plans/stacked-prs-integration.md) supersedes the proposal;
[the runbook](stacked-prs.md) is the active operating guide. The research and
proposal-review sections below describe earlier stages of this same change;
current implementation results and remaining limitations are recorded at the end.
The initial upstream repository copy was subsequently removed at the user's
request; the existing personal installation is now the external prerequisite.

## Guidance actually read

- Root `AGENTS.md`; all scoped instruction headers were inspected for routing.
- `.github/instructions/testing.instructions.md` and
  `.github/instructions/bootstrap-theme.instructions.md` for current validation
  and CI behavior.
- `.agents/skills/add-feature-slice/SKILL.md` and
  `.agents/skills/nova-testing/SKILL.md` for existing workflow organization; no
  feature implementation or test assertion review was performed.
- `docs/agent-setup.md`, `.github/pull_request_template.md`, and
  `.github/workflows/ci.yml`.
- Installed personal `gh-stack/SKILL.md` and all three references:
  `stack-design.md`, `commands.md`, `troubleshooting.md`. Metadata identifies
  upstream tag v0.1.1, skill version 0.1.0.
- For implementation: `.system/skill-creator/SKILL.md`, its validator,
  `scripts/Test-AgentGuidance.ps1`, the installed repository gh-stack skill, and
  current local Codex/Copilot discovery help/schema. Scoped-rule routing was
  rechecked; no application-path instruction applies to these documentation files.
- Microsoft's [instruction-hygiene article](https://devblogs.microsoft.com/dotnet/instructions-hygiene-what-frontier-models-still-need-you-to-say/):
  retain consequential local constraints, scope instructions to the task, link
  detailed references, and verify behavior instead of adding generic advice.

## Evidence

| Inspection | Command or source | Result |
| --- | --- | --- |
| Versions | `gh --version`; `gh extension list`; `gh stack --version`; `gh api repos/github/gh-stack/releases/latest` | CLI 2.96.0; extension 0.1.1; latest release v0.1.1 |
| Remotes/configuration | `git remote -v`; local `git config --get-regexp`; `git worktree list` | One remote, origin; rerere enabled; multiple agent worktrees; main checked out at repo root |
| Repository settings | `gh api repos/eruvalca/Nova` | Public, main default, three merge methods allowed, automatic branch deletion disabled |
| Copilot rule | `gh api repos/eruvalca/Nova/rulesets/21981087` | Default branch target; review on push true; review drafts false |
| Branch protection | `gh api repos/eruvalca/Nova/branches/main/protection` | Build/Unit Tests required; one approval; stale approval dismissal; conversation resolution |
| Native API | `gh api repos/eruvalca/Nova/stacks` | Success, empty array; no end-to-end remote stack yet |
| CLI contracts | Help for stack init/rebase/sync/submit/merge; extension install; skill install | Non-interactive flags, publishing side effects, pinning and shared project skill destination confirmed |
| Versioned implementation | `gh api repos/github/gh-stack/git/ref/tags/v0.1.1`; raw files at that ref | Tag resolves to commit `2bd699a544a09cb5c45a013d03416e0894b0454e` |

Material code inspected:
[merge target resolution](https://github.com/github/gh-stack/blob/v0.1.1/cmd/merge.go#L192),
[trunk resolution](https://github.com/github/gh-stack/blob/v0.1.1/cmd/utils.go#L972),
and the corresponding init, rebase, sync, stack-storage, Git, and GitHub helpers.
Command help and versioned implementation take precedence over abbreviated skill
examples where they omit numeric target ambiguity.

## Isolated local exercises

All fixture repositories, files, branches, and pushes below were confined to a
new temporary directory. The remote was a local bare repository, not GitHub.
No Nova checkout was switched or modified for these exercises.

1. Initialized a `main` seed, cloned a local bare remote, and added a detached
   linked worktree. In the linked worktree ran:
   `gh stack init --base main codex/probe/domain`, committed `domain.txt`, ran
   `gh stack add codex/probe/ui`, and committed `ui.txt`.
2. Ran `gh stack bottom`, committed a domain correction, then
   `gh stack rebase --upstack --no-trunk` and `gh stack top`.
   `git merge-base --is-ancestor codex/probe/domain codex/probe/ui` passed;
   `gh stack view --json` reported both layers with `needsRebase=false`.
3. Advanced remote `main` through a second local clone while the original
   worktree retained its older checked-out main. Ran
   `gh stack rebase --remote origin` in the stack worktree. The CLI warned it
   could not move local main, explicitly selected `origin/main`, and completed.
   Both ancestry assertions passed:
   `git merge-base --is-ancestor origin/main codex/probe/domain` and the
   domain-to-UI check. Local main's SHA remained unchanged.
4. Observed stack metadata under `.git/worktrees/owner/gh-stack`.
   `gh stack view --json` from the original main worktree returned exit 2,
   demonstrating that another worktree does not inherit that navigation state.

The worktree warning was expected and explained by Git's checked-out-branch
protection; fresh remote ancestry was verified. It is not evidence of a failed
rebase or a need to detach another checkout. Old reports of rebasing onto stale
main were checked against the installed version rather than repeated as current
limitations.

## Research sources and interpretation

- [Public preview announcement](https://github.blog/changelog/2026-07-30-stacked-pull-requests-are-now-in-public-preview/)
  and [rollout guidance](https://docs.github.com/en/pull-requests/tutorials/roll-out-stacked-prs):
  current availability supersedes stale private-preview README wording.
- [About stacks](https://docs.github.com/en/pull-requests/get-started/about-stacked-prs),
  [CI](https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/optimizing-ci-for-stacked-pull-requests),
  [reviewing](https://docs.github.com/en/pull-requests/how-tos/review-pull-requests/reviewing-stacked-pull-requests),
  [merging](https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/merging-stacked-pull-requests),
  and [CLI reference](https://docs.github.com/en/pull-requests/reference/stacked-prs-cli-commands).
- [Copilot review configuration](https://docs.github.com/en/copilot/how-tos/copilot-on-github/set-up-copilot/configure-code-review)
  and [cloud-agent limits](https://docs.github.com/en/copilot/concepts/agents/cloud-agent/about-cloud-agent#limitations-of-copilot-cloud-agent).
- [GitHub's agent tutorial](https://docs.github.com/en/copilot/tutorials/stack-ai-generated-code-in-pull-requests):
  specifically demonstrates Copilot CLI. Nova's proposed test-per-layer rule
  follows its own existing gates, not the tutorial's illustrative final test layer.

The recommendation to retain CI filters follows explicit native-stack docs.
Automatic Copilot review on an upper-layer PR remains a pilot verification,
not a claimed observed result. Layer count, worktree ownership, gradual review
readiness, and the minimal repository change set are recommendations.

## Research-stage checks and limitations

- Focused self-review checked the proposal against the existing PR gates and
  the installed CLI contracts. No separate implementation review was needed
  for this inactive research proposal. Future active guidance changes must
  review their effect on coverage and enforcement.
- Application build, format, unit, and integration suites were not run for this
  research; no commit or PR was created. Applicable commit/PR gates remain to
  be completed if these documents are submitted.
- Browser N/A: only proposal and research documentation changed; application,
  runtime, generated assets, and browser-suite inputs are unchanged.
- GitHub submission, automatic reviews, branch protection enforcement on a
  native stack, remote merge, and cleanup have not been exercised. They are
  concrete pilot acceptance checks, not passes.
- Skill installation was inspected through help, not performed. Discovery in
  fresh Codex/Copilot sessions and personal/project skill precedence remain to
  be verified during implementation.
- Final PowerShell checks passed for all 5 local Markdown links, balanced code
  fences, trailing whitespace, and conflict markers in both new documents.
  An initial `git diff --no-index --check -- /dev/null FILE` wrapper incorrectly
  treated the new-file difference exit status as a whitespace failure; no
  whitespace diagnostic was emitted. Direct content checks resolved that
  harness mistake without changing or weakening a repository check.

## Correctness review requested by the user

Reviewed the entire proposal against the same Nova revision, installed gh-stack
skill and all three references, current official stack/CI/merge/Copilot docs,
CLI help, and v0.1.1 implementation. Re-read root guidance and the PR template,
rechecked scoped-rule routing and agent setup, and refreshed the live Copilot
ruleset and main protection. Settings and installed/latest extension versions
remain as recorded above. This is a proposal review in the existing context,
not a separate implementation review or evidence of a remote stack pilot.

The reviewed proposal before correction had SHA-256
`488CA3BEA9DA21AA7E42F7FCD92B62F2A0D05B4983441181EF91E59DB00793B4`.

| Finding | Evidence and consequence | Disposition |
| --- | --- | --- |
| Medium, verified: cleanup could publish unvalidated revisions | The original cleanup paragraph allowed `sync --prune` when surviving layers' publish gates had passed. The installed command reference and [sync implementation](https://github.com/github/gh-stack/blob/v0.1.1/cmd/sync.go) fetch, rebase, then push inside the same call. A newly fetched trunk can change the heads after that validation. | Corrected preparation and cleanup together: active layers use separate rebase, validation, and push; defer combined sync/prune until fresh inspection shows the stack fully merged. The root-to-Nova-recipe routing explicitly overrides the upstream routine-sync recipe. |
| Medium, verified: remote selection did not exclude unpublished local trunk commits | The original final-preparation wording implied `rebase --remote origin` selects the published base. [resolveTrunkTarget](https://github.com/github/gh-stack/blob/v0.1.1/cmd/utils.go#L1026) deliberately selects local main when it is ahead of origin/main. Reproduced with the installed binary: a local-only main commit became an ancestor of the feature branch after rebase. | Added an unpublished-trunk preflight and ownership-aware recovery. `git rev-list --count origin/main..main` returned 1 in the fixture; `git merge-base --is-ancestor LOCAL_ONLY_SHA codex/review-trunk/layer` passed after rebase. No Nova branches were touched. |

Additional precision improvements, without changing the recommendation:

- Submission does detect the PR template. The installed skill's generated-body
  description omits this path. Inspected `runSubmit`, `createPR`, and
  `generatePRBody` in [submit.go](https://github.com/github/gh-stack/blob/v0.1.1/cmd/submit.go)
  plus [FindTemplate](https://github.com/github/gh-stack/blob/v0.1.1/internal/pr/template.go).
  The proposal now distinguishes copying the raw template from filling it in;
  there is no reason to add a separate PR-creation workaround for that behavior.
- The same submission implementation can warn and continue on per-PR failures;
  the proposal now requires complete remote postconditions even on exit zero.
  `view`'s best-effort remote refresh is not sufficient freshness evidence.
- Clarified authentication prerequisites, stepwise PowerShell exit handling,
  reusing an existing owner worktree, cumulative context for cross-layer local
  reviews, and evidence links that resolve on each PR's available revision.

Confirmed without changing policy: native CI inheritance; default-branch review
settings; same-repository linear stacks; non-interactive navigation; draft and
ready semantics; merge-number ambiguity; one shared skill location; per-layer
Nova validation gates; and Copilot cloud's one-PR-per-task limitation. Automatic
Copilot reviews on upper layers and the remote merge lifecycle remain pilot
checks, as before. At this review stage the docs remained proposals and active
guidance was unchanged; the subsequent implementation is recorded below.

Post-correction checks passed: all 5 local Markdown links resolve, code fences
are balanced, and both documents have no trailing whitespace or conflict
markers. Inspected the complete proposal diff against the pre-review snapshot.
The snapshot comparison's exit 1 means documents differ, not a check failure.
Application suites were not rerun for these documentation-only corrections.

## Initial implementation verification

These results cover the initial uncommitted implementation on the base revision
named above, before the requested removal of the upstream copy. Later edits
completed this record and clarified fresh stack-detail
inspection and push-versus-submit behavior in the runbook. Application and
browser-suite inputs remain identical to the base; no application-test pass is
claimed or reused.

| Check | Command or method | Result at that stage |
| --- | --- | --- |
| Shared installation | `gh skill install github/gh-stack gh-stack --pin v0.1.1 --scope project --agent codex` | Installed the four upstream files in `.agents/skills/gh-stack`; Nova recipe added beside it. |
| Upstream integrity | Read each `skills/gh-stack` file through `gh api repos/github/gh-stack/contents/PATH?ref=2bd699a544a09cb5c45a013d03416e0894b0454e`; compare normalized content | All three references and the skill body match. Installer frontmatter and its boundary blank line are the documented differences. MIT license copied from the same commit; provenance recorded. |
| Skill format | `python -X utf8 C:/Users/eruva/.codex/skills/.system/skill-creator/scripts/quick_validate.py .agents/skills/gh-stack`, then the same command for `.agents/skills/nova-stacked-prs` | Both valid. |
| Existing ecosystem parity | `pwsh -NoProfile -File scripts/Test-AgentGuidance.ps1 -SelfTest` | Four agent families and hook mirrors pass; all 17 drift/format/missing/provider fixtures pass. This is regression evidence, not a new-skill discovery test. |
| Copilot discovery | `copilot -C CWD skill list --json` and `copilot -C CWD instruction list --json`, for `D:/repos/Nova` and `D:/repos/Nova/Nova` | CLI 1.0.84-5 finds both shared skills enabled (project/inherited respectively) and root AGENTS.md enabled. |
| Codex discovery | CLI 0.154.0-alpha.6.2: fresh `codex app-server --stdio`; initialize, then `skills/list` with both cwd paths and `forceReload: true` | Both repository skills enabled, no discovery errors. Personal `C:/Users/eruva/.codex/skills/gh-stack/SKILL.md` also listed; no precedence claim or personal configuration change. Read-only discovery used the locally generated protocol schema. |
| Independent behavioral exercise | Fresh agent context read the new sources and handled a hypothetical merged bottom layer, active API/UI PRs, API review defect, and another task's unpublished main commit | Followed local/remote reconciliation, ownership isolation, owner-layer fix, propagation, per-layer validation, post-publication checks, whole-stack reviews, and deferred combined cleanup. No Git/remote mutations or application tests; no blocking policy contradiction found. |
| Markdown and patch integrity | Python content checks over all modified/untracked Markdown, followed by `git diff --check` | Local links/anchors resolve; code fences balanced; no trailing whitespace or conflict markers. Tracked patch check passes. |

The independent evaluator actually read AGENTS.md, the Nova and upstream stack
skills, upstream command/troubleshooting references, runbook, validation record,
and PR template; also API/testing scoped instructions, API/Nova-testing recipes,
the installed test-command skill, and local stack command help for its hypothetical
API work. Its result is a read-only decision exercise, not evidence that a real
feature's validation or remote stack lifecycle passed. It confirmed that surviving
ancestry must determine recovery commands and that combined cleanup stays deferred
while any layer is active. The runbook now also states when submit is needed to
update PR grouping after a partial merge.

Coverage/enforcement review: root gates and the template checklist are preserved.
The recipe explicitly applies them per layer, including drafts, keeps cumulative
context for local reviews, and blocks the upstream sync shortcut from publishing
unvalidated heads. No CI filter, reviewer requirement, hook, or quality check was
disabled. Root additions are selection/routing; detailed operations are linked.

Material check dispositions:

- The existing setup guide's `copilot plugins list --kind ...` command failed with
  `unexpected argument '--kind'`. Installed CLI help identifies `skill list --json`
  and `instruction list --json`; both replacement commands passed from both
  directories, and the guide now uses them.
- The first upstream-body comparison reported a one-character mismatch. Inspection
  showed only the installer removing the blank line after YAML frontmatter. The
  comparison now normalizes that exact boundary and line endings; all remaining
  body/reference content matches. No upstream instructions were edited.

## Current verification after upstream-copy removal

At the user's request, removed all vendored files under `.agents/skills/gh-stack`,
including references, license, and packaging provenance. Updated routing, the Nova
recipe, setup guide, runbook, and decision note to use the user-installed upstream
skill. User-scope installation examples for Codex and Copilot were checked against
`gh skill install --help`; no personal installation was added, updated, or removed.

- Repeated the fresh Codex `skills/list` and Copilot `skill list --json` discovery
  calls above from the root and `Nova/`. Both find the repository Nova recipe and
  their existing user-level gh-stack skill. Codex uses
  `C:/Users/eruva/.codex/skills/gh-stack/SKILL.md`; Copilot uses
  `C:/Users/eruva/.copilot/skills/gh-stack/SKILL.md`. Codex reported no discovery
  errors. Copilot's personal copy also identifies the reviewed v0.1.1 tag.
- The existing Codex skill's SHA-256 before and after removal is unchanged:
  `F90EEC41187457B44640F3D85D2B6069DC702C898B923C79759E7858597D62F7`.
- `quick_validate.py .agents/skills/nova-stacked-prs` passes. Rechecked local
  Markdown links/anchors, fences, whitespace, conflict markers, and
  `git diff --check`; no active links reference the removed copy. Earlier
  installation/discovery evidence above is historical, not the current layout.
- The operational rules are unchanged; the earlier behavioral exercise remains
  relevant to those rules. This follow-up changed dependency discovery/setup only.

## Remaining acceptance work

- Remote pilot: native membership, upper-layer CI, automatic Copilot review on
  readiness and a subsequent push, a lower-layer correction, and authorized
  merge/cleanup. See [the experiment](stacked-prs.md#first-experiment). Repository
  API access and local fixtures do not establish these results.
- Fresh CLI inventories establish discovery. The independent exercise establishes
  source reading and decision behavior in its context; it does not prove an actual
  fresh Copilot model session's selection or either provider's remote execution.
  Follow the setup guide when starting the first experiment. No trust/global
  configuration was granted or changed for verification.
- Build, format, unit, and integration checks were not run for these uncommitted
  guidance-only changes. The existing pre-commit and PR-stage requirements remain
  due if this change is committed or submitted. Browser **N/A**: no application,
  browser-suite, dependency, build/runtime, discovery, or generated-asset inputs
  changed; guidance validation and enforcement review are recorded above.
