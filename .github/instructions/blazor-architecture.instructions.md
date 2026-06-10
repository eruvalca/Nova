---
applyTo: "**/*.razor, **/*.razor.cs, **/*.razor.css, Nova/Program.cs, Nova.Client/Program.cs"
description: "Blazor architecture: component/page placement across Nova.UI/Nova/Nova.Client, SSR-first render-mode rules, feature folders, code-behind and CSS isolation conventions, and data-access rules for components."
---

# Blazor Architecture

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

Interactive (Auto/WebAssembly) components must live in a project referenced by `Nova.Client` — i.e. `Nova.UI` or `Nova.Client` — never in `Nova`.

## Feature Folder Organization

Organize `Nova.UI` by feature, not by technical type:

```
Nova.UI/
  Features/
    Clubs/
      Pages/        # routable components (@page)
      Components/   # feature-specific non-routable components
      Services/     # client-side service implementations / view logic
    Members/
      Pages/
      Components/
  Shared/           # cross-feature components (buttons, modals, etc.)
```

- Routable pages go in `{Feature}/Pages`; non-routable components in `{Feature}/Components`.
- Promote a component to `Shared/` only when a second feature actually needs it.
- Mirror the same feature-based layout for server-side services in `Nova` and contracts in `Nova.Shared`.

## Component Conventions

- **Always use a code-behind file**: every component/page is a pair of `{Name}.razor` (markup only) and `{Name}.razor.cs` (a `partial class` with parameters, state, and logic). Do not use `@code` blocks.
- **Inherit `NovaComponentBase` by default**: `_Imports.razor` sets `NovaComponentBase` as the default base type for components and pages. Keep this default unless a component has a clear reason to use a different base class.
- **DI in code-behind**: prefer constructor injection with primary constructors in the `.razor.cs` file over `@inject`/`[Inject]` when possible. Constructor injection requires the component to be instantiated by DI-aware rendering (.NET 10 supports this); use `[Inject]` properties only when constructor injection is not viable (e.g., generated base-class constraints).
- **Flow cancellation through async work**: pass `ComponentCancellationToken` to async operations (service methods, HTTP calls, EF/query calls exposed via services, delays, streams, etc.) so work stops promptly when the component is disposed.
- **Extend disposal via `DisposeAsyncCore()`**: when component-specific async cleanup is needed, override `DisposeAsyncCore()` in the existing component inheritance chain instead of re-implementing `IAsyncDisposable` on the component.
- **Scoped styles**: component-specific CSS goes in `{Name}.razor.css` (CSS isolation). Do not add component-specific rules to global stylesheets.
- Follow `.github/instructions/csharp-conventions.instructions.md` in code-behind files (XML docs, logging, OneOf, etc.).

## Data Access and Services from Components

- Components never touch `DbContext` types directly. UI calls feature services; services own data access. See `.github/instructions/ef-core-tenancy.instructions.md` for context selection (`NovaDbContext`/`NovaReadDbContext`/`NovaAdminDbContext`).
- Define service contracts in `Nova.Shared` (interfaces + DTOs + OneOf results). Provide:
  - a server implementation in `Nova` (used by static SSR and InteractiveServer), and
  - an HTTP-based implementation in `Nova.Client` calling minimal API endpoints in `Nova`, registered in `Nova.Client/Program.cs` (used when the component runs in WASM).
    Register both sides so an `InteractiveAuto` component resolves the right implementation wherever it renders.
- `HttpContext` is only available during static SSR in `Nova`. Never use it from interactive components or from `Nova.UI`/`Nova.Client`; flow user/tenant state through abstractions (e.g., `AuthenticationStateProvider`, `CurrentUserState` in `Nova.Shared`) instead.
- Keep Identity/Account pages in `Nova` as static SSR — they depend on `HttpContext`, cookies, and `SignInManager`.
