using Nova.Features.Clubs;
using Nova.SharedKernel.Features.Photos;
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

        var errors = ClubCrestValidator.Validate(content, contentType);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void ValidateFailsWhenContentIsEmpty()
    {
        var errors = ClubCrestValidator.Validate([], "image/jpeg");

        errors.ShouldHaveSingleItem();
        errors[0].ShouldBe("A club crest is required.");
    }

    [Fact]
    public void ValidateFailsWhenContentExceedsMaxBytes()
    {
        var oversized = new byte[ProfilePhotoConstraints.MaxBytes + 1];
        _jpegBytes.CopyTo(oversized, 0);

        var errors = ClubCrestValidator.Validate(oversized, "image/jpeg");

        errors.ShouldContain(error => error.Contains("maximum allowed size"));
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null)]
    [InlineData("image/gif")]
    [InlineData("image/svg+xml")]
    [InlineData("application/octet-stream")]
    public void ValidateFailsWhenDeclaredTypeIsNotAllowed(string? contentType)
    {
        var errors = ClubCrestValidator.Validate(_jpegBytes, contentType);

        errors.ShouldContain(error => error.Contains("Only JPEG, PNG, and WebP"));
    }

    [Fact]
    public void ValidateFailsWhenContentIsNotARecognizedImage()
    {
        var errors = ClubCrestValidator.Validate(_gifBytes, "image/jpeg");

        errors.ShouldContain(error => error.Contains("not a recognized"));
    }

    [Fact]
    public void ValidateFailsWhenContentDoesNotMatchDeclaredType()
    {
        // A real PNG renamed/declared as JPEG must be rejected.
        var errors = ClubCrestValidator.Validate(_pngBytes, "image/jpeg");

        errors.ShouldContain(error => error.Contains("does not match"));
    }

    [Fact]
    public void ValidateAcceptsContentTypeIgnoringCase()
    {
        // The declared content type is matched case-insensitively against the sniffed format.
        var errors = ClubCrestValidator.Validate(_jpegBytes, "IMAGE/JPEG");

        errors.ShouldBeEmpty();
    }
}
