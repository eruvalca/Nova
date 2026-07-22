namespace Nova.Shared.Players;

/// <summary>
/// Defines route constants for player lifecycle endpoints so server and WebAssembly clients share one source of truth.
/// </summary>
public static class PlayerEndpoints
{
    /// <summary>
    /// The group prefix for player lifecycle endpoints.
    /// </summary>
    public const string GroupPrefix = "/api/players";

    /// <summary>
    /// Absolute archive route template.
    /// </summary>
    public const string ArchiveTemplate = "/api/players/{playerId:long}/archive";

    /// <summary>
    /// Relative archive route template within <see cref="GroupPrefix"/>.
    /// </summary>
    public const string ArchiveRelative = "{playerId:long}/archive";

    /// <summary>
    /// Absolute restore route template.
    /// </summary>
    public const string RestoreTemplate = "/api/players/{playerId:long}/restore";

    /// <summary>
    /// Relative restore route template within <see cref="GroupPrefix"/>.
    /// </summary>
    public const string RestoreRelative = "{playerId:long}/restore";

    /// <summary>
    /// Builds the archive endpoint URL for a specific player.
    /// </summary>
    /// <param name="playerId">The player identifier to archive.</param>
    /// <returns>The archive endpoint URL.</returns>
    public static string ArchiveUrl(long playerId) => $"/api/players/{playerId}/archive";

    /// <summary>
    /// Builds the restore endpoint URL for a specific player.
    /// </summary>
    /// <param name="playerId">The player identifier to restore.</param>
    /// <returns>The restore endpoint URL.</returns>
    public static string RestoreUrl(long playerId) => $"/api/players/{playerId}/restore";
}
