# JavaScript Interop in Nova

Step-by-step recipe for the small amount of JavaScript Nova components may need. Follow the
rulebook in `.github/instructions/blazor-architecture.instructions.md` ("JavaScript Interop");
this file is the procedure with working examples from committed Nova code.

## Step 1 — Decide you actually need JS

Run through the reject-fast list before writing any JS:

- Bootstrap data API (`data-bs-toggle`, `data-bs-dismiss`) → no JS. The delete modal on
  `/Account/Manage/DeletePersonalData` opens entirely via data attributes.
- CSS scroll snap, `position: sticky`, focus-within → no JS.
- Blazor events (`@onclick`, `@onkeydown`, `@onfocus`) → no JS.
- JS is warranted only for browser-native behaviors Blazor cannot express: focus/scroll
  manipulation, default-action suppression, clipboard, measurement, third-party widgets.
- Static SSR markup must function without custom JS. Custom JS belongs to interactive components
  (rendered interactively), because prerendered/static markup cannot attach listeners.

## Step 2 — Collocate the module

Create `{Component}.razor.js` **next to the owning component**. For a feature component in
`Nova.UI`:

```
Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.razor
Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.razor.js   ← collocated
Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.razor.cs
```

Module anatomy (from `CampaignParticipantDrawer.razor.js`):

```js
export function focus(element) {
    element?.focus();
}
```

Rules:

- Only `export function`s — no `window.*` globals, no top-level side effects other than module
  state (see Step 4).
- Functions take DOM elements (or plain values) as arguments; the component passes `ElementReference`s,
  never hard-coded element `id` strings.
- Null-guard element arguments (`element?.focus()`) — a reference may be null after conditional
  renders.

The build embeds `.razor.js` in the RCL as a static web asset automatically (SDK
`StaticWebAsset` for `js`/`css` files in Razor class libraries). No csproj change is needed.

## Step 3 — Wire the C# side

In the `.razor.cs` code-behind (from `CampaignParticipantDrawer.razor.cs`):

```csharp
public partial class CampaignParticipantDrawer(IJSRuntime jsRuntime) : NovaComponentBase
{
    private ElementReference _closeButton;

    private readonly Lazy<Task<IJSObjectReference>> _moduleTask = new(() => jsRuntime
        .InvokeAsync<IJSObjectReference>(
            "import", "./_content/Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.razor.js")
        .AsTask());

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("focus", _closeButton);
        }
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_moduleTask.IsValueCreated)
        {
            try
            {
                var module = await _moduleTask.Value;
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit is gone; the browser already destroyed the page with it.
            }
        }
    }
}
```

Rules:

- Import lazily with a `Lazy<Task<IJSObjectReference>>` field wrapping `"import"` and the
  `./_content/Nova.UI/...` path. Do not import in the constructor and do not import eagerly.
- The `@ref` on an element binds `ElementReference`; pass it straight through to module functions.
  Use `@ref` on the specific element, not on the whole component.
- Invoke module functions only from `OnAfterRenderAsync(firstRender)` or event handlers. JS interop
  is impossible during prerender and `@ref`s are unset in `OnInitializedAsync`/`OnParametersSet`.
- Override `DisposeAsyncCore()` (Nova's async-disposal extension point, not `IAsyncDisposable`
  directly) and dispose the module there, guarded by `_moduleTask.IsValueCreated`.
- Guard disposal-time interop with a catch of **only** `JSDisconnectedException`. Disposal can run
  after an `InteractiveAuto` component's server circuit has disconnected (refresh, tab close, or
  circuit expiry), so the detach call or `DisposeAsync()` can throw; an uncaught throw faults the
  teardown path. `NovaCropperComponent.DisposeAsync()` is the repository pattern to copy.

## Step 4 — Listeners that outlive one event

If the module attaches a DOM listener (from `CampaignWorkspace.razor.js`):

```js
let activeContainer = null;
let keydownListener = null;

function suppressActivationDefault(event) { /* ... */ }

// Replace-on-attach: called again after every render that recreates the container,
// but keeps exactly one live listener.
export function attachRosterActivationSuppression(container) {
    detachRosterActivationSuppression();
    if (!(container instanceof Element)) {
        return;
    }
    activeContainer = container;
    keydownListener = suppressActivationDefault;
    document.addEventListener('keydown', keydownListener, true);
}

export function detachRosterActivationSuppression() {
    if (keydownListener) {
        document.removeEventListener('keydown', keydownListener, true);
        keydownListener = null;
    }
    activeContainer = null;
}
```

Rules:

- Hold module-level state only for the listener and its scoping container — module state is shared
  by every component instance of that component type, so keep it minimal and idempotent.
