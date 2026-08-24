using Nova.Features.Photos;
using Nova.Shared.Features.Photos;

namespace Nova.Features.Clubs;

/// <summary>
/// Validates club crest uploads server-side: enforces the same size limit and format rules
/// as profile photos (see <see cref="ProfilePhotoConstraints"/>), verifying the actual image
/// format by sniffing magic bytes instead of trusting the supplied content type.
/// </summary>
public static class ClubCrestValidator
{
    /// <summary>
    /// Validates the supplied crest upload against <see cref="ProfilePhotoConstraints"/>.
    /// </summary>
    /// <param name="content">The raw uploaded bytes.</param>
    /// <param name="declaredContentType">The content type declared by the client.</param>
    /// <returns>A list of validation error messages; empty when the upload is valid.</returns>
    public static IReadOnlyList<string> Validate(ReadOnlySpan<byte> content, string? declaredContentType)
    {
        var errors = new List<string>();

        if (content.IsEmpty)
        {
            errors.Add("A club crest is required.");
            return errors;
        }

        if (content.Length > ProfilePhotoConstraints.MaxBytes)
        {
            errors.Add($"The crest exceeds the maximum allowed size of {ProfilePhotoConstraints.MaxBytes / (1024 * 1024)} MB.");
        }

        if (declaredContentType is null
            || !ProfilePhotoConstraints.AllowedContentTypes.Contains(declaredContentType, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add("Only JPEG, PNG, and WebP images are allowed.");
            return errors;
        }

        var sniffed = ProfilePhotoValidator.SniffContentType(content);
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
}
