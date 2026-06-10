namespace Nova.Shared.Photos;

/// <summary>
/// Identifies which stored variant of a profile photo to retrieve.
/// </summary>
public enum ProfilePhotoSize
{
    /// <summary>
    /// The original cropped upload.
    /// </summary>
    Original,

    /// <summary>
    /// The small (64px) square variant.
    /// </summary>
    Small,

    /// <summary>
    /// The medium (256px) square variant.
    /// </summary>
    Medium,

    /// <summary>
    /// The large (1024px) square variant.
    /// </summary>
    Large
}
