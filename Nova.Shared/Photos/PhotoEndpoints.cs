namespace Nova.Shared.Photos;

/// <summary>
/// Defines the route constants for profile photo endpoints so the client and server agree on routes.
/// </summary>
public static class PhotoEndpoints
{
    /// <summary>
    /// The group prefix for profile photo endpoints.
    /// </summary>
    public const string GroupPrefix = "/api/account";

    /// <summary>
    /// The endpoint that accepts the current user's profile photo upload (POST, multipart form data).
    /// </summary>
    public const string Upload = "/api/account/profile-photo";

    /// <summary>
    /// The relative path for profile photo upload within the group.
    /// </summary>
    public const string UploadRelative = "profile-photo";

    /// <summary>
    /// The endpoint that reports the current user's profile photo status (GET).
    /// </summary>
    public const string Status = "/api/account/profile-photo/status";

    /// <summary>
    /// The relative path for photo status within the group.
    /// </summary>
    public const string StatusRelative = "profile-photo/status";

    /// <summary>
    /// The route template for retrieving a user's profile photo (GET), with a <c>size</c> query
    /// parameter. Mapped outside the account group at its absolute path.
    /// </summary>
    public const string GetTemplate = "/api/users/{userId:long}/photo";

    /// <summary>
    /// Builds the URL for retrieving a user's profile photo at the requested size.
    /// </summary>
    /// <param name="userId">The id of the user whose photo to retrieve.</param>
    /// <param name="size">The photo variant to retrieve.</param>
    /// <returns>The relative URL of the photo endpoint.</returns>
    public static string GetPhotoUrl(long userId, ProfilePhotoSize size) =>
        $"/api/users/{userId}/photo?size={size.ToString().ToLowerInvariant()}";

    /// <summary>
    /// The full-document navigation endpoint that refreshes the auth cookie after a photo is saved
    /// and redirects to the supplied return URL. Mapped outside the account group at its absolute path.
    /// </summary>
    public const string Complete = "/Account/ProfilePhoto/Complete";
}
