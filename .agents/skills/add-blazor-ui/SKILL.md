---
name: add-blazor-ui
description: >-
  Add, change, debug, or review Nova Blazor pages and components, including existing forms,
  asynchronous state, navigation, authentication changes, and command recovery. Guides placement,
  render modes, lifecycle/prerender state, parameters, EventCallbacks, binding, EditForm validation,
  CSS isolation, and collocated JS interop. Use for non-firing handlers and corrected form retries.
  Server services use add-feature-slice; endpoints/HTTP clients use add-api-endpoint;
  persistence uses add-domain-persistence; tests-only work uses nova-testing.
---

# Add Blazor UI

Use this skill when creating, changing, debugging, or reviewing a Nova page or component. Resolve
placement, render mode, lifecycle, and state ownership for the affected behavior before editing.
For existing UI, inspect its current composition and sibling paths; apply the relevant checklist
steps without recreating unrelated structure.

Read [Blazor architecture rules](../../../.github/instructions/blazor-architecture.instructions.md)
for the constraints. Before implementation, select the affected
[transitions](../nova-testing/references/transition-evidence.md) and expected visible outcomes.

## Scoped implementation examples

Examples establish the named pattern only. Inspect their relevant regression before copying
behavior; the [component-test reference](../nova-testing/references/blazor-component-tests.md#transition-coverage)
pairs forms, identity, recovery, and URL patterns with tests. No entire page is a universal template.

| Pattern | File |
| --- | --- |
| Base class, `ComponentCancellationToken`, `DisposeAsyncCore` | `Nova.UI\Components\NovaComponentBase.cs` |
| Static SSR redirect (no `@rendermode`) | `Nova.UI\Features\Clubs\Pages\ClubDetail.razor(.cs)` |
| Interactive page: persisted state, query params, debounce, paging | `Nova.UI\Features\Players\Pages\Players.razor(.cs)` |
| Interactive page with child callbacks | `Nova.UI\Features\Clubs\Pages\ClubOnboarding.razor(.cs)` |
| Form component: `EditorRequired`, `EventCallback`, `IValidatableObject` | `Nova.UI\Features\Players\Components\PlayerForm.razor(.cs)` |
| Parent-driven form state and string parameter expressions | `Nova.UI\Features\Teams\Components\TeamForm.razor(.cs)`, `Nova.UI\Features\Teams\Pages\Teams.razor` |
| Component owning its own submit + `EventCallback<T>` | `Nova.UI\Features\Clubs\Components\CreateClubForm.razor(.cs)` |
| Debounce + `DisposeAsyncCore` cleanup | `Nova.UI\Features\Clubs\Components\ClubSearchPanel.razor.cs` |
| Collocated JS module + lazy import + module disposal | `Nova.UI\Features\Campaigns\Components\CampaignParticipantDrawer.razor(.js/.cs)` |
| Listener attach/detach + replace-on-attach | `Nova.UI\Features\Campaigns\Pages\CampaignWorkspace.razor(.js/.cs)` |
| Cross-feature shared component | `Nova.UI\Common\ConfirmDeleteDialog.razor(.cs)` |
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
   interactive and loads data. For club-scoped pages, also handle authentication changes and
   snapshot ownership. For in-session or reload-safe commands, use that reference's pending-command
   recovery section. See [lifecycle-and-state.md](references/lifecycle-and-state.md).
5. **Define parameters, callbacks, and binding.** Public properties for `[Parameter]`,
   `EventCallback` (never `Action`) for child→parent notification, private fields for internal state,
   and explicit `@` expressions when passing fields to child `string` parameters.
   See [parameters-events-binding.md](references/parameters-events-binding.md).
6. **Wire the form**, if any: `EditForm` + `DataAnnotationsValidator`, reusing shared input-record
   rules through `InputValidator` rather than re-declaring them.
   See [forms-and-validation.md](references/forms-and-validation.md).
7. **Style to the design system** (`DESIGN.md` / `.github/instructions/ui-design.instructions.md`):
   component-specific rules go in `{Name}.razor.css` using `rem` units. No global stylesheet edits
   for feature UI, no user-controlled strings in inline `style`.
8. **Add JavaScript only if needed**: use a collocated `{Component}.razor.js` ES module and the
   appropriate C# interop or browser-native lifecycle in [js-interop.md](references/js-interop.md).
9. **Wire URL-backed navigation** — for route markers, tabs, filters, or drawers represented in
   the URL, centralize canonical tokens and defensive normalization in a feature URL-state
   helper. Render local `<a href>` destinations with `aria-current="page"` so refresh, deep links,
   keyboard activation, and scripting-disabled navigation work; use Blazor handlers only as
   progressive enhancement and never as the sole navigation path.
10. **Test** — invoke the `nova-testing` skill and use its
   [Blazor component tests reference](../nova-testing/references/blazor-component-tests.md). Verify
   effective interactivity through the actual page/host and call sites, including inherited or
   per-instance render modes. A local attribute assertion covers only a mode owned by that component;
   bUnit callback success does not prove deployed interaction.
11. **Record the selected transition evidence** — link named tests and, for applicable design
    checks, curated desktop/mobile captures in the single validation record. Follow
    [AGENTS.md](../../../AGENTS.md#completion-and-review) for completion and review triage.
