namespace Nova.Shared.Enums;

/// <summary>
/// Identifies the kind of tag-definition mutation durably recorded by a receipt.
/// </summary>
public enum TagDefinitionMutationType
{
    /// <summary>
    /// Indicates that a tag definition was updated.
    /// </summary>
    Updated = 0,

    /// <summary>
    /// Indicates that a tag definition was archived.
    /// </summary>
    Archived = 1,

    /// <summary>
    /// Indicates that a tag definition was restored.
    /// </summary>
    Restored = 2,
}
