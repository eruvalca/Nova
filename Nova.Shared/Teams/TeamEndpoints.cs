namespace Nova.Shared.Teams;

/// <summary>
/// Defines shared team-management routes used by the server and WebAssembly client.
/// </summary>
public static class TeamEndpoints
{
    /// <summary>
    /// Gets the team-management route group prefix.
    /// </summary>
    public const string GroupPrefix = "/api/teams";

    /// <summary>
    /// Gets the absolute create-team route.
    /// </summary>
    public const string Create = "/api/teams";

    /// <summary>
    /// Gets the create-team route relative to the group.
    /// </summary>
    public const string CreateRelative = "";

    /// <summary>
    /// Gets the absolute update-team route template.
    /// </summary>
    public const string UpdateTemplate = "/api/teams/{teamId:long}";

    /// <summary>
    /// Gets the update-team route template relative to the group.
    /// </summary>
    public const string UpdateRelative = "{teamId:long}";

    /// <summary>
    /// Gets the absolute team-detail route template.
    /// </summary>
    public const string GetDetailTemplate = "/api/teams/{teamId:long}";

    /// <summary>
    /// Gets the team-detail route template relative to the group.
    /// </summary>
    public const string GetDetailRelative = "{teamId:long}";

    /// <summary>
    /// Builds the URL for retrieving a team's detail and placement history.
    /// </summary>
    /// <param name="teamId">The team identifier.</param>
    /// <returns>The team-detail URL.</returns>
    public static string GetDetailUrl(long teamId) => $"{GroupPrefix}/{teamId}";

    /// <summary>
    /// Builds the URL for updating the specified team.
    /// </summary>
    /// <param name="teamId">The team identifier.</param>
    /// <returns>The update-team URL.</returns>
    public static string UpdateUrl(long teamId) => $"{GroupPrefix}/{teamId}";

    /// <summary>
    /// Gets the absolute archive route template.
    /// </summary>
    public const string ArchiveTemplate = "/api/teams/{teamId:long}/archive";

    /// <summary>
    /// Gets the archive route template relative to the group.
    /// </summary>
    public const string ArchiveRelative = "{teamId:long}/archive";

    /// <summary>
    /// Gets the absolute restore route template.
    /// </summary>
    public const string RestoreTemplate = "/api/teams/{teamId:long}/restore";

    /// <summary>
    /// Gets the restore route template relative to the group.
    /// </summary>
    public const string RestoreRelative = "{teamId:long}/restore";

    /// <summary>
    /// Gets the absolute graduation-year update route template.
    /// </summary>
    public const string UpdateGraduationYearTemplate = "/api/teams/{teamId:long}/graduation-year";

    /// <summary>
    /// Gets the graduation-year update route template relative to the group.
    /// </summary>
    public const string UpdateGraduationYearRelative = "{teamId:long}/graduation-year";

    /// <summary>
    /// Builds the URL for archiving a team.
    /// </summary>
    /// <param name="teamId">The team identifier.</param>
    /// <returns>The archive URL.</returns>
    public static string ArchiveUrl(long teamId) => $"{GroupPrefix}/{teamId}/archive";

    /// <summary>
    /// Builds the URL for restoring a team.
    /// </summary>
    /// <param name="teamId">The team identifier.</param>
    /// <returns>The restore URL.</returns>
    public static string RestoreUrl(long teamId) => $"{GroupPrefix}/{teamId}/restore";

    /// <summary>
    /// Builds the URL for updating a team's graduation-year cutoff.
    /// </summary>
    /// <param name="teamId">The team identifier.</param>
    /// <returns>The graduation-year update URL.</returns>
    public static string UpdateGraduationYearUrl(long teamId) => $"{GroupPrefix}/{teamId}/graduation-year";
}
