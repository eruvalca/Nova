using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Photos;
using Nova.Shared.Photos;
using Shouldly;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// End-to-end tests for <see cref="ProfilePhotoService"/> against the real PostgreSQL database
/// and the Azurite blob storage emulator started by the AppHost: saving uploads the original
/// plus 64/256/1024 WebP variants and persists the row; replacing deletes the previous blobs.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public class ProfilePhotoServiceTests(NovaAppHostFixture fixture)
{
    [Fact]
    public async Task SaveProfilePhoto_UploadsBlobsAndPersistsRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = await SeedUserAsync("Pia", cancellationToken);
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = null;
        var service = CreateService();

        var result = await service.SaveProfilePhotoAsync(
            new ProfilePhotoUpload(CreateJpeg(300, 200), "image/jpeg", "photo.jpg"),
            cancellationToken);

        result.IsProblem.ShouldBeFalse(
            result.IsProblem ? $"Expected success but got problem: {result.Problem.Detail}" : null);

        await using var context = fixture.CreateReadContext();
        var photo = await context.NovaUserPhotos.SingleAsync(p => p.NovaUserId == userId, cancellationToken);
        photo.ContentType.ShouldBe("image/jpeg");
        photo.SmallBlobName.ShouldNotBeNull();
        photo.MediumBlobName.ShouldNotBeNull();
        photo.LargeBlobName.ShouldNotBeNull();

        foreach (var blobName in new[] { photo.OriginalBlobName, photo.SmallBlobName, photo.MediumBlobName, photo.LargeBlobName })
        {
            (await fixture.ProfilePhotosContainer.GetBlobClient(blobName).ExistsAsync(cancellationToken)).Value
                .ShouldBeTrue($"blob '{blobName}' should exist");
        }

        // The small variant must be a 64x64 WebP square.
        var smallBlob = await fixture.ProfilePhotosContainer.GetBlobClient(photo.SmallBlobName)
            .DownloadContentAsync(cancellationToken);
        using var smallImage = Image.Load(smallBlob.Value.Content.ToArray());
        smallImage.Width.ShouldBe(ProfilePhotoConstraints.SmallSize);
        smallImage.Height.ShouldBe(ProfilePhotoConstraints.SmallSize);
    }

    [Fact]
    public async Task SaveProfilePhoto_ReplacesExistingPhotoAndDeletesOldBlobs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = await SeedUserAsync("Rae", cancellationToken);
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = null;
        var service = CreateService();

        (await service.SaveProfilePhotoAsync(
            new ProfilePhotoUpload(CreateJpeg(300, 200), "image/jpeg", "first.jpg"), cancellationToken)).IsSuccess.ShouldBeTrue();

        string firstOriginal;
        await using (var context = fixture.CreateReadContext())
        {
            firstOriginal = (await context.NovaUserPhotos.SingleAsync(p => p.NovaUserId == userId, cancellationToken)).OriginalBlobName;
        }

        (await service.SaveProfilePhotoAsync(
            new ProfilePhotoUpload(CreateJpeg(400, 400), "image/jpeg", "second.jpg"), cancellationToken)).IsSuccess.ShouldBeTrue();

        await using (var context = fixture.CreateReadContext())
        {
            var photos = await context.NovaUserPhotos.Where(p => p.NovaUserId == userId).ToListAsync(cancellationToken);
            photos.ShouldHaveSingleItem();
            photos[0].OriginalBlobName.ShouldNotBe(firstOriginal);
        }

        (await fixture.ProfilePhotosContainer.GetBlobClient(firstOriginal).ExistsAsync(cancellationToken)).Value
            .ShouldBeFalse("the replaced original blob should be deleted");
    }

    [Fact]
    public async Task SaveProfilePhoto_RejectsContentThatIsNotAnAllowedImage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = await SeedUserAsync("Sam", cancellationToken);
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = null;
        var service = CreateService();

        var result = await service.SaveProfilePhotoAsync(
            new ProfilePhotoUpload([0x4D, 0x5A, 0x90, 0x00], "image/jpeg", "evil.jpg"),
            cancellationToken);

        result.IsProblem.ShouldBeTrue();
        var errorMessages = result.Problem.Errors?.Values.SelectMany(e => e).ToList() ?? [];
        errorMessages.ShouldContain(error => error.Contains("not a recognized"));
    }

    [Fact]
    public async Task SaveProfilePhoto_RejectsOversizedDimensions_WithoutUploading()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = await SeedUserAsync("Max", cancellationToken);
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = null;
        var service = CreateService();

        // 8193px wide exceeds the 8192px source-dimension limit (cheap to encode at 4px tall).
        var result = await service.SaveProfilePhotoAsync(
            new ProfilePhotoUpload(CreateJpeg(8193, 4), "image/jpeg", "wide.jpg"),
            cancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Detail.ShouldNotBeNull();
        result.Problem.Detail.ShouldContain("dimensions exceed");

        await using var context = fixture.CreateReadContext();
        (await context.NovaUserPhotos.AnyAsync(p => p.NovaUserId == userId, cancellationToken))
            .ShouldBeFalse("no photo row should be persisted for a rejected upload");
    }

    [Fact]
    public async Task SaveProfilePhoto_StripsExifMetadataFromStoredOriginal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = await SeedUserAsync("Gia", cancellationToken);
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = null;
        var service = CreateService();

        var result = await service.SaveProfilePhotoAsync(
            new ProfilePhotoUpload(CreateJpegWithGpsExif(300, 200), "image/jpeg", "gps.jpg"),
            cancellationToken);

        result.IsProblem.ShouldBeFalse(
            result.IsProblem ? $"Expected success but got problem: {result.Problem.Detail}" : null);

        await using var context = fixture.CreateReadContext();
        var photo = await context.NovaUserPhotos.SingleAsync(p => p.NovaUserId == userId, cancellationToken);

        var originalBlob = await fixture.ProfilePhotosContainer.GetBlobClient(photo.OriginalBlobName)
            .DownloadContentAsync(cancellationToken);
        using var original = Image.Load(originalBlob.Value.Content.ToArray());
        original.Metadata.ExifProfile.ShouldBeNull("the stored original must not carry EXIF (incl. GPS) metadata");
        original.Metadata.XmpProfile.ShouldBeNull("the stored original must not carry XMP metadata");
    }

    [Fact]
    public async Task NovaUserPhotos_RejectsSecondRowForSameUser_ViaUniqueIndex()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = await SeedUserAsync("Uli", cancellationToken);

        await using (var context = fixture.CreateAdminContext())
        {
            context.NovaUserPhotos.Add(new NovaUserPhotoEntity { NovaUserId = userId, OriginalBlobName = "first", CreatedById = userId });
            await context.SaveChangesAsync(cancellationToken);
        }

        // A concurrent first upload that lost the check-then-insert race must fail at the
        // database (the service's DbUpdateException catch turns this into a retryable error).
        await using (var context = fixture.CreateAdminContext())
        {
            context.NovaUserPhotos.Add(new NovaUserPhotoEntity { NovaUserId = userId, OriginalBlobName = "second", CreatedById = userId });
            await Should.ThrowAsync<DbUpdateException>(
                () => context.SaveChangesAsync(cancellationToken));
        }
    }

    /// <summary>
    /// Builds the service under test against the fixture's live database and blob container.
    /// </summary>
    /// <returns>The service.</returns>
    private ProfilePhotoService CreateService() => new(
        fixture.ProfilePhotosContainer,
        new FixtureContextFactory<NovaDbContext>(fixture.CreateTenantContext),
        new FixtureContextFactory<NovaReadDbContext>(fixture.CreateReadContext),
        fixture.CurrentUser,
        NullLogger<ProfilePhotoService>.Instance);

    /// <summary>
    /// Seeds a user with a database-generated id and returns the id.
    /// </summary>
    /// <param name="firstName">The user's first name (test marker).</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The new user's id.</returns>
    private async Task<long> SeedUserAsync(string firstName, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = new NovaUserEntity { FirstName = firstName, LastName = "PhotoTest", ClubId = null };
        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);
        return user.Id;
    }

    /// <summary>
    /// Creates an in-memory JPEG of the requested dimensions.
    /// </summary>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <returns>The encoded JPEG bytes.</returns>
    private static byte[] CreateJpeg(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(120, 180, 240));
        using var stream = new MemoryStream();
        image.Save(stream, new JpegEncoder());
        return stream.ToArray();
    }

    /// <summary>
    /// Creates an in-memory JPEG carrying an EXIF profile with GPS coordinates, simulating a
    /// phone-camera upload whose location metadata must be stripped by the service.
    /// </summary>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <returns>The encoded JPEG bytes with GPS EXIF metadata.</returns>
    private static byte[] CreateJpegWithGpsExif(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(120, 180, 240));
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(47), new Rational(36), new Rational(22)]);
        exif.SetValue(ExifTag.GPSLongitudeRef, "W");
        exif.SetValue(ExifTag.GPSLongitude, [new Rational(122), new Rational(19), new Rational(55)]);
        image.Metadata.ExifProfile = exif;

        using var stream = new MemoryStream();
        image.Save(stream, new JpegEncoder());
        return stream.ToArray();
    }

    /// <summary>
    /// An <see cref="IDbContextFactory{TContext}"/> over a fixture context-creation delegate.
    /// </summary>
    /// <typeparam name="TContext">The context type.</typeparam>
    /// <param name="factory">The delegate that creates a context.</param>
    private sealed class FixtureContextFactory<TContext>(Func<TContext> factory) : IDbContextFactory<TContext>
        where TContext : DbContext
    {
        /// <inheritdoc />
        public TContext CreateDbContext() => factory();
    }
}
