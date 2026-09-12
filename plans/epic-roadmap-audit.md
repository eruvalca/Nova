# Epic #163 roadmap audit and roadmap-sync guardrail

Goal: verify that epic #163 and every issue beneath it is internally consistent, honestly ordered,
and accurate about what shipped — then add a small instruction and skill so the hand-maintained
roadmaps stop drifting.

## Status and orientation

All five phases are complete (2026-09-11). This file is the audit record, not a resumable plan.

The durable outputs live elsewhere: the repo-wide rule is in `AGENTS.md` under "Repository
decisions", the procedure and read-only checker are in `.agents/skills/sync-epic-roadmap/`, and the
dated audit section is in epic #163. Run
`node .agents/skills/sync-epic-roadmap/scripts/check-roadmap.mjs` to re-verify the tree.

## Why this exists

Three drifts were found in one sitting on 2026-09-11:

| # | Drift | Status |
|---|---|---|
| 1 | Epic #163 still described the Club area through parents (#204/#207/#215/#217) after those split into review-sized children | **Fixed** — same-day editorial pass |
| 2 | #171 said Tags/Crest shipped "as #261 then #262" while the children say parallel | **Fixed** — #171 reworded |
| 3 | #198's handoff comment said "not merged, so this issue remains open" after #253 merged | **Fixed** — superseding comment |

One stale claim was live and unsuperseded when the audit started — exactly the class it targets:

> #170 comment (2026-09-05T16:55:24Z): "#246 remains unmerged." PR #246 merged 2026-09-05T22:45:42Z.
>
> A superseding comment was posted in Phase 3.

The lesson is not "write more carefully". Membership, state, and counts are all derivable from
GitHub, and a fact you can check beats a fact you remember.

## Scope

**In:** epic #163 plus its 33 open descendants — **34 open issues**, three levels deep.
**Closed descendants (27)** are consulted as evidence for completion claims, not audited or edited:

- Closed direct children (14): #164–#168, #172–#176, #178, #179, #181, #191.
- Closed grandchildren (13): #196, #197, #198, #201, #208–#214, #219, #220.

**Out:** #135 (unrelated Sass migration); product re-planning or reprioritisation (the audit
reports ordering conflicts, it does not invent priority); code, UI, and design work; a wholesale
rewrite of the repo's instructions — the instructions-hygiene pass applies to what this plan adds.

## Deliverables

| # | Deliverable | Where |
|---|---|---|
| 1 | Verified issue tree and work order, plain language | #163 + this file |
| 2 | Findings with evidence | This file + summary comment on #163 |
| 3 | Factual fixes applied | GitHub, within the 34 in-scope issues |
| 4 | Guardrail: repo rule + skill + read-only checker | `AGENTS.md`, `.agents/skills/sync-epic-roadmap/` |
| 5 | Record of audit and guardrail | Dated section in #163 |

## The tree (verified 2026-09-11)

`✓` = closed. Arrows read "lands before"; `+` means the named items are parallel.

| Epic child | Status | Children and order |
|---|---|---|
| #170 Campaign loop | 3/5 | ✓#196, ✓#197, ✓#198, **#199 → #255 → #254**, **#200 → #256 + #257** |
| #171 Club surfaces | 1/7 | ✓#201, #202, #203, **#204 → #258 → (#259 + #260)**, #205, #206, **#207 → (#261 + #262)** |
| #180 Players and Teams | 0/4 | **#215 → #263 → #264**, #216, **#217 → #265 → #266**, #218 |
| #182 Import/export | 2/3 | ✓#219, ✓#220, #221 |
| #177 Supporting surfaces | 0/1 | #222 → then the #177 audit pass |
| #169 Dashboard | no children | After #170/#171/#180 land |

## Work order (plain language)

**Startable now, in parallel**

