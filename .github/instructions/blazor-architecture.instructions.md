---
applyTo: "**/*.razor,**/*.razor.cs,**/*.razor.css,Nova/Program.cs,Nova.Client/Program.cs"
description: "Blazor architecture: placement, SSR-first render modes, persisted state, safe navigation/rendering, bounded data, feature organization, and component service access."
---

# Blazor Architecture

> Declarative rules only. For the **step-by-step recipe** (placement and page-vs-component decision,
> the render-mode decision tree, lifecycle selection, prerender/persisted state, parameters and
> `EventCallback`s, binding, and `EditForm` validation), use the **`add-blazor-ui`** skill
> (`.github/skills/add-blazor-ui/`).

## Project Roles

- `Nova.UI` (Razor class library) is the default home for pages and components. New UI goes here unless a rule below requires otherwise.
- `Nova` (server host) composes the app: `App.razor`, `Routes.razor`, layouts, Identity/Account UI, and anything that requires `HttpContext` or server-only services.
- `Nova.Client` (WebAssembly host) contains only the WASM bootstrap (`Program.cs`, client DI registrations) and components that are exclusively client-side. It should stay thin.
- `Nova.Shared` holds the contracts that let `Nova.UI` stay host-agnostic: service interfaces, DTOs, OneOf result types, validation, and endpoint route constants.

Everything in `Nova.UI`, `Nova.Client`, and `Nova.Shared` can be downloaded to the browser. Never place secrets, connection strings, or server-only logic in these projects.

## SSR-First Render Modes

Build SSR-first; opt into interactivity only when functionality or UX requires it.

1. Default to **static SSR** (no render mode). Use enhanced navigation and enhanced form handling (`<EditForm>` with `FormName`/`[SupplyParameterFromForm]`) before reaching for interactivity.
2. Use **`InteractiveAuto`** when a component genuinely needs interactivity (client-side events, timers, JS interop beyond static enhancements, rich stateful UX).
3. Use **`InteractiveServer`** only when the component must be interactive _and_ cannot run in WASM (depends on server-only services that have no client abstraction). Prefer fixing the abstraction over falling back to server interactivity.
4. Apply render modes at the component or page level, not globally. Do not make the whole app interactive.

Run the ordered decision tree in `.github/skills/add-blazor-ui/references/render-mode-decision.md`
before writing markup; it also covers per-instance `@rendermode` islands on static SSR pages.

Interactive (Auto/WebAssembly) components must live in a project referenced by `Nova.Client` — i.e. `Nova.UI` or `Nova.Client` — never in `Nova`.
Any page or component that relies on event handlers, timers, or interactive behavior must have an effective interactive render mode; static SSR markup can compile and pass tests while its handlers remain non-functional in the deployed app.

## Prerendering and Persistent State

Interactive render modes (`InteractiveAuto`, `InteractiveWebAssembly`, `InteractiveServer`) prerender by default. Plan component initialization for a prerender pass and a later interactive attach pass.

- `OnInitializedAsync` runs during prerender and runs again after interactive attach unless state is restored.
- Use `[PersistentState]` only on **public component properties** so state can be serialized/restored across prerender and attach.
- Prevent duplicate startup fetches by persisting an `Initialized` flag and returning early when it is already set. When restoring persisted source data, also rebuild any derived collections, filter options, or computed view state before returning. Recipe: `.github/skills/add-blazor-ui/references/lifecycle-and-state.md`.
- Keep explicit reload/refetch helper methods for user-triggered refresh actions; the `Initialized` guard is only for startup duplication.

## Onboarding Gates (claim-gated routes)

Two pipeline middlewares in `Nova/Program.cs` redirect every authenticated request until onboarding claims are satisfied:

1. `ProfilePhotoGateMiddleware` — users lacking the `NovaClaimTypes.HasProfilePhoto` claim are redirected to `/Account/ProfilePhoto`.
2. `ClubOnboardingGateMiddleware` — users with a photo but without the `NovaClaimTypes.ClubId` claim are redirected to `/Clubs/Onboarding`.

