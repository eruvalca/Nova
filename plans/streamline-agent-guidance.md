# Streamline Repository Agent Guidance

Reduce context cost and eliminate skill metadata failures across `.github/instructions/`
and `.github/skills/` without losing unique correctness rules, architectural direction,
activation coverage, or practical recipes.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary** (what was done, key
decisions, anything needed to continue with zero context); run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

Use a balanced compression strategy:

- Preserve every unique correctness, security, tenancy, concurrency, validation,
  rendering, and test-selection rule.
- Keep short representative recipes where code is more useful than prose.
- Remove repeated rationale, synonym-heavy trigger lists, duplicated command blocks,
  and long examples already represented by canonical repository code.
- Prefer one canonical owner for each rule and concise cross-references elsewhere.
- Cap every skill frontmatter description at 900 characters.
- Merge or split reference files only when that improves semantic ownership.

Baseline findings:

- `add-blazor-ui/SKILL.md` has a 1,140-character description and exceeds the
  1,024-character platform limit.
- No other skill description exceeds 1,024 characters, but
  `add-domain-persistence` is close enough to the agreed 900-character cap to tighten.
- The largest safe reductions come from duplicate validation, result, trace ID,
  testing-command, and harness-selection guidance.
- No instruction or skill currently needs to be split because of mixed semantics.

## Phase 1: Establish Canonical Rule Ownership

Status: Complete

Suggested executor: orchestrator

- [x] Record a rule-ownership map for duplicated topics before editing:
  dual-layer validation, OneOf versus ServiceResult, ProblemDetails trace IDs,
  logging, functional-core testing, tenant-filter tests, and SQLite versus PostgreSQL
  test selection.
- [x] Assign dual-layer validation rationale and the caller/service table to
  `service-layer.instructions.md`; retain only surface-specific mechanics in
  validation and endpoint guidance.
- [x] Assign OneOf and exhaustive matching conventions to
  `csharp-conventions.instructions.md`; keep only boundary-specific reminders in
  service and functional-core files.
- [x] Assign HTTP `ProblemDetails` trace ID requirements to
  `api-endpoints.instructions.md`; keep only the automatic `ToHttpResult` behavior
  where service guidance needs it.
- [x] Assign complete logging conventions to `csharp-conventions.instructions.md`.
- [x] Assign test-project and harness selection to `testing.instructions.md`, while
  retaining domain-specific assertions in tenancy and functional-core guidance.
- [x] Confirm all planned cross-references use valid relative repository paths and
  stable section headings.

### Verification Plan

- Search the guidance tree for the canonical topic phrases and confirm each full rule
  has one owner; secondary files contain only a short reference or surface-specific
  requirement.

### Phase Summary

Canonical ownership is now explicit: service-layer owns dual-layer validation;
C# conventions own OneOf and logging; API endpoint rules own HTTP ProblemDetails
trace IDs; testing rules own harness selection. Secondary files retain only
surface-specific requirements or short references. Link validation found and removed
two stale references to the deleted `plans/dbcontext-tenancy-design.md`.

## Phase 2: Tighten Always-On Instruction Files

Status: Complete

Suggested executor: orchestrator

- [x] Reduce `csharp-conventions.instructions.md` by about 30%: collapse modern C#
  preference bullets, remove duplicate OneOf wording, shorten documentation/logging
  prose, and move large extension-member and entity-to-DTO examples to an appropriate
  skill reference while retaining `Try*`, exhaustive matching, source-generated
  logging, and EF projection safety rules.
- [x] Reduce `blazor-architecture.instructions.md` by about 20%: retain project
  boundaries, SSR-first defaults, render-mode constraints, component service
  boundaries, `EventCallback`, and inline-style safety; move the detailed persistent
  state recipe and feature-folder tree to `add-blazor-ui` references.
- [x] Reduce `ef-core-tenancy.instructions.md` by about 20%: retain tenant security,
  DbContext selection, filter, migration-service-provider, ordering, and concurrency
  rules; remove duplicated test selection and procedural migration commands.
- [x] Reduce `testing.instructions.md` by about 25%: retain the project-selection
  matrix, MTP constraints, xUnit v3 conventions, HTTP/UI boundary assertions, and test
  isolation; move the Aspire/Playwright procedure to its existing dedicated skill.
