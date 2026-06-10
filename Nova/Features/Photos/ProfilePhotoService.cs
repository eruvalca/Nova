using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Shared.Photos;
using Nova.Shared.Results;
using OneOf.Types;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Nova.Features.Photos;

/// <summary>
/// Server-side implementation of <see cref="IProfilePhotoService"/>: validates uploads,
/// generates resized square variants with ImageSharp, stores blobs in the profile photo
/// container, and persists <see cref="NovaUserPhotoEntity"/> rows for the current user.
/// </summary>
/// <param name="containerClient">The blob container client for the profile photo container.</param>
/// <param name="dbContextFactory">The factory for the tenant-scoped write context.</param>
/// <param name="readDbContextFactory">The factory for the read-only context.</param>
/// <param name="currentUserProvider">The provider for the current user's identity.</param>
/// <param name="logger">The logger.</param>
public sealed partial class ProfilePhotoService(
    BlobContainerClient containerClient,
    IDbContextFactory<NovaDbContext> dbContextFactory,
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<ProfilePhotoService> logger) : IProfilePhotoService
{
    /// <summary>
    /// The maximum pixel dimension accepted for a source image, guarding against decompression bombs.
    /// </summary>
    private const int MaxSourceDimension = 8192;

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> SaveProfilePhotoAsync(ProfilePhotoUpload upload, CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is not long userId)
        {
            return ServiceProblem.Forbidden("You must be signed in to upload a profile photo.");
        }

        var validationErrors = ProfilePhotoValidator.Validate(upload.Content, upload.ContentType);
        if (validationErrors.Count > 0)
        {
            var errorDict = new Dictionary<string, string[]> { ["photo"] = [.. validationErrors] };
            return ServiceProblem.Validation(errorDict);
        }

        var contentType = ProfilePhotoValidator.SniffContentType(upload.Content)!;

        ProcessedVariants variants;
        try
        {
            // Header-only dimension check BEFORE decoding pixels, so a small file declaring
            // huge dimensions (decompression bomb) is rejected without allocating the bitmap.
            var info = Image.Identify(new DecoderOptions { MaxFrames = 1 }, upload.Content);
            if (info.Width > MaxSourceDimension || info.Height > MaxSourceDimension)
            {
                return ServiceProblem.BadRequest($"The image dimensions exceed the maximum of {MaxSourceDimension}px.");
            }

            variants = GenerateVariants(upload.Content, contentType, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidImageContentException or UnknownImageFormatException or NotSupportedException)
        {
            LogImageDecodeFailed(ex, userId);
            return ServiceProblem.BadRequest("The uploaded file could not be processed as an image.");
        }

        var batchId = Guid.CreateVersion7().ToString("N");
        var prefix = $"users/{userId}/{batchId}";
        var originalExtension = GetExtension(contentType);

        var originalBlobName = $"{prefix}-original{originalExtension}";
        var smallBlobName = $"{prefix}-small.webp";
        var mediumBlobName = $"{prefix}-medium.webp";
        var largeBlobName = $"{prefix}-large.webp";

        var uploadedBlobNames = new List<string>(4);
        try
        {
            await UploadBlobAsync(originalBlobName, variants.Original, contentType, uploadedBlobNames, cancellationToken);
            await UploadBlobAsync(smallBlobName, variants.Small, "image/webp", uploadedBlobNames, cancellationToken);
            await UploadBlobAsync(mediumBlobName, variants.Medium, "image/webp", uploadedBlobNames, cancellationToken);
            await UploadBlobAsync(largeBlobName, variants.Large, "image/webp", uploadedBlobNames, cancellationToken);

            string[] previousBlobNames;
            await using (var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken))
            {
                var existing = await dbContext.NovaUserPhotos
                    .FirstOrDefaultAsync(p => p.NovaUserId == userId, cancellationToken);

                if (existing is null)
                {
                    previousBlobNames = [];
                    dbContext.NovaUserPhotos.Add(new NovaUserPhotoEntity
                    {
                        NovaUserId = userId,
                        OriginalBlobName = originalBlobName,
                        SmallBlobName = smallBlobName,
                        MediumBlobName = mediumBlobName,
                        LargeBlobName = largeBlobName,
                        ContentType = contentType,
                        CreatedById = userId
                    });
                }
                else
                {
                    previousBlobNames = CollectBlobNames(existing);
                    existing.OriginalBlobName = originalBlobName;
                    existing.SmallBlobName = smallBlobName;
                    existing.MediumBlobName = mediumBlobName;
                    existing.LargeBlobName = largeBlobName;
                    existing.ContentType = contentType;
                }

                await dbContext.SaveChangesAsync(cancellationToken);
            }

            LogPhotoSaved(userId);
            await DeleteBlobsBestEffortAsync(previousBlobNames, userId);
            return new Success();
        }
        catch (OperationCanceledException)
        {
            await DeleteBlobsBestEffortAsync([.. uploadedBlobNames], userId);
            throw;
        }
        catch (Exception ex) when (ex is RequestFailedException or DbUpdateException)
        {
            LogPhotoSaveFailed(ex, userId);
            await DeleteBlobsBestEffortAsync([.. uploadedBlobNames], userId);
            return ServiceProblem.ServerError("The photo could not be saved. Please try again.");
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ProfilePhotoInfo>> GetCurrentUserPhotoAsync(CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is not long userId)
        {
            return ServiceProblem.NotFound();
        }

        await using var dbContext = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var photo = await dbContext.NovaUserPhotos
            .FirstOrDefaultAsync(p => p.NovaUserId == userId, cancellationToken);

        return photo is null
            ? ServiceProblem.NotFound()
            : new ProfilePhotoInfo(userId, photo.ContentType);
    }

    /// <summary>
    /// Decodes the source image, sanitizes it, and produces the metadata-free re-encoded
    /// original plus the small, medium, and large WebP square variants.
    /// </summary>
    /// <param name="content">The validated source image bytes.</param>
    /// <param name="contentType">The sniffed source content type, used to re-encode the original in its own format.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The sanitized original and the encoded variants.</returns>
    private static ProcessedVariants GenerateVariants(byte[] content, string contentType, CancellationToken cancellationToken)
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
    /// Uploads a blob with the given content type and records its name for cleanup on failure.
    /// </summary>
    /// <param name="blobName">The target blob name.</param>
    /// <param name="content">The blob content.</param>
    /// <param name="contentType">The blob content type.</param>
    /// <param name="uploadedBlobNames">The list tracking successfully uploaded blob names.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the upload.</returns>
    private async Task UploadBlobAsync(string blobName, byte[] content, string contentType, List<string> uploadedBlobNames, CancellationToken cancellationToken)
    {
        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(
            BinaryData.FromBytes(content),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);
        uploadedBlobNames.Add(blobName);
    }

    /// <summary>
    /// Deletes the supplied blobs, logging (but not surfacing) any failures.
    /// </summary>
    /// <param name="blobNames">The blob names to delete.</param>
    /// <param name="userId">The user id, for diagnostics.</param>
    /// <returns>A task representing the deletions.</returns>
    private async Task DeleteBlobsBestEffortAsync(string[] blobNames, long userId)
    {
        foreach (var blobName in blobNames)
        {
            try
            {
                await containerClient.DeleteBlobIfExistsAsync(blobName);
            }
            catch (RequestFailedException ex)
            {
                LogBlobDeleteFailed(ex, blobName, userId);
            }
        }
    }

    /// <summary>
    /// Collects all non-null blob names referenced by a photo entity.
    /// </summary>
    /// <param name="photo">The photo entity.</param>
    /// <returns>The blob names currently referenced by the entity.</returns>
    private static string[] CollectBlobNames(NovaUserPhotoEntity photo)
    {
        string?[] names = [photo.OriginalBlobName, photo.SmallBlobName, photo.MediumBlobName, photo.LargeBlobName];
        return [.. names.OfType<string>()];
    }

    /// <summary>
    /// Maps an allowed content type to a file extension for blob naming.
    /// </summary>
    /// <param name="contentType">The sniffed content type.</param>
    /// <returns>The file extension, including the leading dot.</returns>
    private static string GetExtension(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => ".bin"
    };

    /// <summary>
    /// Holds the sanitized re-encoded original and the encoded photo variants.
    /// </summary>
    /// <param name="Original">The sanitized original, re-encoded in its source format without metadata.</param>
    /// <param name="Small">The encoded small variant.</param>
    /// <param name="Medium">The encoded medium variant.</param>
    /// <param name="Large">The encoded large variant.</param>
    private sealed record ProcessedVariants(byte[] Original, byte[] Small, byte[] Medium, byte[] Large);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to decode uploaded profile photo for user {UserId}.")]
    private partial void LogImageDecodeFailed(Exception exception, long userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Profile photo saved for user {UserId}.")]
    private partial void LogPhotoSaved(long userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to save profile photo for user {UserId}.")]
    private partial void LogPhotoSaveFailed(Exception exception, long userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to delete profile photo blob {BlobName} for user {UserId}.")]
    private partial void LogBlobDeleteFailed(Exception exception, string blobName, long userId);
}
