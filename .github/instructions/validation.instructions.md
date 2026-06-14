---
applyTo: "Nova.Shared/**/*Input.cs,Nova.Shared/Validation/**/*.cs,Nova/Features/**/*Service.cs"
description: "Standardized DataAnnotations validation: InputValidator.Validate<T>, the [NotWhitespace] attribute, the dual-layer rationale, how to add new input records, and the documented ProfilePhotoValidator exception."
---

# Validation Conventions

Nova validates input with **DataAnnotations declared on the input record** as the single source of
truth. Service methods do not hand-roll field checks; they call a shared helper that runs those
annotations. This keeps endpoint-layer validation (automatic, via `AddValidation()`) and
service-layer validation (explicit, authoritative) in agreement because both read the same
attributes.

## The Single Source of Truth: Annotated Input Records

Every input record lives in `Nova.Shared/{Feature}/` and carries its validation rules as attributes
on explicit init-only properties:

```csharp
// Nova.Shared/Clubs/CreateClubInput.cs
using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Clubs;

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

- `[Required]` rejects a missing value (`null`).
- `[NotWhitespace]` rejects an empty or whitespace-only string (see below).
- `[MaxLength(n)]` enforces the upper length bound.

> ⚠️ **Use explicit init-only properties, not positional constructor parameters.**
> Attributes on positional parameters in records (`record Foo([Required] string Bar)`) are placed on
> the *constructor parameter*, not the generated *property*. `Validator.TryValidateObject` reflects
> on properties — it will not see positional-parameter attributes. Always use the explicit property
> form shown above.

## `[NotWhitespace]`

Defined in `Nova.Shared/Validation/NotWhitespaceAttribute.cs`. `[Required]` considers a
whitespace-only string (`"   "`) **valid**, but Nova services must reject blank input.
`[NotWhitespace]` closes that gap: it returns invalid for empty and whitespace-only strings and
valid for `null` (so `[Required]` owns the "missing" message and `[NotWhitespace]` owns the
"present but blank" message). Always pair them: `[Required, NotWhitespace]`.

## `InputValidator.Validate<T>`

Defined in `Nova.Shared/Validation/InputValidator.cs`:

```csharp
public static Dictionary<string, string[]> Validate<T>(T input)
```

It calls `Validator.TryValidateObject(..., validateAllProperties: true)` and projects the results
into the `Dictionary<string, string[]>` shape that `ServiceProblem.Validation` consumes. Empty
dictionary means the input is valid.

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

## Why the service still validates (dual-layer rationale)

Endpoint-layer validation (`AddValidation()`) only runs for HTTP requests. Server-side Blazor (SSR)
pages, background jobs, and tests call services directly via DI and never hit an endpoint. The
service is therefore the authoritative validation boundary regardless of call path. Because both
layers read the **same attributes**, calling `InputValidator.Validate<T>` in the service guarantees
identical rules on every path. See
`.github/instructions/service-layer.instructions.md` → **Dual-Layer Validation**.

## Adding a new input record

1. Create the record in `Nova.Shared/{Feature}/{Name}Input.cs`.
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

`Nova/Features/Photos/ProfilePhotoValidator.cs` validates uploaded image bytes by sniffing magic
bytes from a `ReadOnlySpan<byte>` and cross-checking the declared content type. This rule cannot be
expressed as a DataAnnotations attribute on a model (there is no model property — the input is a raw
byte span and an `IFormFile`), so it stays a standalone validator invoked manually in the upload
handler. This is the **only** sanctioned exception to the "annotate the record + `InputValidator`"
rule. New non-model validation (file size, content sniffing, streaming) follows the same
manual-validator approach described in
`.github/instructions/api-endpoints.instructions.md` → **Manual Endpoint Validation for Non-Model Inputs**.

## Related Files

- `Nova.Shared/Validation/InputValidator.cs` — the shared validation helper.
- `Nova.Shared/Validation/NotWhitespaceAttribute.cs` — the `[NotWhitespace]` attribute.
- `Nova.Shared/Results/ServiceProblem.cs` — `ServiceProblem.Validation(errors)` factory.
- `Nova/Features/Photos/ProfilePhotoValidator.cs` — the documented non-DataAnnotations exception.
- `.github/instructions/service-layer.instructions.md` — service-layer result and validation patterns.
- `.github/instructions/api-endpoints.instructions.md` — endpoint-layer validation patterns.