- [x] Reduce `service-layer.instructions.md` by about 20%: retain validation ordering,
  DI registration, lock order, retry-safe context lifetime, and result-boundary rules;
  replace duplicated general conventions and canonical-example narration with links.
- [x] Reduce `api-endpoints.instructions.md` by about 15%: retain route constants,
  `CreatedAtRoute` argument order, endpoint metadata, authorization, antiforgery,
  binding, and null-payload rules; compress endpoint-removal procedure and duplicated
  validation rationale.
- [x] Tighten `functional-core.instructions.md` and
  `validation.instructions.md` by removing only duplicated testing/result rationale
  and excess related-link prose.
- [x] Keep `observability.instructions.md` essentially unchanged except for any
  duplicate trace ID ownership reference.
- [x] Narrow `applyTo` globs only where content is demonstrably irrelevant:
  remove broad test/entity/client-service targeting after moving the rules that
  justified it; do not narrow `**/*.cs` if doing so would omit production projects or
  repository-wide C# conventions.

### Verification Plan

- Compare each edited file against the baseline checklist and confirm every
  must-preserve rule remains.
- Run a script that reports line and byte counts for every instruction file; confirm
  reductions are material but no file was split solely to reduce size.
- Inspect frontmatter `applyTo` patterns and confirm every retained rule applies to all
  file types that need it.

### Phase Summary

The nine instruction files fell from 67,476 bytes / 982 lines to 59,863 bytes /
758 lines. Large examples and procedures moved to existing on-demand skills or were
replaced by canonical implementation links. No `applyTo` glob was narrowed because
each still covers files needing at least one retained rule. Reviewer-identified
direction losses were corrected: scoped `using var`, generated-source documentation
exclusion, static logging fallback, and the migration add-before-verify path.

## Phase 3: Tighten Skill Metadata and Entry Points

Status: Complete

Suggested executor: smaller-model sub-agent for mechanical description edits, with
orchestrator review of activation coverage

- [x] Rewrite every skill description to stay at or below 900 characters while
  preserving distinct `USE FOR`, `DO NOT USE FOR`, and orchestration signals.
- [x] Reduce `add-blazor-ui` trigger synonyms such as page/component/new Razor file,
  render-mode variants, and duplicate non-firing event symptoms.
- [x] Tighten `add-domain-persistence` enough to provide margin below 900 characters.
- [x] Remove redundant low-level triggers from `add-api-endpoint` and `nova-testing`
  without weakening routing between overlapping skills.
- [x] Keep each `SKILL.md` as the short decision and execution checklist; move detailed
  explanation to references and remove prose already enforced by instruction files.
- [x] Review all inter-skill `INVOKES` and `DO NOT USE FOR` boundaries after edits to
  ensure feature, endpoint, domain, UI, functional-core, and testing requests still
  route unambiguously.

### Verification Plan

- Parse every `.github/skills/**/SKILL.md` frontmatter description and fail if any
  exceeds 900 characters.
- Manually test representative trigger phrases for each overlapping skill and confirm
  the intended skill remains the clearest match.

### Phase Summary

All seven descriptions are within the 900-character budget. The longest are now
`add-domain-persistence` at 861 characters and `add-blazor-ui` at 847. Trigger,
exclusion, and invocation boundaries remain explicit across feature, endpoint,
domain, UI, functional-core, and testing skills.

## Phase 4: Deduplicate and Refocus Skill References

Status: Complete

Suggested executor: smaller-model sub-agents may handle independent skill directories;
the orchestrator must review cross-skill ownership and links

- [x] Cut `add-feature-slice/references/service-result-patterns.md` substantially by
  removing the repeated dual-layer table, result-type documentation, trace-ID prose,
  logging example, and long duplicate service implementations; retain the result
  mapping recipe and one concise representative pattern.
- [x] Tighten `add-feature-slice/references/input-and-validation.md` by removing
  duplicated validation rationale and the repeated `ProfilePhotoValidator` exception,
  while retaining the annotated input recipe and cross-field validation example.
