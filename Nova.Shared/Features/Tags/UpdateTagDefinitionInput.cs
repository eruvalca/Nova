using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Features.Tags;

/// <summary>
/// Describes permanent profile changes for an active tag definition.
/// </summary>
public sealed record UpdateTagDefinitionInput
{
    /// <summary>
    /// Gets the identifier of the tag definition to update.
    /// </summary>
    [Required, Range(1, long.MaxValue)]
    public required long TagId { get; init; }

    /// <summary>
    /// Gets the tag definition's replacement display name.
    /// </summary>
    [Required, NotWhitespace, MaxLength(100)]
    public required string Name { get; init; }

    /// <summary>
    /// Gets the tag definition's replacement color as <c>#RRGGBB</c> (validated case-insensitively).
    /// </summary>
    [Required, NotWhitespace, StringLength(7, MinimumLength = 7), RegularExpression("^#[0-9A-Fa-f]{6}$")]
    public required string Color { get; init; }
}
