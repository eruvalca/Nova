using Azure;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Shared;
using Nova.Shared.Photos;
using Nova.Shared.Results;

namespace Nova.Features.Photos;

/// <summary>
/// Maps the minimal API endpoints for profile photo upload, status, retrieval, and the
/// post-save cookie refresh hop.
/// </summary>
internal static class ProfilePhotoEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps the profile photo endpoints using MapGroup for organization.
        /// </summary>
        /// <returns>The endpoint route builder, for chaining.</returns>
        public IEndpointRouteBuilder MapProfilePhotoEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints.MapGroup(PhotoEndpoints.GroupPrefix).RequireAuthorization();

            // Upload a cropped profile photo. The WASM client posts with the Identity cookie
            // but without a Razor antiforgery token; SameSite=Lax on the Identity cookie
            // protects these JSON/multipart API posts from CSRF.
            group.MapPost(PhotoEndpoints.UploadRelative, UploadHandler)
                .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("UploadProfilePhoto");

            // Get the current user's photo metadata.
            group.MapGet(PhotoEndpoints.StatusRelative, StatusHandler)
                .Produces<ProfilePhotoInfo>()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .WithName("GetProfilePhotoStatus");

            // Serve a profile photo by user ID and size, with ETag caching. Mapped outside
            // the account group because its route lives under /api/users.
            endpoints.MapGet(PhotoEndpoints.GetTemplate, GetHandler)
                .RequireAuthorization()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .WithName("GetProfilePhoto");

            // Cookie refresh hop after upload: reissues auth cookie so claims take effect.
            // Mapped at its absolute /Account path, outside the API group.
            endpoints.MapGet(PhotoEndpoints.Complete, CompleteHandler)
                .RequireAuthorization()
                .WithName("CompleteProfilePhotoUpload");

            return endpoints;
        }
    }

    /// <summary>
    /// Handles profile photo uploads.
    /// </summary>
    private static async Task<IResult> UploadHandler(
        IFormFile file,
        IProfilePhotoService photoService,
        CancellationToken cancellationToken)
    {
        if (file.Length is 0 or > ProfilePhotoConstraints.MaxBytes)
        {
            var message = $"The photo must be between 1 byte and {ProfilePhotoConstraints.MaxBytes / (1024 * 1024)} MB.";
            return ServiceProblem.Validation("file", message).ToHttpResult();
        }

        byte[] content;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        var result = await photoService.SaveProfilePhotoAsync(
            new ProfilePhotoUpload(content, file.ContentType, file.FileName),
            cancellationToken);

        return result.ToHttpResult(_ => TypedResults.NoContent());
    }

    /// <summary>
    /// Handles requests for the current user's photo status.
    /// </summary>
    private static async Task<IResult> StatusHandler(
        IProfilePhotoService photoService,
        CancellationToken cancellationToken)
    {
        var result = await photoService.GetCurrentUserPhotoAsync(cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Handles retrieval of a profile photo by user ID and size, with ETag caching.
    /// </summary>
    private static async Task<IResult> GetHandler(
        long userId,
        [FromQuery] string? size,
        HttpContext context,
        ICurrentUserProvider currentUserProvider,
        IDbContextFactory<NovaReadDbContext> readDbContextFactory,
        BlobContainerClient containerClient,
        CancellationToken cancellationToken)
    {
        // Query enum binding is case-sensitive; accept "small"/"Small" etc. explicitly.
        // Enum.TryParse also accepts arbitrary numeric strings (e.g. "99"), which would skip
        // the owner-only check below yet still resolve to the original blob — reject anything
        // that is not a defined member.
        if (!Enum.TryParse<ProfilePhotoSize>(size, ignoreCase: true, out var photoSize)
            || !Enum.IsDefined(photoSize))
        {
            photoSize = ProfilePhotoSize.Medium;
        }

        // The original may retain more detail than the public variants; only its owner may
        // fetch it. Return 404 (not 403) to avoid leaking whether a photo exists.
        if (photoSize == ProfilePhotoSize.Original && currentUserProvider.UserId != userId)
        {
            return ServiceProblem.NotFound().ToHttpResult();
        }

        NovaUserPhotoEntity? photo;
        await using (var dbContext = await readDbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            photo = await dbContext.NovaUserPhotos
                .FirstOrDefaultAsync(p => p.NovaUserId == userId, cancellationToken);
        }

        var blobName = SelectBlobName(photo, photoSize);
        if (photo is null || blobName is null)
        {
            return ServiceProblem.NotFound().ToHttpResult();
        }

        var contentType = photoSize == ProfilePhotoSize.Original
            ? photo.ContentType ?? "application/octet-stream"
            : "image/webp";

        var blobClient = containerClient.GetBlobClient(blobName);
        try
        {
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var etag = $"\"{properties.Value.ETag.ToString().Trim('"')}\"";

            // no-cache (not max-age) so the browser revalidates with If-None-Match on every
            // use; the photo URL is stable per user, so a freshness lifetime would keep
            // serving the old image after a new upload. Unchanged photos still get 304s.
            context.Response.Headers.CacheControl = "private, no-cache";
            context.Response.Headers.ETag = etag;

            if (context.Request.Headers.IfNoneMatch.Any(value => value == etag))
            {
                return TypedResults.StatusCode(StatusCodes.Status304NotModified);
            }

            var download = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return TypedResults.Stream(download.Value.Content, contentType);
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            return ServiceProblem.NotFound().ToHttpResult();
        }
    }

    /// <summary>
    /// Handles the post-upload cookie refresh: reissues the auth cookie so the
    /// HasProfilePhoto claim takes effect, then returns to the requested local URL.
    /// </summary>
    private static async Task<IResult> CompleteHandler(
        HttpContext context,
        UserManager<NovaUserEntity> userManager,
        SignInManager<NovaUserEntity> signInManager,
        IProfilePhotoService photoService,
        [FromQuery] string? returnUrl,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
        {
            return TypedResults.Challenge();
        }

        var photoResult = await photoService.GetCurrentUserPhotoAsync(cancellationToken);
        if (photoResult.IsProblem)
        {
            // No photo saved yet; send the user back to the photo page.
            return TypedResults.LocalRedirect("~/Account/ProfilePhoto");
        }

        await signInManager.RefreshSignInAsync(user);
        var target = !string.IsNullOrEmpty(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
            ? returnUrl.TrimStart('/')
            : string.Empty;
        return TypedResults.LocalRedirect($"~/{target}");
    }

    /// <summary>
    /// Selects the blob name for the requested photo size, falling back to the original
    /// when a variant has not been generated.
    /// </summary>
    /// <param name="photo">The photo entity, or <see langword="null"/> when the user has no photo.</param>
    /// <param name="size">The requested size.</param>
    /// <returns>The blob name to serve, or <see langword="null"/> when unavailable.</returns>
    private static string? SelectBlobName(NovaUserPhotoEntity? photo, ProfilePhotoSize size) => photo is null
        ? null
        : size switch
        {
            ProfilePhotoSize.Small => photo.SmallBlobName ?? photo.OriginalBlobName,
            ProfilePhotoSize.Medium => photo.MediumBlobName ?? photo.OriginalBlobName,
            ProfilePhotoSize.Large => photo.LargeBlobName ?? photo.OriginalBlobName,
            ProfilePhotoSize.Original => photo.OriginalBlobName,
            _ => null
        };
}
