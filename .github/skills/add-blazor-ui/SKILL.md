---
name: add-blazor-ui
description: >-
  Recipe for building Nova Blazor pages and components: project placement, page-vs-component, the
  render-mode decision tree, lifecycle selection, prerender/persisted state, parameters and
  EventCallbacks, binding, and EditForm validation.
  USE FOR: add a Blazor page, add a Razor component, new .razor file, make a component interactive, choose a render mode, InteractiveAuto vs InteractiveServer vs static SSR, @rendermode, @onclick not firing / button does nothing, page vs component, where does this component go, add a [Parameter], Razor string parameter literal vs expression, EventCallback vs Action, child notifies parent, @bind / @bind:after, EditForm and validation in the UI, duplicate data load on prerender, [PersistentState], StateHasChanged, OnInitializedAsync vs OnParametersSet vs OnAfterRenderAsync, component code-behind, CSS isolation.
  DO NOT USE FOR: server services or ServiceResult work (use add-feature-slice), HTTP endpoints or WASM client services (use add-api-endpoint), entities/EF/migrations (use add-domain-persistence), writing or running tests only (use nova-testing).
  INVOKES: nova-testing (component test step).
---

# Add Blazor UI

Use this skill when creating or changing a Nova page or component. It resolves the four decisions
agents most often get wrong — **where it goes**, **page or component**, **which render mode**, and
**which lifecycle method** — before any markup is written.

Always-on rules live in `.github/instructions/blazor-architecture.instructions.md`. This skill is the
procedure; that file is the rulebook. Where both apply, they agree — do not contradict either.

## Canonical Nova examples

| Pattern | File |
| --- | --- |
| Base class, `ComponentCancellationToken`, `DisposeAsyncCore` | `Nova.UI\Components\NovaComponentBase.cs` |
| Static SSR page (no `@rendermode`) | `Nova.UI\Features\Clubs\Pages\ClubDetail.razor(.cs)` |
| Interactive page: persisted state, query params, debounce, paging | `Nova.UI\Features\Players\Pages\Players.razor(.cs)` |
| Interactive page with child callbacks | `Nova.UI\Features\Clubs\Pages\ClubOnboarding.razor(.cs)` |
| Form component: `EditorRequired`, `EventCallback`, `IValidatableObject` | `Nova.UI\Features\Players\Components\PlayerForm.razor(.cs)` |
| Parent-driven form state and string parameter expressions | `Nova.UI\Features\Teams\Components\TeamForm.razor(.cs)`, `Nova.UI\Features\Teams\Pages\Teams.razor` |
| Component owning its own submit + `EventCallback<T>` | `Nova.UI\Features\Clubs\Components\CreateClubForm.razor(.cs)` |
| Debounce + `DisposeAsyncCore` cleanup | `Nova.UI\Features\Clubs\Components\ClubSearchPanel.razor.cs` |
| Cross-feature shared component | `Nova.UI\Shared\ConfirmDeleteDialog.razor(.cs)` |
| Per-instance interactive island on a static SSR page | `Nova\Components\Account\Pages\Manage\DeletePersonalData.razor` |

`Nova.UI\_Imports.razor` already provides `@inherits Nova.UI.Components.NovaComponentBase` and
`@using static Microsoft.AspNetCore.Components.Web.RenderMode`. Do not re-add either.

## Ordered checklist

1. **Decide placement and page-vs-component.** Which project, routable or not, which feature folder.
   See [placement-and-page-vs-component.md](references/placement-and-page-vs-component.md).
2. **Decide the render mode** by running the decision tree top to bottom and stopping at the first
   match. Do this *before* writing markup — it constrains where the file may live.
   See [render-mode-decision.md](references/render-mode-decision.md).
3. **Create the `.razor` + `.razor.cs` pair.** Markup in the `.razor`, all logic in a `partial class`
   in the `.razor.cs`. Never use an `@code` block. Inject services with a primary constructor.
   Directive order in the `.razor`: `@page` → `@rendermode` → `@attribute [Authorize...]` → `@using`.
4. **Choose the lifecycle method and plan for prerender.** One-time load vs. reacting to parameters
   vs. DOM/JS work; add the `[PersistentState]` + `Initialized` guard when the component is
   interactive and loads data. See [lifecycle-and-state.md](references/lifecycle-and-state.md).
5. **Define parameters, callbacks, and binding.** Public properties for `[Parameter]`,
   `EventCallback` (never `Action`) for child→parent notification, private fields for internal state,
   and explicit `@` expressions when passing fields to child `string` parameters.
   See [parameters-events-binding.md](references/parameters-events-binding.md).
6. **Wire the form**, if any: `EditForm` + `DataAnnotationsValidator`, reusing shared input-record
   rules through `InputValidator` rather than re-declaring them.
   See [forms-and-validation.md](references/forms-and-validation.md).
7. **Style with Bootstrap first**; component-specific rules go in `{Name}.razor.css` using `rem`
   units. No global stylesheet edits for feature UI, no user-controlled strings in inline `style`.
8. **Test** — invoke the `nova-testing` skill and use its
   [Blazor component tests reference](../nova-testing/references/blazor-component-tests.md). An
   interactive page needs a render-mode assertion: bUnit fires callbacks even when the deployed page
   would render as static SSR, so a passing callback test does **not** prove the button works.

## Self-check before finishing

- The component has an effective interactive render mode if it has **any** `@onclick`,
  `@onchange`, `@bind`, timer, or JS interop. (Silent-failure #1.)
- If interactive, the file lives in `Nova.UI` or `Nova.Client` — never in `Nova`.
- Every async service call receives `ComponentCancellationToken`.
- Data comes from a feature service; no `DbContext` and no `HttpContext` in the component.
- No `@code` block; markup and logic are in the `.razor` / `.razor.cs` pair.
- If it loads data and is interactive, prerender double-loading is handled and derived state is
  rebuilt on restore.
- `StateHasChanged` is present only where genuinely required (see the lifecycle reference).
