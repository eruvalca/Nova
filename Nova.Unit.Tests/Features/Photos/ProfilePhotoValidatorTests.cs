using Nova.Features.Photos;
using Shouldly;

namespace Nova.Unit.Tests.Features.Photos;

/// <summary>
/// Tests for <see cref="ProfilePhotoValidator"/>: size limits, allowed content types, and
/// magic-byte sniffing (the declared content type must match the actual file content).
/// </summary>
public class ProfilePhotoValidatorTests
{
    private static readonly byte[] _jpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
    private static readonly byte[] _pngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
    private static readonly byte[] _webpBytes = [.. "RIFF"u8.ToArray(), 0x24, 0x00, 0x00, 0x00, .. "WEBP"u8.ToArray(), .. "VP8 "u8.ToArray()];
    private static readonly byte[] _gifBytes = [.. "GIF89a"u8.ToArray(), 0x01, 0x00, 0x01, 0x00];

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public void ValidatePassesWhenContentMatchesDeclaredType(string contentType)
    {
        var content = contentType switch
        {
            "image/jpeg" => _jpegBytes,
            "image/png" => _pngBytes,
            _ => _webpBytes
        };

        var errors = ProfilePhotoValidator.Validate(content, contentType);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void ValidateFailsWhenContentIsEmpty()
    {
        var errors = ProfilePhotoValidator.Validate([], "image/jpeg");

        errors.ShouldHaveSingleItem();
        errors[0].ShouldContain("No photo");
    }

    [Fact]
    public void ValidateFailsWhenContentExceedsMaxBytes()
    {
        var oversized = new byte[Nova.SharedKernel.Features.Photos.ProfilePhotoConstraints.MaxBytes + 1];
        _jpegBytes.CopyTo(oversized, 0);

        var errors = ProfilePhotoValidator.Validate(oversized, "image/jpeg");

        errors.ShouldContain(error => error.Contains("maximum allowed size"));
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null)]
    [InlineData("image/gif")]
    [InlineData("image/svg+xml")]
    [InlineData("application/octet-stream")]
    public void ValidateFailsWhenDeclaredTypeIsNotAllowed(string? contentType)
    {
        var errors = ProfilePhotoValidator.Validate(_jpegBytes, contentType);

        errors.ShouldContain(error => error.Contains("Only JPEG, PNG, and WebP"));
    }

    [Fact]
    public void ValidateFailsWhenContentIsNotARecognizedImage()
    {
        var errors = ProfilePhotoValidator.Validate(_gifBytes, "image/jpeg");

        errors.ShouldContain(error => error.Contains("not a recognized"));
    }

    [Fact]
    public void ValidateFailsWhenContentDoesNotMatchDeclaredType()
    {
        // A real PNG renamed/declared as JPEG must be rejected.
        var errors = ProfilePhotoValidator.Validate(_pngBytes, "image/jpeg");

        errors.ShouldContain(error => error.Contains("does not match"));
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    public void SniffContentTypeDetectsFormatFromMagicBytes(byte[] content, string expected) => ProfilePhotoValidator.SniffContentType(content).ShouldBe(expected);

    [Fact]
    public void SniffContentTypeDetectsWebpFromRiffHeader() => ProfilePhotoValidator.SniffContentType(_webpBytes).ShouldBe("image/webp");

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03 })]
    public void SniffContentTypeReturnsNullForUnknownContent(byte[] content) => ProfilePhotoValidator.SniffContentType(content).ShouldBeNull();

    [Fact]
    public void SniffContentTypeReturnsNullForRiffThatIsNotWebp()
    {
        // RIFF container that is not WebP (e.g. WAV).
        byte[] wav = [.. "RIFF"u8.ToArray(), 0x24, 0x00, 0x00, 0x00, .. "WAVE"u8.ToArray()];

        ProfilePhotoValidator.SniffContentType(wav).ShouldBeNull();
    }
}
