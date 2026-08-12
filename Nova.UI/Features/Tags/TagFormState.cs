using System.ComponentModel.DataAnnotations;
using Nova.Shared.Features.Tags;
using Nova.Shared.Validation;

namespace Nova.UI.Features.Tags;

/// <summary>
/// Mutable create/edit form state for tag definitions, reusing the shared input-record validation rules.
/// </summary>
public sealed class TagFormState : IValidatableObject
{
    /// <summary>
    /// The default color applied to newly created tag definitions.
    /// </summary>
    private const string DefaultColor = "#0D6EFD";

    /// <summary>
    /// Gets or sets whether this state represents edit mode.
    /// </summary>
    public bool IsEdit { get; set; }

    /// <summary>
    /// Gets or sets the tag-definition identifier in edit mode.
    /// </summary>
    public long TagId { get; set; }

    /// <summary>
    /// Gets or sets the tag definition's display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tag definition's <c>#RRGGBB</c> color.
    /// </summary>
    public string Color { get; set; } = DefaultColor;

    /// <summary>
    /// Creates a default create-mode form state.
    /// </summary>
    /// <returns>A create-mode form state.</returns>
    public static TagFormState CreateDefault() => new();

    /// <summary>
    /// Creates an edit-mode form state from a tag definition.
    /// </summary>
    /// <param name="tag">The tag definition to edit.</param>
    /// <returns>An edit-mode form state.</returns>
    public static TagFormState FromDto(TagDefinitionDto tag) => new()
    {
        IsEdit = true,
        TagId = tag.PlayerTagId,
        Name = tag.Name,
        Color = tag.Color
    };

    /// <summary>
    /// Converts this state to a create-tag-definition input payload.
    /// </summary>
    /// <returns>A create-tag-definition input payload.</returns>
    public CreateTagDefinitionInput ToCreateInput() => new()
    {
        Name = Name,
        Color = NormalizeColor(Color)
    };

    /// <summary>
    /// Converts this state to an update-tag-definition input payload.
    /// </summary>
    /// <returns>An update-tag-definition input payload.</returns>
    public UpdateTagDefinitionInput ToUpdateInput() => new()
    {
        TagId = TagId,
        Name = Name,
        Color = NormalizeColor(Color)
    };

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

    /// <summary>
    /// Normalizes a color token to uppercase <c>#RRGGBB</c> for payload serialization.
    /// </summary>
    /// <param name="color">The raw color token.</param>
    /// <returns>The normalized color token.</returns>
    private static string NormalizeColor(string color)
        => string.IsNullOrWhiteSpace(color) ? color : color.Trim().ToUpperInvariant();
}
