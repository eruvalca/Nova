---
applyTo: "Nova.Shared/**/*Input.cs,Nova.Shared/Validation/**/*.cs,Nova/Features/**/*Service.cs"
description: "Validation rules: DataAnnotations on input records as single source of truth, the [NotWhitespace] attribute, InputValidator.Validate<T>, dual-layer rationale, and the ProfilePhotoValidator exception."
---

# Validation Rules

> Declarative rules only. For the **step-by-step recipe and full code examples** (annotated record,
> `InputValidator` usage, adding a new input record), use the **`add-feature-slice`** skill
> (`.github/skills/add-feature-slice/`).

## Annotated input records are the single source of truth

- Every input record lives in `Nova.Shared/{Feature}/{Name}Input.cs` and carries its validation rules
  as DataAnnotations. Services do not hand-roll field checks; they run the same attributes.
- **Use explicit init-only properties, not positional constructor parameters.** Attributes on
  positional record parameters land on the *constructor parameter*, which `Validator.TryValidateObject`
  does not reflect on. The explicit property form is required.

## `[Required]` + `[NotWhitespace]`

- `[Required]` rejects a missing value (`null`) but treats `"   "` as valid.
- `[NotWhitespace]` (`Nova.Shared/Validation/NotWhitespaceAttribute.cs`) rejects empty/whitespace-only
  strings and passes `null` (so `[Required]` owns "missing", `[NotWhitespace]` owns "present but blank").
- **Always pair them** (`[Required, NotWhitespace]`) on every string that must contain non-blank text.
- Add `[MaxLength]`, `[Range]`, `[EmailAddress]`, etc. as appropriate.

## `InputValidator.Validate<T>`

- `Nova.Shared/Validation/InputValidator.cs` runs `Validator.TryValidateObject(…,
  validateAllProperties: true)` and projects results into the `Dictionary<string, string[]>` shape
  `ServiceProblem.Validation` consumes (empty = valid).
- Call it at the top of the consuming service method and short-circuit with
  `ServiceProblem.Validation(errors)` when `errors.Count > 0`.
- **Do not** rebuild a dictionary by hand with `IsNullOrWhiteSpace`/`.Length` checks for a rule an
  attribute already expresses — add or change the attribute on the record instead.

## Dual-layer rationale

Endpoint-layer validation (`AddValidation()`) runs only for HTTP requests; SSR pages, background jobs,
and tests call services directly. The service is therefore the authoritative validation boundary on
every path. Both layers read the same attributes. See
`.github/instructions/service-layer.instructions.md` → **Dual-Layer Validation**.

## Documented exception: `ProfilePhotoValidator`

`Nova/Features/Photos/ProfilePhotoValidator.cs` validates uploaded image bytes by sniffing magic bytes
from a `ReadOnlySpan<byte>` and cross-checking the declared content type. This cannot be expressed as a
DataAnnotations attribute (there is no model property — the input is a raw byte span + `IFormFile`), so
it stays a standalone validator invoked manually in the upload handler. This is the **only** sanctioned
exception to the "annotate the record + `InputValidator`" rule. Other non-model validation (file size,
content sniffing, streaming) follows the same manual-validator approach — see
`.github/instructions/api-endpoints.instructions.md` → endpoint validation.

## Related

- `.github/skills/add-feature-slice/` — full input + validation recipe and examples.
- `Nova.Shared/Validation/InputValidator.cs`, `Nova.Shared/Validation/NotWhitespaceAttribute.cs`.
- `Nova.Shared/Results/ServiceProblem.cs`.
- `Nova/Features/Photos/ProfilePhotoValidator.cs`.
- `.github/instructions/service-layer.instructions.md`, `.github/instructions/api-endpoints.instructions.md`.
