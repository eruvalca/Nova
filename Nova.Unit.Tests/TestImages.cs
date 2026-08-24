using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Nova.Unit.Tests;

/// <summary>
/// Produces small, valid image byte arrays for unit tests that exercise the shared
/// ImageSharp processing pipeline (club crest creation, profile photo validation).
/// </summary>
public static class TestImages
{
    /// <summary>
    /// Creates an in-memory JPEG of the requested dimensions filled with the request color.
    /// </summary>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <returns>The encoded JPEG bytes.</returns>
    public static byte[] CreateJpeg(int width = 64, int height = 64)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(120, 180, 240));
        using var stream = new MemoryStream();
        image.Save(stream, new JpegEncoder());
        return stream.ToArray();
    }
}