- Any new signed-in page or route sits behind these gates automatically. A route that must be reachable pre-onboarding needs an explicit exemption in the middleware (`/Account`, `/api`, `/_framework`, `/_content`, `/_blazor`, `/health`, `/alive`, `/not-found`, `/Error`, `/favicon`, and files with a path extension are exempt; `/Clubs` is additionally exempt from the club gate).
- The gates read **claims, not the database**. After a photo upload or membership change, refresh claims with `ClubMembershipClaimRefresher` (see `.github/instructions/ef-core-tenancy.instructions.md`) or users loop through redirects until their cookie updates.

## Feature Folder Organization

Organize `Nova.UI` by feature, not by technical type:

```
Nova.UI/
  Features/
    {Feature}/
      Pages/       # @page (routable) components
      Components/  # non-routable feature components
      Services/    # client-side service implementations / view logic
  Shared/          # cross-feature components
```

- Routable pages go in `{Feature}/Pages`; non-routable components in `{Feature}/Components`.
- Promote a component to `Shared/` only when a second feature actually needs it.
- Mirror the same feature-based layout for server-side services in `Nova` and contracts in `Nova.Shared`:
  use `Nova/Features/{Feature}/` and `Nova.Shared/Features/{Feature}/` respectively.
  `Nova.Shared` keeps non-feature concerns (`Results/`, `Security/`, `Validation/`, `Enums/`) at the
  top level alongside `Features/`.
- `Nova.Client/Services/` organizes HTTP client services by feature subfolder (`Nova.Client/Services/{Feature}/Http{Feature}Service.cs`).

## Component Conventions

- **Always use a code-behind file**: every component/page is a pair of `{Name}.razor` (markup only) and `{Name}.razor.cs` (a `partial class` with parameters, state, and logic). Do not use `@code` blocks.
- **Inherit `NovaComponentBase` by default**: `_Imports.razor` sets `NovaComponentBase` as the default base type for components and pages. Keep this default unless a component has a clear reason to use a different base class.
- **DI in code-behind**: prefer constructor injection with primary constructors in the `.razor.cs` file over `@inject`/`[Inject]` when possible. Constructor injection requires the component to be instantiated by DI-aware rendering (.NET 10 supports this); use `[Inject]` properties only when constructor injection is not viable (e.g., generated base-class constraints).
- **Flow cancellation through async work**: pass `ComponentCancellationToken` to async operations (service methods, HTTP calls, EF/query calls exposed via services, delays, streams, etc.) so work stops promptly when the component is disposed.
- **Extend disposal via `DisposeAsyncCore()`**: when component-specific async cleanup is needed, override `DisposeAsyncCore()` in the existing component inheritance chain instead of re-implementing `IAsyncDisposable` on the component.
- **Choose the lifecycle method by purpose**: `OnInitializedAsync` for one-time data loading;
  `OnParametersSet(Async)` to react to `[Parameter]`/`[SupplyParameterFromQuery]` values (it runs on
  every parameter set, so guard one-time projection behind a flag or an actual-change check);
  `OnAfterRenderAsync(firstRender)` only for DOM, JS interop, and `@ref` access, never for data loading.
- **Use `EventCallback`/`EventCallback<T>` for child-to-parent notification**, never `Action`,
  `Action<T>`, or `Func<Task>`. `EventCallback` re-renders the parent that supplied the handler;
  `Action` does not, so the parent's UI silently goes stale.
- **Do not call `StateHasChanged` defensively**: `ComponentBase` re-renders after lifecycle methods
  and component event handlers. Call it only when state changes outside those paths (timer, JS
  callback, non-UI service event), and marshal with `await InvokeAsync(StateHasChanged)`.
