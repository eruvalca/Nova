# Placement and page vs. component

Answer these in order. Each answer constrains the next.

## 1. Which project?

| Question | Answer |
| --- | --- |
| Default for anything new | **`Nova.UI`** |
| Needs `HttpContext`, cookies, `SignInManager`, or other server-only services with no client abstraction | `Nova` (and it must be static SSR) |
| Exclusively client-side, WASM-only bootstrap concerns | `Nova.Client` — keep it thin (today it holds only `Auth.razor` and `RedirectToLogin.razor`) |

Hard constraints:

- **Interactive (`InteractiveAuto` / `InteractiveWebAssembly`) components must live in a project
  referenced by `Nova.Client`** — that is `Nova.UI` or `Nova.Client`, never `Nova`. A component in
  `Nova` can only be static SSR or `InteractiveServer`.
- `Nova.UI`, `Nova.Client`, and `Nova.Shared` are all downloadable to the browser. No secrets,
  connection strings, or server-only logic in any of them.
- Identity/Account pages stay in `Nova` as static SSR. That area sets
  `@attribute [ExcludeFromInteractiveRouting]` in its `_Imports.razor`.

## 2. Page or component?

It is a **page** when it needs its own URL: a user can navigate to it, link to it, bookmark it, or be
redirected to it. Pages carry `@page` and go in `{Feature}/Pages`.

It is a **component** when it is a piece of UI a page (or another component) composes. Components
have no `@page` and go in `{Feature}/Components`.

Decide by the routing need, not by size. Signals that you want a component even though the work feels
"page-sized":

- The markup appears on more than one page.
- It owns a self-contained interaction (a form, a search panel, a dialog) that a page coordinates.
- It needs to be an interactive island inside an otherwise static SSR page — only a component can be
  given a per-instance `@rendermode` (see the render-mode reference).

Signals you actually want a page:

- A route parameter identifies what is shown (`@page "/players/{PlayerId:long}"`).
- It is a navigation target after a redirect or a link.
- It carries its own authorization policy for the whole screen.

A page may be thin: `Players.razor` is a page that composes `PlayerForm` components. Splitting a
large page into feature components is preferred over one large page.

## 3. Which folder?

```
Nova.UI/
  Features/
    Clubs/
      Pages/        # routable components (@page)
      Components/   # feature-specific non-routable components
      Services/     # client-side service implementations / view logic
    Players/
      Pages/
      Components/
  Shared/           # cross-feature components only
  Components/       # framework-level base types (NovaComponentBase)
```

- Start in the owning feature's folder. Put it in `Shared/` **only when a second feature actually
  needs it** — not in anticipation. `ConfirmDeleteDialog` earned `Shared/` because the Account area
  and club deletion both use it.
- Mirror the same feature layout for server services in `Nova` and contracts in `Nova.Shared`.
- A new feature means a new `Features/{Feature}/` folder with `Pages/` and `Components/`.

## 4. File set to create

Always a pair, plus optional isolated CSS:

```
Features/{Feature}/Pages/{Name}.razor        # markup only
Features/{Feature}/Pages/{Name}.razor.cs     # partial class: parameters, state, logic
Features/{Feature}/Pages/{Name}.razor.css    # optional, component-scoped styles
```

- The `.razor.cs` declares `public partial class {Name}` in the namespace matching the folder
  (`Nova.UI.Features.Clubs.Pages`).
- `_Imports.razor` sets `NovaComponentBase` as the default base type, so the code-behind usually does
  **not** need `: NovaComponentBase`. Stating it explicitly is also fine and is done in several
  components; be consistent within a file.
- Inject services via a **primary constructor** on the partial class, with `<param>` XML docs:

  ```csharp
  /// <summary>
  /// Displays the signed-in member's club details and roster.
  /// </summary>
  /// <param name="clubDetailService">The service that loads club detail data.</param>
  /// <param name="navigationManager">The navigation manager used for access-denied redirects.</param>
  public partial class ClubDetail(
      IClubDetailService clubDetailService,
      NavigationManager navigationManager)
  ```

  Use `[Inject]` properties only when constructor injection is not viable.
- Never use an `@code` block in the `.razor`.

## 5. Data access

Components call **feature services** resolved from DI. They never touch `DbContext`, and they never
touch `HttpContext` (it exists only during static SSR in `Nova`, and using it makes the component
un-hostable in WASM). Flow user/tenant state through `AuthenticationStateProvider` or
`CurrentUserState`.

Service contracts live in `Nova.Shared`; a server implementation lives in `Nova` and an HTTP
implementation in `Nova.Client`. Both must be registered — see the render-mode reference for why
`InteractiveAuto` depends on this.
