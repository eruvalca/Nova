---
name: plan-issue
description: >-
  Turn multi-step planning into a durable, resumable GitHub issue: create a new issue whose body carries a detailed `## Technical Plan` section (phases with checkbox items, per-phase verification plans and summaries, final recap, deployment plan), split scope that warrants it into a parent issue with verified parent <-> sub-issue links, and keep the issue(s) updated as work proceeds.
  USE FOR: planning multi-step work, plan mode, designing a strategy, roadmap, multi-phase tasks, a durable plan artifact, plan progress tracking in an issue, "real work" planning, handing a plan to a future agent, splitting large multi-workstream plans into a parent issue with linked sub-issues.
  DO NOT USE FOR: trivial single-session tasks, quick one-off edits, filing a bug report without a plan (create the issue directly), executing the planned work (invoke the relevant Nova skill, e.g. add-feature-slice), or only writing/running tests (use nova-testing).
---

# Plan Issue

Turn planning into a durable, resumable **GitHub issue**. The issue — not the
conversation and not a markdown file — is the source of truth: its
`## Technical Plan` section records what to do, what's done, how it was
verified, and how to deploy. Any future agent can resume from the issue with
zero prior context.

**Use when** planning multi-step / multi-session work that may outlive the
current session. Skip for trivial single-session tasks.

**ALWAYS INVOKE THIS SKILL** when the user mentions planning, designing a
strategy, establishing a roadmap, or any multi-phase/multi-step work,
regardless of specific phrasing.

## 1. Reach complete understanding first

Do **not** write the plan until scope is fully understood. Relentlessly ask the
user questions until you both share a complete understanding with **no gaps** —
treat an unasked question as a future bug.

- Don't stop at the first round; keep going until no ambiguity, assumption, or
  open decision remains. Probe edges: scope boundaries (in/out), dependencies,
  constraints, success criteria, data, environments, deployment, failure cases,
  and whether the scope should be one issue or a parent with sub-issues.
- Surface every assumption for the user to confirm. If an answer opens a new
  unknown, ask the follow-up — drill down recursively.
- Use the `ask_user` tool for concrete choices. When done, summarize the full
  scope back and only proceed once the user confirms nothing is missing.

## 2. Create the issue(s) with the Technical Plan

1. **Check for an existing issue first**: search the target repository for an
   open issue that already carries a `## Technical Plan` section covering this
   work; if one exists, adopt it — skip to "Keep the issue updated".
2. **Decide the issue structure**: one issue, or a parent issue with linked
   sub-issues. Apply the criteria in "Split large scope into a parent issue
   with sub-issues" and, when splitting, follow that section's creation and
   verification steps.
