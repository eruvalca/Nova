using Nova.Shared.Enums;

namespace Nova.Shared.Features.Tags;

/// <summary>
/// Represents a club's tag definition and its lifecycle state.
/// </summary>
public sealed record TagDefinitionDto
{
    /// <summary>
    /// Gets the tag definition's unique identifier.
    /// </summary>
    public required long PlayerTagId { get; init; }

    /// <summary>
    /// Gets the tag definition's display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the tag definition's normalized <c>#RRGGBB</c> color.
    /// </summary>
    public required string Color { get; init; }

    /// <summary>
    /// Gets the tag definition's lifecycle state.
    /// </summary>
    public required LifecycleStatus LifecycleStatus { get; init; }
}