1. **#255** Place queue/evidence/decision → then **#254** corrections.
2. **#256** Close readiness and close/reopen — does not need #221.
3. **#258** Seasons directory → then **#259** detail and **#260** start next season.
4. **#263** Players directory → then **#264** manual form and lifecycle.
5. **#265** Teams directory/lifecycle → then **#266** team detail and effective roster.
6. **#261** Tags and **#262** Crest — parallel, no shared composition.

**Gated**

| Item | Gate |
|---|---|
| #257 Closed record | **#221** required for the CSV action only — #257 states "capture, paging, and print can be built before it lands" |
| #259, #260 | #258 |
| #264 | #263 |
| #266 | #265 |
| #254 | #255 |
| #216 | #264 — #216's own text says "follows #215", which now means #215's delivery (#263 → #264) |
| #202, #203 | agreed shared-entry ownership |
| #218 | backend complete; coordinate entry ownership with #263 |
| #205, #206 | none — ready and whole |
| #169 | canonical #170/#171/#180 destinations |
| #177 | last, after #222 |

**Coordination rules that are easy to miss**

- The Club shell and its routes are shared: #258/#259/#260, #261/#262 and #265/#266 must coordinate
  shell/route changes rather than editing them independently.
- #217/#265/#266 are the single canonical Teams implementation consumed by #171 and #169.
- Parent integration gates stay with **#170, #180, #171, #182** — children do not close them.
- Aspire integration and browser suites are serialized per machine.

## Checks

Seven mechanical (M) and seven read-and-judge (R). Only M checks are implemented by the checker;
R checks are read by an agent against the tree.

| # | Group | Check | Kind |
|---|---|---|---|
| 1 | Structure | Each roadmap block lists exactly the children GitHub returns — both directions. #163's dated summary is exempt (it lists the six open children only, by design) | M |
| 2 | Structure | Every completion count and checkbox matches real child states | M |
| 3 | Structure | No issue is parented twice or listed under a non-parent. Arrows in this plan and in the blocks denote **order, not ancestry**; only structural listings are compared to the API | M |
| 4 | Evidence | Every "complete via #PR" / "merged in #PR" claim resolves to a **merged** PR | M |
| 5 | Evidence | Every cited revision is honest: cited as delivered → reachable from `origin/main`; cited as a tested head → its PR is merged and the merged tree matches | M |
| 6 | Evidence | Every `#N` referenced in a block exists and is the right kind (issue vs PR) | M |
| 7 | Evidence | Every file/blob link resolves at the revision it names | M |
| 8 | Order | A parent's stated order matches its children's stated order | R |
| 9 | Order | Parallel claims are symmetric — no child says parallel while its parent says "then" | R |
| 10 | Order | Any legacy "Recommended PR sequence" section still declares the native-child roadmap authoritative | R |
| 11 | Order | Every "requires #X" names an issue that exists and is in the needed state | R |
| 12 | Mandate | No surface, route, or capability is owned by two issues | R |
| 13 | Mandate | Every parent acceptance criterion is owned by at least one child | R |
| 14 | Hygiene | No stale **and unsuperseded** language; a later "this supersedes" comment resolves the earlier one | R |

**Check 5 caveat:** squash merges mean a validated head is legitimately absent from `main`.
`1d09236a` is not an ancestor of `main`; its content equals `96edc944`, which is. Judge the claim,
not the SHA's presence.

## Phase 1: Inventory, baselines, and checker

Status: Complete

- [ ] Walk the tree with `gh api repos/eruvalca/Nova/issues/<n>/sub_issues --paginate` from #163
      down; record number, title, state, parent, and order into one table.
- [ ] Confirm the totals: **34 open** in scope, **27 closed** descendants. Correct this file if not.
- [ ] Snapshot every in-scope body and comment set to the session workspace (enables the Phase 3
      and Phase 5 diffs).
- [ ] Re-baseline the three drifts in "Why this exists" plus the live #246 claim, recording the
      superseding comment or fix for each.
