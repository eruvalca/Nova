# Add `add-blazor-ui` Skill

Add a model-invoked Agent Skill that gives agents a step-by-step decision procedure for building
Blazor pages and components in Nova (placement, page-vs-component, render mode, lifecycle,
parameters/`EventCallback`/binding, `EditForm`), restructure
`.github/instructions/blazor-architecture.instructions.md` into declarative rules that point at it,
and wire the skill into `copilot-instructions.md`, `add-feature-slice`, and `nova-testing`.

**Documentation-only.** No `Nova.UI`, `Nova.Client`, or `Nova` component code changes.

## Background

Every backend concern in this repo has a paired *rules file* + *recipe skill*
(`api-endpoints` → `add-api-endpoint`, `testing` → `nova-testing`, etc.). Blazor is the only major
concern with rules but **no recipe skill**, and `add-feature-slice` orchestrates
input → service → endpoint → WASM client → tests and stops **before the UI**.

Observed agent failure modes this work must eliminate:

- Choosing a page when a non-routable component was correct (and vice versa).
- Adding interactivity that static SSR + enhanced nav/forms already covers.
- Picking `InteractiveServer` where `InteractiveAuto` was required, or omitting `@rendermode`
  entirely on a component that has `@onclick` handlers (compiles, renders, silently does nothing).
- Wrong lifecycle method (`OnInitializedAsync` vs `OnParametersSet` vs `OnAfterRenderAsync`),
  duplicate fetches across prerender + interactive attach, missing `Initialized` guard, missing
  derived-state rebuild after `[PersistentState]` restore.
- `Action` instead of `EventCallback` (no automatic `StateHasChanged`), mutating `[Parameter]`
  properties for owned state, misuse of `@bind` / `@bind:after`, unnecessary `StateHasChanged`.

## Ground truth in this repo (use as canonical examples)

