using Nova.Shared.Photos;

namespace Nova.Features.Photos;

/// <summary>
/// Validates profile photo uploads server-side: enforces the size limit and verifies the
/// actual image format by sniffing magic bytes instead of trusting the supplied content type.
/// </summary>
public static class ProfilePhotoValidator
{
    /// <summary>
    /// Validates the supplied upload against <see cref="ProfilePhotoConstraints"/>.
    /// </summary>
    /// <param name="content">The raw uploaded bytes.</param>
    /// <param name="declaredContentType">The content type declared by the client.</param>
    /// <returns>A list of validation error messages; empty when the upload is valid.</returns>
    public static IReadOnlyList<string> Validate(ReadOnlySpan<byte> content, string? declaredContentType)
    {
        var errors = new List<string>();

        if (content.IsEmpty)
        {
            errors.Add("No photo was provided.");
            return errors;
        }

        if (content.Length > ProfilePhotoConstraints.MaxBytes)
        {
            errors.Add($"The photo exceeds the maximum allowed size of {ProfilePhotoConstraints.MaxBytes / (1024 * 1024)} MB.");
        }

        if (declaredContentType is null
            || !ProfilePhotoConstraints.AllowedContentTypes.Contains(declaredContentType, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("Only JPEG, PNG, and WebP images are allowed.");
            return errors;
        }

        var sniffed = SniffContentType(content);
        if (sniffed is null)
        {
            errors.Add("The file content is not a recognized JPEG, PNG, or WebP image.");
        }
        else if (!string.Equals(sniffed, declaredContentType, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("The file content does not match its declared image type.");
        }

        return errors;
    }

    /// <summary>
    /// Determines the actual image content type from the file's magic bytes.
    /// </summary>
    /// <param name="content">The raw file bytes.</param>
    /// <returns>The sniffed content type, or <see langword="null"/> when the bytes match no allowed format.</returns>
    public static string? SniffContentType(ReadOnlySpan<byte> content)
    {
        // JPEG: FF D8 FF
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return "image/jpeg";
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (content.Length >= pngSignature.Length && content[..pngSignature.Length].SequenceEqual(pngSignature))
        {
            return "image/png";
        }

        // WebP: "RIFF" .... "WEBP"
        if (content.Length >= 12
            && content[..4].SequenceEqual("RIFF"u8)
            && content[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}
