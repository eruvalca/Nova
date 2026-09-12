# Roadmap checks

The 14 checks an epic roadmap audit runs. The first seven are implemented by
`scripts/check-roadmap.mjs`; the rest are read by an agent.

## Check list

| # | Group | Check | Kind |
|---|---|---|---|
| 1 | Structure | A block exists if and only if the issue has native children, and it names every child GitHub returns | Mechanical |
| 2 | Structure | Every completion count and checkbox matches real child states, and every checked child has a closing PR merged into the default branch | Mechanical |
| 3 | Structure | No issue is parented twice, and no checklist entry names something that is not a child. Arrows denote **order, not ancestry** | Mechanical |
| 4 | Evidence | Every "complete via #PR" / "merged in #PR" claim resolves to a PR merged **into the default branch** | Mechanical |
| 5 | Evidence | Every cited revision is honest: delivered → reachable from `origin/main`; tested head → off-main is expected for a squash merge | Mechanical |
| 6 | Evidence | Every `#N` referenced in a block exists and is the right kind (issue vs PR) | Mechanical |
| 7 | Evidence | Every file/blob link resolves at the revision it names | Mechanical |
| 8 | Order | A parent's stated order matches its children's stated order | Read |
| 9 | Order | Parallel claims are symmetric — no child says parallel while its parent says "then" | Read |
| 10 | Order | Any legacy "Recommended PR sequence" still declares the native-child roadmap authoritative | Read |
| 11 | Order | Every "requires #X" names an issue that exists and is in the needed state | Read |
| 12 | Mandate | No surface, route, or capability is owned by two issues | Read |
| 13 | Mandate | Every parent acceptance criterion is owned by at least one child | Read |
| 14 | Hygiene | No stale **and unsuperseded** language; a later "this supersedes" comment resolves the earlier one | Read |

## Mechanical scope

`check-roadmap.mjs` reads the marker block only, not the whole body. Dated narrative sections above
or below the block are prose history: they legitimately cite tested revisions that are off-main and
superseded handoffs. The read pass covers them, plus comments.

Check 2 treats delivery as the repository rule defines it: a checked child must be closed **and** have
a closing pull request merged into the default branch, so an issue closed as not-planned, or a PR
merged into a feature branch, is reported rather than accepted. Check 4 applies the same test to PRs
cited as delivering a child. Both consult GitHub's `closedByPullRequestsReferences`; the CLI verifies
the merge commit is reachable from `origin/<default>` when a git checkout is available.

## Commands

```powershell
# Verify membership and state for one parent (paginate: parents can exceed one page)
gh api repos/eruvalca/Nova/issues/<n>/sub_issues --paginate --jq '.[] | "\(.number) \(.state)"'

# Confirm a PR merged into the DEFAULT branch — merged_at alone only proves some branch
gh pr view <n> --repo eruvalca/Nova --json number,state,mergedAt,baseRefName,mergeCommit --jq '{number,state,mergedAt,baseRefName,mergeCommit:.mergeCommit.oid}'
git fetch origin main --quiet; git merge-base --is-ancestor <merge-commit> origin/main; echo $LASTEXITCODE

# Confirm a cited revision is on the default branch
git merge-base --is-ancestor <sha> origin/main; echo $LASTEXITCODE

# Compare a squash merge to its validated head: use the PR's merge commit, never the moving tip
git rev-parse <validated-sha>^{tree} <merge-commit>^{tree}

# Run all mechanical checks (live, no HTTP cache; a 60-issue tree takes a few minutes)
node .agents/skills/sync-epic-roadmap/scripts/check-roadmap.mjs

# Run the checker's own tests, and replay a scenario offline
node --test .agents/skills/sync-epic-roadmap/scripts/check-roadmap.Tests.mjs
node .agents/skills/sync-epic-roadmap/scripts/check-roadmap.mjs --fixture <file.json> <owner/repo> <epic>
```

## False-positive traps

- **The epic summary is not a child list.** Epic #163's block states counts per *child group*
  (`#170 Campaign loop (3 of 5)`), so counts belong to the named child, not to the epic. A `(N of M)`
  on a line is checked against the first issue named on that line.
- **Arrows are order, not ancestry.** `#204 → #258 → #259` means "lands before", not "child of".
- **Superseded is not stale.** A comment that a later comment explicitly corrects is resolved. Only
  report claims that are still wrong and never corrected.
- **Run with permission to read issues.** The checker only issues GETs; it never writes.

## Finding template

```
check <n> · <issue> · "<quoted text>" · <what is wrong> · <evidence: command result, quoted
counter-text, or API field> · <proposed correction> · <factual | judgment>
```

- **factual** — objectively wrong (a count, a state, a merged-PR claim, a dead reference). Fix it.
- **judgment** — changes scope, priority, or product intent. Report it; do not edit without approval.

## Fix discipline

1. Re-fetch the issue immediately before editing — other sessions work these issues.
2. Assert the target string matches **exactly once**; never blind-replace a body.
3. Re-read after writing and confirm the change landed and the structure survived.
4. If the body changed under you, do not force the edit: post a superseding comment instead.
5. Prefer a short status comment over rewriting history when the stale claim is a comment.

## The marker contract

```html
<!-- native-child-roadmap:start -->
...human- and agent-written roadmap content...
<!-- native-child-roadmap:end -->
```

- A block is present **iff** the issue has native children.
- Nothing generates the block. These checks make it verifiable, not automatic.
- Keep the prose readable: the block is read by agents as context, so a wrong map misleads work.