- [ ] Define the checker's membership contract before coding: a *listed child* is a `#N` token inside
      the marker block that GitHub returns as a sub-issue of that block's owner; PR references are
      excluded. Blocks mix prose and numbered lists, so parse tokens, not lines.
- [ ] Build the read-only checker `.agents/skills/sync-epic-roadmap/scripts/check-roadmap.mjs`
      covering checks 1–7 only. Node + `gh api`, no writes, exit 0 clean / 1 with findings.
- [ ] Run it and capture the initial report as the Phase 2 input.

### Verification Plan

- The recorded counts match GitHub exactly (set equality, not eyeballing).
- Snapshots exist for all 34 in-scope issues.
- Checker runs end-to-end and prints a findings report; its exit code matches whether findings exist.

### Phase Summary

Inventory matched the plan exactly (34 open, 27 closed; 61 nodes), and every open issue's body and comments were snapshotted for scope proof. The checker was built and debugged against live data: GitHub's REST API returns lowercase states, and the epic's `(N of M)` counts describe grandchild groups, so a count is evaluated against the first issue named on its line. Cross-run HTTP caching was removed — a checker that reports stale facts is worse than a slow one.

## Phase 2: Run the checks and record findings

Status: Complete

- [ ] Run the checker (checks 1–7) and take its report as the mechanical findings.
- [ ] Read for checks 8–14 against the inventory and the snapshots.
- [ ] Write every finding as: issue · quoted text · what is wrong · evidence · proposed correction.
- [ ] Classify each as **factual drift** (Phase 3 fixes it) or **judgment call** (report only), and
      record a zero-findings path: if a check finds nothing, say so with the evidence.

### Verification Plan

- Every finding cites a quoted line plus a command result or resolvable link.
- Each of the 14 checks has an explicit outcome (finding or clean) — none silently skipped.
- No finding predates this phase; each is re-derived from the live API.

### Phase Summary

Mechanical checks 1–7 clean across 61 issues and 14 blocks. Judgment checks 8–14 produced six findings: four factual (corrected in Phase 3) and two judgment calls, both resolved after investigation. See the Audit findings table.

## Phase 3: Apply the fixes

Status: Complete

- [ ] Fix all **factual drift**: counts, checkboxes, member lists, merged-PR claims, dead
      references, and stale-and-unsuperseded status language.
- [ ] For every edit: re-fetch immediately before writing (other sessions are working these
      issues), assert the target string matches exactly once, then re-read and confirm.
