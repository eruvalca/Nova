using System.Text;

namespace Nova.Shared.Features.Tags;

/// <summary>
/// Defines shared tag-definition routes used by the server and WebAssembly client.
/// </summary>
public static class TagEndpoints
{
    /// <summary>
    /// Gets the tag-definition route group prefix.
    /// </summary>
    public const string GroupPrefix = "/api/tags";

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
    public const string UpdateTemplate = "/api/tags/{tagId:long}";

    /// <summary>
    /// Gets the update-tag-definition route template relative to the group.
    /// </summary>
    public const string UpdateRelative = "{tagId:long}";

    /// <summary>
    /// Builds the URL for updating the specified tag definition.
    /// </summary>
    /// <param name="tagId">The tag-definition identifier.</param>
    /// <returns>The update-tag-definition URL.</returns>
    public static string UpdateUrl(long tagId) => $"{GroupPrefix}/{tagId}";

    /// <summary>
    /// Gets the absolute management-list route.
    /// </summary>
    public const string GetListTemplate = "/api/tags";

    /// <summary>
    /// Gets the management-list route relative to the group.
    /// </summary>
    public const string GetListRelative = "";

    /// <summary>
    /// Builds a tag-definition management-list URL from the accepted optional filters.
    /// </summary>
    /// <param name="search">The optional tag-name search term.</param>
    /// <param name="lifecycleStatus">The optional lifecycle view.</param>
    /// <returns>The tag-definition management-list URL.</returns>
    public static string GetListUrl(
        string? search = null,
        string? lifecycleStatus = null)
    {
        var url = new StringBuilder(GetListTemplate);
        var querySegments = new List<string>();

        if (!string.IsNullOrWhiteSpace(search))
        {
            querySegments.Add($"search={Uri.EscapeDataString(search.Trim())}");
        }

        var normalizedLifecycleStatus = lifecycleStatus?.Trim().ToLowerInvariant() switch
        {
            "active" => "active",
            "archived" => "archived",
            _ => null
        };
        if (normalizedLifecycleStatus is not null)
        {
            querySegments.Add($"lifecycleStatus={normalizedLifecycleStatus}");
        }

        if (querySegments.Count > 0)
        {
            _ = url.Append('?').Append(string.Join('&', querySegments));
        }

        return url.ToString();
    }

    /// <summary>
    /// Gets the absolute active-choices route.
    /// </summary>
    public const string GetChoicesTemplate = "/api/tags/choices";

    /// <summary>
    /// Gets the active-choices route relative to the group.
    /// </summary>
    public const string GetChoicesRelative = "choices";

    /// <summary>
    /// Builds the URL for retrieving active tag-definition choices.
    /// </summary>
    /// <returns>The active-choices URL.</returns>
    public static string GetChoicesUrl() => $"{GroupPrefix}/{GetChoicesRelative}";

    /// <summary>
    /// Gets the absolute archive route template.
    /// </summary>
    public const string ArchiveTemplate = "/api/tags/{tagId:long}/archive";

    /// <summary>
    /// Gets the archive route template relative to the group.
    /// </summary>
    public const string ArchiveRelative = "{tagId:long}/archive";

    /// <summary>
    /// Builds the URL for archiving a tag definition.
    /// </summary>
    /// <param name="tagId">The tag-definition identifier.</param>
    /// <returns>The archive URL.</returns>
    public static string ArchiveUrl(long tagId) => $"{GroupPrefix}/{tagId}/archive";

    /// <summary>
    /// Gets the absolute restore route template.
    /// </summary>
    public const string RestoreTemplate = "/api/tags/{tagId:long}/restore";

    /// <summary>
    /// Gets the restore route template relative to the group.
    /// </summary>
    public const string RestoreRelative = "{tagId:long}/restore";

    /// <summary>
    /// Builds the URL for restoring a tag definition.
    /// </summary>
    /// <param name="tagId">The tag-definition identifier.</param>
    /// <returns>The restore URL.</returns>
    public static string RestoreUrl(long tagId) => $"{GroupPrefix}/{tagId}/restore";
}
