using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Features.Tags;

/// <summary>
/// Describes a new tag definition to create in the current club.
/// </summary>
public sealed record CreateTagDefinitionInput
{
    /// <summary>
    /// Gets the tag definition's display name.
    /// </summary>
    [Required, NotWhitespace, MaxLength(100)]
    public required string Name { get; init; }

    /// <summary>
    /// Gets the tag definition's color as <c>#RRGGBB</c> (validated case-insensitively).
    /// </summary>
    [Required, NotWhitespace, StringLength(7, MinimumLength = 7), RegularExpression("^#[0-9A-Fa-f]{6}$")]
    public required string Color { get; init; }
}
