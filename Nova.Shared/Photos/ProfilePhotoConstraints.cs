namespace Nova.Shared.Photos;

/// <summary>
/// Defines the validation constraints and variant sizes for profile photo uploads,
/// shared by client-side and server-side validation so both enforce identical rules.
/// </summary>
public static class ProfilePhotoConstraints
{
    /// <summary>
    /// The maximum allowed upload size in bytes (10 MB).
    /// </summary>
    public const long MaxBytes = 10 * 1024 * 1024;

    /// <summary>
    /// The content types accepted for profile photo uploads.
    /// </summary>
    public static readonly IReadOnlyList<string> AllowedContentTypes =
    [
        "image/jpeg",
        "image/png",
        "image/webp"
    ];

    /// <summary>
    /// The <c>accept</c> attribute value for file inputs, derived from <see cref="AllowedContentTypes"/>.
    /// </summary>
    public const string AcceptAttribute = "image/jpeg,image/png,image/webp";

    /// <summary>
    /// The pixel size (width and height) of the small square variant.
    /// </summary>
    public const int SmallSize = 64;

    /// <summary>
    /// The pixel size (width and height) of the medium square variant.
    /// </summary>
    public const int MediumSize = 256;

    /// <summary>
    /// The pixel size (width and height) of the large square variant.
    /// </summary>
    public const int LargeSize = 1024;
}
