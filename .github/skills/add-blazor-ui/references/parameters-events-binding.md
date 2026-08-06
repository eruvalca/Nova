# Parameters, events, and binding

## Parameters

`[Parameter]` members must be **public properties with public setters** — the framework assigns them.
A field or a private setter will not bind.

```csharp
/// <summary>Gets or sets the mutable form state for create/edit operations.</summary>
[Parameter, EditorRequired]
public PlayerFormState Model { get; set; } = PlayerFormState.CreateDefault();

/// <summary>Gets or sets whether a save operation is in progress.</summary>
[Parameter]
public bool IsSubmitting { get; set; }
```

- Add `[EditorRequired]` to parameters the component cannot work without. It produces a compile-time
  warning at the call site when omitted.
- Give every parameter a non-null default (`string.Empty`, `[]`) so the component renders safely
  before the parent supplies a value.
- Prefer `IReadOnlyList<T>` over `List<T>` for collection parameters.
- Every parameter gets an XML doc comment.

### String parameter values: literal text versus C# expressions

Quoted text passed to a child component `string` parameter is literal unless it is marked as a C#
expression:

```razor
<!-- Wrong: renders the text "_formError". -->
<TeamForm ErrorMessage="_formError" />

<!-- Correct: passes the backing-field value. -->
<TeamForm ErrorMessage="@_formError" />
```

This call-site rule is separate from the receiving component rule: `TeamForm.ErrorMessage` must
still be a public `[Parameter]` property. The Teams and Players pages are the canonical expression
examples.

Route parameters are also `[Parameter]` properties, matched by name to the `@page` template:

```razor
@page "/players/{PlayerId:long}"
```
```csharp
[Parameter]
public long PlayerId { get; set; }
```

Query-string values use `[SupplyParameterFromQuery]` and may be private, since the framework — not a
parent — supplies them. Project them into state under an apply-once guard (see
[lifecycle-and-state.md](lifecycle-and-state.md)).

## Never mutate a parameter for owned state

A `[Parameter]` property is owned by the parent. Writing to it is silently reverted on the next
parameter set and desynchronizes parent and child.

If the child needs to mutate parameter-derived state, copy into private state — on first load, or
when the incoming value actually changes — and mutate the copy:

```csharp
private PlayerFormState? _editForm;   // private, mutable, owned by this component
```

To tell the parent about the change, raise an `EventCallback`.

## `EventCallback`, not `Action`

Use `EventCallback` / `EventCallback<T>` for every child→parent notification. Never `Action`,
`Action<T>`, or `Func<Task>`.

The reason is concrete: when a child invokes an `EventCallback`, Blazor automatically calls
`StateHasChanged` on the **parent that supplied the handler**, so the parent re-renders. An `Action`
does not, and the parent's UI silently fails to update. `EventCallback` is also a struct that handles
sync and async handlers uniformly and marshals to the right synchronization context.

```csharp
/// <summary>
/// Invoked when the club is successfully created. The created <see cref="ClubDto"/> is passed as the argument.
/// </summary>
[Parameter]
public EventCallback<ClubDto> OnClubCreated { get; set; }
```

Invoke it with `InvokeAsync`:

```csharp
result.Switch(
    club => _ = OnClubCreated.InvokeAsync(club),
    problem => _error = problem.Detail ?? "An error occurred creating the club. Please try again.");
```

Conventions:

- Name callbacks `On{Event}` — `OnClubCreated`, `OnJoinRequested`, `OnValidSubmit`, `OnCancel`.
- Prefer the strongly typed `EventCallback<T>` when the parent needs the payload; use the
  non-generic `EventCallback` for "it happened" signals.
- A parameterless `EventCallback` can be bound straight to a DOM event:
  `<button type="button" @onclick="OnCancel">Cancel</button>`.
- Guard against double-invocation with an `_submitting` flag and `disabled="@_submitting"` on the
  button.
- The parent handles the callback with a plain method (`HandleClubCreated`) and does **not** call
  `StateHasChanged`.

## Choosing where the work lives

Two valid shapes, both present in the repo:

- **Child owns the operation** — `CreateClubForm` calls `IClubService.CreateClubAsync` itself and
  raises `OnClubCreated` with the result. Use this when the component is self-contained and reusable.
- **Child is presentational, parent owns the operation** — `PlayerForm` renders fields and raises
  `OnValidSubmit`; `Players` performs the create/update and passes `IsSubmitting`, `ErrorMessage`,
  and structured blockers back down as parameters. Use this when several parents drive the same form
  differently, or when the parent must coordinate reloads.

Pick one per component and keep it consistent; do not split an operation across both.

## Binding

- `@bind="_value"` for two-way binding on a DOM element; `@bind-Value="Model.X"` for the built-in
  `Input*` components.
- `@bind:event="oninput"` to update on each keystroke instead of on change.
- `@bind:after="HandlerAsync"` to run logic *after* the bound value is written — prefer it over
  hand-rolled `@onchange` handlers that also assign the field.
- `@bind:get` / `@bind:set` when the value needs normalization or the setter must do work.
- Binding to a child component parameter requires the `{Name}Changed` callback pair:
  `public T Value { get; set; }` + `public EventCallback<T> ValueChanged { get; set; }`, consumed as
  `@bind-Value="_x"`.

When the raw event data is needed (or the value needs parsing before it is stored), handle
`@onchange`/`@oninput` explicitly with `ChangeEventArgs`:

```csharp
private void OnConfirmChanged(ChangeEventArgs e) => _confirmed = e.Value is true;
```

Note that a `select` bound to a nullable numeric filter usually needs a string projection property
(`GraduationYearFilterText`) because `<option value="">` cannot bind to `int?` directly — see
`Players.razor.cs`.