- [x] Tighten `add-api-endpoint/references/handlers-and-results.md` by removing the
  redundant `Results<T>` example, repeated trace-ID try/catch example, and oversized
  complete endpoint sample; retain handler shape, `ToHttpResult`, created responses,
  and mapping guidance.
- [x] Decide whether
  `add-api-endpoint/references/metadata-auth-antiforgery.md` is clearer as a small
  focused reference or as a section in `handlers-and-results.md`; merge only if the
  resulting file remains easy to navigate.
- [x] Remove duplicated harness-selection, common-conventions, and run-command blocks
  from both `nova-testing` harness references; keep those topics once in
  `nova-testing/SKILL.md` or one semantically named shared reference.
- [x] Tighten `add-blazor-ui` lifecycle and parameter references where they repeat
  always-on architecture rules; retain concrete lifecycle, persisted-state,
  `EventCallback`, binding, and validation recipes.
- [x] Move any large examples removed from instruction files only when they add a
  recipe not already present; otherwise link to the canonical repository
  implementation instead of relocating duplication.
- [x] Review all remaining references for duplicated command blocks, rationale, and
  canonical examples, and remove only repetitions that add no operational step.

### Verification Plan

- Run a repository-wide Markdown link check or a targeted script over
  `.github/instructions` and `.github/skills`; expect no broken local links.
- Search for duplicated command blocks and distinctive repeated paragraphs; expect a
  single canonical copy unless a short surface-specific reminder is justified.
- Compare every skill checklist with its references and confirm all referenced files
  and headings exist.

### Phase Summary

Skill content fell from 126,454 bytes / 2,595 lines to 112,697 bytes / 2,311
lines. The largest reductions removed duplicate service implementations, endpoint
examples, harness-selection prose, and repeated run commands. The focused
`metadata-auth-antiforgery.md` reference remains separate because its security scope
is distinct from handler/result conversion.

## Phase 5: Final Guidance Regression Review

Status: Complete

Suggested executor: orchestrator, followed by a read-only reviewer

- [x] Review the complete diff for accidental loss of security, tenancy, concurrency,
  validation, rendering, observability, endpoint contract, and test-harness rules.
- [x] Verify no cross-reference creates a circular reading requirement where two files
  defer the same rule to each other.
- [x] Verify concise files remain self-sufficient at activation time: instructions
  state mandatory rules, while skills state when to activate and how to execute.
- [x] Re-measure all file sizes, line counts, and skill description lengths; record the
  before/after totals in the phase summary.
- [x] Confirm all Markdown and YAML frontmatter parses cleanly.
- [x] Ask a read-only reviewer to identify lost direction, ambiguous routing, stale
  links, or reductions that removed necessary recipe steps; resolve high-confidence
  findings.

### Verification Plan

- Description-length script: all skill descriptions are at most 900 characters.
- Frontmatter parse check: every instruction has valid `applyTo`; every skill has a
  valid `name` and `description`.
- Local-link check: all repository-relative Markdown links resolve.
- Diff review: no unique must-preserve rule identified in Phases 1-2 was removed.
- Size report: show before/after bytes and lines for all guidance files.

### Phase Summary

All metadata, local Markdown links, explicit repository Markdown paths, and diff
formatting pass. Total guidance fell from 193,930 bytes / 3,577 lines to 172,902
bytes / 3,072 lines: 21,028 bytes (10.8%) and 505 lines (14.1%) removed. A read-only
review found two compressed-away qualifiers and a stale path; all were corrected, and
the follow-up review reported no remaining findings or regressions.

## Final Recap

Streamlined all repository instruction and skill guidance with balanced compression.
Every unique correctness and safety rule remains, duplicated topics now have canonical
owners, large examples defer to focused recipes or canonical code, and all skill
descriptions are below the agreed 900-character cap. No files were split, and the
endpoint metadata/auth/antiforgery reference remained separate because its semantic
scope is useful.

## Deployment Plan

No runtime deployment is required. Review the committed instruction pass plus the
remaining working-tree skill, correction, and plan changes as one logical change.
After merge, restart or reload Copilot/agent sessions that cache repository guidance
so the updated skill metadata and instructions are discovered.
