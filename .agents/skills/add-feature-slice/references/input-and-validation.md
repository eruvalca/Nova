# Input and Validation Recipe

Nova validates input with **DataAnnotations declared on the input record** as the single source of
truth. Service methods do not hand-roll field checks; they call a shared helper that runs those
annotations. This keeps endpoint-layer validation (automatic, via `AddValidation()`) and
service-layer validation (explicit, authoritative) in agreement because both read the same
attributes.

## The Single Source of Truth: Annotated Input Records

Every input record lives in `Nova.Shared/Features/{Feature}/` and carries its validation rules as attributes
on explicit init-only properties:

```csharp
// Nova.Shared/Features/Clubs/CreateClubInput.cs
using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Features.Clubs;

public sealed record CreateClubInput
{
    [Required, NotWhitespace, MaxLength(200)]
    public required string Name { get; init; }

    [Required, NotWhitespace, MaxLength(100)]
    public required string City { get; init; }

    [Required, NotWhitespace, MaxLength(100)]
    public required string State { get; init; }
}
```

- `[Required]` rejects `null`, empty strings, and whitespace-only strings by default.
- `[NotWhitespace]` rejects an empty or whitespace-only string (see below).
- `[MaxLength(n)]` enforces the upper length bound.

> ⚠️ **Use explicit init-only properties, not positional constructor parameters.**
> Attributes on positional parameters in records (`record Foo([Required] string Bar)`) are placed on
> the *constructor parameter*, not the generated *property*. `Validator.TryValidateObject` reflects
> on properties — it will not see positional-parameter attributes. Always use the explicit property
> form shown above.

Canonical file: `Nova.Shared\Features\Clubs\CreateClubInput.cs`.

## `[NotWhitespace]`

Defined in `Nova.Shared/Validation/NotWhitespaceAttribute.cs`. It rejects empty and whitespace-only
strings and accepts `null`. .NET 10's `[Required]` already rejects blank strings unless
`AllowEmptyStrings` is enabled; the Nova attribute is an explicit constraint, not a framework-gap
workaround. Keep the existing `[Required, NotWhitespace]` convention and its error contracts. Do
not assume the pair produces separate missing/blank messages or remove annotations as a side effect
of correcting the explanation.

Canonical file: `Nova.Shared\Validation\NotWhitespaceAttribute.cs`.

## `InputValidator.Validate<T>`

Defined in `Nova.Shared/Validation/InputValidator.cs`:

```csharp
public static Dictionary<string, string[]> Validate<T>(T input)
```

It calls `Validator.TryValidateObject(..., validateAllProperties: true)` and projects the results
into the `Dictionary<string, string[]>` shape that `ServiceProblem.Validation` consumes. Empty
dictionary means the input is valid.

Canonical file: `Nova.Shared\Validation\InputValidator.cs`.

### Usage in a service method

Run it at the top of the service method and short-circuit on failure:

```csharp
public async Task<ServiceResult<ClubDto>> CreateClubAsync(
    CreateClubInput input,
    CancellationToken cancellationToken = default)
{
    var errors = InputValidator.Validate(input);
    if (errors.Count > 0)
    {
        return ServiceProblem.Validation(errors);
    }

    // ... business-rule checks (conflicts, authorization) and persistence
}
```

Do **not** rebuild a `Dictionary<string, string[]>` by hand with `string.IsNullOrWhiteSpace` /
`.Length` checks for rules that an attribute already expresses. Add or change the attribute on the
record instead.

## Adding a new input record

1. Create the record in `Nova.Shared/Features/{Feature}/{Name}Input.cs`.
2. Declare explicit required init-only properties (not positional constructor parameters — see the
   warning above).
3. Annotate every member with the appropriate DataAnnotations
   (`[Required]`, `[NotWhitespace]` for non-blank strings, `[MaxLength]`, `[Range]`,
   `[EmailAddress]`, etc.).
4. In the service method that consumes it, validate with
   `var errors = InputValidator.Validate(input);` and return
   `ServiceProblem.Validation(errors)` when `errors.Count > 0`.
5. Do not duplicate those rules in the service body.

## Documented exception: `ProfilePhotoValidator`

`Nova/Features/Photos/ProfilePhotoValidator.cs` validates uploaded image bytes by magic-byte sniffing —
an approach that cannot be expressed as a DataAnnotation. It is the **only** sanctioned exception to
the "annotate the record + `InputValidator`" rule. See
`add-api-endpoint/references/validation-and-problemdetails.md` → manual validation for non-model inputs.