- **Replace-on-attach**: if Blazor recreates the element the listener scopes to (any `@if` render
  branch), the same module may be attached again on the next render. `attach` must detach first so
  exactly one listener is live.
- **Attach only when the container element is rendered.** A listener whose container lives in a
  conditional branch must not be attached with an unset `ElementReference` — an empty roster never
  renders the region, and the serialized reference arrives in JS as a plain object whose
  `contains()` check throws on every keydown. In C#, attach when the branch renders and detach when
  it does not (see `CampaignWorkspace.OnAfterRenderAsync`); in JS, have `attach` reject non-`Element`
  arguments as defense in depth.
- Detach in the component's `DisposeAsyncCore()` **before** disposing the module (see
  `CampaignWorkspace.razor.cs`: `detachRosterActivationSuppression`, then `module.DisposeAsync()`).

## Step 5 — Test with bUnit

bUnit mocks `import` via `JSInterop.SetupModule`; the module path must match the C# import string
exactly (from `CampaignWorkspaceTests.cs`):

```csharp
private const string WorkspaceModulePath = "./_content/Nova.UI/Features/Campaigns/Pages/CampaignWorkspace.razor.js";

var workspaceModule = JSInterop.SetupModule(WorkspaceModulePath);
workspaceModule.Setup<double?>("captureScroll", _ => true).SetResult(120);
var restoreScroll = workspaceModule.SetupVoid("restoreScroll", _ => true);
restoreScroll.SetVoidResult();
```

Rules:

- `SetupModule(path)` matches the **exact** import path string — keep the path in a `const`
  mirrored between component and test.
- Use loose argument matchers (`_ => true`) unless the assertion targets the arguments. When it
  does, capture the `ElementReference` **before** the triggering action and assert on the captured
  id (bUnit's htmlized `blazor:elementreference` attribute goes empty after a re-render — see
  `CampaignWorkspaceTests.cs` for the pre-capture pattern with `ShouldBeOfType<ElementReference>()`).
- Verify invocation with `VerifyInvoke("captureScroll")` and inspect `.Arguments`; never assert
  `blazor:elementreference` against post-action DOM.
- Include the module interop in the `nova-testing` component-test step, not a separate test file.

## Pitfalls

- **Navigation focus can be lost during interactive attach.** When attachment replaces the heading
  focused by enhanced navigation, use the existing `ClubShell.razor.cs` / `.razor.js` pattern for
  pages in that shell. It scopes the lookup to the hall and restores focus only while no other
  control owns it. If asynchronous content has no heading yet, retain a pending flag and try on
  the next normal render; do not spin a render loop or stop unconditionally after `firstRender`.
  Success, empty/not-found, and terminal error branches need a heading. A reused shell also needs
  an explicit close action for its mobile directory when a destination link is activated.

- **RCL collocated modules are not auto-loaded.** The SDK ships `.razor.js` as a static asset, but
  nothing imports it until the component does `"import", "./_content/Nova.UI/..."`. A missing or
  mismatched path fails only at runtime, not at build time — and not silently: awaiting
  `_moduleTask.Value` faults with a `JSException` (for example, `Failed to fetch dynamically
  imported module`), which from `OnAfterRenderAsync` faults the render/circuit. bUnit catches path
  typos (`SetupModule` fails the test).
- **JS during prerender is forbidden.** `IJSRuntime` calls throw while prerendering. Always go
  through `OnAfterRenderAsync(firstRender)` or an event handler; never `OnInitializedAsync`.
- **Recreated containers leak or break listeners.** If the element the listener scopes to lives in
  an `@if` branch, Blazor recreates the DOM node on every render that changes the branch. Without
  replace-on-attach, either multiple listeners accumulate (leak) or the new node has none. Re-attach
  on every render pass where the element is visible (see Step 4).
- **`DisposeAsyncCore` ordering.** Detach listeners before disposing the module; after
  `DisposeAsync()` the module reference is unusable.
- **No page-wide helpers.** Do not add helpers to `Nova/wwwroot/js/` or `window.*` globals. If two
  components share JS, prefer separate collocated modules over a shared global; reconsider the
  component split before sharing.
- **No speculative empty `site.js`.** `site.js` was deleted in the scoped-interop refactor and must
  not be recreated. An empty script still costs a request/parse on every page and invites
  page-global helpers back. If site-wide JS ever becomes necessary, use one of: a layout-collocated
  module (`App.razor.js` / `MainLayout.razor.js`), a lazily imported interop service, or the
  `blazor.web.js` loader callbacks (`beforeBlazorStarts`) for pre-initialization code. Inspect
  `App.razor` for the current site-wide script set before assuming what is already loaded.
