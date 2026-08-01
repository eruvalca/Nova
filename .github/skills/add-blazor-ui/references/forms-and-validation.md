# Forms and validation

## The rule that matters most

**Never re-declare business validation rules in the UI.** The DataAnnotations on the shared
`Nova.Shared\{Feature}\{Name}Input.cs` record are the single source of truth. A form model bridges to
them through `InputValidator` so the client and server can never disagree.

See `.github/instructions/validation.instructions.md` for the rule set and
`.github/skills/add-feature-slice/references/input-and-validation.md` for authoring the input record.

## Bridging a form model to the shared input record

Form models must be mutable classes with settable properties (records with `init` accessors cannot be
bound). Implement `IValidatableObject` and delegate to `InputValidator.Validate(...)` over the shared
input, converting each error into a `ValidationResult` for the matching field:

```csharp
public sealed class PlayerFormState : IValidatableObject
{
    public bool IsEdit { get; set; }
    public string FirstName { get; set; } = string.Empty;
    // ...

    public static PlayerFormState CreateDefault() => new();

    public static PlayerFormState FromDetail(PlayerDetailDto detail) => new() { /* ... */ };

    public CreatePlayerInput ToCreateInput() => new() { FirstName = FirstName, /* ... */ };

    public UpdatePlayerInput ToUpdateInput() => new() { PlayerId = PlayerId, /* ... */ };

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var errors = IsEdit
            ? InputValidator.Validate(ToUpdateInput())
            : InputValidator.Validate(ToCreateInput());

        foreach (var (field, messages) in errors)
        {
            foreach (var message in messages)
            {
                yield return new ValidationResult(message, [field]);
            }
        }
    }
}
```

The `ValidationResult` member name must match the form property name so `ValidationMessage For="..."`
renders it next to the right input.

Put the form-state class in the same file as the form component's partial class (as `PlayerForm` does)
or in the feature's `Components` folder. A small form whose fields never leave the component may
instead use a private nested model with its own DataAnnotations — see `CreateClubForm.FormModel` —
but only when there is no corresponding shared input record.

## Interactive form markup

```razor
<EditForm Model="Model" OnValidSubmit="OnValidSubmit">
    <DataAnnotationsValidator />

    @if (!string.IsNullOrWhiteSpace(ErrorMessage))
    {
        <div class="alert alert-danger" role="alert" aria-live="assertive">@ErrorMessage</div>
    }

    <div class="mb-3">
        <label for="player-first-name" class="form-label">First name</label>
        <InputText id="player-first-name" class="form-control" @bind-Value="Model.FirstName" />
        <ValidationMessage For="() => Model.FirstName" class="text-danger small" />
    </div>

    <button type="submit" class="btn btn-primary" disabled="@IsSubmitting">
        @if (IsSubmitting)
        {
            <span class="spinner-border spinner-border-sm me-2" aria-hidden="true"></span>
        }
        @SubmitButtonText
    </button>
</EditForm>
```

- `OnValidSubmit` (not `OnSubmit`) so the handler runs only after validation passes.
- `<DataAnnotationsValidator />` is required; without it nothing validates.
- Use the built-in inputs — `InputText`, `InputNumber`, `InputDate`, `InputSelect`,
  `InputCheckbox`, `InputTextArea` — with `@bind-Value`. Plain `<input>` inside an `EditForm` does not
  participate in validation.
- Pair every input with a `<label for>` and a `<ValidationMessage For="() => Model.X" />`.
- For a nullable `InputSelect`, add an explicit empty option: `<option value="">Not specified</option>`.
- Disable the submit button while submitting and show a spinner; guard the handler with the same flag
  so a double click cannot submit twice.

## Static SSR forms

A form on a static SSR page posts to the server instead of running a client handler. It needs:

- `<EditForm Model="Input" FormName="unique-form-name" OnValidSubmit="...">` — `FormName` is required
  and must be unique on the page.
- `[SupplyParameterFromForm] public {Model} Input { get; set; }` in the code-behind to receive the
  posted values.

Prefer this over adding a render mode when the only interaction is submitting the form. See the
Identity pages in `Nova\Components\Account\Pages\` for examples.

## Surfacing server results

Client-side DataAnnotations validation is a UX affordance, not a security control — the service
validates again. Show what the server returns:

- Map the `ServiceProblem` `Detail` to a form-level alert
  (`_error = problem.Detail ?? "…"`), with a friendly fallback message. Never render a raw exception.
- Structured blockers (for example `GraduationYearBlockerItem`) come back as typed collections;
  render them as a list in an `alert alert-warning` with `aria-live="polite"` rather than flattening
  them into one string.
- Field-level server errors that map to a specific input should be surfaced through the model's
  `Validate` results or a `ValidationMessageStore`, not by concatenating into the page error.

Keep the success message from being wiped by the reload that follows a successful mutation — clear it
at the next intentional user action instead.

## Accessibility and styling

- `role="alert"` on error containers; `aria-live="assertive"` for errors, `"polite"` for advisory
  blockers; `aria-hidden="true"` on decorative spinners.
- Bootstrap-first: `mb-3`, `form-label`, `form-control`, `form-select`, `btn`, `alert`, `row g-3`.
  Add scoped CSS in `{Name}.razor.css` only when Bootstrap cannot express the requirement, using
  `rem` units.