3. **Title**: a concise, descriptive title in sentence case (e.g. "Member
   suspension workflow").
4. **Repository**: default to the current session's repository. Only file the
   issue in another repository when the user explicitly asks. If that
   repository defines issue templates (`.github/ISSUE_TEMPLATE/`) or org
   issue types, follow the template structure — fold the `## Technical Plan`
   section into it — and pass `issue_type` to `create_issue` when the repo
   uses issue types.
5. **Create the issue** with the `create_issue` tool. The body is the
   goal/scope summary at the top followed by a `## Technical Plan` section
   using the template below.
6. **Labels**: apply only when the user asks or repo label conventions clearly
   apply; otherwise leave labels unset.
7. After creation, tell the user the issue number(s) and URL(s).

**Do NOT create a plan markdown file** (no `plans/*.md`). The issue *is* the
plan artifact — the single durable source of truth. Scratch notes may go in the
session artifacts folder, but never the plan itself.

```markdown
<1-2 sentence goal and scope.>

## Technical Plan

### For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is
done, set its status to `Complete` and write its **Phase Summary** (what was
done, key decisions, anything needed to continue with zero context); run the
phase's **Verification Plan** and record the result before moving on. Update
the issue body with the GitHub issue tools (see "Keep the issue updated") and
preserve everything outside this section. When all phases are done, fill in
**Final Recap** and **Deployment Plan**.

**Required deliverable — full test suite passes.** Before the issue (or any
sub-issue) is marked complete, the entire test suite must be green: unit,
integration, and browser. The integration and browser suites do **not** run in
CI and require the Aspire AppHost locally, so run and pass them locally and
record the results — CI green alone is insufficient. Tests intentionally skipped
for a valid, documented reason are the only acceptable exception — record them explicitly rather
than silently dropping them.

### Phase 1: <Title>

Status: Not started <!-- Not started | In progress | Complete -->

<!-- Optional. Only add when a phase benefits from delegation (see "Plan for
     the right tools and models"). Omit for phases the orchestrator should do
     itself. -->
Suggested executor: <e.g. sub-agent w/ smaller model | orchestrator>

- [ ] <concrete, actionable item>
- [ ] <concrete, actionable item>

#### Verification Plan

- <command/check the agent can run autonomously, with expected result>
<!-- If the phase touches a UI or web flow, include a browser/Playwright check
     here (e.g. "Playwright: load /checkout, submit form, assert order confirmation"). -->

#### Phase Summary

_(write when phase completes)_

### Phase 2: <Title>

Status: Not started

- [ ] <actionable item>

#### Verification Plan

- <autonomous check>

#### Phase Summary

_(write when phase completes)_

### Final Recap

_(write when all phases complete: summary of the entire piece of work, including
confirmation that the full test suite — unit, integration, and browser — passes,
or that any non-green tests were intentionally skipped for a documented reason)_

### Deployment Plan

_(write when all phases complete: step-by-step deployment instructions)_
```

## 3. Split large scope into a parent issue with sub-issues

Before creating anything, decide whether the scope is one issue or a hierarchy.

**Split** into a parent issue with sub-issues when any of these hold:

- Multiple largely independent workstreams that can proceed in parallel or be
  delegated to different executors.
- The scope spans distinct feature areas or deliverables with clear boundaries.
- A single issue's `## Technical Plan` would become unwieldy — roughly 3+
  phases with substantial items each is a smell, not a hard rule.
- Distinct milestones that will be tracked, shipped, or resumed separately.

**Do not split** for tightly coupled phases that can't meaningfully start
without the previous one finishing, for scope that fits comfortably in one
`## Technical Plan`, or for work too small to warrant a plan issue at all.

**When splitting:**

1. Create the **parent issue** first with the `create_issue` tool: goal/scope
   summary at the top, then a `## Technical Plan` whose phases map 1:1 to the
   planned sub-issues; each phase names the sub-issue that owns its execution
   (e.g. `### Phase 1: Club invitations — owned by sub-issue #N`).
2. Create each **sub-issue** with `create_issue`: its own goal/scope summary,
   a `## Technical Plan` covering that workstream's phases, and a
   `Parent issue: #<n>` reference line near the top.
3. Link them with `github-sub_issue_write` (`method: add`) — add each
   sub-issue to the parent. Note: the tool expects the sub-issue's node ID,
   not its number; get it with `gh issue view <n> --json id` if needed.
4. **Verify the hierarchy** (below), then report all issue numbers and URLs.

**Verify parent <-> sub-issue links** — required whenever sub-issues were
created; never report success with an unverified hierarchy:

- `github-issue_read` on the **parent**, `method: get` — assert `has_children`
  is `true`.
- `github-issue_read` on the **parent**, `method: get_sub_issues` — the list
  must contain exactly the sub-issues you created (match by number and title),
  no more, no fewer.
- `github-issue_read` on **each sub-issue**, `method: get` — assert
  `has_parent` is `true`; then `method: get_parent` must return the parent
  issue number.
- If any link is missing or wrong, repair it with `github-sub_issue_write` and
  re-verify before telling the user the work is done.

## 4. Keep the issue updated as work proceeds

The agent doing the work (or resuming it) updates the issue, not the chat.

- **Adopt, don't duplicate**: you checked for an existing `Technical Plan`
  issue in step 1; if a matching issue turns up later, resume it instead of
  creating a new one.
- **Read before writing**: fetch the current issue body first (`gh issue view
  <n> --json body`or`github-issue\_read`with`method: get\`), then rewrite it
  preserving the goal/scope summary and completed-phase content; change only
  what changed.
- **Parent + sub-issue plans**: record execution updates in the owning
  sub-issue; when a sub-issue completes, mirror its outcome in the parent's
  phase summary and mark the parent's phase checkbox.
- **Write back** the full body via the GitHub issue update path
  (`github-issue_write` with `method: update`, or `gh issue edit <n>
  --body-file`).
- Mark items `- [x]` as they complete. Set a phase's status and write its
  **Phase Summary** only after its Verification Plan passes. Fill **Final
  Recap** and **Deployment Plan** only when all phases actually complete — never
  pre-fill placeholders.

## 5. Plan for the right tools and models

Good plans account for *how* work will be verified and *who* (which agent/model)
should execute each part. Apply judgement — neither of the below is required for
every task; add them only when they genuinely help.

### Nova recipes and verification commands

For work that touches Nova code, each phase should name the repo skill that
contains the execution recipe (`add-feature-slice`, `add-domain-persistence`,
`add-api-endpoint`, `add-blazor-ui`, `nova-testing`, `extract-functional-core`)
so a future agent loads the right conventions instead of improvising.
Verification Plan commands must be the repo's real commands:

```powershell
dotnet build Nova.slnx
dotnet format Nova.slnx --verify-no-changes
dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj
dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj
dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj
```

**Full-suite pass is a required deliverable.** Every issue and sub-issue must
leave the entire test suite green before it is considered complete — unit,
integration, and browser. Integration and browser suites require the Aspire
AppHost locally and do **not** run in CI, so CI green is not sufficient; they
must be run and passed locally. Tests intentionally skipped for a valid,
documented reason are the only acceptable
exception — record them explicitly. Name the suites in the phase that applies
and record their results before marking the issue done.

### Browser / Playwright testing

When the work touches a UI, web page, or user-facing flow, plan for
browser-based verification instead of relying only on unit tests or manual
"click around" checks.

- Add a **Playwright/browser check** to the relevant phase's Verification Plan:
  a concrete, runnable scenario with an expected result (e.g. "Playwright: log
  in, add item to cart, assert cart count = 1").
- Cover the flows the change actually affects — new/changed pages, forms,
  navigation, auth, and visible error states — not every page in the app.
- Skip this for work with no UI surface (pure backend, libraries, CLI, infra).

### Sub-agents and model selection

Structure the plan so implementation can be **orchestrated by a more capable
model** that delegates well-scoped, mechanical, or parallelizable work to
**sub-agents running less capable (cheaper/faster) models**.

- **Delegate to a smaller-model sub-agent** when a task is well-specified and
  low-ambiguity: repetitive edits, boilerplate, mechanical refactors, test
  scaffolding, doc updates, or independent research threads that can run in
  parallel.
- **Keep on the orchestrating (more capable) model** anything needing deep
  reasoning, cross-cutting design decisions, ambiguous scope, or tight
  coordination between parts.
- When a phase or item is a good delegation candidate, note it with a
  `Suggested executor:` line (see template).
- Don't over-engineer: for small or highly interdependent tasks, a single
  capable agent doing the work directly is simpler and better.

## Anti-patterns

- **Vague items** — each checkbox is a concrete task ("Add retry logic to
  `PaymentClient.Charge`"), not a theme ("improve payments").
- **Non-autonomous verification** — give runnable commands with expected
  output, not "test it manually".
- **Writing the plan to a file** — the plan lives in the issue's
  `## Technical Plan` section; never create `plans/*.md` as the artifact.
- **Duplicate issues** — check for an existing issue with a `Technical Plan`
  section before creating a new one.
- **Blind body rewrites** — always fetch the current issue body before editing;
  never clobber the goal/scope summary or completed-phase content.
- **Pre-filling summaries** — phase summaries, recap, and deployment plan stay
  as placeholders until that work actually completes.
- **Unlinked sub-issues** — creating related issues without linking them
  parent <-> sub-issue, or reporting success without verifying the links
  (`has_children` on the parent, `get_parent` on each child).
- **Over-splitting** — splitting into sub-issues that aren't independently
  meaningful, or splitting tightly coupled work that should stay a single
  issue.
