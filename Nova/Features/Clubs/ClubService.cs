using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
/// <param name="currentUserProvider">The provider for the current user's identity.</param>
/// <param name="crestContainerClient">The blob container client for the club crest container.</param>
/// <param name="logger">The logger.</param>
public sealed partial class ClubService(
    IDbContextFactory<NovaAdminDbContext> adminDbContextFactory,
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
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

            crestVariants = ImageVariantProcessor.GenerateCrestVariants(input.CrestContent, crestContentType, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidImageContentException or UnknownImageFormatException or NotSupportedException)
        {
            LogCrestImageDecodeFailed(ex, userId);
            return ServiceProblem.BadRequest("The uploaded crest file could not be processed as an image.");
        }

        // A stable operation id generated ONCE per logical request: it is written onto the club
        // row, used to check for an ambiguous commit after a retryable failure, and reused as the
        // blob batch id so retry attempts upload to the SAME blob names (Azure uploads overwrite
        // by default, making re-uploads idempotent). The id is deliberately NOT derived from the
        // club id: Postgres identity sequences do not roll back, so a retried insert can receive a
        // different ClubId and the blob names must remain stable across attempts.
        var creationOperationId = Guid.CreateVersion7();
        var securityStamp = NewSecurityStamp();
        var concurrencyStamp = NewConcurrencyStamp();
        var batchId = creationOperationId.ToString("N");
        var originalExtension = ImageVariantProcessor.GetExtension(crestContentType);
        var prefix = $"clubs/{userId}/{batchId}";

        var originalBlobName = $"{prefix}-original{originalExtension}";
        var smallBlobName = $"{prefix}-small.webp";
        var mediumBlobName = $"{prefix}-medium.webp";
        var largeBlobName = $"{prefix}-large.webp";

        // Upload the crest blobs BEFORE the retried transaction. Holding four network round-trips
        // inside the database transaction would lengthen the lock window, and names are stable, so
        // a retried attempt can simply overwrite them. The transaction itself only does DB work.
        try
        {
            await UploadBlobAsync(originalBlobName, crestVariants.Original, crestContentType, uploadedBlobNames, cancellationToken);
            await UploadBlobAsync(smallBlobName, crestVariants.Small, "image/webp", uploadedBlobNames, cancellationToken);
            await UploadBlobAsync(mediumBlobName, crestVariants.Medium, "image/webp", uploadedBlobNames, cancellationToken);
            await UploadBlobAsync(largeBlobName, crestVariants.Large, "image/webp", uploadedBlobNames, cancellationToken);
        }
        catch (Exception ex) when (ex is RequestFailedException)
        {
            LogCrestBlobUploadFailed(ex, userId);
            await DeleteBlobsBestEffortAsync([.. uploadedBlobNames], userId);
            return ServiceProblem.ServerError("The club crest could not be uploaded. Please try again.");
        }
        catch (OperationCanceledException)
        {
            // The DB work has not started, so no row can reference these blobs yet: delete the
            // blobs uploaded so far instead of leaving permanent orphans in the container.
            await DeleteBlobsBestEffortAsync([.. uploadedBlobNames], userId);
            throw;
        }

        ServiceResult<ClubDto> result;
        // Tracks whether the current execution-strategy attempt reached its commit, so the
        // cancellation cleanup below can distinguish "commit never attempted" (blobs are
        // definitely orphans, delete them) from "commit attempted, outcome uncertain" (the
        // crest row may reference the blobs — keep them rather than destroy committed data).
        var commitAttempted = new CommitAttemptTracker();
        try
        {
            // Fresh contexts per attempt: EF's change tracker must not carry the first attempt's
            // entities, keys, or Added crest row into a retry (the root cause of the duplicate
            // crest-row crashes). The strategy is created once from a short-lived setup context.
            result = await ExecuteWithFreshContextAsync(
                commitAttempted,
                (db, tracker) => CreateClubAsync(
                    db,
                    input,
                    userId,
                    creationOperationId,
                    securityStamp,
                    concurrencyStamp,
                    originalBlobName,
                    smallBlobName,
                    mediumBlobName,
                    largeBlobName,
                    crestContentType,
                    tracker,
                    cancellationToken),
                db => VerifyClubCreationAsync(db, userId, creationOperationId, cancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!commitAttempted.AnyAttempted)
            {
                await DeleteBlobsBestEffortAsync([.. uploadedBlobNames], userId);
            }

            throw;
        }
        catch (Exception ex) when (ex is DbUpdateException or RequestFailedException)
        {
            LogClubCreationFailed(ex, userId);
            // Rollback happened (or never started); best-effort remove any uploaded crest blobs
            // so a failed club creation leaves no orphaned storage. When the ambiguity was
            // resolved by verification, no exception escapes and this catch is not reached.
            // When the noise is a NON-transient failure after an earlier attempt reached its
            // commit, the club (and its crest row) may exist — keep the blobs rather than
            // destroy data a committed row references.
            if (!commitAttempted.AnyAttempted)
            {
                await DeleteBlobsBestEffortAsync([.. uploadedBlobNames], userId);
            }

            return ServiceProblem.ServerError("The club could not be created. Please try again.");
        }

        if (result.IsProblem)
        {
            // The transaction never committed (validation reached inside the strategy, e.g. the
            // user lookup failed or a unique violation); remove the uploaded blobs.
            await DeleteBlobsBestEffortAsync([.. uploadedBlobNames], userId);
            return result;
        }

        var clubDto = result.Value;

        LogClubCreated(userId, clubDto.ClubId);
        return clubDto;
    }

    /// <summary>
    /// Creates the club, assigns membership, and persists the crest row in a single transaction
    /// using one execution attempt with a fresh context.
    /// </summary>
    /// <param name="db">The fresh admin context for this execution attempt.</param>
    /// <param name="input">The validated club and crest input.</param>
    /// <param name="userId">The creator's user identifier.</param>
    /// <param name="creationOperationId">The stable logical creation identifier.</param>
    /// <param name="securityStamp">The claims-invalidation marker shared by retry attempts.</param>
    /// <param name="concurrencyStamp">The Identity concurrency marker shared by retry attempts.</param>
    /// <param name="originalBlobName">The uploaded original crest blob name.</param>
    /// <param name="smallBlobName">The uploaded small crest blob name.</param>
    /// <param name="mediumBlobName">The uploaded medium crest blob name.</param>
    /// <param name="largeBlobName">The uploaded large crest blob name.</param>
    /// <param name="crestContentType">The validated original crest content type.</param>
    /// <param name="commitAttempted">Tracks whether this attempt reached its commit.</param>
    /// <param name="cancellationToken">A token that cancels database work.</param>
    /// <returns>The created club or a known service problem.</returns>
    private async Task<ServiceResult<ClubDto>> CreateClubAsync(
        NovaAdminDbContext db,
        CreateClubInput input,
        long userId,
        Guid creationOperationId,
        string securityStamp,
        string concurrencyStamp,
        string originalBlobName,
        string smallBlobName,
        string mediumBlobName,
        string largeBlobName,
        string crestContentType,
        CommitAttemptTracker commitAttempted,
        CancellationToken cancellationToken)
    {
        // NpgsqlRetryingExecutionStrategy requires wrapping user-initiated transactions in
        // CreateExecutionStrategy().ExecuteAsync() so the whole unit can be retried on transient failures.
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireUserMembershipLockAsync(userId, cancellationToken);

        // Re-read membership after taking the user lock so club creation cannot overwrite a
        // membership concurrently assigned by join approval or another membership mutation.
        var user = await db.Users.FindAsync([userId], cancellationToken);
        if (user is null)
        {
            // The club could not be created, so the uploaded blobs must be cleaned up by the caller.
            return ServiceProblem.ServerError("The current user could not be found.");
        }

        if (user.ClubId is not null)
        {
            return ServiceProblem.Conflict("You already belong to a club.");
        }

        // Create club
        var club = new ClubEntity
        {
            Name = input.Name.Trim(),
            City = input.City.Trim(),
            State = input.State.Trim(),
            CreatedById = userId,
            CreationOperationId = creationOperationId
        };

        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);

        // The generated club id is now available. Take the club lock after the already-held user
        // lock, then reload the user before applying membership-sensitive effects.
        await db.AcquireClubMembershipLockAsync(club.ClubId, cancellationToken);
        await db.Entry(user).ReloadAsync(cancellationToken);
        if (user.ClubId is not null)
        {
            return ServiceProblem.Conflict("You already belong to a club.");
        }

        var administratorRoleId = await db.Roles
            .Where(role => role.NormalizedName == Roles.ClubAdmin.ToUpperInvariant())
            .Select(role => (long?)role.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (administratorRoleId is null)
        {
            return ServiceProblem.ServerError("The club administrator role is not configured.");
        }

        // Assign membership, administrator role, and claims-invalidating stamps atomically with
        // the club and crest rows. The completion endpoint only reissues the acting user's cookie.
        user.ClubId = club.ClubId;
        user.SecurityStamp = securityStamp;
        user.ConcurrencyStamp = concurrencyStamp;
        var alreadyAdministrator = await db.UserRoles.AnyAsync(
            userRole => userRole.UserId == user.Id && userRole.RoleId == administratorRoleId.Value,
            cancellationToken);
        if (!alreadyAdministrator)
        {
            db.UserRoles.Add(new IdentityUserRole<long> { UserId = user.Id, RoleId = administratorRoleId.Value });
        }

        // Persist the crest row inside the same transaction so a rollback never leaves a database
        // record without blobs (or vice versa). The .NET IDs generated above must be identical to
        // the blob names uploaded before the transaction.
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

        // Mark commit attempt BEFORE CommitAsync so a failure during the commit itself triggers
        // ambiguous-commit verification instead of an unguarded retry.
        commitAttempted.MarkAttempted();
        await tx.CommitAsync(cancellationToken);

        return club.ToClubDto();
    }

    /// <summary>Creates the logical club creation's durable claims-invalidation marker.</summary>
    /// <returns>A new security stamp.</returns>
    private static string NewSecurityStamp() => Guid.NewGuid().ToString("N");

    /// <summary>Creates the marker that rejects Identity writes tracked before club creation.</summary>
    /// <returns>A new Identity concurrency stamp.</returns>
    private static string NewConcurrencyStamp() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Checks whether a club-creation transaction with an uncertain commit outcome was committed
    /// and reconstructs its successful service result without replaying the insert.
    /// </summary>
    private async Task<ExecutionResult<ServiceResult<ClubDto>>> VerifyClubCreationAsync(
        NovaAdminDbContext db,
        long userId,
        Guid creationOperationId,
        CancellationToken cancellationToken)
    {
        var club = await db.Clubs
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.CreatedById == userId
                    && candidate.CreationOperationId == creationOperationId,
                cancellationToken);

        if (club is null)
        {
            return new ExecutionResult<ServiceResult<ClubDto>>(successful: false, default!);
        }

        LogClubCreationCommitRecovered(club.ClubId, creationOperationId, userId);
        return new ExecutionResult<ServiceResult<ClubDto>>(successful: true, club.ToClubDto());
    }

    /// <summary>
    /// Runs a club-creation mutation inside EF Core's retrying execution strategy and verifies
    /// whether an ambiguous commit succeeded before allowing the strategy to replay the mutation.
    /// Verification only runs for an attempt that reached its commit; a transient failure raised
    /// before the commit cannot have applied the mutation, so the observed state belongs to an
    /// earlier request and must not be credited to this one. The same <see cref="CommitAttemptTracker"/>
    /// is shared with the caller so it can decide whether an escaped cancellation still needs blob
    /// cleanup (only safe when the current attempt never reached its commit).
    /// </summary>
    /// <typeparam name="TResult">The result produced by the mutation attempt.</typeparam>
    /// <param name="commitAttempted">Tracks whether the current attempt reached its commit.</param>
    /// <param name="operation">The mutation to run with a fresh admin context and commit tracker.</param>
    /// <param name="verifySucceeded">The verification query to run with a fresh admin context.</param>
    /// <param name="cancellationToken">A token that cancels strategy setup, mutation, or verification.</param>
    /// <returns>The mutation result or the reconstructed result from successful commit verification.</returns>
    private async Task<TResult> ExecuteWithFreshContextAsync<TResult>(
        CommitAttemptTracker commitAttempted,
        Func<NovaAdminDbContext, CommitAttemptTracker, Task<TResult>> operation,
        Func<NovaAdminDbContext, Task<ExecutionResult<TResult>>> verifySucceeded,
        CancellationToken cancellationToken)
    {
        await using var executionStrategyDb = await adminDbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = executionStrategyDb.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(
            (Operation: operation, VerifySucceeded: verifySucceeded, CommitAttempted: commitAttempted),
            async (state, _) =>
            {
                state.CommitAttempted.Reset();
                await using var db = await adminDbContextFactory.CreateDbContextAsync(cancellationToken);
                return await state.Operation(db, state.CommitAttempted);
            },
            async (state, _) =>
            {
                if (!state.CommitAttempted.Attempted)
                {
                    return new ExecutionResult<TResult>(successful: false, default!);
                }

                await using var db = await adminDbContextFactory.CreateDbContextAsync(cancellationToken);
                return await state.VerifySucceeded(db);
            },
            cancellationToken);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to upload club crest blobs for UserId={UserId}.")]
    private partial void LogCrestBlobUploadFailed(Exception exception, long userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to decode the uploaded crest image for UserId={UserId}.")]
    private partial void LogCrestImageDecodeFailed(Exception exception, long userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Club created successfully: ClubId={ClubId} by UserId={UserId}.")]
    private partial void LogClubCreated(long userId, long clubId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recovered club creation after ambiguous commit: ClubId={ClubId}, OperationId={CreationOperationId}, UserId={UserId}.")]
    private partial void LogClubCreationCommitRecovered(long clubId, Guid creationOperationId, long userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to create club for UserId={UserId}.")]
    private partial void LogClubCreationFailed(Exception exception, long userId);

}
