# Stacked PRs in Nova

Use a stack when an issue has dependent changes that are easier to review as
separate, working increments. Keep one coherent change in one PR and unrelated
changes in separate PRs. Start with two or three layers for the first experiment.
Native stacks are linear and stay within one repository.

[AGENTS.md](../AGENTS.md#completion-and-review) owns repository policy;
[nova-stacked-prs](../.agents/skills/nova-stacked-prs/SKILL.md) is the agent recipe.
The user-installed upstream `gh-stack` skill supplies command mechanics. This
page supplies machine setup, examples, and operational details.
Research and verification are in [the validation record](stacked-prs-validation.md).

## Machine setup

Verify Git, GitHub CLI, authentication, and repository push access. The tested
installation was GitHub CLI 2.96.0 with gh-stack 0.1.1. On a fresh machine:

```powershell
gh auth status --hostname github.com
# If needed, authenticate with gh auth login before continuing.
gh extension install github/gh-stack --pin v0.1.1
gh stack --version
```

If the extension already exists, inspect its version instead of reinstalling it.
The repository contains the shared Nova recipe only. Reuse an existing personal
`gh-stack` skill after checking its source and version. If the active agent cannot
discover it, install at **user scope**, using the matching command:

```powershell
# Codex (skip when already installed for this user):
gh skill install github/gh-stack gh-stack --pin v0.1.1 --scope user --agent codex
# Copilot (only if it does not already discover a user installation):
gh skill install github/gh-stack gh-stack --pin v0.1.1 --scope user --agent github-copilot
```

These are per-agent alternatives; install only for the agents that need it.
Do not overwrite an existing skill or add a project copy. A Codex personal copy
does not guarantee Copilot discovery. Start a fresh session and follow
[agent discovery checks](agent-setup.md#verify-a-fresh-session) to confirm the
Nova recipe and upstream skill are both available and their actual sources are read.

Run examples **one step at a time**, replacing `N`, branch names, and uppercase
placeholders with observed values. Stop on failed commands. In PowerShell
automation check `$LASTEXITCODE` after each native command; subsequent lines do
not automatically stop after a Git or gh failure.

## Start and navigate

Inspect status, existing worktrees, remotes, and `gh stack view --json` first
(exit 2 means this checkout has no stack). Reuse an appropriate existing task
worktree or create a dedicated owner from the repository root:

```powershell
git fetch origin
git worktree add -b codex/issue-N/domain ..\Nova-issue-N origin/main
Set-Location ..\Nova-issue-N
git config --local rerere.enabled true
git config --local remote.pushDefault origin
gh stack init --base main codex/issue-N/domain
gh stack view --json
```

The new branch must not already exist and the destination must be available.
`init` adopts this branch. Creating a missing branch through `init` instead
would use local `main`, which may be stale. Repository-local Git configuration
is normally shared across linked worktrees. One owner performs all stack mutations
in one worktree; separate independent stacks can use separate worktrees.

Choose boundaries by behavior, with tests in the layer they prove. A domain/API/UI
split is useful only if each intermediate checkout works. Keep a breaking
contract and its consumers together when separating them would break a layer.
Record the plan in the issue/change's single validation record before coding.

After implementing, validating, and committing the bottom layer, create the next:

```powershell
gh stack add codex/issue-N/api
```

`add` requires the current top branch. Prefer deliberate staging and commits over
automatic add-all shortcuts. Navigate with explicit `gh stack checkout BRANCH`,
`up`, `down`, `top`, and `bottom`; `switch` and normal `modify` are interactive.
Inspect `git branch --show-current` after navigation. Preserve unrelated work.

## Publish and start reviews

1. Satisfy [the opening gate](../AGENTS.md#pull-request-test-gate) for **every
   layer**, including drafts. Use each layer's complete checkout. A passing top
   does not prove the layers below it. Required local reviews get the layer
   diffs plus cumulative context for behavior spanning layers.
2. Prepare each body from [the PR template](../.github/pull_request_template.md).
   Describe its purpose, parent, issue work remaining, and link its section of
   the single validation record. Ensure the linked revision contains the evidence;
   a file existing only in a higher layer will not resolve on a lower branch.
3. Submit all implemented layers, avoiding empty placeholder branches:

   ```powershell
   gh stack submit --auto --remote origin
   ```

4. Inspect **fresh remote** results for every expected layer. Compare PR bases,
   head SHAs, and native stack membership with the intended local chain:

   ```powershell
   gh api repos/eruvalca/Nova/stacks --paginate
   gh api repos/eruvalca/Nova/stacks/STACK_NUMBER
   gh pr view PR_NUMBER --json number,url,baseRefName,headRefName,headRefOid,isDraft,state,reviewDecision,statusCheckRollup
   ```

   `view --json` is useful for local navigation but its remote refresh is
   best-effort. Submission can partially succeed and still exit zero after
   warning about a failed PR operation. Inspect before retrying.
5. v0.1.1 copies the detected repository template verbatim into new draft PRs;
   it does not complete it. Verify template detection, apply each prepared body
   and title, and read the result before marking that PR ready:

   ```powershell
   gh pr edit PR_NUMBER --title TITLE --body-file BODY_FILE
   gh pr view PR_NUMBER --json title,body
   gh pr ready PR_NUMBER
   ```

   `submit --auto --open` makes new **and existing** PRs ready. Use it only when
   every affected PR is ready. Starting reviews on stable lower layers first can
   reduce repeated upper-layer reviews.

For partial issue delivery use `Refs #N`; reserve closing language for delivery
of all acceptance criteria to `main`, and verify the actual issue state afterward.

Nova's inspected Copilot rule reviews new pushes and excludes drafts. Monitor
**every active PR** for checks, review bodies, inline threads, issue comments, and
suppressed findings; apply the existing root triage rules. Paginate API reads
when collecting these. `gh pr checks` does not wait for a Copilot review, and
silence is not evidence that it ran. Keep expected but missing reviews pending.

## Correct a layer and update the stack

Fix the owning layer, even when the finding appeared on an upper PR:

```powershell
gh stack checkout codex/issue-N/domain
# Edit, deliberately stage, validate, and commit the correction.
gh stack rebase --upstack --no-trunk --remote origin
gh stack view --json
# Visit and validate every affected layer; update its evidence before publishing.
gh stack push --remote origin
```

Batch known fixes from lower layers upward. Recheck each changed PR's head,
checks, approval freshness, and findings afterward. Keep per-layer revisions and
dispositions in the single record. `--no-trunk` propagates the local correction;
final integration still needs current trunk. Existing suite serialization and
browser-evidence reuse rules apply after every rebase.

For trunk-inclusive preparation:

```powershell
git fetch origin
git log origin/main..main
# Proceed only after resolving any unpublished local main commits.
gh stack rebase --remote origin
# Validate the resulting layer checkouts and record evidence, then publish.
gh stack push --remote origin
```

In v0.1.1, an ahead-of-remote local `main` is deliberately selected as the trunk,
including unpublished commits. Resolve their ownership and intended disposition
or use a clean clone; do not reset another agent's branch. If local main is merely
behind and checked out elsewhere, v0.1.1 can use fetched `origin/main` without
moving that checkout. Inspect ancestry after warnings instead of forcing a detach.

**Do not use routine `gh stack sync` for active Nova layers.** It fetches,
reconciles, rebases, and pushes without a validation pause. Evidence for the
pre-call heads does not cover the new heads it might publish.

## Merge, cleanup, and recovery

Stacking does not confer merge authority. When merging is authorized, satisfy
the root merge gate on each layer in the intended bottom-up set and use native
stack merging. Select a method explicitly; Nova currently permits all three.
For a whole-stack merge, with an observed and verified stack number:

```powershell
gh stack merge STACK_NUMBER --yes --squash
```

Use the chosen `--merge` or `--rebase` instead if appropriate. In v0.1.1 a numeric
target resolves as a **stack number first**, then a PR number. For a partial merge,
verify that namespace and the exact prefix before executing; a PR target includes
all lower unmerged PRs. `--yes` suppresses a prompt, not requirements. Native
stacks currently do not support ordinary auto-merge.

Verify remote completion and remaining membership. GitHub may retarget/rebase
surviving PRs; inspect and reconcile local state before rebase, validation, and
publication. `push` updates branch heads only; if PR bases or native membership
need updating, use `submit --auto --remote origin` after validation and verify
those remote results. Defer `sync --prune --remote origin` until **both local and fresh remote
state agree every tracked layer is merged**, with no new or unsubmitted layers.
It is not a prune-only command. If cleanup cannot switch to a main checked out
elsewhere, defer the affected deletion. Remote branches are currently retained
by Nova; clean them only after checking dependencies. Start a new stack for new work.

| Situation | Action |
| --- | --- |
| Rebase conflict | Resolve and stage, then `gh stack rebase --continue`; `--abort` restores the stack. |
| Sync conflict | Sync restores pre-rebase state; use separate rebase to reproduce and resolve. |
| Sync says `Sync aborted` | It can exit zero on divergence; inspect both chains, not just the exit code. |
| Push/submit partly succeeds | Inspect each remote branch/PR before retrying; publication is not atomic. |
| Local/remote grouping differs | Read upstream troubleshooting and preserve work before adopting a chain. `unstack --local` removes tracking only. |
| Reorder/remove a layer | Read upstream troubleshooting; metadata changes alone do not move commits. Plan the ancestry changes. |
| Handoff | Give the owner worktree, branches in order, trunk, stack/PR IDs, heads, record, unresolved findings, and pending gates. |

For a remote handoff, `gh stack checkout PR_URL` accepts an observed PR URL.
Local stack metadata belongs to the owner worktree's Git directory; another
worktree does not automatically have it. Set the intended remote configuration,
inspect fresh membership after checkout, and hand off the whole stack.

## First experiment

Use a representative feature with two or three valid layers and one local Codex
or Copilot CLI coordinator. GitHub's [cloud-agent limitations](https://docs.github.com/en/copilot/concepts/agents/cloud-agent/about-cloud-agent#limitations-of-copilot-cloud-agent)
still specify one branch and one PR per task; that entry point needs a separate
coordination design. A starter request for either local agent:

> Implement issue #N using the Nova stacked-PR recipe. Choose one PR or a short
> stack based on the dependency boundaries. Follow the repository gates, publish
> the PRs, and monitor all of them for Copilot and human findings. Report the
> stack, PR links, validation record, and pending work.

Record the pilot's actual results for native membership, upper-layer CI, Copilot
review on readiness **and a later push**, a lower-layer fix propagated upward,
and an authorized merge/cleanup. These remote behaviors have not yet been
exercised in Nova. Compare review clarity against validation time and repeated
review rounds before broadening use.

Keep the current main-only [CI filters](../.github/workflows/ci.yml): GitHub's
[native-stack CI documentation](https://docs.github.com/en/pull-requests/how-tos/merge-and-close-pull-requests/optimizing-ci-for-stacked-pull-requests)
says layers trigger workflows as if targeting the trunk. Ordinary chained PRs
are insufficient. No new review bot, hook, or workflow is needed for the pilot.

## Versions and upgrades

The reviewed upstream baseline is [v0.1.1](https://github.com/github/gh-stack/tree/v0.1.1/skills/gh-stack),
commit `2bd699a544a09cb5c45a013d03416e0894b0454e` (skill metadata version 0.1.0).
The extension and upstream skill are user-managed dependencies; Nova vendors
neither the upstream skill nor its license/provenance files. Check installed
versions and skill tracking metadata rather than assuming they match this baseline.

Review extension and skill upgrades together. Compare upstream commands/references
and recheck the Nova constraints
for submit/template behavior, local trunk selection, sync side effects, merge
target resolution, and worktree ownership. Update the reviewed baseline and
repository policy in the Nova recipe/runbook when needed. Repeat local exercises
and relevant pilot checks when behavior changes. Do not overwrite personal skills
or install an unpinned replacement as a routine setup step.
