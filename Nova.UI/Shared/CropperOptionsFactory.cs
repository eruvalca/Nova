using Cropper.Blazor.Models;

namespace Nova.UI.Shared;

/// <summary>
/// Single source of truth for the <see cref="Options"/> used by the club crest cropper, so both
/// crop entry points (club creation and the club admin page) share the exact same free-form
/// configuration and cannot drift apart.
/// </summary>
public static class CropperOptionsFactory
{
    /// <summary>
    /// Creates the free-form cropper options for the club crest: no fixed aspect ratio and the
    /// full image pre-selected as the crop area, matching the profile-photo cropper behavior.
    /// </summary>
    /// <returns>A new <see cref="Options"/> instance configured for crest cropping.</returns>
    public static Options CreateCrestOptions() => new()
    {
        ViewMode = ViewMode.Vm1,
        AutoCrop = true,
        AutoCropArea = 1m,
    };
}
