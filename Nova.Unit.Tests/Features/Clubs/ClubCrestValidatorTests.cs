using Nova.Features.Clubs;
using Nova.Shared.Features.Photos;
using Shouldly;

namespace Nova.Unit.Tests.Features.Clubs;

/// <summary>
/// Tests for <see cref="ClubCrestValidator"/>: the required-upload rule, size limits, allowed
/// content types, and magic-byte sniffing (the declared content type must match the actual file).
/// Crests reuse <see cref="ProfilePhotoConstraints"/> so every rule must mirror the profile-photo
/// validator exactly.
/// </summary>
public class ClubCrestValidatorTests
{
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
    private static readonly byte[] WebpBytes = [.. "RIFF"u8.ToArray(), 0x24, 0x00, 0x00, 0x00, .. "WEBP"u8.ToArray(), .. "VP8 "u8.ToArray()];
    private static readonly byte[] GifBytes = [.. "GIF89a"u8.ToArray(), 0x01, 0x00, 0x01, 0x00];

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public void Validate_Passes_WhenContentMatchesDeclaredType(string contentType)
    {
        var content = contentType switch
        {
            "image/jpeg" => JpegBytes,
            "image/png" => PngBytes,
            _ => WebpBytes
        };

        var errors = ClubCrestValidator.Validate(content, contentType);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_Fails_WhenContentIsEmpty()
    {
        var errors = ClubCrestValidator.Validate([], "image/jpeg");

        errors.ShouldHaveSingleItem();
        errors[0].ShouldBe("A club crest is required.");
    }

    [Fact]
    public void Validate_Fails_WhenContentExceedsMaxBytes()
    {
        var oversized = new byte[ProfilePhotoConstraints.MaxBytes + 1];
        JpegBytes.CopyTo(oversized, 0);

        var errors = ClubCrestValidator.Validate(oversized, "image/jpeg");

        errors.ShouldContain(error => error.Contains("maximum allowed size"));
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null)]
    [InlineData("image/gif")]
    [InlineData("image/svg+xml")]
    [InlineData("application/octet-stream")]
    public void Validate_Fails_WhenDeclaredTypeIsNotAllowed(string? contentType)
    {
        var errors = ClubCrestValidator.Validate(JpegBytes, contentType);

        errors.ShouldContain(error => error.Contains("Only JPEG, PNG, and WebP"));
    }

    [Fact]
    public void Validate_Fails_WhenContentIsNotARecognizedImage()
    {
        var errors = ClubCrestValidator.Validate(GifBytes, "image/jpeg");

        errors.ShouldContain(error => error.Contains("not a recognized"));
    }

    [Fact]
    public void Validate_Fails_WhenContentDoesNotMatchDeclaredType()
    {
        // A real PNG renamed/declared as JPEG must be rejected.
        var errors = ClubCrestValidator.Validate(PngBytes, "image/jpeg");

        errors.ShouldContain(error => error.Contains("does not match"));
    }

    [Fact]
    public void Validate_AcceptsContentType_IgnoringCase()
    {
        // The declared content type is matched case-insensitively against the sniffed format.
        var errors = ClubCrestValidator.Validate(JpegBytes, "IMAGE/JPEG");

        errors.ShouldBeEmpty();
    }
}
