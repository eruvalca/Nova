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
}