- [ ] If a body changed under you, do not force the edit: post a superseding comment instead.
- [ ] Post short status comments where a stale comment would otherwise mislead (the #198 pattern).
- [ ] Report **judgment calls** with options; edit only what is approved.
- [ ] Re-run the checker until clean.

### Verification Plan

- Checker reports clean across all 34 issues.
- Snapshot diff shows only intended edits; three edited bodies spot-checked for structure
  (headings intact, newlines preserved).
- No issue outside the 34 was touched.

### Phase Summary

Four factual findings were fixed across seven issue bodies, each with an exactly-one-match assertion against a freshly fetched body; #170 and #200 also received superseding comments. The snapshot diff confirms only the eight intended bodies and two comment threads changed.

## Phase 4: Add the guardrail

Status: Complete

Design rule from the instructions-hygiene guidance: keep the always-loaded file to the smallest set
of high-signal facts, and put the procedure where it loads on demand.

- [ ] Add **one row** to the routing table in `AGENTS.md` → `## Instruction and skill routing`:

  | Concern | Rules in `.github/instructions/` | Recipe in `.agents/skills/` |
  |---|---|---|
  | Epic issue roadmaps (GitHub issues) | — (repo-wide rule above, in Repository decisions) | `sync-epic-roadmap` |

- [ ] Add a short rule under `## Repository decisions` (≈5 lines, no generic advice):

  > **Issue roadmaps are hand-maintained.** A parent issue carries a
  > `<!-- native-child-roadmap:start -->…<!-- native-child-roadmap:end -->` block; nothing generates
  > it, and a block is present only when the issue has native children. Before editing one,
  > reconcile membership and state with
  > `gh api repos/eruvalca/Nova/issues/<n>/sub_issues --paginate`. Mark a child complete only when
  > its closing PR is merged into `main`. Procedure: `sync-epic-roadmap`.

- [ ] Create `.agents/skills/sync-epic-roadmap/` with `SKILL.md` (procedure, kept tight) and
      `references/roadmap-checks.md` (the 14 checks, exact commands, finding template, fix
      discipline).
- [ ] Keep the checker in the skill's `scripts/`, **not** in CI: issue state is volatile and other
      sessions work concurrently, so a failing CI job would be noise. Record that reasoning.
- [ ] Match repo skill conventions: directory name = `name:` front matter, folded `description:`
      with `USE FOR` / `DO NOT USE FOR` triggers and cross-skill routing.
- [ ] Check for duplication first — `grep` `AGENTS.md`, `.github/instructions/`, and
      `.agents/skills/` for roadmap/sub-issue guidance and move or remove rather than restate.
- [ ] Ship `.agents/skills/` only. (AGENTS.md: a Copilot copy under `.github/skills/` must keep the
      `.agents/skills/` copy "complete and in sync (same version, same behavior)" — nothing enforces
      parity, and one copy is sufficient.)
- [ ] Validate every command the rule and skill quote by having actually run it in Phases 1–3.

### Verification Plan

- `./scripts/Test-AgentGuidance.ps1` and `./scripts/Test-AgentGuidance.ps1 -SelfTest` pass.
- `node --test scripts/AgentHooks.Tests.mjs` passes.
- `grep` shows no duplicated roadmap-sync guidance in agent-facing files.
- Every quoted command was executed during this work.
- No C#/Razor/project file changes, so `dotnet build`, `dotnet format` and the three suites
  exercise nothing new — **state that explicitly** in the commit or PR validation record rather
  than skipping silently, and note that CI also runs `dotnet format --verify-no-changes` and
  `npm run check:contrast`, which are unaffected.

### Phase Summary

`AGENTS.md` gained the routing-table row and a five-line marker/verification rule. `.agents/skills/sync-epic-roadmap/` ships `SKILL.md`, `references/roadmap-checks.md`, and the read-only checker. `Test-AgentGuidance.ps1`, its `-SelfTest`, and `AgentHooks.Tests.mjs` pass; a grep confirmed no pre-existing roadmap guidance to duplicate.

## Phase 5: Record and hand off

Status: Complete

- [ ] Add a dated section to #163: the verified tree, the work order, and what the guardrail enforces.
- [ ] Comment on each materially changed parent so watchers see the outcome.
- [ ] Fill this file's Final Recap and Handoff; update the session todos.
- [ ] Confirm `git status` shows only the intended guardrail files.

### Verification Plan

- A reader can answer "what is next?" from #163 alone.
- Checker reports clean after all edits.
- `git status` matches the intended file list exactly.

### Phase Summary

A dated "Roadmap audit — September 11, 2026" section records the corrections, the two judgment calls, and what the guardrail enforces. #170 and #200 carry outcome comments; the verified tree and work order needed no edit.

## Risks and notes

- **Tracker churn.** Every finding is a public edit. The fix policy limits edits to facts.
- **Judgment is not mechanisable.** Ordering intent comes from the issues, not from any API. The
  checker covers 7 of 14 checks; the rest stay read-and-judge.
- **Moving target.** Other sessions work these issues concurrently — re-fetch before each edit and
  re-run the checker at the end. A clean pass is a snapshot, not a guarantee.
- **Instruction bloat.** The main risk to the guardrail is adding too much. If a line does not change
  an outcome, it should not exist.
- **False-positive risk.** Checks 1, 3 and 14 misfire if read literally — see the #163 summary
  exemption, the order-versus-ancestry rule, and the supersession rule. Getting these wrong means
  editing a tracker that is already correct.

## Audit findings (Phase 2 output)

**Mechanical (checks 1–7): clean.** 61 issues walked, 14 roadmap blocks, zero drift — membership, checkboxes,
"(N of M)" counts, merged-PR claims, `#N` references, cited revisions, and blob links all agree with GitHub.

**Judgment (checks 8–14): 6 findings, 4 corrected, 2 reported.**

| # | Check | Issue | Finding | Action |
|---|---|---|---|---|
| 1 | 8, 9 | #170 | "delivered as #256 then #257" and "#200 tracks #256 then #257" contradict #200/#257, which say parallel | Fixed → "#256/#257 in parallel" |
| 2 | 11 | #200 | Prose asserted a blanket "#221 remains required"; its block and #256 say only #257 needs it | Fixed → scoped to the #257 record/export slice |
| 3 | 11 | #202, #206, #207, #215, #216 | Completed foundations described as outstanding ("Requires #176/#178/#191/#175/the Club shell child") | Fixed → `Completed: …` form used by their own children |
| 4 | 14 | #170 | Comment said "#246 remains unmerged"; PR #246 merged 2026-09-05T22:45:42Z, never superseded | Fixed → superseding comment |
| 5 | 13 | #170 | Acceptance said "five flow briefs" while Governing briefs list six; player intake is delivered by #180 | Resolved — the five are now named; intake attributed to #180 |
| 6 | 13 | #163 | Completion definition required docs/journey map/briefs to converge, but no child owned it | Resolved — #177's acceptance now owns the check and disposition |

Checks 8, 9, 10, 11, 12, 13 and 14 each reported explicitly; the other stale claims found on #163, #170,
#199 and #200 were correctly resolved by later superseding comments.

## Final Recap

**Delivered.** The epic #163 tree was audited end to end — 34 open issues, with 27 closed descendants used
as delivery evidence — and the tree, ordering, and completion map were verified accurate. The mechanical
checks are clean; four judgment-check findings were corrected, and the two remaining judgment calls were
resolved after investigation — #170's criterion now names the five briefs it binds, and #177's acceptance
owns the documentation-convergence check.

**Guardrail shipped.** `AGENTS.md` gained a routing-table row and a five-line rule stating the marker
contract, the authoritative membership command, and that a child is complete only when its closing PR is
merged. `.agents/skills/sync-epic-roadmap/` holds the procedure (`SKILL.md`), the 14 checks with commands
and fix discipline (`references/roadmap-checks.md`), and a read-only checker
(`scripts/check-roadmap.mjs`). Deliberately no writer, no generator, no CI job.

**Repo changes:** `AGENTS.md` (3 lines) plus the new skill directory. Nothing else. Not committed — this
workspace commits only on explicit request.

**Issue changes:** bodies #163, #170, #200, #202, #206, #207, #215, #216; comments #170, #200.

**Verification:** checker clean on a live re-run; snapshot diff shows only the 8 intended bodies and 2
comment threads changed; `Test-AgentGuidance.ps1`, its `-SelfTest`, and `AgentHooks.Tests.mjs` pass.

## Handoff

**For the owner:** say whether the guardrail files should be committed. Both audit judgment calls were
resolved on 2026-09-11: #170's acceptance criterion now names the five briefs it binds, and #177's
acceptance owns the documentation-convergence check.

**For the next agent:** run
`node .agents/skills/sync-epic-roadmap/scripts/check-roadmap.mjs` after any merge that a roadmap claims as
delivery, after a child closes or splits, or before trusting an epic roadmap for "what is next?". Read the
skill's `references/roadmap-checks.md` first — it carries the false-positive traps that would otherwise
cause edits to a correct tracker.

**Deliberately left open:** no writing automation and no scheduled job (issue state is volatile and other
sessions work these issues concurrently); this record is retained in `plans/` with the other completed plans.
