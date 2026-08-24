using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nova.Components.Account;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Photos;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Photos;
using Nova.Shared.Results;
using OneOf.Types;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace Nova.Features.Clubs;

/// <summary>
/// Server-side implementation of <see cref="IClubCrestService"/>: validates crest uploads,
/// generates resized variants with ImageSharp (a 64px square small variant plus
/// aspect-preserving medium and large variants), stores blobs in the club crest
/// container, and persists <see cref="ClubCrestEntity"/> rows for a given club. Change/remove
/// operations are restricted to the club's admins and mark every club member's claims stale
/// so the <see cref="Nova.Shared.Security.NovaClaimTypes.HasClubCrest"/> claim propagates.
/// </summary>
/// <param name="containerClient">The blob container client for the club crest container.</param>
/// <param name="dbContextFactory">The factory for the tenant-scoped write context.</param>
/// <param name="currentUserProvider">The provider for the current user's identity.</param>
/// <param name="clubMembershipClaimRefresher">The refresher used to mark club members' claims stale.</param>
/// <param name="logger">The logger.</param>
public sealed partial class ClubCrestService(
    [FromKeyedServices("club-crests")] BlobContainerClient containerClient,
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ClubMembershipClaimRefresher clubMembershipClaimRefresher,
    ILogger<ClubCrestService> logger) : IClubCrestService
{
    /// <inheritdoc />
    public async Task<ServiceResult<Success>> ChangeClubCrestAsync(long clubId, ClubCrestUpload upload, CancellationToken cancellationToken = default)
    {
        if (!IsClubAdmin(clubId) || currentUserProvider.UserId is not long userId)
        {
            LogForbiddenCrestAccess(clubId, currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You do not have permission to change this club's crest.");
        }

        var validationErrors = ClubCrestValidator.Validate(upload.Content, upload.ContentType);
        if (validationErrors.Count > 0)
        {
            return ServiceProblem.Validation(new Dictionary<string, string[]> { ["crest"] = [.. validationErrors] });
        }

        var contentType = ProfilePhotoValidator.SniffContentType(upload.Content)!;

        ImageVariantProcessor.ProcessedVariants variants;
        try
        {
            // Header-only dimension check BEFORE decoding pixels, so a small file declaring
            // huge dimensions (decompression bomb) is rejected without allocating the bitmap.
            var info = Image.Identify(new DecoderOptions { MaxFrames = 1 }, upload.Content);
            if (info.Width > ImageVariantProcessor.MaxSourceDimension || info.Height > ImageVariantProcessor.MaxSourceDimension)
            {
                return ServiceProblem.BadRequest($"The crest image dimensions exceed the maximum of {ImageVariantProcessor.MaxSourceDimension}px.");
            }

            variants = ImageVariantProcessor.GenerateCrestVariants(upload.Content, contentType, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidImageContentException or UnknownImageFormatException or NotSupportedException)
        {
            LogCrestImageDecodeFailed(ex, clubId);
            return ServiceProblem.BadRequest("The uploaded crest file could not be processed as an image.");
        }

        var batchId = Guid.CreateVersion7().ToString("N");
        var prefix = $"clubs/{clubId}/{batchId}";
        var originalExtension = ImageVariantProcessor.GetExtension(contentType);

        var originalBlobName = $"{prefix}-original{originalExtension}";
        var smallBlobName = $"{prefix}-small.webp";
        var mediumBlobName = $"{prefix}-medium.webp";
        var largeBlobName = $"{prefix}-large.webp";

        var uploadedBlobNames = new List<string>(4);
        string[] previousBlobNames;
        // Set immediately after SaveChangesAsync succeeds; before that, no row points at
        // the new batch, so the cleanup on cancellation may safely delete those blobs.
        // After the commit, the row references them, so cancellation during the old-blob
        // cleanup must NOT delete the newly committed batch.
        var committed = false;
        try
        {
            await UploadBlobAsync(originalBlobName, variants.Original, contentType, uploadedBlobNames, cancellationToken);
            await UploadBlobAsync(smallBlobName, variants.Small, "image/webp", uploadedBlobNames, cancellationToken);
            await UploadBlobAsync(mediumBlobName, variants.Medium, "image/webp", uploadedBlobNames, cancellationToken);
            await UploadBlobAsync(largeBlobName, variants.Large, "image/webp", uploadedBlobNames, cancellationToken);

            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var existing = await dbContext.ClubCrests
                .FirstOrDefaultAsync(c => c.ClubId == clubId, cancellationToken);

            if (existing is null)
            {
                previousBlobNames = [];
                dbContext.ClubCrests.Add(new ClubCrestEntity
                {
                    ClubId = clubId,
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
            committed = true;

            LogCrestChanged(clubId);
            await DeleteBlobsBestEffortAsync(previousBlobNames, clubId);
        }
        catch (OperationCanceledException)
        {
            if (!committed)
            {
                await DeleteBlobsBestEffortAsync([.. uploadedBlobNames], clubId);
            }

            throw;
        }
        catch (Exception ex) when (ex is RequestFailedException or DbUpdateException)
        {
            LogCrestChangeFailed(ex, clubId);
            await DeleteBlobsBestEffortAsync([.. uploadedBlobNames], clubId);
            return ServiceProblem.ServerError("The club crest could not be changed. Please try again.");
        }

        // The crest changed, so HasClubCrest is now (possibly) true for every member; bump
        // each member's security stamp and let the next revalidation rebuild their claims.
        var staleResult = await clubMembershipClaimRefresher.MarkClubUsersClaimsStaleAsync(clubId);
        if (staleResult.IsT1)
        {
            LogClaimStaleFailed(clubId);
        }

        return new Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> RemoveClubCrestAsync(long clubId, CancellationToken cancellationToken = default)
    {
        if (!IsClubAdmin(clubId))
        {
            LogForbiddenCrestAccess(clubId, currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You do not have permission to remove this club's crest.");
        }

        string[] blobNames;
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var existing = await dbContext.ClubCrests
                .FirstOrDefaultAsync(c => c.ClubId == clubId, cancellationToken);

            if (existing is null)
            {
                return ServiceProblem.NotFound("The requested club crest was not found.");
            }

            blobNames = CollectBlobNames(existing);
            dbContext.ClubCrests.Remove(existing);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is RequestFailedException or DbUpdateException)
        {
            LogCrestRemovalFailed(ex, clubId);
            return ServiceProblem.ServerError("The club crest could not be removed. Please try again.");
        }

        LogCrestRemoved(clubId);
        await DeleteBlobsBestEffortAsync(blobNames, clubId);

        // The crest is gone, so HasClubCrest is now false for every member; bump each
        // member's security stamp and let the next revalidation rebuild their claims.
        var staleResult = await clubMembershipClaimRefresher.MarkClubUsersClaimsStaleAsync(clubId);
        if (staleResult.IsT1)
        {
            LogClaimStaleFailed(clubId);
        }

        return new Success();
    }

    /// <summary>
    /// Determines whether the current user is an admin of the given club.
    /// </summary>
    /// <param name="clubId">The club id of the crest being managed.</param>
    /// <returns><see langword="true"/> when the current user is a club admin of that club.</returns>
    private bool IsClubAdmin(long clubId) =>
        currentUserProvider.IsClubAdmin && currentUserProvider.ClubId == clubId;

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
    /// <param name="clubId">The club id, for diagnostics.</param>
    /// <returns>A task representing the deletions.</returns>
    private async Task DeleteBlobsBestEffortAsync(string[] blobNames, long clubId)
    {
        foreach (var blobName in blobNames)
        {
            try
            {
                await containerClient.DeleteBlobIfExistsAsync(blobName);
            }
            catch (RequestFailedException ex)
            {
                LogBlobDeleteFailed(ex, blobName, clubId);
            }
        }
    }

    /// <summary>
    /// Collects all non-null blob names referenced by a crest entity.
    /// </summary>
    /// <param name="crest">The crest entity.</param>
    /// <returns>The blob names currently referenced by the entity.</returns>
    private static string[] CollectBlobNames(ClubCrestEntity crest)
    {
        string?[] names = [crest.OriginalBlobName, crest.SmallBlobName, crest.MediumBlobName, crest.LargeBlobName];
        return [.. names.OfType<string>()];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to decode uploaded club crest for club {ClubId}.")]
    private partial void LogCrestImageDecodeFailed(Exception exception, long clubId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Denied club crest access for club {ClubId} to user {UserId}.")]
    private partial void LogForbiddenCrestAccess(long clubId, long userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Club crest changed for club {ClubId}.")]
    private partial void LogCrestChanged(long clubId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Club crest removed for club {ClubId}.")]
    private partial void LogCrestRemoved(long clubId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to change club crest for club {ClubId}.")]
    private partial void LogCrestChangeFailed(Exception exception, long clubId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to remove club crest for club {ClubId}.")]
    private partial void LogCrestRemovalFailed(Exception exception, long clubId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to delete club crest blob {BlobName} for club {ClubId}.")]
    private partial void LogBlobDeleteFailed(Exception exception, string blobName, long clubId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to mark club {ClubId} members' claims stale after a crest change.")]
    private partial void LogClaimStaleFailed(long clubId);
}
