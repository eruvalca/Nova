# Render-mode decision

Nova is **SSR-first**: interactivity is opted into per component, never applied app-wide. Run this
tree top to bottom and **stop at the first match**.

## The decision tree

**Q1 — Does the component need client-side interactivity at all?**

It does if it has any of: an event handler (`@onclick`, `@onchange`, `@oninput`, `@onkeydown`), a
`@bind` that must react without a form post, a timer or debounce, JS interop, or stateful UX that
must update without a round trip.

It does **not** if it only renders data, or if the interaction is a link, a navigation, or a form
post. Enhanced navigation and enhanced form handling (`<EditForm>` with `FormName` and
`[SupplyParameterFromForm]`) cover a great deal without any render mode.

→ **No interactivity needed: use static SSR — no `@rendermode` directive at all.**
Example: `Nova.UI\Features\Clubs\Pages\ClubDetail.razor` validates the membership claim and redirects
a legacy route with zero interactive handlers, so it has no render mode.

**Q2 — Can it run in WebAssembly?**

It can if every service it depends on has a client implementation (an `Http{Feature}Service` in
`Nova.Client` registered against the same `Nova.Shared` interface) and it does not need
`HttpContext`, cookies, `SignInManager`, or other server-only state.

→ **Yes: use `InteractiveAuto`.** This is Nova's standard interactive mode; every interactive page
in the repo uses it.

```razor
@page "/players"
@rendermode InteractiveAuto
@attribute [Authorize(Policy = Policies.RequireClubMember)]
```

`InteractiveAuto` renders on the server for the first visit and on WebAssembly once the runtime is
downloaded, so **both implementations of every service it uses must be registered** — the server one
in `Nova\Program.cs` and the HTTP one in `Nova.Client\Program.cs`. A missing client registration
surfaces as a DI failure only after the WASM runtime attaches.

**Q3 — Is it interactive but genuinely cannot run in WASM?**

→ **`InteractiveServer`, as a last resort.** Nova currently has **zero** `InteractiveServer`
components. Before choosing it, try to add the missing client abstraction instead: define the
contract in `Nova.Shared` and implement it over HTTP in `Nova.Client`. If you still choose
`InteractiveServer`, state in the component's XML docs which server-only dependency forced it.

## Interactive islands on a static SSR page

When a page must stay static SSR (typically an Identity/Account page in `Nova`) but needs one
interactive widget, apply the render mode **per instance at the call site** instead of promoting the
whole page:

```razor
<AssignClubAdminPanel @rendermode="InteractiveAuto" />
<ConfirmDeleteDialog @rendermode="InteractiveAuto" ClubName="@(_preview.ClubName!)" FormId="delete-user" />
```

The child component itself carries no `@rendermode` directive; the host page decides. This is why
such components must live in `Nova.UI` (referenced by `Nova.Client`) even when the hosting page lives
in `Nova`.

## The silent failure to avoid

A component with `@onclick` handlers and **no effective render mode** compiles, renders correct
markup, and passes bUnit tests — while the handlers do nothing in the running app. There is no error.

Guard against it:

- Whenever you add the first handler to a component, re-run this decision tree.
- A child component inherits its host's render mode. If the child is interactive but the host page is
  static SSR, either give the host a render mode or give the child instance one.
- Add a render-mode assertion or a browser-suite scenario (`Nova.Browser.Tests`) for interactive
  pages — bUnit invokes callbacks regardless of the deployed render mode, so a green component
  test proves nothing here.

## Placement consequences

| Render mode | Allowed projects |
| --- | --- |
| Static SSR | `Nova.UI`, `Nova` |
| `InteractiveAuto` / `InteractiveWebAssembly` | `Nova.UI`, `Nova.Client` only |
| `InteractiveServer` | `Nova.UI`, `Nova` |

Never apply a render mode globally in `App.razor`/`Routes.razor` to fix a single component.

## Prerendering follows from the choice

All interactive render modes prerender by default, so any interactive component that loads data runs
its initialization **twice** — once during prerender, once on attach. Handle that in
[lifecycle-and-state.md](lifecycle-and-state.md).
