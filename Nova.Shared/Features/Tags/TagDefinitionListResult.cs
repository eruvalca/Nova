namespace Nova.Shared.Features.Tags;

/// <summary>
/// Represents the bounded tag-definition management-list response. <see cref="Items"/> is capped at
/// <see cref="TagDefinitionLimits.MaxTagDefinitions"/>, and <see cref="HasMore"/> reports whether additional
/// matching rows were truncated so callers can distinguish a full page from a club that has exactly the cap.
/// </summary>
public sealed record TagDefinitionListResult
{
    /// <summary>
    /// Gets the matching tag-definition rows, bounded to <see cref="TagDefinitionLimits.MaxTagDefinitions"/>.
    /// </summary>
    public required IReadOnlyList<TagDefinitionDto> Items { get; init; }

    /// <summary>
    /// Gets a value indicating whether more matching rows exist beyond the bounded page.
    /// </summary>
    public required bool HasMore { get; init; }
}
