using Nova.Features.Photos;
using Shouldly;

namespace Nova.Unit.Tests.Features.Photos;

/// <summary>
/// Tests for <see cref="ProfilePhotoValidator"/>: size limits, allowed content types, and
/// magic-byte sniffing (the declared content type must match the actual file content).
/// </summary>
public class ProfilePhotoValidatorTests
{
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
    private static readonly byte[] WebpBytes = [.. "RIFF"u8.ToArray(), 0x24, 0x00, 0x00, 0x00, .. "WEBP"u8.ToArray(), .. "VP8 "u8.ToArray()];
    private static readonly byte[] GifBytes = [.. "GIF89a"u8.ToArray(), 0x01, 0x00, 0x01, 0x00];

    [Theory]
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

        var errors = ProfilePhotoValidator.Validate(content, contentType);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_Fails_WhenContentIsEmpty()
    {
        var errors = ProfilePhotoValidator.Validate([], "image/jpeg");

        errors.ShouldHaveSingleItem();
        errors[0].ShouldContain("No photo");
    }

    [Fact]
    public void Validate_Fails_WhenContentExceedsMaxBytes()
    {
        var oversized = new byte[Nova.Shared.Photos.ProfilePhotoConstraints.MaxBytes + 1];
        JpegBytes.CopyTo(oversized, 0);

        var errors = ProfilePhotoValidator.Validate(oversized, "image/jpeg");

        errors.ShouldContain(error => error.Contains("maximum allowed size"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("image/gif")]
    [InlineData("image/svg+xml")]
    [InlineData("application/octet-stream")]
    public void Validate_Fails_WhenDeclaredTypeIsNotAllowed(string? contentType)
    {
        var errors = ProfilePhotoValidator.Validate(JpegBytes, contentType);

        errors.ShouldContain(error => error.Contains("Only JPEG, PNG, and WebP"));
    }

    [Fact]
    public void Validate_Fails_WhenContentIsNotARecognizedImage()
    {
        var errors = ProfilePhotoValidator.Validate(GifBytes, "image/jpeg");

        errors.ShouldContain(error => error.Contains("not a recognized"));
    }

    [Fact]
    public void Validate_Fails_WhenContentDoesNotMatchDeclaredType()
    {
        // A real PNG renamed/declared as JPEG must be rejected.
        var errors = ProfilePhotoValidator.Validate(PngBytes, "image/jpeg");

        errors.ShouldContain(error => error.Contains("does not match"));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    public void SniffContentType_DetectsFormat_FromMagicBytes(byte[] content, string expected)
    {
        ProfilePhotoValidator.SniffContentType(content).ShouldBe(expected);
    }

    [Fact]
    public void SniffContentType_DetectsWebp_FromRiffHeader()
    {
        ProfilePhotoValidator.SniffContentType(WebpBytes).ShouldBe("image/webp");
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03 })]
    public void SniffContentType_ReturnsNull_ForUnknownContent(byte[] content)
    {
        ProfilePhotoValidator.SniffContentType(content).ShouldBeNull();
    }

    [Fact]
    public void SniffContentType_ReturnsNull_ForRiffThatIsNotWebp()
    {
        // RIFF container that is not WebP (e.g. WAV).
        byte[] wav = [.. "RIFF"u8.ToArray(), 0x24, 0x00, 0x00, 0x00, .. "WAVE"u8.ToArray()];

        ProfilePhotoValidator.SniffContentType(wav).ShouldBeNull();
    }
}
