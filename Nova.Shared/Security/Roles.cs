namespace Nova.Shared.Security;

/// <summary>
/// Defines role names used by authorization policies throughout the application.
/// </summary>
public static class Roles
{
    /// <summary>
    /// Grants access to platform-wide administrative capabilities.
    /// </summary>
    public const string Admin = "Admin";

    /// <summary>
    /// Grants access to administrative capabilities scoped to a club.
    /// </summary>
    public const string ClubAdmin = "ClubAdmin";

    /// <summary>
    /// Grants access to standard authenticated user capabilities.
    /// </summary>
    public const string StandardUser = "StandardUser";
}
