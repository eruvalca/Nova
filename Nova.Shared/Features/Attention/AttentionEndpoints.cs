namespace Nova.Shared.Features.Attention;

/// <summary>
/// Defines the shared route constants for the club attention read endpoint so the server and
/// WebAssembly client agree on the route.
/// </summary>
public static class AttentionEndpoints
{
    /// <summary>
    /// The group prefix for club attention endpoints.
    /// </summary>
    public const string GroupPrefix = "/api/attention";

    /// <summary>
    /// Gets the club attention projection route (GET).
    /// </summary>
    public const string GetClubAttention = GroupPrefix;

    /// <summary>
    /// Gets the club attention projection route relative to the attention group (empty maps GET to
    /// the group root).
    /// </summary>
    public const string GetClubAttentionRelative = "";

    /// <summary>
    /// Gets the route name assigned to the club attention projection query.
    /// </summary>
    public const string GetClubAttentionRouteName = "GetClubAttention";
}