| Pattern | File |
| --- | --- |
| Base class, `ComponentCancellationToken`, `DisposeAsyncCore` | `Nova.UI\Components\NovaComponentBase.cs` |
| Interactive page: persisted state, `Initialized` guard, query params, debounce CTS, paging/truncation | `Nova.UI\Features\Players\Pages\Players.razor(.cs)` |
| Static SSR page (no `@rendermode`) | `Nova.UI\Features\Clubs\Pages\ClubDetail.razor` |
| Form component: `[Parameter, EditorRequired]`, `EventCallback`, `IValidatableObject` reusing `InputValidator` | `Nova.UI\Features\Players\Components\PlayerForm.razor.cs` |
| Per-instance `@rendermode` on a child from a static SSR host page | `Nova\Components\Account\Pages\Manage\DeletePersonalData.razor` |
| Cross-feature shared component | `Nova.UI\Shared\ConfirmDeleteDialog.razor(.cs)` |
| Existing bUnit component tests | `Nova.Unit.Tests\Components\*.cs`, `Nova.Unit.Tests\Players\PlayerComponentsTests.cs` |
| Skill format/frontmatter to imitate | `.github\skills\add-api-endpoint\SKILL.md`, `.github\skills\nova-testing\SKILL.md` |
| Rules-file format to imitate (banner → skill) | `.github\instructions\testing.instructions.md` |

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to
`Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue
with zero context); run the phase's **Verification Plan** and record the result before moving on.
When all phases are done, fill in **Final Recap** and **Deployment Plan**.

Constraint that applies to every phase: **do not modify any `.razor`, `.razor.cs`, `.razor.css`, or
other product code.** Only files under `.github/` and this plan file change.

## Phase 1: Ground the guidance

Status: Complete

Establish the factual basis so the skill documents *this repo's* patterns and is correct against
.NET 10 Blazor semantics, rather than generic advice.

Suggested executor: orchestrator (findings feed directly into Phase 2 authoring)

- [x] Read every `Nova.UI` component/page pair listed in the ground-truth table and record the
      concrete conventions actually in use (render mode per file, lifecycle overrides used, where
      state is a field vs. property, `EventCallback` usage, DI style).
- [x] Record which existing pages are static SSR vs. `InteractiveAuto`, and the observable reason for
      each, so the render-mode decision tree matches reality (e.g. `ClubDetail` static vs.
      `Players` interactive).
- [x] Verify .NET 10 Blazor semantics for each rule the skill will assert — lifecycle order and
      prerender double-invocation, `[PersistentState]` on public properties, `EventCallback` vs.
      `Action` re-render behavior, `@bind:after`, `OnAfterRenderAsync(firstRender)` — against
      official Microsoft Learn Blazor documentation. Note any repo rule that conflicts with the docs
      and surface it to the user instead of silently "correcting" the repo.
- [x] Confirm `Nova.UI\_Imports.razor` sets `NovaComponentBase` as the default base type, and capture
      what `_Imports.razor` already provides so the skill doesn't tell agents to re-add usings.
- [x] Write the findings to `plans/add-blazor-ui-skill-findings.md` (working notes; delete in Phase 5).

### Verification Plan

- `plans/add-blazor-ui-skill-findings.md` exists and lists, per canonical file, the render mode,
  lifecycle overrides, and parameter/callback patterns observed.
- `git status --short` shows no modifications outside `plans/`.

### Phase Summary

Findings recorded in `plans/add-blazor-ui-skill-findings.md`.

Key facts established:

- **Render-mode ledger**: static SSR = `ClubDetail` + all Identity pages; `InteractiveAuto` =
  `Players`, `PlayerDetail`, `ClubOnboarding`, `ManageProfilePhoto`, `ProfilePhoto`, `Auth`;
  **`InteractiveServer` has zero usages** — the skill treats it as a last resort needing
  justification. Per-instance `@rendermode` islands are used from static SSR Identity pages.
- **Lifecycle reality**: `OnInitializedAsync` dominates; `OnParametersSet` appears once (`Players`,
  projecting `[SupplyParameterFromQuery]` behind an apply-once guard); `OnAfterRender{Async}`,
  `SetParametersAsync`, `ShouldRender`, `@key`, `@bind:after` have **zero usages**;
  `StateHasChanged` appears only twice, once as `await InvokeAsync(StateHasChanged)`.
- **`_Imports.razor`** already supplies `@inherits NovaComponentBase` and
  `@using static ...Web.RenderMode`, so the skill must not tell agents to re-add them.
- **Doc verification (Microsoft Learn, .NET 10)**: `[PersistentState]` on public properties,
  `OnParametersSet` running on every parameter set, `OnAfterRender` being the DOM/JS-safe point,
  `ShouldRender` skipping non-first renders, and `EventCallback` auto-calling `StateHasChanged` on
  the parent were all confirmed. **No conflicts** between repo rules and official docs — nothing to
  escalate.
- **Seven concrete documentation gaps** identified, which became the outline for the skill's five
  reference files.

Verification: findings file written; `git status --short` showed changes confined to `plans/`.

## Phase 2: Author the `add-blazor-ui` skill

Status: Complete

Create `.github/skills/add-blazor-ui/` following the structure of `add-api-endpoint` (a short
`SKILL.md` checklist that links to focused `references/*.md` files).

Suggested executor: orchestrator (this is the core reasoning artifact; do not delegate)

- [x] Create `.github/skills/add-blazor-ui/SKILL.md` with frontmatter matching the repo's convention:
      `name`, and a `description` containing a one-line purpose plus `USE FOR:` / `DO NOT USE FOR:` /
      `INVOKES:` clauses. `USE FOR` must include the phrasings agents actually hit — add a Blazor
      page, add a component, make a component interactive, choose a render mode, fix a non-firing
      `@onclick`, add a parameter/`EventCallback`, wire an `EditForm`, fix duplicate data loading on
      prerender. `DO NOT USE FOR` must route service/endpoint work to `add-feature-slice` /
      `add-api-endpoint` and test-only work to `nova-testing`. `INVOKES: nova-testing`.
- [x] In `SKILL.md`, include a **canonical Nova examples** table and a numbered ordered checklist
      (decide placement → decide render mode → scaffold `.razor` + `.razor.cs` pair → lifecycle and
      state → parameters/callbacks → form/validation → styling → invoke `nova-testing`), with each
      step linking to its reference file. Keep `SKILL.md` under ~120 lines; detail lives in
      `references/`.
- [x] Create `references/placement-and-page-vs-component.md`: which project (`Nova.UI` default;
      `Nova` only for `HttpContext`/Identity; `Nova.Client` stays thin), the routable-vs-non-routable
      decision, `{Feature}/Pages` vs `{Feature}/Components` vs `Shared/` promotion rule, and the hard
      constraint that interactive components must live in a project referenced by `Nova.Client`.
- [x] Create `references/render-mode-decision.md`: an explicit ordered decision tree ending in exactly
      one of static SSR / `InteractiveAuto` / `InteractiveServer`, with the disqualifying question for
      each (can enhanced nav + `EditForm`/`FormName`/`[SupplyParameterFromForm]` do this? does it need
      client events, timers, or JS interop? does it depend on a server-only service with no client
      abstraction — and can that abstraction be added instead?). Include the per-instance
      `<Child @rendermode="InteractiveAuto" />` pattern for interactive islands hosted by static SSR
      pages, the "handlers without a render mode silently do nothing" failure mode, and the rule that
      both server and HTTP implementations of a service must be registered for `InteractiveAuto`.
- [x] Create `references/lifecycle-and-state.md`: which lifecycle method to use for what
      (`OnInitializedAsync` = one-time load; `OnParametersSet(Async)` = react to changed parameters
      and query params, with the applied-once guard pattern; `OnAfterRenderAsync(firstRender)` = DOM/JS
      only, never data loading), prerender + interactive-attach double-invocation, the
      `[PersistentState]` + `Initialized` guard recipe including rebuilding derived/filter state on
      restore, explicit reload helpers for user-triggered refresh, when `StateHasChanged` is and isn't
      needed, `ComponentCancellationToken` flow-through, and `DisposeAsyncCore` for cleanup.
- [x] Create `references/parameters-events-binding.md`: `[Parameter]` must be a public property with a
      public setter; `[EditorRequired]` for mandatory parameters; `EventCallback`/`EventCallback<T>`
      over `Action`/`Func` (and why — automatic re-render of the parent); never mutate a `[Parameter]`
      for owned state (copy-on-change into private state); fields for internal mutable UI state vs.
      properties for computed/normalized values; `@bind`, `@bind:get`/`@bind:set`, `@bind:after`; and
      child→parent notification patterns.
- [x] Create `references/forms-and-validation.md`: `EditForm` with `Model`, `OnValidSubmit`, and
      `DataAnnotationsValidator`; static SSR forms needing `FormName` +
      `[SupplyParameterFromForm]`; the mutable form-state class implementing `IValidatableObject` that
      delegates to `InputValidator.Validate(...)` over the shared input record (per `PlayerFormState`),
      so client validation never re-implements server rules; surfacing `ServiceProblem` /
      structured blocker results as form-level errors; and the submit-in-progress / feedback-message
      preservation rule.
- [x] Cross-check the finished skill against `.github/instructions/blazor-architecture.instructions.md`
      so no rule is contradicted, and against `csharp-conventions.instructions.md` for XML docs and
      naming in code-behind examples.

### Verification Plan

- `.github/skills/add-blazor-ui/SKILL.md` plus the five `references/*.md` files exist.
- `SKILL.md` frontmatter parses as YAML and contains `name: add-blazor-ui` and a `description` with
  `USE FOR:`, `DO NOT USE FOR:`, and `INVOKES:` — confirm by diffing its shape against
  `.github/skills/add-api-endpoint/SKILL.md`.
- Every `references/*.md` link in `SKILL.md` resolves to a file that exists (check each relative path).
- Every Nova file path cited in the skill exists on disk (verify each path resolves).
- No rule in the skill contradicts `blazor-architecture.instructions.md` (spot-check render mode,
  code-behind, `[PersistentState]`, and Bootstrap-first styling rules).

### Phase Summary

Created `.github/skills/add-blazor-ui/` with `SKILL.md` (canonical-examples table, 8-step ordered
checklist, and a final self-check list) plus five references:
`placement-and-page-vs-component.md`, `render-mode-decision.md`, `lifecycle-and-state.md`,
`parameters-events-binding.md`, `forms-and-validation.md`.

Key authoring decisions:

- The render-mode tree is **stop-at-first-match** with three exits (static SSR → `InteractiveAuto` →
  `InteractiveServer`), and records that `InteractiveServer` has zero repo usages, so choosing it
  requires documenting the server-only dependency that forced it.
- The "silent failure" (handlers with no effective render mode compile, render, and pass bUnit) is
  called out in `SKILL.md`, the render-mode reference, and the binding reference — the three places
  an agent could arrive from.
- Placement is resolved *before* markup because the render mode constrains the project
  (`InteractiveAuto` cannot live in `Nova`).
- `SKILL.md` explicitly says `_Imports.razor` already supplies `@inherits NovaComponentBase` and
  `@using static ...RenderMode`, preventing redundant re-adds.
- Forms reference leads with "never re-declare validation rules in the UI" and shows the
  `IValidatableObject` → `InputValidator` bridge, plus static-SSR `FormName` /
  `[SupplyParameterFromForm]` as the alternative to adding interactivity for a form post.

Verification run: frontmatter is well-formed (only `name` and `description` top-level keys,
`description: >-` folded, contains `USE FOR:` / `DO NOT USE FOR:` / `INVOKES:`); all five
`references/*.md` links resolve; all 14 cited Nova file paths exist on disk (the only non-resolving
token is the intentional `Nova.Shared\{Feature}\{Name}Input.cs` placeholder). The one link to
`../nova-testing/references/blazor-component-tests.md` was still pending at this point and is created
in Phase 4.

## Phase 3: Restructure the Blazor instructions file

Status: Complete

Keep `.github/instructions/blazor-architecture.instructions.md` as the always-on rules file, but make
it declarative and point at the new skill — mirroring how `testing.instructions.md` defers to
`nova-testing`.

Suggested executor: sub-agent w/ smaller model (mechanical restructure against an explicit spec),
reviewed by the orchestrator

- [x] Add a pointer banner directly under the `# Blazor Architecture` heading, in the same blockquote
      style as `testing.instructions.md`: declarative rules only; for the step-by-step recipe
      (render-mode decision tree, lifecycle selection, parameters/callbacks, forms) use the
      `add-blazor-ui` skill at `.github/skills/add-blazor-ui/`.
- [x] Move recipe-shaped prose and worked detail into the skill references, leaving crisp rules
      behind. Preserve **every** existing rule — including the non-obvious hard-won ones: rebuild
      derived collections after persisted-state restore, don't clear mutation feedback during reload,
      untrusted return-URL normalization, bounded/paged data with `TotalCount` disclosure, no
      user-controlled strings in inline `style`, `rem` units, browser-visible claims. Nothing is
      deleted outright; anything removed must appear in a skill reference.
- [x] Add a `## Related` section listing `.github/skills/add-blazor-ui/`,
      `.github/instructions/csharp-conventions.instructions.md`, and
      `.github/instructions/testing.instructions.md`.
- [x] Leave the `applyTo` frontmatter glob unchanged so the file still auto-loads for `*.razor*`,
      `Nova/Program.cs`, and `Nova.Client/Program.cs`.

### Verification Plan

- Diff the file before/after and confirm each removed line's content is present in a
  `.github/skills/add-blazor-ui/references/*.md` file — produce an explicit rule-by-rule mapping and
  record it in the Phase Summary. Zero rules may be dropped.
- Frontmatter `applyTo` and `description` are byte-identical to the pre-change values.
- The file contains a link to `.github/skills/add-blazor-ui/`.

### Phase Summary

Light-touch restructure — the file was already well-formed declarative rules, so the risk of losing a
hard-won rule outweighed aggressive trimming.

Changes:

1. Pointer banner under `# Blazor Architecture`, matching the `testing.instructions.md` blockquote
   style.
2. Pointer to the render-mode decision tree after the SSR-first list.
3. **Rule-by-rule mapping — exactly one line was rewritten:** the `Initialized`-flag bullet
   ("persist a boolean `Initialized` flag and check it at the top of `OnInitializedAsync`; if already
   initialized, return before loading data again") became a declarative rule plus a pointer to
   `references/lifecycle-and-state.md`, where the full recipe (with the `Players` code) now lives.
   **No other line was removed or reworded. Zero rules dropped.**
4. **Added three new always-on rules** covering the gaps behind the reported agent mistakes:
   lifecycle-method selection (incl. `OnParametersSet` running on every parameter set),
   `EventCallback` over `Action` (with the parent-re-render rationale), and not calling
   `StateHasChanged` defensively. These are in the instructions file — not only the skill — because
   instructions load automatically for every `*.razor*` edit while skills load on intent.
5. `## Related` section linking the skill, `csharp-conventions`, `validation`, and `testing`.

Frontmatter (`applyTo`, `description`) is untouched — confirmed by the diff showing no changes above
line 5.

## Phase 4: Wire the skill into the ecosystem

Status: Complete

Make the skill discoverable and make the feature-slice orchestrator hand off to it, so agents reach it
without the user naming it.

Suggested executor: sub-agent w/ smaller model (small, well-specified edits)

- [x] `.github/copilot-instructions.md`: add `add-blazor-ui` to the `## Skills` bullet list, in the
      same one-line format as the existing entries, placed to read naturally alongside
      `add-feature-slice`.
- [x] `.github/skills/add-feature-slice/SKILL.md`: add a **UI step** to the ordered checklist after
      the WASM client step and before tests — invoke `add-blazor-ui` for pages/components; do not
      duplicate its details. Update the frontmatter `INVOKES:` clause to include `add-blazor-ui`, and
      adjust `DO NOT USE FOR` so UI-only work routes to `add-blazor-ui`.
- [x] `.github/skills/add-blazor-ui/SKILL.md`: confirm its final step invokes `nova-testing` and that
      its `DO NOT USE FOR` routes service/endpoint work back to `add-feature-slice` /
      `add-api-endpoint` — the cross-references must be consistent in both directions.
- [x] `.github/skills/nova-testing/`: add a short `references/blazor-component-tests.md` covering the
      bUnit + NSubstitute component-test recipe used by `Nova.Unit.Tests\Components\*` — rendering a
      component with substituted services, asserting an `EventCallback` fired, and the render-mode
      assertion required by `testing.instructions.md` (bUnit invokes callbacks even when the deployed
      page would be static SSR, so a component test alone cannot prove interactivity). Link it from
      `nova-testing/SKILL.md`'s reference list.
- [x] Re-read `.github/instructions/testing.instructions.md` and confirm its existing bUnit and
      render-mode bullets still agree with the new reference (add a pointer if useful; do not restate).

### Verification Plan

- `copilot-instructions.md` `## Skills` list contains `add-blazor-ui`.
- `add-feature-slice/SKILL.md` frontmatter `INVOKES:` names `add-blazor-ui`, and its ordered checklist
  has a UI step between the WASM client step and the tests step.
- `nova-testing/SKILL.md` links to `references/blazor-component-tests.md`, and that file exists.
- Grep the `.github` tree for `add-blazor-ui` and confirm it is referenced from
  `copilot-instructions.md`, `add-feature-slice/SKILL.md`, and
  `blazor-architecture.instructions.md`.
- Every relative link introduced in this phase resolves to an existing file.

### Phase Summary

_(write when phase completes)_

## Phase 5: Validate the skill end to end

Status: Complete

Prove the skill actually changes agent behavior on the exact decisions that were being missed, then
clean up.

Suggested executor: orchestrator, delegating each dry-run scenario to an independent sub-agent so the
skill is exercised without the authoring context

- [x] Dry-run scenario A (**static SSR**): give a sub-agent only the repo + skills and ask how it
      would build a read-only club summary page. Expected: `Nova.UI/Features/Clubs/Pages`, **no**
      `@rendermode`, `.razor` + `.razor.cs` pair, data via a feature service, no `DbContext`.
- [x] Dry-run scenario B (**interactive**): ask how it would add a filterable, paged list page with a
      search box. Expected: `InteractiveAuto`, `[PersistentState]` + `Initialized` guard, derived
      filter state rebuilt on restore, `ComponentCancellationToken`, paging or documented bounded max
      with `TotalCount` disclosure.
- [x] Dry-run scenario C (**child component + callback**): ask how a child component notifies its
      parent that a row was selected. Expected: `[Parameter] public EventCallback<T> OnSelected`, no
      mutation of `[Parameter]` state, no manual `StateHasChanged` in the parent, and — if the parent
      page is static SSR — either an interactive render mode or a per-instance `@rendermode` island.
- [x] Record each dry-run's verdict; if any scenario produces a wrong answer, fix the skill wording
      (usually `USE FOR` triggers or the decision tree's ordering) and re-run that scenario.
- [x] Confirm documentation-only: `git status --short` shows changes limited to `.github/` and
      `plans/`.
- [x] Delete the Phase 1 working notes at `plans/add-blazor-ui-skill-findings.md`.
- [x] Build the solution once to confirm nothing was disturbed: `dotnet build Nova.slnx`
      (or the repo's solution file) succeeds with no new errors.

### Verification Plan

- All three dry-run scenarios produce the expected answers, with the verdicts recorded in the Phase
  Summary.
- `dotnet build` on the solution succeeds (documentation-only change must not break the build).
- `git status --short` lists no paths outside `.github/` and `plans/`.
- `plans/add-blazor-ui-skill-findings.md` no longer exists.

### Phase Summary

Three independent `explore` sub-agents (deliberately a lightweight model, with no authoring context)
were each given only a feature request and told to consult the repo's own docs. All three read the
`add-blazor-ui` skill unprompted and answered correctly.

**Scenario A — read-only club summary screen.** Answered: `Nova.UI\Features\Clubs\Pages\`, routable
page, **no `@rendermode`** (citing the render-mode tree's "only renders data" exit and `ClubDetail` as
precedent), `.razor` + `.razor.cs` pair, `OnInitializedAsync` only, `IClubDetailService` with
"must NOT touch `DbContext`/`HttpContext`", and correctly concluded `[PersistentState]` is
unnecessary because static SSR has no prerender/attach cycle. ✅

**Scenario B — filterable, paged Teams page.** Answered: `InteractiveAuto` and explained the render
mode constrains the project to `Nova.UI`/`Nova.Client`; correct directive ordering; **both** DI
registrations (`Nova/Program.cs` + `Nova.Client/Program.cs`) with the note that a missing client
registration only fails after WASM attach; `OnParametersSet` for query strings **with the
applied-once guard and the reason** (fires on every parameter set); the full `[PersistentState]` +
`Initialized` shape and named rebuilding derived state as the commonly forgotten step; debounce CTS
with `DisposeAsyncCore` cleanup; the paging/`TotalCount` truncation requirement; and correctly said
**no** `StateHasChanged` is needed. ✅

**Scenario C — child row-list component with a selection callback.** Answered:
`Features/{Feature}/Components/`, promote to `Shared/` only on real second use, never in `Nova` if
interactive; `EventCallback<T>` with the exact declaration and the concrete consequence of using
`Action` (parent never re-renders, UI silently stale); private field for the highlighted row plus the
no-mutating-parameters rule; parent must **not** call `StateHasChanged`; and — the key result — it
caught the static-SSR host trap and gave **both** fixes (render mode on the host page vs. per-instance
`@rendermode` island). It also produced the bUnit callback test *and* the render-mode assertion,
stating what bUnit cannot prove. ✅

No wording fixes were required. Supporting verification: `dotnet build Nova.slnx` succeeded (0
errors; only pre-existing NuGet advisory warnings); `git status --short` shows changes confined to
`.github/` and `plans/`; the findings file was deleted.

Separately verified during authoring that the render-mode assertion recipe actually works: `@rendermode`
compiles to a `__PrivateComponentRenderModeAttribute` deriving from `RenderModeAttribute`, and a
reflection probe against the built `Nova.UI.dll` returned `InteractiveAutoRenderMode` for `Players`
and `null` for `ClubDetail`.

## Final Recap

Nova had a rules file for Blazor but no recipe skill, while every backend concern had both — and
`add-feature-slice` stopped before the UI. That gap matched the reported agent failures (page vs.
component, unnecessary or missing interactivity, wrong render mode, wrong lifecycle method, `Action`
instead of `EventCallback`).

Delivered, documentation-only:

1. **New skill `.github/skills/add-blazor-ui/`** — `SKILL.md` (canonical-examples table, 8-step
   ordered checklist, closing self-check) plus five references:
   - `placement-and-page-vs-component.md` — project choice, routable-vs-not, feature folders, file set.
   - `render-mode-decision.md` — stop-at-first-match tree (static SSR → `InteractiveAuto` →
     `InteractiveServer` as a justified last resort), interactive islands, the silent-failure warning,
     and the placement consequences table.
   - `lifecycle-and-state.md` — lifecycle selection table, the `OnParametersSet` guard, the
     `[PersistentState]` + `Initialized` recipe incl. rebuilding derived state, cancellation and
     debounce cleanup, when `StateHasChanged` is genuinely needed, field-vs-property, `OneOf` result
     handling, bounded data.
   - `parameters-events-binding.md` — `[Parameter]`/`[EditorRequired]` rules, no parameter mutation,
     `EventCallback` over `Action` with the rationale, the two work-ownership shapes, binding forms.
   - `forms-and-validation.md` — the `IValidatableObject` → `InputValidator` bridge, `EditForm`
     markup, static-SSR `FormName`/`[SupplyParameterFromForm]`, surfacing server problems.
2. **`blazor-architecture.instructions.md`** — kept and lightly restructured: pointer banner, two
   inline skill pointers, one recipe bullet converted to a rule + pointer (zero rules dropped), a
   `## Related` section, and **three new always-on rules** (lifecycle selection, `EventCallback` over
   `Action`, don't call `StateHasChanged` defensively) so the highest-frequency mistakes are corrected
   even when no skill is invoked.
3. **`nova-testing`** — new `references/blazor-component-tests.md` (bUnit + NSubstitute rendering,
   `EventCallback` assertion, verified render-mode assertion recipe, persisted-state restore testing),
   linked from its `SKILL.md` and from `testing.instructions.md`.
4. **Wiring** — `add-blazor-ui` listed in `copilot-instructions.md`; `add-feature-slice` gained a UI
   step (now step 7 of 8) and names `add-blazor-ui` in both `INVOKES:` and `DO NOT USE FOR:`;
   `add-blazor-ui` routes back to those skills and invokes `nova-testing`.

No product code changed. Validation: three lightweight-model dry-runs all answered correctly, all
markdown links across `.github/` resolve, all cited Nova paths exist, and the solution builds.

## Deployment Plan

This is a documentation-only change to `.github/` — there is nothing to deploy to any environment.
To land it:

1. Review the diff:
   ```powershell
   cd D:\repos\Nova
   git --no-pager status --short
   git --no-pager diff -- .github
   ```
2. Stage and commit (the new skill folder is untracked, so add it explicitly):
   ```powershell
   git add .github/skills/add-blazor-ui .github/skills/nova-testing/references/blazor-component-tests.md
   git add .github/copilot-instructions.md .github/instructions/blazor-architecture.instructions.md .github/instructions/testing.instructions.md .github/skills/add-feature-slice/SKILL.md .github/skills/nova-testing/SKILL.md
   git add plans/add-blazor-ui-skill.md
   git commit -m "Add add-blazor-ui skill for Blazor page/component decisions"
   ```
3. Push and open a PR against `main`.
4. **Activation is automatic.** `blazor-architecture.instructions.md` loads for any `*.razor*` edit via
   its unchanged `applyTo` glob; `add-blazor-ui` is model-invoked from its `USE FOR:` triggers. No
   configuration, restart, or tool registration is required.
5. After merge, sanity-check in a fresh session by asking an agent to add a Blazor page and confirming
   it invokes `add-blazor-ui` and states a render-mode decision before writing markup.
6. If agents still pick the wrong render mode or lifecycle method in real use, tune the `USE FOR:`
   trigger phrases in `.github/skills/add-blazor-ui/SKILL.md` first — routing failures are far more
   likely than content gaps.

