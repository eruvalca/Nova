namespace Nova.Shared.Security;

/// <summary>
/// Defines custom claim type names used by the application.
/// </summary>
public static class NovaClaimTypes
{
    /// <summary>
    /// The claim type holding the user's club (tenant) id.
    /// </summary>
    public const string ClubId = "nova:club_id";

    /// <summary>
    /// The claim type indicating the user has uploaded a profile photo.
    /// </summary>
    public const string HasProfilePhoto = "nova:has_profile_photo";
}
