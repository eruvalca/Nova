# Strengthen Nova's agent guidance, verification, and Codex/Copilot compatibility

## Goal and agreed scope

Make agents consistently apply Nova's existing rules, inspect related implementations, and prove behavior before PR review. Repair demonstrated compatibility and maintenance defects through the existing instructions, skills, hooks, and CI.

Scope: setup changes and bounded validation; independent local review for substantial behavior changes. Excluded: broad application sweeps, new agent frameworks/packages/runtime installations, personal-configuration changes, application refactors, APIs, schemas, and domain-contract changes.

## For Future Agents

This is the accepted implementation plan. Mark checkboxes only after the work is done. Record phase verification, decisions, limitations, and a short handoff summary before moving on. Preserve user settings/trust/model preferences. Run build-capable commands serially in this checkout and serialize Aspire suites across the machine. All phases were initially saved as Not started; implementation evidence belongs here, not in duplicated test-count records.

## Compatibility baseline and decisions

Research inspected Codex CLI 0.153.4, Copilot CLI 1.0.83, and Node 24.15.0. These are observed baselines, not minimum-version claims.

- Keep one root AGENTS.md and canonical .github/instructions. Codex's startup root-to-cwd discovery needs the explicit read-on-demand bridge; Copilot CLI supports applyTo. Sources: [Codex discovery](https://learn.chatgpt.com/docs/agent-configuration/agents-md), [Copilot instructions](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-custom-instructions).
- Keep .agents/skills primary and retain the existing Copilot Impeccable mirror. Both CLIs support shared recipes; discovery is not proof of loading/application. Sources: [Codex skills](https://learn.chatgpt.com/docs/build-skills), [Copilot skills](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-skills).
- Retain four provider-specific custom-agent definitions with equivalent behavior. No registry/new model pins. Sources: [Codex agents](https://learn.chatgpt.com/docs/agent-configuration/subagents), [Copilot agents](https://docs.github.com/en/copilot/reference/custom-agents-configuration).
- Preserve supported hook matchers/adapters/output. Repair root resolution and Windows commands. Sources: [Codex hooks](https://learn.chatgpt.com/docs/hooks), [Copilot hooks](https://docs.github.com/en/copilot/reference/hooks-reference).
- Avoid disputed imports, duplicate-agent precedence, and optional invocation-control fields. Use explicit read instructions and local verification.
- Treat GitHub PR review separately from CLI/cloud hooks. Current review documentation uses head-branch instructions/skills; do not repeat old base-branch/size-limit claims or assume CLI hooks execute in PR review. [Review documentation](https://docs.github.com/en/copilot/how-tos/use-copilot-agents/request-a-code-review/use-code-review).

## Phase 1: Instruction ownership, applicability, and completion

Status: Implemented; verification below

- [x] Simplify repeated root inventories into concise routing; preserve commands, boundaries, domain constraints, dual-ecosystem requirements, and missing-file handling.
- [x] Cover adding, changing, debugging, and reviewing existing behavior; revisit applicability as paths/behavior expand and read each source once unless changed.
- [x] Preserve content-based routing where filenames are insufficient (including EF-using component tests).
- [x] Add: "For a defect fix, identify the violated invariant, inspect related implementations and call sites, and verify each affected path before completion. Report the behavioral evidence and checks run."
- [x] Require separate local review before PR creation for auth/authorization, persisted/recoverable operations, async ownership, HTTP contracts, provider-sensitive persistence, and behavior spanning components; allow focused self-review for small mechanical/copy/format edits.
- [x] Reviewer gets complete diff, intended behavior, constraints, and test evidence; findings require concrete reasoning. After a finding inspect sibling paths before the next push, including suppressed review-body findings.
- [x] Narrow UI design to presentation files/Sass, API rules to endpoint/contracts/client/boundary helpers/registration, tenancy to persistence owners/data/tenancy tests with content-based test routing. Leave unrelated domain scopes unchanged.

### Verification Plan

Inspect UI, endpoint, service, persistence writer, component/provider tests, and pure policy representatives. Review the tracked inventory for missed endpoint/persistence owners and unnecessary loading.

### Phase Summary

Implemented concise routing, content-based applicability, sibling completion, separate review, and narrower scopes. Tracked-owner and representative-file checks passed; live CLI exercises are recorded below.

## Phase 2: Behavioral recipes and contradictions

Status: Implemented; follow-up checklist clarification remains open

- [x] Resolve lifecycle rule: startup server queries in initialization; browser-dependent recovery may reconcile after attachment; retain ownership checks after JS and HTTP awaits.
- [x] Update Blazor skill discovery for existing forms, async state, navigation, authentication, recovery.
- [x] Add completion routing in Blazor and feature-slice skills; consolidate applicable transition matrix into existing testing references without duplicating #244 rules.
- [x] Require rendered/outcome evidence rather than direct callback invocation for form retry/deployed interaction; use controlled delayed tasks for ordering and prove failing-before where practical.
- [x] Pair canonical examples with tests of the specific invariant rather than endorsing a whole page.
- [x] Strengthen producer -> serialization -> client -> UI contract checks in existing endpoint/testing recipes.
- [x] Narrow XML-doc policy to public/shared contracts and meaningful APIs; explain non-obvious internal ownership/invariants/effects; no bulk comment deletion.
- [ ] Complete affected contradiction cleanup: the later review found the retained Blazor
  self-check overstates interactivity, placement, and HttpContext prohibitions. Its correction
  remains open while scaffolded Account guidance is discussed; no Account refactor is implied.

| Changed behavior | Applicable evidence |
| --- | --- |
| Validation | Submit -> error -> edit -> unchanged parent rerender -> successful resubmission |
| Identity/permissions | Initial, same-role club switch, role-only change, first clubless notification |
| Async ownership | Old success/failure/cleanup cannot affect newer work or disposed component |
| Recoverable mutation | Persistence failure before dispatch, uncertain result, reload/replay, partial cleanup |
| URL state | Query and rendering agree after reset, permissions, history, return navigation |
| HTTP contracts | Producer guarantees, required fields, relationships, validation, rendering agree |
| Composed UI | Browser semantics, focus, and touch targets |

### Verification Plan

Map substantive PR #244 families to rule, recipe, and actual behavioral-test exemplar. Distinguish existing rules from changes. Validate modified skill frontmatter/references.

### Phase Summary

Implemented transition evidence and scoped regression examples, full contract-chain checks, improved skill triggers, and proportional XML documentation. Also resolved the component/DbContext contradiction and corrected an obsolete lifecycle example. Frontmatter/reference checks passed.

## Phase 3: Custom-agent alignment and drift prevention

Status: Implemented; verification below

- [x] Canonical source: four Impeccable skill agents/*.toml files.
- [x] Repair installed asset-producer and finish-reviewer; check other two.
- [x] Align Copilot bodies with narrowly enumerated path/invocation/native-tool differences.
- [x] Add dependency-free scripts/Test-AgentGuidance.ps1: optional repository root; same-format TOML comparison; known-format body/frontmatter extraction; names/descriptions; explicit provider substitutions only; missing/unsupported/drift failures; compare hook-installer copies; nonzero concise failures; no auto-repair.
- [x] Add self-test mode with temporary fixtures for parity, behavioral drift, missing file, unexpected shape, unapproved substitution.

No general parser, generator, or whole-tree sync framework.

### Verification Plan

Run positive/negative validator tests on Windows and Ubuntu CI. Normalize line endings/final newline only for identical TOMLs; do not hide arbitrary prose differences.

### Phase Summary

Canonical and installed definitions now align; the checker covers exact provider differences and installer mirrors. Windows and Ubuntu parity and negative fixtures pass. Separate review caught and closed invalid YAML scalar acceptance.

## Phase 4: Hook portability and Razor coverage

Status: Implemented; verification below

- [x] Add detector.extensions .razor/html while preserving other config.
- [x] Resolve Codex hook path from Git root, including nested launches.
- [x] Explicit Codex Windows command and Copilot PowerShell entry; retain Bash/Unix.
- [x] Safely quote paths, preserve stdin and event cwd, support spaces.
- [x] Fix manifest producer in both skill copies and update committed manifests; installer replaces recognized entries.
- [x] Preserve matchers/adapters/output/timeouts/advisory behavior/unrelated hooks.
- [x] No packages/runtime upgrades/installations; retain runtime requirement.
- [x] Update both hook references for CLI/cloud activation/trust/manual fallback; never equate presence/exit 0 with a completed scan.
- [x] Add temporary-fixture tests: clean/problem Razor with directives/conditional markup; CSS/JS; exclude .razor.cs; Codex/Copilot edit/raw patch; root/nested/spaced paths/stdin; advisory/Stop; malformed input/unavailable runtime; reinstall twice retains commands/unrelated hooks/idempotence.
- [x] Separate event-routing tests from real-detector fixture; use existing Node APIs/built-in runner.

### Verification Plan

Run fixtures without polluting checkout caches. Verify generated entries match producer and reinstall cannot undo portability. Real CLI/desktop trust-dependent execution is tracked separately.

### Phase Summary

Updated both manifest producers and generated manifests, Razor HTML routing, root resolution for launch/config/cache/Stop, native Windows commands, and hook references. Windows and Linux fixture results and degraded-detector limits are recorded below.

## Phase 5: CI and verifiable operation

Status: Implemented; verification below

- [x] Add validator/self-tests to existing CI.
- [x] Add dotnet format Nova.slnx --verify-no-changes --no-restore after restore/build.
- [x] Preserve build/contrast/unit jobs and local serial Aspire suites.
- [x] PR template: one validation record for tested revision, commands/results, review disposition, unavailable checks. No duplicated changing counts.
- [x] Add docs/agent-setup.md: layouts/canonical ownership/discovery/hooks/trust/manual fallback/official links.
- [x] Evidence records observed versions/dates, not invented minimum versions.
- [x] Preserve personal settings/trust/plugins/models/global skills; report conflicts instead of changing them.

### Verification Plan

Mechanical checks, actual tool checks, behavioral exercises, required repository gates, and independent review below.

### Phase Summary

Added CI parity/self-tests, hook tests, formatting, PR validation evidence, and setup documentation. Existing jobs/runtime versions and local Aspire gates are preserved. Final live-tool/CI results and handoff remain in progress.

## Verification and acceptance

### Mechanical

- [x] Parity/self-tests; modified skills/references.
- [x] Hook producer/routing/real detector fixtures.
- [x] Formatting/build/contrast pass.
- [x] No new packages/lockfiles/installations/unrelated artifacts.

### Actual tools

Fresh sessions in both CLIs, at root and a nested directory:

- [x] Root/scoped instruction discovery checked in fresh sessions; actual matching-source reads
  captured in CLI traces. Follow-up checkout sessions confirm current root guidance injection;
  the earlier disposable-copy trust limitation does not invalidate these later observations.
- [ ] Full recipe-loading conformance in both CLIs: not met. Codex reads verified; Copilot skipped
  selected recipes in valid follow-up sessions despite their availability. An explicit-recipe
  diagnostic loaded them but still produced incomplete behavioral analysis. No catalog-only claim.
- [x] Intended custom-agent definition selected in root/nested sessions of both CLIs.
- [ ] Native hook verification across both CLIs: Copilot root/nested advisory verified in the
  trusted checkout; Codex pending native hook trust review. Direct transport tests pass.
- [x] Respect native hook trust process; no trust changes or bypasses.
- [ ] Verify Codex desktop separately; record unavailable host/trust/runtime checks as unverified.

### Bounded behavioral exercises

Disposable copies with updated guidance, no expected defect revealed:

- [x] Both CLIs repaired seeded sibling validation stores; independent rendered resubmission passed.
- [ ] Full delayed-completion review coverage in both CLIs: completed follow-up reviews leave Copilot findings and regression scenarios incomplete.
- [ ] Full preview/paging contract coverage in both CLIs: completed follow-up reviews leave Copilot producer-to-consumer analysis incomplete.
- [x] Both policy controls found the boundary defect without invoking a visual-design workflow.

Evaluate actual source loading, sibling checks, outcome evidence, and scope. No permanent benchmark service or seeded application defect.

### Repository gates

Run serially; no concurrent build-capable commands in checkout; Aspire suites use machine-wide serialization.

- [x] dotnet build Nova.slnx
- [x] dotnet format Nova.slnx --verify-no-changes (with --no-restore after build)
- [x] dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build
- [x] dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build
- [x] dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build
- [x] Independent final review of instruction coverage, parity exceptions, generated commands, and evidence.
- [x] Resolved independent findings; pre-PR application gates passed and limitations recorded.

## Delivery

One cohesive setup-maintenance PR with reviewable commits:
1. Instructions/recipes.
2. Agent alignment/parity.
3. Hooks/Razor/installer regressions.
4. CI/docs/evidence.

## Verification record

Verification date: 2026-09-05 America/Chicago (2026-09-06 UTC). Observed Codex CLI
0.153.4, Copilot CLI 1.0.83, Node 24.15.0. Application baseline is `6f943d5`;
this branch changes setup/documentation only. Final commit/CI results follow below.

- Build passed with zero errors and three existing Sass `@import` deprecation warnings.
- Formatting (`--verify-no-changes --no-restore`) and `npm run check:contrast` passed.
- Unit: 2,558 passed; integration: 531 passed; browser: 120 passed and seven explicitly
  skipped opt-in screenshot helpers (`NOVA_A11Y_SCREENSHOTS` was unset). No failures.
- Guidance parity and normalized positive/17 negative fixture checks passed on Windows,
  including invalid YAML scalars discovered during separate review. The same checks passed on
  Ubuntu in [CI run 34007396788](https://github.com/eruvalca/Nova/actions/runs/34007396788),
  tested commit `f58f2e21e120afcd733171ea6e9f29436023716f`.
- Hook suite: Windows Node 24, 14 passed; Linux Node 24 container, 11 passed and three explicit
  Windows transport skips. Existing Ubuntu CI uses Node 20: launchers require Node 22+,
  so dispatch cases explicitly skip there and test the honest unavailable-runtime guard.
  The existing Ubuntu Node 20 adapter/installer job also passed in that CI run, retaining the
  explicit unsupported-runtime dispatch skips. No runtime was installed or upgraded locally.
- The real detector found broken Razor markup and processed clean conditional Razor. It emitted
  `DEGRADED`: optional HTML parser modules are absent. Regex analysis ran; custom properties,
  selector matching, and computed contrast remain outside that evidence.
- Full tracked-owner scan: all 29 server endpoint/result owners and 133 EF/persistence owners
  identified by source-content checks match the revised scopes. Presentation files match UI
  guidance; server policies/services do not. EF-using tests route by content from testing rules.
- Four modified skill frontmatters and references passed dependency-free checks; named test
  examples exist and relevant assertions were inspected. The skill-creator validator itself
  could not run because its Python environment lacks PyYAML; no dependency was added.
- Native custom-agent probes: Copilot root/nested selected the finish reviewer and requested
  recapture for missing evidence. Codex root and nested selected the configured role and returned
  `disposition: recapture`. Codex's initial root `--ephemeral` delegation failed with a missing
  parent-thread error; ordinary persisted-session delegation succeeded from both directories.
- Fresh Copilot repository inventory from root and `Nova.UI` found 17 enabled skills (including
  the revised descriptions) and root AGENTS.md, with no discovery errors. Inventory does not
  report live custom agents/hooks or prove skill application; those have separate probes.
- Personal duplicate names were inspected without modification. `aspire`, `aspire-deployment`,
  and `aspire-init` SKILL.md files match the repo; personal `aspire-monitoring`,
  `aspire-orchestration`, and `dotnet-inspect` differ. Use explicit repository paths for Nova
  work; reconciliation of personal copies remains the user's configuration choice.
- Codex desktop native hook execution remains **unverified**: this session has no desktop hook
  diagnostics/trust control or fresh-desktop-session test surface. CLI tests are not substituted
  for desktop evidence, and no trust/configuration was changed to bypass that limit.
- A separate interactive Codex CLI startup in the actual worktree explicitly reported
  **two new/changed hooks needing review**. Its native Hooks view showed PostToolUse and Stop
  installed, active=0, review=1 each. Only the diagnostic view was opened; it was closed without
  trusting or disabling anything. This explains why native dispatch cannot yet be claimed here.
- Separate reviewer found that matching description text could still be invalid unquoted YAML.
  Fixed the checker to reject ambiguous scalars, and added five metadata-negative cases.
- A fresh Codex full-diff review found one additional P2: Windows PowerShell's native-output
  decoding could corrupt a Unicode Git-root path. A second independent hook reviewer found
  malformed-event auditing could resolve nested configuration instead of the pinned Git root.
  Both corrections have failing-before/passing-after regressions and independent re-review with
  no remaining findings. These invariants were checked across both provider copies and both
  hook entry paths before the next push.

### Defect-family coverage

| PR #244 family | Existing guidance retained | Addition/correction and focused evidence |
| --- | --- | --- |
| Stale validation stores | Clear on edits; do not replay an unchanged error snapshot | Transition matrix requires rendered retry; creation and metadata regression examples are paired with their implementations. |
| Identity/late completions | Club ownership and cancellation | First clubless notification, role changes, late success/failure/cleanup, and busy-state evidence. |
| Recovery persistence | Persist before dispatch; retain original command | Every dispatch path and partial cleanup; startup-vs-browser-recovery contradiction resolved. |
| Contract drift | Required fields, null guards, bounded results | Producer → serialization → client → UI check with actual HTTP/client/rendered examples. |
| URL drift | Safe navigation/query handling | Corrected obsolete startup-only example; permission reset, same-route, and browser history evidence. |
| Composed UI | Semantics, touch, focus | Existing Draft browser journey linked to its actual form/table/focus assertions. |
| Piecemeal corrections | No equivalent completion requirement | Sibling-invariant inspection, separate review, and suppressed review-body findings. |
| Documentation noise | Blanket XML policy | Meaningful public/shared contracts and non-obvious internal invariants replace ceremonial documentation. |

Canonical transition examples live in the existing testing skill reference; this ledger does not
duplicate their changing test names/counts. Independent final reviews and Ubuntu setup checks are
complete. The live application-CI status is attached to [PR #249](https://github.com/eruvalca/Nova/pull/249).

### Bounded CLI exercises and their limits

Fixtures used real standalone Razor components and bUnit from the existing NuGet cache, plus
read-only async/producer/DTO/client/UI and pure-policy examples. No expected defects were disclosed
in task prompts. No seeded application defect or benchmark framework was added to the repository.

| Exercise | Codex CLI | Copilot CLI |
| --- | --- | --- |
| Form retry, root cwd | Normal persisted retry read Blazor/testing SKILL.md and focused references, fixed both siblings, demonstrated ten failing checks before repair and 24 passing afterward. Independent rendered verifier also passed the full error/edit/unchanged-parent/resubmit sequence with corrected payloads. | Fixed both siblings; independent full rendered verifier passed. Its own tests omitted the unchanged-parent rerender and corrected-payload assertion. It read the form reference directly, without a SKILL.md/transition-reference read in the trace. |
| Async ownership, nested Nova.UI | Read applicable recipes; progress identified same-role club changes, stale failure/finally cleanup, and delayed clearing. Initial review was interrupted at its bounded time limit before a final verdict. | Initial and wording-follow-up reviews found different subsets. Misses included old cleanup/feedback, visible state before awaited storage, and first clubless identity. Configuration trust caveat below prevents attributing those misses to the trusted checkout. |
| Preview/paging contract, nested Nova.UI | Read API/feature/testing recipes and identified success-body validation/truncation concerns, but the same bounded cutoff prevents a full coverage claim. | Found bounds, row identity/name, count/page consistency, and truncation; omitted required JSON fields/null-row cases. Same trust caveat. |
| Policy control, root/nested | Correct inclusive-boundary finding; actual functional-core/testing recipe reads. No Impeccable workflow, although product/design files were read during call-site exploration. | Correct inclusive-boundary finding and direct tests; no visual workflow. |

The initial Codex ephemeral form run also hit a subagent parent-thread lookup error. Its normal
persisted retry completed. Slow/buffered process output alone was not diagnosed as a broken sandbox.

**Copilot fixture validity:** initial copies had an unborn Git branch; committing the baselines
did not establish configuration trust. An interactive `/env` launch explicitly prompted for folder
trust, which was not granted. Noninteractive tool permissions and repository inventory therefore
do not prove those copies' startup instructions/hooks were applied. Preserve the partial outcomes
as observations, not evidence that the already trusted Nova checkout ignores its recipes. The
small root wording clarification now explicitly covers reading recipes before review recommendations;
the follow-up exercise does not establish its effectiveness. No broader instruction framework was added.

In the actual trusted checkout, Copilot's `/env` lists the repository instructions, skills, custom
agents, and one postToolUse hook. Separate native edit probes use temporary ignored fixtures so
configuration trust need not change. Native create events from both root and `Nova.UI` produced
audit records with repository-root cwd, `.razor`, one finding, and `emitted: true`; both agents
reported the expected `[broken-image]` advisory. The nested fixture had to be inside its allowed
working directory. An earlier outside-cwd attempt was permission denied, not a hook failure.
The first root probe additionally created an unrequested marker after receiving feedback; it
reported the scope mistake, and all probe files were removed. The nested probe left its intentional
finding unchanged without extra files. Dispatch success does not establish perfect scope adherence.

The Copilot personal dotnet plugin also reported `lspServers must be an object`; it was not changed.
All exercise processes were closed. Raw prompts, fixture code, source-read indexes, and logs remain
under the temporary `nova-agent-exercises-6df0f2805e664807bca2c1939528afd4` directory; custom-role and
trusted-checkout probes are under `nova-agent-native-probes`. This plan is the durable evidence summary.

### Follow-up behavioral acceptance

Status: Evaluation complete; full cross-CLI behavioral acceptance **not met** (2026-09-06).

The user approved reopening the two outcomes and completing smaller, separate reviews. These
runs used guidance at `6fd33f62c4bf02f1eeefa69c52b4a86f213bd91d`; subsequent changes only update this
verification record. Observed executables remain Codex CLI 0.153.4, Copilot CLI 1.0.83, and Node
24.15.0. Existing model choices were inherited. No trust, personal settings, runtime, or application
changes were made.

- [x] Complete and independently assess async-ownership reviews in both CLIs.
- [x] Complete and independently assess contract-consistency reviews in both CLIs.
- [x] Verify final evidence wording and exact fixture cleanup independently.

Method: place temporary ignored samples in this checkout, run async reviews from the root and
contract reviews from `Nova.UI`, and obtain final verdicts without a time cutoff. Neutral prompts
identify intended behavior and scope, not seeded defects. Native session records confirm the
current root `AGENTS.md` body and skill availability; successful tool results establish actual
source reads. Source inspection and proposed regression scenarios are the evidence: these review
exercises do not execute regressions or establish deployed/browser behavior.

| Review | Verified source use | Independently assessed result |
| --- | --- | --- |
| Codex async | Both Blazor/testing skill bodies, focused lifecycle/transition guidance, and direct Players/CampaignEntry implementation and regression reads. | Expected ownership families covered: identity changes, storage-before-concealment/failure, stale storage continuations, obsolete save success/failure/cleanup, disposal, and current busy ownership. Controlled-delay scenarios supplied. |
| Copilot async | Current root guidance and native skill tool available; Blazor rules and lifecycle reference read, but neither selected skill body nor testing transition reference loaded. | Partial. Found same-role club changes, delayed concealment, stale failure, and overlapping reconciliation. Missed stale `finally`/newer busy ownership; some reproduction reasoning was inaccurate. |
| Codex contract | Selected API/feature/Blazor/testing recipes and focused contract/transition references; complete supplied producer, serializer, DTO, client, and markup read. | All four expected families covered: required field presence, nested row validation, exact snapshot cardinality, and rendered truncation. Correctly distinguished malformed-payload acceptance from producer output and rejected invented sorting/uniqueness guarantees. |
| Copilot contract | Current root guidance and native skill tool available; API/service/Blazor rules and complete supplied chain read, but selected recipes and focused contract/testing references not loaded. | Partial. Found rendered truncation, but missed absent required fields and exact count/page relationships; dismissed null/invalid rows as unreachable with a trusted producer despite the required client validation contract. |

One fresh **explicit-recipe diagnostic** added only the Blazor/testing `SKILL.md` paths to the
neutral async request. Copilot then read both bodies and the lifecycle/transition references.
It identified stale catch/finally ownership and removed the first run's incorrect rendering claim,
but omitted storage-delay/failure concealment and stale post-storage continuations. It also asserted
a missing render mode despite the sample's absent host composition. Its proposed busy-state test
did not explicitly hold a newer save pending. This is partial coverage, not proof that explicit
recipe activation resolves reasoning gaps or that automatic routing works.

Scoring qualifications:

- The first `(null, "")` identity notification is skipped by the sample. Codex and the named-recipe
  diagnostic noticed the transition-matrix case, but the sample does not establish persisted startup
  state or an initial-notification effect contract. Its omission alone is not a demonstrated
  production defect, and asserting already-empty rows would not prove reconciliation occurred.
- Copilot's baseline claim that `_busy = true` never renders during the first incomplete await is
  false: [.NET 10 ComponentBase.HandleEventAsync](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Components/Components/src/ComponentBase.cs#L326-L338)
  schedules that initial render. Its older-reconciliation example also incorrectly described an old
  club payload, although the real continuation clears current state and dispatches using the current
  club. Findings were assessed by the actual code, not by severity labels or finding counts.
- Codex's async final named Teams among inspected examples; the trace establishes it as referenced
  by guidance, not directly inspected. The table credits only verified direct example reads.
- The first Copilot contract launcher accidentally hid the native skill tool with a read-only tool
  allowlist. Its completed result is retained as supplementary harness evidence only. The scored
  fresh run exposed the skill tool; the recipe omissions persisted. No failed or partial run was
  replaced by a later success claim.

Raw prompts, unchanged fixture snapshots/hashes, CLI commands, native events, final verdicts, and
scored reports remain in temporary `nova-async-acceptance-b0ed046e614b4030a3c85d4cd0aa3c13` and
`nova-contract-acceptance-a3f98962b1ac48b4b1a7df8884627eac` directories. The independent reviewer read
the actual prompts, source, tool evidence, and finals, then checked this final summary. Both
`async-acceptance-report.md` and `contract-acceptance-report.md` exist there. All nine owned sample
files retained their hashes; their three fixture directories were removed and independently checked
absent. This section is the durable result summary.

Post-exercise maintenance checks passed: `dotnet format Nova.slnx --verify-no-changes --no-restore`,
`dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build`, and `git diff --check`.
Only this plan changed after the tested guidance revision. Integration/browser results above remain
at their recorded revision; this intermediate documentation-only push does not rerun those suites.

Handoff: the requested valid reviews are finished; the outcome checkboxes above intentionally remain
open. The evidence supports retaining behavior-based tests and separate review, not asserting that
correct instructions alone ensure consistent agent execution. No new instruction text, agent
framework, or model pin was added to chase these results. Authentication scaffold treatment was
discussed separately; neither Account code nor the outstanding Blazor checklist wording was changed.

## Final Recap

Implementation changes across all five phases are delivered; the phase 2 checklist clarification
and full behavioral acceptance remain open. Separate review caught and closed three concrete
setup defects before publication: ambiguous Markdown descriptions, Unicode Git-root decoding,
and early-event nested audit configuration. Application build/format/contrast and all three suites
passed; final guidance/hook checks passed on Windows, with Linux hook evidence recorded above.
Ubuntu setup checks passed after publication and are linked above. Later changes only update this
verification record; the PR validation record identifies the tested revision and final CI status.

Remaining limitations are explicit: Codex native hooks need trust review; desktop execution was
not verified; optional detector parsers are absent. The earlier disposable-copy evidence retains
its trust qualification; valid follow-up reviews now establish incomplete Copilot recipe application
and behavioral coverage despite available guidance. The named-recipe diagnostic is also partial.
The known simple skill frontmatters were checked without adding the unavailable PyYAML dependency.
These are not clean-scan or universal agent-conformance claims.

Handoff: implementation changes are delivered; behavioral acceptance was evaluated and remains
unmet as recorded above. Preserve personal configuration, start fresh sessions, and consult the
PR's final CI status. Future substantial work must use the
source/transition/sibling evidence record and separate review rather than assuming a skill catalog
entry proves correct behavior. Revisit results through ordinary PRs, with no monitoring framework.

## Deployment Plan

No app deployment, API change, or schema migration. Deliver one setup-maintenance PR, keep the
four change groups reviewable, and leave merge to the normal review process. After checkout/merge,
start fresh CLI/desktop sessions and review changed hooks through native trust controls; this work
does not grant trust. Keep the existing all-suite pre-merge gate and machine-wide Aspire serialization.
