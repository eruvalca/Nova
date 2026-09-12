---
name: sync-epic-roadmap
description: >-
  Verify and repair the hand-maintained native-child-roadmap blocks in Nova's GitHub epic issues so
  parents and children agree on membership, completion, ordering, and dependencies. USE FOR:
  reconciling a roadmap block after a child lands, closes, or splits; auditing an epic and its
  descendants for consistency; verifying "merged in #PR" or "(N of M)" completion claims; checking
  that parent and child ordering claims agree; adding a newly created child to its parent's block.
  DO NOT USE FOR: code, domain, UI, or test work (use the feature skills), general GitHub
  housekeeping such as labels or triage, or writing brand-new issue scope.
---

# Epic roadmap sync

Issue bodies carry a hand-maintained roadmap block. GitHub records the facts (membership, state,
completion); the block records the judgement (order, ownership, why). Drift between them misleads
every agent that reads the issue as context. This recipe reconciles the two.

## When to run it

- A child issue closes, reopens, splits, or is re-parented.
- A new native child is created under an existing parent.
- Before relying on an epic roadmap for "what is next?".
- After a merge that a roadmap claims as delivery.

## Procedure

1. **Collect the facts.** Walk the tree with `sub_issues` (paginate) and record number, title, state,
   and parent. Never work from memory or from a previously read copy.
2. **Run the mechanical checks.** `node .agents/skills/sync-epic-roadmap/scripts/check-roadmap.mjs`
   covers checks 1–7. It is read-only and exits non-zero when it finds something.
3. **Read for checks 8–14.** Ordering agreement, parallel symmetry, legacy sequence sections,
   dependency state, single ownership, acceptance coverage, and stale-and-unsuperseded language.
4. **Fix factual drift.** Counts, checkboxes, merged-PR claims, dead references, and stale status
   language. Follow the fix discipline in the reference.
5. **Report judgment calls.** Anything that changes scope, priority, or product intent is a decision
   for the user, not an edit.
6. **Re-run the checker** until clean, then record what changed.

Read `references/roadmap-checks.md` for the full check list, the exact commands, the false-positive
traps, the finding template, and the fix discipline. Read it before editing any block.

## Rules that are easy to get wrong

- Mark a child complete **only** when its closing PR is merged into `main`. A green or approved PR is
  not delivery.
- A validated revision may be absent from `main` when the merge was squashed — compare trees before
  calling a claim stale (`git rev-parse <sha>^{tree} origin/main^{tree}`).
- Arrows in blocks mean **order**, not parentage.
- A comment that a later comment supersedes is resolved history, not a stale claim.
- Keep the block readable. The point is a map a human and an agent can both trust, not a report dump.
