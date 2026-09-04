# Lifecycle and state

## Which lifecycle method

| Need | Method | Notes |
| --- | --- | --- |
| Load data once for this component instance | `OnInitializedAsync` | Runs once per instance — but an interactive component is instantiated twice (prerender + attach). See below. |
| React to a `[Parameter]` or `[SupplyParameterFromQuery]` value | `OnParametersSet` / `OnParametersSetAsync` | Runs after initialization **and every time the component receives parameters**, which includes every parent re-render. Never do unguarded work here. |
| Touch the DOM, call JS, or use `@ref` element references | `OnAfterRenderAsync(bool firstRender)` | The only place the DOM and JS are safe. Guard one-time work with `if (!firstRender) return;`. Not called during static SSR/prerender. Never load data here. |
| Async cleanup (timers, `CancellationTokenSource`, JS module references) | `DisposeAsyncCore()` | Override it; do not re-implement `IAsyncDisposable` — `NovaComponentBase` already does. |

`SetParametersAsync` and `ShouldRender` are not used anywhere in Nova. Do not introduce them without
a measured reason.

## Reacting to parameters without re-running work

`OnParametersSet` fires on every parameter set, so guard one-time projection with a flag. `Players`
projects query-string filters exactly once:

```csharp
[SupplyParameterFromQuery(Name = "search")]
private string? SearchQuery { get; set; }

protected override void OnParametersSet()
{
    if (_queryFiltersApplied)
    {
        return;
    }

    _queryFiltersApplied = true;
    _searchDraft = SearchQuery ?? string.Empty;
    _searchApplied = _searchDraft;
}
```

If the component *should* react to later parameter changes, compare against the last applied value
and act only on an actual change — never unconditionally.

## Prerender + interactive attach

Interactive render modes prerender by default. `OnInitializedAsync` therefore runs during the
prerender pass and again after the interactive runtime attaches, which double-fetches data and makes
the UI flicker.

Fix with `[PersistentState]` (the .NET 10 declarative model) plus an `Initialized` guard:

```csharp
/// <summary>Gets or sets the persisted startup roster snapshot used across prerender and attach.</summary>
[PersistentState]
public PagedResult<PlayerListItem>? PersistedRoster { get; set; }

/// <summary>Gets or sets whether startup initialization already completed during prerender.</summary>
[PersistentState]
public bool Initialized { get; set; }

protected override async Task OnInitializedAsync()
{
    if (Initialized)
    {
        _roster = PersistedRoster;
        if (_roster is not null)
        {
            RefreshAvailableFilters(_roster.Items);   // rebuild derived state
        }
        _isLoading = false;
        return;
    }

    _isLoading = true;
    await LoadRosterAsync();
    PersistStartupState();
    Initialized = true;
}
```

Rules:

- `[PersistentState]` works only on **public properties** — the framework persists them by reflection.
  A private field or a private property will not persist.
- Persist the loaded data, the startup error message, and the `Initialized` flag.
- **Rebuild derived state on restore.** Persisting rows without reconstructing filter options,
  computed collections, or view state makes prerendered controls disappear or drift after attach.
- Keep an explicit reload helper (`LoadRosterAsync`) for user-triggered refresh. The `Initialized`
  guard is only for startup duplication — never let it block a refresh action.
- This is unrelated to `ExcludeFromInteractiveRouting`, which controls routing/rendering, not
  duplicate initialization.

## Independent startup regions

When a page loads independent regions, start their loaders together but keep each region's loading,
data, empty, and error state separate. Persist every region's startup result or error plus the shared
initialization flag. A local retry reloads and re-persists only its region; it must not clear or
relabel successful neighbors. `ClubOverview.razor.cs` is the canonical example.

## Cancellation

Pass `ComponentCancellationToken` (from `NovaComponentBase`) into every async service call, HTTP
call, delay, and stream so work stops when the component is disposed:

```csharp
var result = await playerService.GetPlayerRosterAsync(input, ComponentCancellationToken);
```

After disposal the property returns an already-canceled token rather than throwing.

When a loader catches HTTP failures, re-throw `OperationCanceledException` if
`ComponentCancellationToken` or the loader's owned request token is canceled. A component being
disposed is not a user-visible unavailable state; only unrelated transport cancellation belongs in
the recoverable-error branch.

For a debounce, use a separate `CancellationTokenSource` field so each keystroke cancels the pending
delay, and clean it up in `DisposeAsyncCore`:

```csharp
protected override ValueTask DisposeAsyncCore()
{
    _debounceCts?.Cancel();
    _debounceCts?.Dispose();
    _debounceCts = null;
    return ValueTask.CompletedTask;
}
```

See `ClubSearchPanel.razor.cs` for the full debounce pattern (cancel pending → `Task.Delay(ms, token)`
→ `catch (OperationCanceledException) { return; }`).

## `StateHasChanged` — usually unnecessary

`ComponentBase` re-renders automatically after lifecycle methods and after any event handler it
invoked, including `EventCallback` invocations. Nova has only two `StateHasChanged` calls in the whole
UI. Do **not** add it defensively.

Call it only when state changes outside a lifecycle method or component event — e.g. a timer
callback, a JS interop callback, or an event raised by a non-UI service. From those contexts marshal
to the renderer's synchronization context:

```csharp
await InvokeAsync(StateHasChanged);
```

## Field vs. property for state

- **Private fields** for internal mutable UI state: `_loading`, `_error`, `_selectedId`, timers,
  `CancellationTokenSource`. Blazor gains no reactivity from making these properties.
- **Private/protected properties** for computed or normalized values:
  `protected bool IsRosterTruncated => _roster is not null && _roster.TotalCount > _roster.Items.Count;`
- **Public properties** where the framework requires them: `[Parameter]` and `[PersistentState]`.

## Handling service results and errors

Services return `OneOf` results; branch with `Switch`, map `Forbidden` to a redirect, and put
everything else into a page-level error field:

```csharp
result.Switch(
    detail => _club = detail,
    problem =>
    {
        if (problem.Kind == ServiceProblemKind.Forbidden)
        {
            navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
            return;
        }

        _error = problem.Detail ?? "Failed to load club details. Please refresh and try again.";
    });
```

A `NotFound` problem is sometimes the expected empty state rather than an error — see
`ClubOnboarding`.

**Preserve mutation feedback across refreshes**: when a successful mutation sets a status message and
then reloads data, the reload helper must not clear that message before it can render. Clear feedback
at an intentional user-action boundary instead.

## Bounded data

A UI backed by a paged endpoint must implement paging, or deliberately request a documented bounded
maximum and show when `TotalCount` exceeds the loaded rows. Never silently render only the default
first page — especially when filter options are derived from the loaded rows.
