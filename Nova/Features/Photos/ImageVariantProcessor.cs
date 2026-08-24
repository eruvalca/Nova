using Nova.Shared.Features.Photos;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Nova.Features.Photos;

/// <summary>
/// Shared ImageSharp pipeline used by both profile photos and club crests: sanitizes the
/// source (auto-orient, strip metadata) and produces a re-encoded original plus small,
/// medium, and large center-cropped WebP square variants.
/// </summary>
public static class ImageVariantProcessor
{
    /// <summary>
    /// The maximum pixel dimension accepted for a source image, guarding against decompression bombs.
    /// </summary>
    public const int MaxSourceDimension = 8192;

    /// <summary>
    /// Decodes the source image, sanitizes it, and produces the metadata-free re-encoded
    /// original plus the small, medium, and large WebP square variants.
    /// </summary>
    /// <param name="content">The validated source image bytes.</param>
    /// <param name="contentType">The sniffed source content type, used to re-encode the original in its own format.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The sanitized original and the encoded variants.</returns>
    public static ProcessedVariants GenerateVariants(byte[] content, string contentType, CancellationToken cancellationToken)
    {
        var decoderOptions = new DecoderOptions { MaxFrames = 1 };
        using var image = Image.Load(decoderOptions, content);

        // Bake the EXIF orientation into the pixels, then strip metadata (EXIF/GPS, XMP)
        // so neither the stored original nor the variants leak location or device data.
        image.Mutate(context => context.AutoOrient());
        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;

        return new ProcessedVariants(
            EncodeOriginal(image, contentType, cancellationToken),
            EncodeSquareVariant(image, ProfilePhotoConstraints.SmallSize, cancellationToken),
            EncodeSquareVariant(image, ProfilePhotoConstraints.MediumSize, cancellationToken),
            EncodeSquareVariant(image, ProfilePhotoConstraints.LargeSize, cancellationToken));
    }

    /// <summary>
    /// Re-encodes the sanitized source image in its original format so the stored
    /// "original" blob carries no EXIF/XMP metadata.
    /// </summary>
    /// <param name="source">The decoded, sanitized source image.</param>
    /// <param name="contentType">The sniffed source content type selecting the encoder.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The re-encoded original bytes.</returns>
    private static byte[] EncodeOriginal(Image source, string contentType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ImageEncoder encoder = contentType switch
        {
            "image/png" => new PngEncoder(),
            "image/webp" => new WebpEncoder(),
            _ => new JpegEncoder()
        };

        using var stream = new MemoryStream();
        source.Save(stream, encoder);
        return stream.ToArray();
    }

    /// <summary>
    /// Produces a center-cropped square variant of the source image encoded as WebP.
    /// </summary>
    /// <param name="source">The decoded source image.</param>
    /// <param name="size">The target square size in pixels.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The encoded WebP bytes.</returns>
    private static byte[] EncodeSquareVariant(Image source, int size, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var variant = source.Clone(context => context.Resize(new ResizeOptions
        {
            Size = new Size(size, size),
            Mode = ResizeMode.Crop,
            Position = AnchorPositionMode.Center
        }));

        using var stream = new MemoryStream();
        variant.Save(stream, new WebpEncoder());
        return stream.ToArray();
    }

    /// <summary>
    /// Maps an allowed content type to a file extension for blob naming.
    /// </summary>
    /// <param name="contentType">The sniffed content type.</param>
    /// <returns>The file extension, including the leading dot.</returns>
    public static string GetExtension(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".bin"
    };

    /// <summary>
    /// Holds the sanitized re-encoded original and the encoded image variants.
    /// </summary>
    /// <param name="Original">The sanitized original, re-encoded in its source format without metadata.</param>
    /// <param name="Small">The encoded small variant.</param>
    /// <param name="Medium">The encoded medium variant.</param>
    /// <param name="Large">The encoded large variant.</param>
    public sealed record ProcessedVariants(byte[] Original, byte[] Small, byte[] Medium, byte[] Large);
}
