namespace Nova.Shared.Security;

/// <summary>
/// Defines authorization policy names used throughout the application.
/// </summary>
public static class Policies
{
    /// <summary>
    /// Requires the Admin role.
    /// </summary>
    public const string RequireAdmin = "RequireAdmin";

    /// <summary>
    /// Requires the ClubAdmin role (or Admin).
    /// </summary>
    public const string RequireClubAdmin = "RequireClubAdmin";

    /// <summary>
    /// Requires an authenticated user with a club membership claim.
    /// </summary>
    public const string RequireClubMember = "RequireClubMember";
}