- **Use properties where the framework requires them**: `[Parameter]` members must be `public` properties with `public` setters; `[PersistentState]` persists `public` properties. Prefer `private`/`protected` properties for computed values, normalization, or when getter/setter logic adds clarity.
- **Don't mutate parameters directly for owned state**: if a child component needs to mutate parameter-derived state, do not write back to the `[Parameter]` property. Copy to private component state only on first load or when the incoming parameter value actually changes, then mutate that private state.
- **Use fields for internal mutable UI state by default**: private fields are preferred for purely internal mutable state (`_loading`, `_error`, `_selectedId`, timers, `CancellationTokenSource`, etc.); Blazor doesn't gain reactivity from converting those values to properties.
- **Mark string parameter expressions explicitly in Razor**: quoted text passed to a child component
  `string` parameter is a literal unless it is marked as a C# expression. Use
  `ErrorMessage="@_formError"` to pass a backing field; `ErrorMessage="_formError"` renders the field
  name. This is separate from the rule that the receiving `[Parameter]` member is a public property.
- **Preserve mutation feedback across refreshes**: when a successful mutation sets a status message
  and then reloads data, the reload helper must not clear that message before it can render. Clear
  feedback at an intentional user-action boundary instead.
- **Scoped styles**: component-specific CSS goes in `{Name}.razor.css` (CSS isolation). Do not add component-specific rules to global stylesheets.
- Follow `.github/instructions/csharp-conventions.instructions.md` in code-behind files (XML docs, logging, OneOf, etc.).

## Bootstrap-First Styling

- Prefer stock Bootstrap components, classes, and utilities before writing custom CSS.
- For navigation and layout behavior (for example navbar collapse, alignment, spacing, borders, positioning), use Bootstrap-native markup and utility classes first.
- Add custom CSS only when a specific requirement cannot be met with Bootstrap alone; keep those rules minimal and scoped in the component's `.razor.css`.
- Avoid global stylesheet overrides for feature-specific UI when Bootstrap utilities or component-scoped styles can satisfy the requirement.
- Use `rem` units for all custom CSS length values; `px` is acceptable only for hairline borders (e.g., `border: 1px solid`) or pixel-exact requirements.
- Never interpolate persisted or user-controlled strings directly into an inline `style` attribute; normalize through a strict allowlist (e.g., `#RRGGBB`) and use a safe fallback.

## Navigation and bounded data

- Treat query-string return URLs as untrusted. Normalize them to well-formed local relative paths;
  reject absolute URLs, network-path references, and malformed values, then fall back to a known
  local route.
- A UI backed by a paged endpoint must implement paging or deliberately request a documented bounded
  maximum and show when `TotalCount` exceeds the loaded rows. Never silently render only the default
  first page, especially when filter choices are derived from the loaded data.

## Data Access and Services from Components

- Components never touch `DbContext` types directly. UI calls feature services; services own data access. See `.github/instructions/ef-core-tenancy.instructions.md` for context selection (`NovaDbContext`/`NovaReadDbContext`/`NovaAdminDbContext`).
- Define service contracts in `Nova.Shared` (interfaces + DTOs + OneOf results). Provide a server implementation in `Nova` (static SSR + InteractiveServer) and an HTTP-based implementation in `Nova.Client` (WASM), both registered so `InteractiveAuto` resolves the right one wherever it renders.
- `HttpContext` is only available during static SSR in `Nova`. Never use it from interactive components or from `Nova.UI`/`Nova.Client`; flow user/tenant state through abstractions (e.g., `AuthenticationStateProvider`, `CurrentUserState` in `Nova.Shared`) instead.
- Claims serialized into interactive/WASM authentication state are browser-visible. Serialize only claims required by the UI; if `SerializeAllClaims` is the only mechanism, document why and do not treat the claims as secrets or as a replacement for server authorization.
- Keep Identity/Account pages in `Nova` as static SSR — they depend on `HttpContext`, cookies, and `SignInManager`.

## Related

- `.github/skills/add-blazor-ui/` — build recipe: placement, render-mode decision, lifecycle and prerender state, parameters/`EventCallback`/binding, and `EditForm` validation.
- `.github/instructions/csharp-conventions.instructions.md` — XML docs, naming, OneOf, and logging in code-behind files.
- `.github/instructions/validation.instructions.md` — DataAnnotations on shared input records and `InputValidator`.
- `.github/instructions/testing.instructions.md` — bUnit coverage and render-mode assertion requirement for interactive pages.
