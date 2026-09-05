# PR #244: improve code quality before PR review

Implement the approved retrospective plan for [PR #244](https://github.com/eruvalca/Nova/pull/244). Baseline: `6f943d5177a537bbe4a89a7b797321911ff0414b`. The causal priority is missing behavioral transitions and sibling implementations, followed by dependable validation and compatible guidance. Passing an existing suite is execution evidence, not evidence that a new behavior has adequate tests.

## For future agents

Mark items complete only after their verification passes. Record each phase's results and remaining limitations below. Keep build-capable commands serial in this checkout, and integration/browser suites serial across the machine. Root coordinates builds while independent workers own guidance, UI/unit code, and integration/browser infrastructure. Record run details in ignored `artifacts/verification/`; keep this document a durable handoff, not a second test-result database.

## Accepted decisions and limits

- Support Codex, Copilot CLI, and Copilot cloud; preserve compatible VS Code handling.
- Keep `AGENTS.md` as the sole repo-wide source and `.agents/skills` canonical. Remove the redundant Impeccable copy only after discovery proof in both ecosystems, including cloud.
- Narrow XML documentation to useful contracts and non-obvious ownership, effects, recovery, lifecycle, and cancellation; retain useful comments.
- Keep fresh full local pre-PR/premerge gates. Establish full hosted CI after two consecutive proof runs, then require it for normal merges with current-base checks. Preserve administrator bypass and existing approval/review policy.
- Hooks provide inexpensive advice and one bounded continuation at an explicitly armed implementation checkpoint. They never run full suites or model reviews themselves.
- Preserve historical tracked design evidence. Curate future approved/final evidence in Git and put intermediate output in run-specific artifact directories.
- Do not build a generic state-management framework, publication barrier, new persisted recovery format, automatic orphan reaper, binary-attestation/cache system, general guidance compiler, mutation-testing platform, or custom review collector.
- Production HTTP APIs, schema, persisted recovery payloads/storage keys, and authentication policy remain unchanged.

## Phase 1: upstream workflow and guidance corrections

Status: Complete (workflow/adapter integration continues in phase 6)

- [x] Add one conditional transition/effect mapping reference for recoverable commands, contextual server-validation forms, and async state whose identity/resource/authority can change.
- [x] Map state owner, initial/retry/confirmation/recovery entry points, HTTP/JS await boundaries, permitted effects, and named existing/proposed tests. Classify consequential scenarios as covered, missing, or not applicable. Avoid a Cartesian product and exclude ordinary changes without these boundaries.
- [x] Require bounded implementation-pattern sibling inspection for confirmed defects: reproduced/limited reproduction, fixed/already safe/intentionally different siblings, regression, and a neighboring scenario. A second recurrence triggers inspection, not automatic abstraction.
- [x] Add a shared independent pre-PR review brief using existing host reviewer/delegation facilities. Stop after supported behavioral findings and affected neighboring flows are resolved; do not require repeated zero-comment passes for style/speculation.
- [x] Narrow UI/API/EF/testing scopes while preserving lifecycle/placement rules; fix component DbContext, post-render recovery, real HTTP integration, shared-feature path, and Required-whitespace contradictions.
- [x] Narrow XML docs and remove duplicated tutorials/obsolete workarounds without losing local architectural facts or proven traps.
- [x] Document paginated gh/API review retrieval and a compact disposition ledger, including suppressed findings; do not add a collector.

Verification: scope positive/negative fixtures, skill/frontmatter/reference validation, review of the shared recipe against the actual #244 missing sibling sequence. Commands in guidance must match implemented tools. Record which checks are not yet available rather than claiming future CI is active.

Phase summary: Conditional mapping and review-closure references are shared by the existing recipes. Split browser and navigation rules into narrower files, and routed both from AGENTS.md. Scope/frontmatter/reference tests pass; instruction outcomes remain subject to the phase-7 evaluation. No new review collector or general instruction compiler.

## Phase 2: concrete feedback and validation repair

Status: Complete; included in the first successful full local run

- [x] Reproduce settled CampaignEntry success/error feedback surviving Draft A to B on the same mounted component.
- [x] Clear success/mutation/field/readiness feedback on campaign ownership transitions, preserving same-campaign refresh feedback.
- [x] Extract internal ServerValidationMessages for exactly creation and metadata forms; retain local models/field mapping, observed parent-snapshot identity across context replacement, and subscription disposal.
- [x] Prove submit, rejection, correction, unchanged-parent rerender, and actual successful resubmission for both forms, including payload/callback assertions, inline-season and cross-field mapping, new/null snapshots, context replacement, and disposal.
- [x] Inventory the existing regression corpus and keep coverage/sibling dispositions in `plans/pr-244-code-coverage.md`.

Verification: focused component tests; record a failing-before/passing-after feedback regression; preserve existing corrected-resubmit assertions. Full local validation before PR/merge.

Phase summary: The new feedback theory failed both rows against unchanged production code (44 other CampaignEntry cases passed). After the fix/extraction, all 142 focused CampaignEntry, CampaignComponents, CampaignFormValidation, ServerValidationMessages, and NewCampaignRecovery tests passed with no skips. Independent review found no supported production defect. Three new nested-mapping assertions initially assumed the wrong CSS class; captured markup showed correct field association, and the tests now assert the actual visible control/error pair.

## Phase 3: verification foundation and full CI

Status: In progress

- [x] Standardize Node 24 across the engineering runner, CI, npm/build preflight, and hook runtime requirements, separate from the Sass dependency tree.
- [x] Implement `node eng/verify.mjs plan --profile push --base <commit>`, `run --profile quick --suite unit --filter-class <pattern>`, `run --profile push --base <commit>`, `run --profile pre-pr|pre-merge`, `run --profile ci --install-browser`, and `status --profile <profile> --json`.
- [x] Quick is focused and never full readiness; push always unit plus conservative affected checks; full profiles run fresh build/engineering/format/contrast/all three suites. Build once and use --no-build.
- [x] Select base diff, staged/unstaged, nonignored untracked inputs, and both rename sides; fail invalid base. UI/client adds browser; server/shared/auth/provider/harness/project/package/AppHost/verification or unknown executable/config changes conservatively add integration/browser.
- [x] Write ignored source fingerprint, head/base, policy/profile, exact command/version, timestamp/result/skip/report evidence. Start/end source changes invalidate a run; reports cannot invalidate themselves.
- [x] Require successful exits, parseable reports, executed tests, no failures, and only explicitly allowlisted optional screenshot skips. Missing/zero/cancelled/timed-out/cleanup-failed runs are nonpassing. Never combine old suites or automatically rerun whole suites until green.
- [x] Put bounded machine-wide OS locking in the shared Aspire fixture, with useful diagnostics and release through teardown; serialize checkout builds.
- [x] Harden partial initialization and disposal so every owned resource is cleaned even if an earlier cleanup fails; retain four browser workers and isolate run/test output paths; capture useful failures without an orphan reaper.
- [x] Add Ubuntu 24.04 full CI using pinned .NET/Aspire, Node 24, Docker, and matching Playwright dependencies, build once/serial suites, timeouts, read-only permissions, superseded-run cancellation, and always-upload artifacts (30 days).
- [x] Run engineering/adapter tests on Windows and Linux. Record tested checkout SHA and PR head/base.
- [ ] Obtain two consecutive hosted full passes, then update required checks/current-base policy while preserving admin bypass and existing approval/review settings. Keep old protections until replacements work.

Verification: positive/failure/zero/missing/stale/skip/report runner fixtures; Windows/Linux lock contention and release; actual full local run; two actual hosted runs; read-back of branch-protection settings. Initial hosted passes prove feasibility, not permanent reliability.

Phase summary: The Node 24 runner, honest ignored evidence, shared fixture lock/cleanup, and full hosted workflow are implemented. The first full local run passed. Independent review added a missing HTTP-client selector and reproduced the Linux process-group cleanup gap; 30 Windows engineering tests pass with the explicit POSIX-only skip, and all 31 pass in a Node 24 Linux container, including actual descendant termination. Hosted runs and the required-check transition remain pending.

## Phase 4: ownership pilot and contract coverage

Status: Complete; pilot and accepted consumers passed focused and full local validation

- [x] Add a shared controlled authentication provider; retain page-specific assertions and ordinary held completions.
- [x] Pilot internal UiIdentityScope and UiRequestOwner by composition on NewCampaign/CampaignEntry. Separate notification order from applied user/club/authority revision, and independently concurrent load/mutation/edit/storage lanes.
- [x] Apply one ownership rule at result/error/post-JS/navigation/finally boundaries; retain cancellation and exact operation IDs, payloads, keys, routes, and recovery behavior.
- [x] Prove pending/unchanged auth preserves edits, legitimate publication, pending requests, and busy ownership; newest changed identity clears old visible ownership before awaiting replacement work. Disposal rejects late work. No quarantine/publication barrier.
- [x] Inventory startup overtaking/notification order/first empty identity/club-user-authority changes/disposal; old success/forbidden/transport/finally after newer work; initial/retry storage, incompatible data, ambiguous outcomes/recreation/partial cleanup; original ID/payload retention.
- [x] Preserve optional receipt failure behavior: roster usable, no success claim, focus theft, or acknowledgement of invalid data. Keep actual browser receipt JS and keyboard/focus tests.
- [x] Preserve direct visits, return context, same-component reuse, and confirmation on rising/falling preview counts. Add Back/Forward coverage only if missing.
- [x] Retain otherwise-valid raw JSON fixtures isolating one invalid condition: required/optional/omitted/null/malformed/nested data, paging agreement, Draft previews. Keep readiness strict and directory count/row drift intentional.
- [x] Promote existing integration reader interceptor without weakening import tests and add a PostgreSQL readiness test for six active teams plus archived: one Teams reader, full count, exact ordered five-item preview, archived exclusion. Retain HTTP/client tests.

Verification: existing focused regressions unchanged in meaning plus preservation tests, representative negative control for new claimed protection, real PostgreSQL regression, existing browser handoff plus missing history case if applicable, independent pilot review, full local gates.

Phase summary: The two-page pilot passed 158 focused regressions after independent review reproduced and closed two late post-JavaScript continuation gaps. The shared helpers distinguish pending authentication ordering from applied identity ownership and retain page-owned policy. Existing critical recovery, optional receipt, confirmation, and client-contract tests remain; the new PostgreSQL one-reader regression failed under the isolated AsSplitQuery negative control, then passed restored. The real browser opening journey now exercises missing Back/Forward receipt behavior. The full local run passed 2,593 unit, 533 integration, and 120 browser tests (seven explicitly optional screenshot skips).

## Phase 5: conditional remaining consumer adoption

Status: Complete; Campaigns and Players adopted, Teams intentionally retained local behavior

- [x] Advance only when pilot tests pass unchanged in meaning, scope transitions and recovery are preserved, duplicated ownership decisions are replaced, and helpers have no page-specific business switches.
- [x] Adopt on equivalent consumers, retaining separate operation lanes and page-owned business resets. Assess Teams against its existing role-change semantics before deciding whether adoption preserves behavior.
- [x] Keep local implementation for a consumer whose semantics differ; document its evidence-backed difference rather than force uniformity.

Verification: map each replaced result/exception/post-JS/finally check; focused consumer tests, independent review, full local validation. Record a deterministic go/no-go disposition for each consumer.

Phase summary: Campaigns and Players replace their duplicated ownership decisions with the pilot helpers. Teams retains its local implementation: role loss resets management state while preserving the roster and its in-flight club request, so applying the full identity helper would change behavior. Shared controlled-authentication tests prove its existing semantics. Independent review found two Campaigns sibling omissions (late forbidden season-choice navigation and identity-owned cached season choices); both have failing-before controls and narrow fixes. All 244 focused cohort tests pass, followed by the successful full local run.

## Phase 6: compatibility and bounded hooks

Status: In progress

- [x] Implement small deterministic sync/check/scope tooling for four existing canonical custom-agent adapters and owned hook entries; repair drift and validate frontmatter/references without a general compiler.
- [x] Update provider constants, paths, installers, repair/reset/removal logic before removing the duplicate skill; preserve unrelated user hooks in every write path.
- [ ] Prove shared skill discovery in Codex, Copilot CLI, and Copilot cloud; remove redundant `.github/skills/impeccable` only after those checks.
- [x] Normalize provider command-hook input/output using current official docs, including Windows/Linux and compatible VS Code formats. Fix .razor discovery and associated .razor.css scanning; keep existing detector advice inexpensive and deduplicated.
- [x] Add `verify expect --session <id> --profile <profile> --base <commit>` and `verify defer --session <id> --reason <reason>`; associate intent with current request/worktree/branch. Verification execution does not arm intent.
- [x] Expire checkpoints on new user requests; defer waiting/blocked work without passing it; ambiguous/missing intent is advisory. Cap at one continuation per checkpoint and honor native guards even after cosmetic edits.
- [x] Hooks only point to the common runner; never run suites/models or synthesize a passing record. Trust/disable/failure remain honest capability limitations; CI is authoritative.

Verification: deterministic/idempotent adapter checks; wrong metadata/reference/drift negatives; actual host smoke tests; payload fixtures for two sessions, subagents, branch changes, review/plan/wait states, malformed evidence/runtime failure/timeouts/repeated failures, nested/spaced cwd and multi-file/shell edits. Verify installers with absent duplicate and unrelated hook entries.

Phase summary: Canonical references, four generated agent adapters, hook repair/reset paths, and Razor/CSS detection are checked together. Both installed native CLIs discover the shared skill and execute the single-block/defer lifecycle under host trust. Copilot's synthetic corrective prompt expires intent but retains only the used continuation allowance when its text exactly matches the hash of our emitted reason. Cloud discovery remains the prerequisite for deleting the real legacy skill; VS Code has documented adapter fixture coverage, not a claimed native run.

## Phase 7: evidence refinement and behavioral evaluation

Status: In progress

- [x] Retain approved comps/provenance/final viewports/reviewer disposition/source manifest in Git for new work; use dedicated run directories for intermediate output and preserve every historical tracked evidence file/reference.
- [x] Update PR template with behavioral mapping and generated execution summary; batch coherent fixes and inspect each reviewed revision; split future work by behavior rather than arbitrary diff size; recheck existing follow-ups before opening duplicates.
- [ ] Compare baseline/candidate guidance on the same three bounded starting tasks and acceptance tests: contextual validation, asynchronous identity ownership, durable commands. Use Codex and Copilot, task outcomes rather than expected fixes, record clients/models/settings and observable discovery.
- [ ] Use a simple results table for missed invariants, introduced regressions, sibling inspection, correction effort/repeated families, validation cost and flakiness; do not claim hidden-context access or statistical proof from a small sample.
- [ ] Remove/revise guidance only when observed outcomes support it.

Verification: historical-evidence tree hashes/references unchanged; new output locations do not overwrite tracked defaults; primary host discovery and bounded behavioral exercises; final independent review and fresh full verification.

Phase summary: Pending.

## Final acceptance

- [x] Feedback defect reproduced/fixed and actual corrected resubmission proven in both forms.
- [x] Ownership and recovery contracts preserved; siblings have explicit dispositions.
- [x] Critical recovery, optional receipts, query contracts, navigation, and focus have meaningful coverage.
- [ ] Stale/incomplete verification is nonpassing and full hosted checks protect normal merges.
- [ ] Shared guidance, skills, agents, and hooks function in both ecosystems.
- [x] Historical design evidence remains intact.

## Final recap

Pending implementation and validation.

## Execution journal

| Check | Result / evidence |
| --- | --- |
| Feedback negative control | 46 tests: 44 pass, 2 newly added regression rows fail; `artifacts/verification/feedback-red` |
| Feedback/validation repair | Build succeeds; 142 focused unit tests pass, 0 skips; `artifacts/verification/feedback-validation-pass` |
| Provider regression and interceptor preservation | 26 PostgreSQL tests pass, 0 skips; `artifacts/verification/provider-focused` |
| Initial browser attempt | Nonpassing: pinned Playwright Chromium was absent; fixture reported initialization failure and cleaned up. Matching Chromium installed before retry; `artifacts/verification/browser-focused` |
| Engineering preflight | 21 Node tests passed, including reports, stale evidence, lock contention, change selection, adapter envelopes, scope checks, and hook cap/session cases; more edge cases added as independent review identifies supported gaps |
| Engineering expanded checks | 27 Node tests pass, including staged/index inputs, changed base refs, malformed latest evidence, branch/request isolation, native envelope fixtures, and prospective artifact preservation |
| Readiness negative control | Changing only AsSingleQuery to AsSplitQuery makes the new PostgreSQL regression fail: expected one Teams reader, observed two. Production change reverted; `artifacts/verification/readiness-negative-control` |
| Readiness and capacity lock | 4 focused PostgreSQL/OS-lock tests pass, 0 skips; `artifacts/verification/readiness-lock-pass` |
| Browser receipt/history regression | 3 browser tests pass after installing the repository-matched browser; trace/network output retained; `artifacts/verification/browser-installed` |
| Ownership pilot acceptance | 158 focused unit tests pass, 0 skips, build has no warnings; fresh quick run `2026-09-05T21-38-56.640Z-fcc8081f`. Separate post-recovery and post-focus continuations failed before their guards were fixed |
| Campaigns late forbidden control | Current edit redirects as intended; changed-identity completion incorrectly redirects before the guard. 1 pass / 1 fail; `artifacts/verification/campaigns-forbidden-red` |
| Campaigns cached season control | Unchanged identity reuses its authorized cache; changed club incorrectly offers the previous club's season. 1 pass / 1 fail; `artifacts/verification/campaigns-cache-red-exact` |
| Paired evaluation fixture controls | All six historical negative/positive controls are eligible: validation 1 fail/1 pass then 2 pass; identity 6 fail/3 pass then 9 pass; durable commands 2 fail/1 pass then 3 pass. Model results remain pending; see `pr-244-evaluation.md` |
| Expanded ownership cohort | 244 focused unit tests pass, 0 skips; quick run `2026-09-05T21-55-35.080Z-fb02156e` |
| First full local gate | Pre-PR run `2026-09-05T21-56-36.507Z-f2313020` passed: engineering/guidance/build/format/contrast, 2,593 unit, 533 integration, 120 browser, 7 allowlisted optional screenshot skips. Later engineering edits require a new fresh readiness run |
| Native local compatibility | Codex and Copilot CLI both discover only the canonical skill in isolated exports and perform native checkpoint/one-block/defer lifecycles. Copilot requires actual project folder trust; `--add-dir` alone was insufficient. No global trust settings changed |
| Cross-platform engineering | Windows: 30 pass, one explicit POSIX-only skip. Linux: all 31 pass, including cancellation after the direct parent exits while a grandchild ignores SIGTERM. Linux command used `docker run --rm --init --network none` with read-only source and `node:24-bookworm` (digest `sha256:be23f54a88d34e8824c741b19b91064094f92c1c97b194144bfc8b50d67258e2`) |

A complete local run is now recorded, but its evidence cannot be reused for later engineering changes or a different commit. Hosted proof, branch protection changes, cloud discovery, and paired model evaluation remain incomplete. See `pr-244-compatibility.md` for the exact scope of native evidence.

## Deployment / repository rollout

Publish bounded reviewed commits to the implementation branch after local gates. Obtain hosted proof before changing required checks; read back and preserve unrelated protection fields and admin bypass. No product schema/API deployment or data migration is required. Any external capability that cannot be exercised must remain explicitly incomplete with its exact prerequisite and evidence, rather than be marked passed.
