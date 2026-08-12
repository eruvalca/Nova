namespace Nova.Shared.Features.Tags;

/// <summary>
/// Defines shared tag-definition API routes used by the server and WebAssembly client.
/// </summary>
public static class TagDefinitionEndpoints
{
    /// <summary>
    /// Gets the tag-definition route group prefix.
    /// </summary>
    public const string GroupPrefix = "/api/tags";

    /// <summary>
    /// Gets the route name for the active tag-definition query used in UI calls.
    /// </summary>
    public const string GetActiveRouteName = "GetActiveTagDefinitions";

    /// <summary>
    /// Gets the route name for the archived tag-definition query used in UI calls.
    /// </summary>
    public const string GetArchivedRouteName = "GetArchivedTagDefinitions";

    /// <summary>
    /// Gets the absolute create-tag-definition route.
    /// </summary>
    public const string Create = "/api/tags";

    /// <summary>
    /// Gets the create-tag-definition route relative to the group.
    /// </summary>
    public const string CreateRelative = "";

    /// <summary>
    /// Gets the absolute update-tag-definition route template.
    /// </summary>
    public const string UpdateTemplate = "/api/tags/{tagDefinitionId:long}";

    /// <summary>
    /// Gets the update-tag-definition route relative to the group.
    /// </summary>
    public const string UpdateRelative = "{tagDefinitionId:long}";

    /// <summary>
    /// Gets the absolute active tag-definition query route.
    /// </summary>
    public const string ListActive = "/api/tags/active";

    /// <summary>
    /// Gets the active tag-definition list route relative to the group.
    /// </summary>
    public const string ListActiveRelative = "active";

    /// <summary>
    /// Gets the absolute archived tag-definition query route.
    /// </summary>
    public const string ListArchived = "/api/tags/archived";

    /// <summary>
    /// Gets the archived tag-definition list route relative to the group.
    /// </summary>
    public const string ListArchivedRelative = "archived";

    /// <summary>
    /// Gets the absolute archive route template.
    /// </summary>
    public const string ArchiveTemplate = "/api/tags/{tagDefinitionId:long}/archive";

    /// <summary>
    /// Gets the archive route template relative to the group.
    /// </summary>
    public const string ArchiveRelative = "{tagDefinitionId:long}/archive";

    /// <summary>
    /// Gets the absolute restore route template.
    /// </summary>
    public const string RestoreTemplate = "/api/tags/{tagDefinitionId:long}/restore";

    /// <summary>
    /// Gets the restore route template relative to the group.
    /// </summary>
    public const string RestoreRelative = "{tagDefinitionId:long}/restore";

    /// <summary>
    /// Builds the update URL for the specified tag definition.
    /// </summary>
    /// <param name="tagDefinitionId">The tag-definition identifier.</param>
    /// <returns>The tag-definition update URL.</returns>
    public static string UpdateUrl(long tagDefinitionId) => $"{GroupPrefix}/{tagDefinitionId}";

    /// <summary>
    /// Builds the archive URL for the specified tag definition.
    /// </summary>
    /// <param name="tagDefinitionId">The tag-definition identifier.</param>
    /// <returns>The tag-definition archive URL.</returns>
    public static string ArchiveUrl(long tagDefinitionId) => $"{GroupPrefix}/{tagDefinitionId}/archive";

    /// <summary>
    /// Builds the restore URL for the specified tag definition.
    /// </summary>
    /// <param name="tagDefinitionId">The tag-definition identifier.</param>
    /// <returns>The tag-definition restore URL.</returns>
    public static string RestoreUrl(long tagDefinitionId) => $"{GroupPrefix}/{tagDefinitionId}/restore";
}
