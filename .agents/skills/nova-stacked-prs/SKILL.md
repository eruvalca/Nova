---
name: nova-stacked-prs
description: >-
  Plan and operate stacked PRs in Nova with its validation gates and Copilot review
  loop. Use when splitting an issue into dependent PRs, creating or navigating a
  stack, handling reviews or rebases, publishing, merging, recovering, or handing
  off stacked work; also use when a stack is already checked out or gh-stack is
  invoked directly.
---
# Nova stacked PRs

Read the user-installed `gh-stack` skill for mechanics and its references when
their triggers apply. Confirm its source path and version in the active agent;
it is an external prerequisite, not vendored in Nova. If unavailable, use the
[setup instructions](../../../docs/stacked-prs.md#machine-setup) before operating
a stack. The rules here take precedence over its
routine sync/push examples and illustrative tests-last layering. Use
[the runbook](../../../docs/stacked-prs.md) for setup, publication examples,
version-specific hazards, recovery, and the pilot acceptance checks. Existing
[AGENTS.md gates](../../../AGENTS.md#pull-request-test-gate) remain authoritative.

## Plan and own the stack

- Prefer one PR for one coherent change. Use a short linear stack (usually 2–4
  layers) when dependencies have useful, independently valid review boundaries.
  Keep tests with the behavior they prove. Keep incompatible producers and
  consumers together; do not add compatibility scaffolding just to create layers.
- Before coding, record the ordered layers and issue acceptance criteria in the
  change's single validation record. Use a local Codex or Copilot CLI coordinator;
  Copilot cloud's one-PR-per-task workflow cannot own a whole stack.
- Give one owner one worktree for stack mutations. Inspect status, worktrees,
  remotes, and existing local/remote stack state before creating or adopting one.
  Reuse an appropriate task worktree. Do not spread its branches across concurrent
  worker checkouts while rebasing. Use `codex/` branch names by default.
- Create the first branch from successfully fetched `origin/main`, then adopt it
  with `init --base main`; a missing branch passed to init starts from local main.
  Before any trunk-inclusive rebase, fetch and inspect `git log origin/main..main`:
  v0.1.1 includes unpublished local main commits. Resolve their ownership first or
  use a clean clone; do not reset or detach another agent's checkout.
- Use explicit non-interactive commands. Confirm checkout after navigation and
  inspect state after errors; PowerShell scripts must check native exit codes.

## Validate and publish

- Apply opening, intermediate-push, and merge gates to **each layer's checkout**.
  A passing top layer does not prove lower layers. Preserve the root gate's suite
  serialization, browser applicability, evidence reuse, and separate-review rules;
  provide cumulative context where invariants span layers. Drafts still need the
  opening gate. Avoid empty placeholder branches: submit processes all layers.
- Keep one validation record with per-layer parent/head revisions, results,
  review dispositions, and evidence-reuse comparisons. Each PR links its section
  at a revision where that evidence exists; upper-only files cannot prove a lower
  PR. Preserve lower sections as upper layers extend the record.
- Use **rebase, then validate, then push/submit**. Do not use `sync` to publish
  active layers: it can change heads and push them without a validation pause.
  Read the runbook's cleanup rules before pruning, especially after partial merges.
- Prepare completed [PR-template](../../../.github/pull_request_template.md) bodies
  before `submit --auto --remote origin`. v0.1.1 copies the detected template
  verbatim; fill each PR with `gh pr edit --body-file` and verify its body before
  `gh pr ready`. `submit --open` makes existing PRs ready too. Use `Refs #N` for
  partial delivery and reserve closure for all acceptance criteria reaching main.
- Verify every intended PR's base, head, and native stack membership with fresh
  GitHub reads after publishing, even on exit zero. Submit/push can partially
  succeed; `view --json` may show cached PR state. Nova's main-targeted CI relies
  on **native** stack membership, not merely chained PR bases.

## Review, land, and hand off

- Monitor every active PR's checks, review bodies, inline threads, comments, and
  suppressed findings under the root triage rules. `gh pr checks` does not wait
  for Copilot reviews; no comments does not mean a review completed. Verify the
  expected review actually ran. Draft reviews are disabled in Nova's current rule.
- Fix the owning layer, then propagate upward with
  `gh stack rebase --upstack --no-trunk --remote origin`. Validate all affected
  layers before pushing. Recheck changed heads, review/approval freshness, and
  pending findings across the stack; batch known fixes to limit repeated reviews.
- Preserve the user's merge authority. For an authorized merge, read the runbook,
  verify the exact bottom-up PR set, and select a merge method explicitly.
  v0.1.1 interprets a numeric target as a **stack number first**, then a PR number.
  `--yes` only suppresses a prompt. Verify remote completion before issue/roadmap
  completion or cleanup; use `sync-epic-roadmap` when native child issues apply.
- Handoff: owner worktree, ordered branches, trunk, native stack/PR identifiers,
  current heads, validation record, unresolved findings, and pending gates. For a
  remote handoff use an observed PR URL with `gh stack checkout`, then inspect both
  local state and fresh remote membership. Hand off the whole stack, not just its top.
