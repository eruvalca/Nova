namespace Nova.Shared.Players;

/// <summary>
/// Defines shared route constants for player detail endpoints so server and client remain synchronized.
/// </summary>
public static class PlayerEndpoints
{
    /// <summary>
    /// The group prefix for player endpoints.
    /// </summary>
    public const string GroupPrefix = "/api/players";

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
