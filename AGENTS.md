> Single source of truth for repo-wide agent instructions — read by OpenAI Codex, Copilot CLI,
> Copilot cloud agent, Copilot code review, and VS Code agent mode. Path-scoped rules live in
> `.github/instructions/` and load per the matching rules below. All agent-facing guidance in
> this repo must stay compatible with **both GitHub Copilot and OpenAI Codex** — see
> "Dual-ecosystem compatibility".

# Repository Overview

- This solution is a .NET 10 Blazor web app.
- The server/host project is `Nova/Nova.csproj`.
- The Blazor WebAssembly project for interactive components is `Nova.Client/Nova.Client.csproj`.
- The shared UI library is `Nova.UI/Nova.UI.csproj`.
- The shared models, interfaces, endpoints, results, validation, and utilities project is `Nova.SharedKernel/Nova.SharedKernel.csproj`.
- The automated browser workflow suite (Playwright, local-only) is `Nova.Browser.Tests/Nova.Browser.Tests.csproj`.
- Aspire instrumentation is configured in `Nova.AppHost/Nova.AppHost.csproj` and `Nova.ServiceDefaults/Nova.ServiceDefaults.csproj`.

## Build & validation

- Build: `dotnet build Nova.slnx`
- Within one worktree, do not run build-capable `dotnet build` or `dotnet test` commands concurrently: building `Nova/Nova.csproj` may execute `npm ci`, which replaces the shared `Nova/node_modules` tree. Run `dotnet build Nova.slnx` first, then use `--no-build` for tests.
- Keep Aspire-backed integration and browser suites serial across all worktrees on this machine. Docker capacity is shared even though run identities, ports and data are isolated; concurrent suites can exhaust bounded hydration/storage retries. Keep application source and generated assets fixed while a browser suite runs so its result identifies one build.
- Run: `dotnet run --project Nova.AppHost` (delegates through the Aspire 13.5 CLI bundle; Aspire provisions PostgreSQL 18 and the Azurite blob emulator for `profile-photos`, and exposes the dashboard plus `/health`/`/alive`). `Nova` has no usable connection string or blob client without the AppHost.
- OpenAPI document: `/openapi` (Development only).
- Format check (required before commit): `dotnet format Nova.slnx --verify-no-changes`; apply fixes with `dotnet format Nova.slnx`
- Finish source-writing `dotnet format` before resuming edits or other source-writing operations in the same worktree; it can overwrite changes made after it loaded the workspace.
- Bootstrap theme: `npm ci` then `npm run build:css` (from `Nova/`) compiles the Sass theme to `Nova/wwwroot/css/bootstrap-theme.css`; `npm run check:contrast` validates WCAG contrast and asserts no Bootstrap-blue literals. Run both from `Nova/` after any `scss/` or `package.json` change.
- Environment-gated capabilities — the approved design comp flow, accessibility captures, and the Aspire/browser evidence flags — read their variables from the **user** scope. A shell process does not inherit a variable set after it started, so check `[Environment]::GetEnvironmentVariable('NAME', 'User')` before concluding a capability is unavailable on this machine.
- After the build, unit tests: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build`
- Integration tests (require the Aspire AppHost for PostgreSQL): `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --no-build` — see the local PR test gate below.
- Browser tests (Playwright against the Aspire AppHost, local-only): `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj --no-build` — requires a one-time browser download per machine: `Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1 install chromium`.

### Pull request test gate

Both Aspire-backed suites start their own AppHost through the test fixture, so run `dotnet test`
directly rather than starting one first. CI only builds and runs unit tests; integration and browser
tests are local-only.

- **Unit and integration:** run both full suites locally and ensure they pass before opening a PR;
  re-run both and ensure they pass before merge. On intermediate pushes, run unit tests always and
  integration tests for provider, HTTP-boundary, or EF changes; run integration tests when their
  impact is uncertain.
- **Browser applicability:** browser evidence is required when application or browser-suite inputs
  change: source (including shared test helpers), dependencies, build/runtime configuration,
  discovery, or generated assets. Otherwise record browser **N/A** with the impact rationale, for
  example for documentation or unrelated unit-test changes. Changes to test commands, guidance, or
  validation policy still need appropriate checks and review of their effect on coverage and enforcement.
- **Browser selection:** select by affected behavior, including backend services and HTTP contracts
  used by browser flows, as well as UI, CSS, and JS interop. Record named scenarios/classes and the
  selection rationale. Use the full suite for broad or uncertain impact, especially shared
  authentication, recovery, navigation/layout, rendering, or HTTP contracts.

| Browser validation stage | Required evidence |
| --- | --- |
| Before opening a PR | Selected browser scenarios pass; the full suite may be explicitly pending for a focused change. |
| Intermediate pushes | Re-run the selection affected by the push. |
| Before merge | A full browser-suite pass covers the final inputs, unless browser N/A applies. Targeted evidence is insufficient. |

Reuse a browser pass only while the application and browser-suite inputs listed above remain
unchanged. In the validation record, identify the tested revision and any uncommitted changes included
in the run, and compare them with the current revision, including incoming changes after a rebase or
merge. Changes to these inputs require new evidence under the stage gate; later documentation-only
edits can reuse the pass with the comparison recorded.

## Repository decisions

- Package versions are managed centrally in `Directory.Packages.props` (`ManagePackageVersionsCentrally`). Add or update versions there — never put a `Version` attribute on a `PackageReference`.
- SDK version is pinned in `global.json` (roll-forward `latestFeature`).
- This is early, pre-release development with **no production data or deployed users**. Never design for backwards compatibility: breaking schema/API/contract changes are preferred over legacy-tolerant designs, and there is no existing data to preserve.
- Identity emails are no-ops (`IdentityNoOpEmailSender`, `RequireConfirmedAccount = false`) and no external login providers are registered. Do not build features that assume email delivery or third-party login.
- The Bootstrap theme is Sass-compiled into `Nova/wwwroot/css/bootstrap-theme.css` and is **not committed** to source control — the Sass sources in `Nova/scss/` are the source of truth, and the CSS is regenerated by the `BuildBootstrapTheme` MSBuild target (see `.github/instructions/bootstrap-theme.instructions.md`).
- The UI follows the "Fieldhouse Wayfinding" design system. `PRODUCT.md` (product intent) and `DESIGN.md` (tokens, named rules, navigation semantics) are the **design-system source of truth**; per-surface briefs are in `.impeccable/surfaces/`. Never add raw hex in `*.razor.css` — use `--bs-*` semantic variables (see `.github/instructions/ui-design.instructions.md`).
- **Generated comps are indicative, not measurable.** A generated comp may misrepresent operational
  type density or omit parts of the composition, especially the shared shell.
  Reference a new comp on the surface's own shell at the target breakpoint, and confirm before locking
  it that it depicts the composition the brief commits to. A comp-diff score only measures a comp that
  depicts that composition: a below-threshold score against one that does not is a comp defect to
  raise, not a build defect to iterate against. If you cannot inspect the rasters in your environment,
  say so plainly, establish the composition check by measuring each candidate against the surface's own
  capture instead of asserting it, and get the user's confirmation before locking.

- **Issue roadmaps are hand-maintained.** Each parent issue carries a `<!-- native-child-roadmap:start -->` … `<!-- native-child-roadmap:end -->` block; nothing generates it, and a block exists only when the issue has native children. Before editing one, reconcile membership and state with `gh api repos/eruvalca/Nova/issues/<n>/sub_issues --paginate`. Mark a child complete only when its closing PR is merged into `main` — a validated revision may be off-main when the merge was squashed. A superseding comment resolves an old one. Procedure: `sync-epic-roadmap`.

## Dual-ecosystem compatibility (mandatory)

All agent-facing guidance — instructions, skills, custom agents, and hooks — must work for **both GitHub Copilot and OpenAI Codex**, plus any other agent that reads the open standards. This is a hard rule, not a preference:

- This `AGENTS.md` is the single repo-wide instructions file for every ecosystem. Do not add a parallel `.github/copilot-instructions.md`; repo-wide rules go here, path-scoped rules go in `.github/instructions/`.
- Copilot auto-loads `.github/instructions/*.instructions.md` via their `applyTo` frontmatter; Codex does not. Those files are also written to be read on demand (see Instruction and skill routing below) and must never depend on Copilot-only loading behavior.
- Skills use the open Agent Skills format. `.agents/skills/` is read by both Codex and Copilot and is the primary location; any skill that also ships a Copilot-specific copy under `.github/skills/` must keep the `.agents/skills/` copy complete and in sync (same version, same behavior).
- Custom agents ship in both formats: `.github/agents/*.agent.md` for Copilot and `.codex/agents/*.toml` for Codex. The four Impeccable definitions in `.agents/skills/impeccable/agents/*.toml` are canonical; installed and Copilot copies must preserve their behavior with only documented provider substitutions.
- Hooks ship in both formats: `.github/hooks/*.json` for Copilot, `.codex/hooks.json` for Codex.
- When a rule, skill, agent, or hook changes, update every ecosystem copy in the same change — never let them drift.

## Instruction and skill routing

Before adding, changing, debugging, or reviewing behavior, inspect `.github/instructions/` and
read every `*.instructions.md` whose `applyTo` globs match the affected paths. Copilot CLI supports
automatic matching; Codex must read these sources explicitly. Treat matching rules as mandatory.
Read each source once unless it changes; reconsider applicability when paths or behavior expand.
If a referenced instruction file cannot be found, state which file is missing and ask the user to
provide it before proceeding with the affected area.

Filenames are only the first routing pass. Read rules for the behavior under inspection too:
tests using EF or `TenancyTestHarness` need tenancy rules; HTTP serialization and DTO changes need
API rules; components handling validation need validation rules. A pure policy or documentation
change does not by itself require a visual-design workflow.

| Concern                                                   | Rules in `.github/instructions/`                               | Recipe in `.agents/skills/`                                                   |
| --------------------------------------------------------- | -------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| C# style, OneOf, documentation, logging                   | `csharp-conventions.instructions.md`                           | Relevant feature recipe                                                       |
| Blazor forms, state, navigation, authentication, recovery | `blazor-architecture.instructions.md`                          | `add-blazor-ui`                                                               |
| UI design and Sass theme                                  | `ui-design.instructions.md`, `bootstrap-theme.instructions.md` | `impeccable` when design work is needed                                       |
| Service or complete HTTP/WASM feature                     | `service-layer.instructions.md`, `validation.instructions.md`  | `add-feature-slice`                                                           |
| Endpoints, wire contracts, HTTP clients                   | `api-endpoints.instructions.md`                                | `add-api-endpoint`                                                            |
| EF, tenancy, schema, persistence helpers                  | `ef-core-tenancy.instructions.md`                              | `add-domain-persistence`                                                      |
| Season/campaign lifecycle                                 | `season-lifecycle.instructions.md`                             | `add-domain-persistence`                                                      |
| Participation and saved placement decisions               | `placement-decisions.instructions.md`                          | Relevant feature recipe                                                       |
| Deterministic policy extraction                           | `functional-core.instructions.md`                              | `extract-functional-core`                                                     |
| Durable activity feed and attention projections           | Tenancy, service, and API rules above                          | `add-activity-feed`                                                           |
| Tests and behavioral verification                         | `testing.instructions.md`                                      | `nova-testing`; `aspire-playwright-validation` for one-off browser acceptance |
| Telemetry and correlation                                 | `observability.instructions.md`                                | Relevant feature recipe                                                       |
| Epic issue roadmaps (GitHub issues)                       | — (repo-wide rule in Repository decisions)                     | `sync-epic-roadmap`                                                           |

Before implementation or review recommendations, read the selected recipe's `SKILL.md` and its
applicable references, including for existing behavior. Record the sources actually read with
the validation evidence; an entry in the skill catalog is not evidence that its instructions
were applied. Generic Aspire, .NET inspection, and Playwright recipes also live in
`.agents/skills/`; choose them by the actual operation.

## Completion and review

### One validation record

- Keep one authoritative validation record per change. Reuse the issue's existing record; otherwise
  use `docs/<issue-or-topic>-validation.md`. The PR body links to it. Evidence-packet indexes and
  issue updates link to the record instead of copying commands, test counts, or review summaries.
- Record the scope, tested revision (including any later documentation-only changes), guidance
  actually read, relevant transitions and sibling paths proved, exact commands/results, review
  dispositions, and remaining limitations. Link named tests and curated artifacts rather than
  narrating execution. A focused documentation change needs evidence appropriate to its content,
  not an application transition matrix.
- Keep the current result for each check with its tested revision. Retain concise material failure
  dispositions: what failed, cause or unresolved status, fix, and confirming evidence. A green rerun
  alone does not resolve an unexplained failure or establish that contention caused it. Resolve
  required failures before merge. Record each review finding once with its source, disposition, and
  evidence; link any required original review or approval artifact. Do not maintain a second
  execution diary in the PR or capture README.
- The recipes select small, relevant evidence; they do not waive applicable build, format, unit,
  integration, browser, migration-model, or design checks. Apply the PR-stage, browser applicability,
  and evidence-reuse rules in the [test gate](#pull-request-test-gate), identify the revision of any
  reused unaffected-suite result, and complete the required final validation before merge. An
  unavailable check remains a limitation, not a pass.

### Review triage

- For a defect fix, identify the violated invariant, inspect related implementations and call sites,
  and verify each affected path.
- Before opening a PR, obtain a separate local review for changes affecting authentication or
  authorization, persisted/recoverable operations, asynchronous state ownership, HTTP contracts,
  provider-sensitive persistence, or behavior spanning multiple components. Use another reviewer
  or a fresh agent context in either CLI; no particular custom agent or framework is required.
  Small copy, formatting, and isolated mechanical edits may use a focused self-review.
- Give the reviewer the complete diff, intended behavior, applicable constraints, and test evidence.
  Require concrete findings supported by code and reproducible reasoning; the implementer's summary
  is context, not proof.
- Inspect human and bot comments, inline threads, and suppressed findings in review bodies.
  Suppression is not resolution. Check each finding against the agreed scope, applicable rule, and
  actual behavior before changing code or adding permanent guidance.
- Fix demonstrated defects in the change and inspect sibling paths for the same invariant before
  the next push. Explain inapplicable findings with evidence. Distinguish optional improvements from
  defects; keep unrelated cleanup or speculative optimization out of the change and track a follow-up
  when warranted. An unresolved in-scope defect cannot be relabeled as an optional improvement.
- Resolve a review thread only after verifying its fix or explaining why it does not apply in the PR.
  Keep unresolved findings explicit in the validation record; passing tests alone do not resolve them.
- Diagnostic suppressions, weakened validation, skipped tests, and disabled checks are quality-control
  changes: require an explicit rationale and review of their effect on coverage and enforcement.
  Keep legitimate exceptions narrowly scoped and preserve justified existing exceptions. Fix the
  underlying failure; do not hide or bypass it merely to make verification green.

### Durable design evidence

- Commit durable design inputs and decisions: product/design documents, the design sidecar,
  surface briefs, shared configuration, approved comps with provenance and the recorded check that
  each depicts the composition it locks, and the validation record. Curate representative final
  captures and automated visual-test baselines.
  Impeccable build state, scaffolds, rejected concepts, repeated captures, crops, heatmaps and raw
  run output are local artifacts; `.gitignore` excludes them unless explicitly retained.
  Add narrow `.gitignore` exceptions for each new approved comp/provenance and curated evidence
  package; do not force-add an entire generated directory. Existing tracked artifacts are not
  removed by new ignore rules; migrate them deliberately within the relevant change's scope.
  Before untracking evidence, preserve required approval and failure history in a durable archive
  with a revision/checksum and a link from the validation record. An ignored local file alone is
  not a shared archive. Fix references to archived evidence, and preserve local files needed to
  resume work. This retention policy does not waive any design or validation check.
