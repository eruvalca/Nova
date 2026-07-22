namespace Nova.Shared.Players;

/// <summary>
/// Defines shared route constants for player endpoints so the client and server agree on routes.
/// </summary>
public static class PlayerEndpoints
{
    /// <summary>
    /// The group prefix for player endpoints.
    /// </summary>
    public const string GroupPrefix = "/api/players";

    /// <summary>
    /// Creates a new player and enrolls them in all Active campaigns (POST).
    /// </summary>
    public const string Create = "/api/players";

    /// <summary>
    /// The relative path for player creation within the group.
    /// </summary>
    public const string CreateRelative = "";

    /// <summary>
    /// The route template for updating a specific player's permanent profile (PUT).
    /// Use <see cref="UpdateUrl"/> to build the URL.
    /// </summary>
    public const string UpdateTemplate = "/api/players/{playerId:long}";

    /// <summary>
    /// The relative route template for player updates within the group.
    /// </summary>
    public const string UpdateRelative = "{playerId:long}";

    /// <summary>
    /// Builds the URL for updating a specific player.
    /// </summary>
    /// <param name="playerId">The identifier of the player to update.</param>
    /// <returns>The absolute URL of the update endpoint.</returns>
    public static string UpdateUrl(long playerId) => $"/api/players/{playerId}";

    /// <summary>
    /// The absolute template for retrieving one player's detail/history payload.
    /// </summary>
    public const string GetDetailTemplate = "/api/players/{playerId:long}";

    /// <summary>
    /// The relative template for retrieving one player's detail/history payload inside the player group.
    /// </summary>
    public const string GetDetailRelative = "{playerId:long}";

    /// <summary>
    /// Builds the URL for retrieving one player's detail/history payload.
    /// </summary>
    /// <param name="playerId">The player identifier.</param>
    /// <returns>The absolute player detail endpoint URL.</returns>
    public static string GetDetailUrl(long playerId) => $"/api/players/{playerId}";
}
