using Cropper.Blazor.Components;
using Cropper.Blazor.Models;

namespace Nova.UI.Shared;

/// <summary>
/// Exports the cropped canvas of a <see cref="CropperComponent"/> as JPEG bytes, used when
/// re-uploading a cropped image (club crest or profile photo). Abstracted so component
/// tests can substitute the export without a browser.
/// </summary>
public interface ICropperCanvasExporter
{
    /// <summary>
    /// Exports the cropped canvas of the given cropper as JPEG bytes at up to
    /// <see cref="CropperCanvasExporter.MaxDimension"/> × <see cref="CropperCanvasExporter.MaxDimension"/>
    /// on a solid white background.
    /// </summary>
    /// <param name="cropper">The cropper component whose current crop should be exported.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The JPEG bytes of the cropped image.</returns>
    Task<byte[]> ExportAsync(CropperComponent cropper, CancellationToken cancellationToken);
}

/// <summary>
/// Shared implementation of <see cref="ICropperCanvasExporter"/> that mirrors the chunked
/// <c>GetCroppedCanvasDataInBackgroundAsync</c> + stream-to-bytes logic used by the profile
/// photo editor, so crest and photo uploads share one export path.
/// </summary>
public sealed class CropperCanvasExporter : ICropperCanvasExporter
{
    /// <summary>
    /// The maximum width and height in pixels of the exported image.
    /// </summary>
    public const int MaxDimension = 1024;

    /// <inheritdoc />
    public async Task<byte[]> ExportAsync(CropperComponent cropper, CancellationToken cancellationToken)
    {
        // Export as JPEG (universally supported by canvas) on a white background so
        // transparent source regions don't turn black. The background transfer streams
        // the image in chunks, which keeps SignalR messages small on server circuits.
        var imageReceiver = await cropper.GetCroppedCanvasDataInBackgroundAsync(
            new GetCroppedCanvasOptions
            {
                MaxWidth = MaxDimension,
                MaxHeight = MaxDimension,
                FillColor = "#ffffff",
                ImageSmoothingQuality = "high"
            },
            "image/jpeg",
            0.9f,
            maximumReceiveChunkSize: null,
            cancellationToken);

        using var imageStream = await imageReceiver.GetImageChunkStreamAsync(cancellationToken);
        return imageStream.ToArray();
    }
}

/// <summary>
/// Static entry point for the cropper canvas export, kept for call sites that have no need
/// for substitution (e.g. the profile photo editor).
/// </summary>
public static class CropperCanvasExport
{
    /// <summary>
    /// Exports the cropped canvas of the given cropper as JPEG bytes at up to
    /// <see cref="CropperCanvasExporter.MaxDimension"/> × <see cref="CropperCanvasExporter.MaxDimension"/>
    /// on a solid white background, smoothing with "high" quality.
    /// </summary>
    /// <param name="cropper">The cropper component whose current crop should be exported.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The JPEG bytes of the cropped image.</returns>
    public static Task<byte[]> ExportAsync(CropperComponent cropper, CancellationToken cancellationToken)
        => new CropperCanvasExporter().ExportAsync(cropper, cancellationToken);
}
