using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Extensions.Clubs;
using Nova.Features.Photos;
using Nova.Features.Shared;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.Shared.Validation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace Nova.Features.Clubs;

/// <summary>
/// Server-side implementation of <see cref="IClubService"/>: creates clubs (with the required
/// club crest) and searches for clubs.
/// </summary>
/// <param name="adminDbContextFactory">The factory for the unfiltered admin write context.</param>
/// <param name="readDbContextFactory">The factory for the read-only context.</param>
/// <param name="userManager">The identity user manager used to assign the ClubAdmin role.</param>
/// <param name="currentUserProvider">The provider for the current user's identity.</param>
/// <param name="crestContainerClient">The blob container client for the club crest container.</param>
/// <param name="logger">The logger.</param>
public sealed partial class ClubService(
    IDbContextFactory<NovaAdminDbContext> adminDbContextFactory,
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    UserManager<NovaUserEntity> userManager,
    ICurrentUserProvider currentUserProvider,
    [FromKeyedServices("club-crests")] BlobContainerClient crestContainerClient,
    ILogger<ClubService> logger) : IClubService
{
    /// <inheritdoc />
    public async Task<ServiceResult<ClubDto>> CreateClubAsync(CreateClubInput input, CancellationToken cancellationToken = default)
    {
        // Validate input against the DataAnnotations declared on CreateClubInput.
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        // Check if current user already belongs to a club
        if (currentUserProvider.ClubId.HasValue)
        {
            return ServiceProblem.Conflict("You already belong to a club.");
        }

        // Get current user ID
        if (currentUserProvider.UserId is not long userId)
        {
            return ServiceProblem.Forbidden("You must be signed in to create a club.");
        }

        // Validate the required crest upload with the shared profile-photo rules.
        var crestErrors = ClubCrestValidator.Validate(input.CrestContent, input.CrestContentType);
        if (crestErrors.Count > 0)
        {
            return ServiceProblem.Validation(new Dictionary<string, string[]> { ["crest"] = [.. crestErrors] });
        }

        var crestContentType = ProfilePhotoValidator.SniffContentType(input.CrestContent)!;
        var uploadedBlobNames = new List<string>(4);

        ImageVariantProcessor.ProcessedVariants crestVariants;
        try
        {
            // Header-only dimension check BEFORE decoding pixels, so a small file declaring
            // huge dimensions (decompression bomb) is rejected without allocating the bitmap.
            var info = Image.Identify(new DecoderOptions { MaxFrames = 1 }, input.CrestContent);
            if (info.Width > ImageVariantProcessor.MaxSourceDimension || info.Height > ImageVariantProcessor.MaxSourceDimension)
            {
                return ServiceProblem.BadRequest($"The crest image dimensions exceed the maximum of {ImageVariantProcessor.MaxSourceDimension}px.");
            }

            crestVariants = ImageVariantProcessor.GenerateVariants(input.CrestContent, crestContentType, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidImageContentException or UnknownImageFormatException or NotSupportedException)
        {
            LogCrestImageDecodeFailed(ex, userId);
            return ServiceProblem.BadRequest("The uploaded crest file could not be processed as an image.");
        }

        try
        {
            await using var db = await adminDbContextFactory.CreateDbContextAsync(cancellationToken);

            // Load user
            var user = await db.Users.FindAsync([userId], cancellationToken);
            if (user is null)
            {
                return ServiceProblem.ServerError("The current user could not be found.");
            }

            // Create club
            var club = new ClubEntity
            {
                Name = input.Name.Trim(),
                City = input.City.Trim(),
                State = input.State.Trim(),
                CreatedById = userId
            };

            // NpgsqlRetryingExecutionStrategy requires wrapping user-initiated transactions in
            // CreateExecutionStrategy().ExecuteAsync() so the whole unit can be retried on transient failures.
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

                db.Clubs.Add(club);
                await db.SaveChangesAsync(cancellationToken);

                // Now club.ClubId is set, assign it to the user
                user.ClubId = club.ClubId;
                db.Users.Update(user);
                await db.SaveChangesAsync(cancellationToken);

                // Generate and upload the crest blobs under clubs/{clubId}/{batchId}, then
                // persist the crest row inside the same transaction so a rollback never
                // leaves a database record without blobs (or vice versa).
                var batchId = Guid.CreateVersion7().ToString("N");
                var prefix = $"clubs/{club.ClubId}/{batchId}";
                var originalExtension = ImageVariantProcessor.GetExtension(crestContentType);

                var originalBlobName = $"{prefix}-original{originalExtension}";
                var smallBlobName = $"{prefix}-small.webp";
                var mediumBlobName = $"{prefix}-medium.webp";
                var largeBlobName = $"{prefix}-large.webp";

                await UploadBlobAsync(originalBlobName, crestVariants.Original, crestContentType, uploadedBlobNames, cancellationToken);
                await UploadBlobAsync(smallBlobName, crestVariants.Small, "image/webp", uploadedBlobNames, cancellationToken);
                await UploadBlobAsync(mediumBlobName, crestVariants.Medium, "image/webp", uploadedBlobNames, cancellationToken);
                await UploadBlobAsync(largeBlobName, crestVariants.Large, "image/webp", uploadedBlobNames, cancellationToken);

                db.ClubCrests.Add(new ClubCrestEntity
                {
                    ClubId = club.ClubId,
                    OriginalBlobName = originalBlobName,
                    SmallBlobName = smallBlobName,
                    MediumBlobName = mediumBlobName,
                    LargeBlobName = largeBlobName,
                    ContentType = crestContentType,
                    CreatedById = userId
                });

                await db.SaveChangesAsync(cancellationToken);

                await tx.CommitAsync(cancellationToken);
            });

            // Add ClubAdmin role outside the transaction. UserManager uses the DI-scoped Identity
            // context, which may already track a NovaUserEntity for this request — passing our
            // factory-context instance would cause an identity-map conflict on Attach. Re-fetch
            // the user through UserManager so the role update uses its own tracked instance.
            var identityUser = await userManager.FindByIdAsync(userId.ToString());
            if (identityUser is null)
            {
                LogClubAdminRoleAssignmentFailed(userId, club.ClubId);
            }
            else
            {
                // If the Identity context tracked a stale copy of the user (loaded before our
                // ClubId update), UserManager's UpdateAsync would persist all of its properties
                // and clobber ClubId back to null — stamp the new value on its instance first.
                identityUser.ClubId = club.ClubId;

                var roleResult = await userManager.AddToRoleAsync(identityUser, Roles.ClubAdmin);
                if (!roleResult.Succeeded)
                {
                    LogClubAdminRoleAssignmentFailed(userId, club.ClubId);
                    // Club is created and user is a member; role failure is logged but not fatal for the user
                }
            }

            LogClubCreated(userId, club.ClubId);
            return club.ToClubDto();
        }
        catch (OperationCanceledException)
        {
            await DeleteBlobsBestEffortAsync([.. uploadedBlobNames], userId);
            throw;
        }
        catch (Exception ex) when (ex is DbUpdateException or RequestFailedException)
        {
            LogClubCreationFailed(ex, userId);
            // Rollback happened (or never started); best-effort remove any uploaded crest blobs
            // so a failed club creation leaves no orphaned storage.
            await DeleteBlobsBestEffortAsync([.. uploadedBlobNames], userId);
            return ServiceProblem.ServerError("The club could not be created. Please try again.");
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<ClubDto>>> SearchClubsAsync(string? query, CancellationToken cancellationToken = default)
    {
        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<ClubEntity> baseQuery = db.Clubs;

        if (!string.IsNullOrWhiteSpace(query))
        {
            var trimmedQuery = query.Trim();
            var uppercaseSearch = trimmedQuery.ToUpperInvariant();
            var escapedSearch = LikePatternEscaper.EscapeLikePattern(trimmedQuery);
            baseQuery = db.Database.IsNpgsql()
                ? baseQuery.Where(c =>
                    EF.Functions.ILike(c.Name, $"%{escapedSearch}%", @"\") ||
                    EF.Functions.ILike(c.City, $"%{escapedSearch}%", @"\") ||
                    EF.Functions.ILike(c.State, $"%{escapedSearch}%", @"\"))
                : baseQuery.Where(c =>
                    c.Name.ToUpper().Contains(uppercaseSearch) ||
                    c.City.ToUpper().Contains(uppercaseSearch) ||
                    c.State.ToUpper().Contains(uppercaseSearch));
        }

        var clubs = await baseQuery
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var dtos = clubs.Select(c => c.ToClubDto()).ToList().AsReadOnly();
        return dtos;
    }

    /// <summary>
    /// Uploads a single crest blob to the crest container and tracks its name for failure cleanup.
    /// </summary>
    /// <param name="blobName">The target blob name.</param>
    /// <param name="content">The blob content.</param>
    /// <param name="contentType">The blob content type.</param>
    /// <param name="uploadedBlobNames">The list tracking successfully uploaded blob names.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the upload.</returns>
    private async Task UploadBlobAsync(
        string blobName,
        byte[] content,
        string contentType,
        List<string> uploadedBlobNames,
        CancellationToken cancellationToken)
    {
        var blobClient = crestContainerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(
            BinaryData.FromBytes(content),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);
        uploadedBlobNames.Add(blobName);
    }

    /// <summary>
    /// Best-effort deletes the uploaded crest blobs when club creation fails, so a failed
    /// creation never leaves orphaned blobs in the container.
    /// </summary>
    private async Task DeleteBlobsBestEffortAsync(IReadOnlyList<string> blobNames, long userId)
    {
        foreach (var blobName in blobNames)
        {
            try
            {
                await crestContainerClient.DeleteBlobIfExistsAsync(blobName);
            }
            catch (RequestFailedException ex)
            {
                LogCrestBlobCleanupFailed(ex, userId, blobName);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to clean up crest blob {BlobName} after failed club creation by UserId={UserId}.")]
    private partial void LogCrestBlobCleanupFailed(Exception exception, long userId, string blobName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to decode the uploaded crest image for UserId={UserId}.")]
    private partial void LogCrestImageDecodeFailed(Exception exception, long userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Club created successfully: ClubId={ClubId} by UserId={UserId}.")]
    private partial void LogClubCreated(long userId, long clubId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to create club for UserId={UserId}.")]
    private partial void LogClubCreationFailed(Exception exception, long userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to assign ClubAdmin role to UserId={UserId} for ClubId={ClubId}.")]
    private partial void LogClubAdminRoleAssignmentFailed(long userId, long clubId);
}
