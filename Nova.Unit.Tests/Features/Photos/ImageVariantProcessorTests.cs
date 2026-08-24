using Nova.Features.Photos;
using Nova.Shared.Features.Photos;
using Shouldly;
using SixLabors.ImageSharp;

namespace Nova.Unit.Tests.Features.Photos;

/// <summary>
/// Unit tests for the shared ImageSharp variant pipeline:
/// <see cref="ImageVariantProcessor.GenerateVariants"/> keeps producing square profile-photo
/// variants, while <see cref="ImageVariantProcessor.GenerateCrestVariants"/> produces a square
/// 64px small variant plus aspect-preserving medium/large variants for club crests.
/// </summary>
public sealed class ImageVariantProcessorTests
{
    private const int SourceWidth = 3200;
    private const int SourceHeight = 2000;
    private const double SourceAspect = (double)SourceWidth / SourceHeight;

    /// <summary>
    /// The tolerance for aspect-ratio comparisons, allowing for the resize rounding of one pixel.
    /// </summary>
    private const double AspectTolerance = 0.02;

    [Fact]
    public void GenerateCrestVariants_FromNonSquareSource_ProducesSquareSmallVariant()
    {
        var (small, _, _) = GenerateCrestVariantsFromSource();

        using var image = Image.Load(small);
        image.Width.ShouldBe(ProfilePhotoConstraints.SmallSize);
        image.Height.ShouldBe(ProfilePhotoConstraints.SmallSize);
    }

    [Fact]
    public void GenerateCrestVariants_FromNonSquareSource_PreservesAspectForMediumVariant()
    {
        var (_, medium, _) = GenerateCrestVariantsFromSource();

        using var image = Image.Load(medium);
        image.Width.ShouldBeLessThanOrEqualTo(ProfilePhotoConstraints.MediumSize);
        image.Height.ShouldBeLessThanOrEqualTo(ProfilePhotoConstraints.MediumSize);
        Math.Max(image.Width, image.Height).ShouldBe(ProfilePhotoConstraints.MediumSize,
            "the medium variant must scale the longer edge up to the maximum bound");
        (image.Width / (double)image.Height).ShouldBe(SourceAspect, AspectTolerance);
    }

    [Fact]
    public void GenerateCrestVariants_FromNonSquareSource_PreservesAspectForLargeVariant()
    {
        var (_, _, large) = GenerateCrestVariantsFromSource();

        using var image = Image.Load(large);
        image.Width.ShouldBeLessThanOrEqualTo(ProfilePhotoConstraints.LargeSize);
        image.Height.ShouldBeLessThanOrEqualTo(ProfilePhotoConstraints.LargeSize);
        Math.Max(image.Width, image.Height).ShouldBe(ProfilePhotoConstraints.LargeSize,
            "the large variant must scale the longer edge up to the maximum bound");
        (image.Width / (double)image.Height).ShouldBe(SourceAspect, AspectTolerance);
    }

    [Fact]
    public void GenerateVariants_StillProducesSquares_ForProfilePhotos()
    {
        var variants = ImageVariantProcessor.GenerateVariants(
            TestImages.CreateJpeg(SourceWidth, SourceHeight),
            "image/jpeg",
            CancellationToken.None);

        using var small = Image.Load(variants.Small);
        small.Width.ShouldBe(ProfilePhotoConstraints.SmallSize);
        small.Height.ShouldBe(ProfilePhotoConstraints.SmallSize);

        using var medium = Image.Load(variants.Medium);
        medium.Width.ShouldBe(ProfilePhotoConstraints.MediumSize);
        medium.Height.ShouldBe(ProfilePhotoConstraints.MediumSize);

        using var large = Image.Load(variants.Large);
        large.Width.ShouldBe(ProfilePhotoConstraints.LargeSize);
        large.Height.ShouldBe(ProfilePhotoConstraints.LargeSize);
    }

    /// <summary>
    /// Generates the crest variants from a deliberately non-square source image.
    /// </summary>
    /// <returns>The small, medium, and large encoded variants.</returns>
    private static (byte[] Small, byte[] Medium, byte[] Large) GenerateCrestVariantsFromSource()
    {
        var variants = ImageVariantProcessor.GenerateCrestVariants(
            TestImages.CreateJpeg(SourceWidth, SourceHeight),
            "image/jpeg",
            CancellationToken.None);

        return (variants.Small, variants.Medium, variants.Large);
    }
}
