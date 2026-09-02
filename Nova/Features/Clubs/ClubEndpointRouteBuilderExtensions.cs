using Azure;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Shared;
using Nova.Shared.Features.Account;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Photos;
using Nova.Shared.Results;
using Nova.Shared.Security;

namespace Nova.Features.Clubs;

/// <summary>
/// Maps the minimal API endpoints for club creation, search, and club join request management.
/// </summary>
internal static class ClubEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps the club endpoints using MapGroup for organization.
        /// </summary>
        /// <returns>The endpoint route builder, for chaining.</returns>
        public IEndpointRouteBuilder MapClubEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints.MapGroup(ClubEndpoints.GroupPrefix).RequireAuthorization();

            // Create a new club with a required crest upload; the current user becomes the club admin.
            // The WASM client posts with the Identity cookie but without a Razor antiforgery token;
            // SameSite=Lax on the Identity cookie protects this multipart API post from CSRF.
            group.MapPost(ClubEndpoints.CreateRelative, CreateClubHandler)
                .Produces<ClubDto>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableValidation()
                .DisableAntiforgery()
                .WithName("CreateClub");

            // Search clubs by name, city, or state.
            group.MapGet(ClubEndpoints.SearchRelative, SearchClubsHandler)
                .Produces<IReadOnlyList<ClubDto>>()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName("SearchClubs");

            // Get the current user's pending join request, if any.
            group.MapGet(ClubEndpoints.PendingRequestRelative, GetPendingRequestHandler)
                .Produces<ClubJoinRequestDto>()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .WithName("GetPendingJoinRequest");

            // Submit a request for the current user to join a specific club.
            group.MapPost(ClubEndpoints.CreateJoinRequestRelative, CreateJoinRequestHandler)
                .Produces<ClubJoinRequestDto>(StatusCodes.Status201Created)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("CreateJoinRequest");

            // Cancel a pending join request owned by the current user.
            group.MapDelete(ClubEndpoints.CancelJoinRequestRelative, CancelJoinRequestHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .DisableAntiforgery()
                .WithName("CancelJoinRequest");

            // List a specific club's pending join requests (ClubAdmin only).
            group.MapGet(ClubEndpoints.AdminJoinRequestsRelative, GetClubJoinRequestsHandler)
                .Produces<IReadOnlyList<ClubJoinRequestDto>>()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .RequireAuthorization(Policies.RequireClubAdmin)
                .WithName("GetClubJoinRequests");

            // Approve a pending join request (ClubAdmin only).
            group.MapPost(ClubEndpoints.ApproveJoinRequestRelative, ApproveJoinRequestHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .DisableAntiforgery()
                .RequireAuthorization(Policies.RequireClubAdmin)
                .WithName("ApproveJoinRequest");

            // Reject a pending join request (ClubAdmin only).
            group.MapPost(ClubEndpoints.RejectJoinRequestRelative, RejectJoinRequestHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .DisableAntiforgery()
                .RequireAuthorization(Policies.RequireClubAdmin)
                .WithName("RejectJoinRequest");

            // Get the current user's club members.
            group.MapGet(ClubEndpoints.GetMembersRelative, GetClubMembersHandler)
                .Produces<IReadOnlyList<ClubMemberDto>>()
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .RequireAuthorization(Policies.RequireClubMember)
                .WithName("GetClubMembers");

            group.MapPost(ClubEndpoints.PromoteMemberRelative, PromoteMemberHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .RequireAuthorization(Policies.RequireClubAdmin)
                .WithName("PromoteClubMember");

            group.MapPost(ClubEndpoints.DemoteMemberRelative, DemoteMemberHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .RequireAuthorization(Policies.RequireClubAdmin)
                .WithName("DemoteClubMember");

            group.MapDelete(ClubEndpoints.RemoveMemberRelative, RemoveMemberHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .RequireAuthorization(Policies.RequireClubAdmin)
                .WithName("RemoveClubMember");

            group.MapDelete(ClubEndpoints.LeaveClubRelative, LeaveClubHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .RequireAuthorization(Policies.RequireClubMember)
                .WithName("LeaveClub");

            // Serve a club crest by club ID and size, with ETag caching. Mapped outside
            // the clubs group because its route lives under /api/clubs/{clubId}/crest.
            endpoints.MapGet(ClubCrestEndpoints.GetTemplate, GetCrestHandler)
                .RequireAuthorization()
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .WithName("GetClubCrest");

            // Change a club's crest (ClubAdmin only, multipart upload).
            group.MapPost(ClubCrestEndpoints.ManageRelative, ChangeCrestHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableValidation()
                .DisableAntiforgery()
                .RequireAuthorization(Policies.RequireClubAdmin)
                .WithName("ChangeClubCrest");

            // Remove a club's crest (ClubAdmin only).
            group.MapDelete(ClubCrestEndpoints.ManageRelative, RemoveCrestHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .RequireAuthorization(Policies.RequireClubAdmin)
                .WithName("RemoveClubCrest");

            // Cookie refresh hop after club creation: reissues auth cookie so claims take effect.
            // Mapped at its absolute path, outside the API group.
            endpoints.MapGet(ClubEndpoints.Complete, CompleteHandler)
                .RequireAuthorization()
                .WithName("CompleteClubOnboarding");

            return endpoints;
        }
    }

    /// <summary>
    /// Handles club creation requests (multipart form: name, city, state, and required crest file).
    /// </summary>
    private static async Task<IResult> CreateClubHandler(
        [FromForm] string name,
        [FromForm] string city,
        [FromForm] string state,
        HttpContext context,
        IClubService clubService,
        CancellationToken cancellationToken)
    {
        // Read the crest from the form instead of binding a required IFormFile parameter: a
        // missing required file would make the framework produce a 400 before the handler runs,
        // leaving the structured validation problem below unreachable.
        var crest = context.Request.Form.Files.GetFile("crest");

        if (crest is null || crest.Length is 0 or > ProfilePhotoConstraints.MaxBytes)
        {
            var message = $"The crest must be between 1 byte and {ProfilePhotoConstraints.MaxBytes / (1024 * 1024)} MB.";
            return ServiceProblem.Validation("crest", message).ToHttpResult();
        }

        byte[] crestContent;
        await using (var stream = crest.OpenReadStream())
        using (var buffer = new MemoryStream((int)crest.Length))
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            crestContent = buffer.ToArray();
        }

        var input = new CreateClubInput
        {
            Name = name,
            City = city,
            State = state,
            CrestContent = crestContent,
            CrestContentType = crest.ContentType
        };

        var result = await clubService.CreateClubAsync(input, cancellationToken);
        // No GET-club-by-id endpoint exists yet, so return 201 without a Location header.
        return result.ToHttpResult(club => TypedResults.Created((string?)null, club));
    }

    /// <summary>
    /// Handles retrieval of a club crest by club ID and size, with ETag caching.
    /// </summary>
    private static async Task<IResult> GetCrestHandler(
        long clubId,
        [FromQuery] string? size,
        HttpContext context,
        IDbContextFactory<NovaReadDbContext> readDbContextFactory,
        [FromKeyedServices("club-crests")] BlobContainerClient containerClient,
        CancellationToken cancellationToken)
    {
        // Query enum binding is case-sensitive; accept "small"/"Small" etc. explicitly.
        // Enum.TryParse also accepts arbitrary numeric strings (e.g. "99"), which would skip
        // the variant checks below yet still resolve to a blob — reject anything that is not
        // a defined member.
        if (!Enum.TryParse<ProfilePhotoSize>(size, ignoreCase: true, out var crestSize)
            || !Enum.IsDefined(crestSize))
        {
            crestSize = ProfilePhotoSize.Medium;
        }

        // The original may retain more detail than the public variants; club crests are
        // public-facing within the club, so only the generated variants are ever served. Return
        // 404 (not 403) to avoid leaking whether a crest exists.
        if (crestSize == ProfilePhotoSize.Original)
        {
            return ServiceProblem.NotFound().ToHttpResult();
        }

        ClubCrestEntity? crest;
        await using (var dbContext = await readDbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            crest = await dbContext.ClubCrests
                .FirstOrDefaultAsync(c => c.ClubId == clubId, cancellationToken);
        }

        var blobName = SelectBlobName(crest, crestSize);
        if (crest is null || blobName is null)
        {
            return ServiceProblem.NotFound().ToHttpResult();
        }

        var blobClient = containerClient.GetBlobClient(blobName);
        try
        {
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var etag = $"\"{properties.Value.ETag.ToString().Trim('"')}\"";

            // no-cache (not max-age) so the browser revalidates with If-None-Match on every
            // use; the crest URL is stable per club, so a freshness lifetime would keep
            // serving the old image after a new upload. Unchanged crests still get 304s.
            context.Response.Headers.CacheControl = "private, no-cache";
            context.Response.Headers.ETag = etag;

            if (context.Request.Headers.IfNoneMatch.Any(value => value == etag))
            {
                return TypedResults.StatusCode(StatusCodes.Status304NotModified);
            }

            var download = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return TypedResults.Stream(download.Value.Content, "image/webp");
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            return ServiceProblem.NotFound().ToHttpResult();
        }
    }

    /// <summary>
    /// Handles changing a club's crest (multipart upload, ClubAdmin only).
    /// </summary>
    private static async Task<IResult> ChangeCrestHandler(
        long clubId,
        [FromForm] IFormFile? crest,
        HttpContext context,
        UserManager<NovaUserEntity> userManager,
        SignInManager<NovaUserEntity> signInManager,
        IClubCrestService clubCrestService,
        CancellationToken cancellationToken)
    {
        // The file is bound as a *nullable* form parameter: the framework pre-reads the form
        // (returning 415 for non-form content types and 400 for bodyless requests — the same
        // pre-bind behavior as before the missing-file fix), while a missing "crest" part
        // binds null and reaches the structured validation problem below.
        if (crest is null || crest.Length is 0 or > ProfilePhotoConstraints.MaxBytes)
        {
            var message = $"The crest must be between 1 byte and {ProfilePhotoConstraints.MaxBytes / (1024 * 1024)} MB.";
            return ServiceProblem.Validation("crest", message).ToHttpResult();
        }

        byte[] crestContent;
        await using (var stream = crest.OpenReadStream())
        using (var buffer = new MemoryStream((int)crest.Length))
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            crestContent = buffer.ToArray();
        }

        var result = await clubCrestService.ChangeClubCrestAsync(
            clubId,
            new ClubCrestUpload(crestContent, crest.ContentType),
            cancellationToken);

        if (result.IsProblem)
        {
            return result.ToHttpResult(_ => TypedResults.NoContent());
        }

        // The crest changed and the acting admin's security stamp was bumped when the club's
        // members were marked stale; reissue their cookie so HasClubCrest takes effect now.
        await RefreshAdminCookieAsync(context, userManager, signInManager, cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Handles removing a club's crest (ClubAdmin only).
    /// </summary>
    private static async Task<IResult> RemoveCrestHandler(
        long clubId,
        HttpContext context,
        UserManager<NovaUserEntity> userManager,
        SignInManager<NovaUserEntity> signInManager,
        IClubCrestService clubCrestService,
        CancellationToken cancellationToken)
    {
        var result = await clubCrestService.RemoveClubCrestAsync(clubId, cancellationToken);
        if (result.IsProblem)
        {
            return result.ToHttpResult(_ => TypedResults.NoContent());
        }

        // The crest was removed and the acting admin's security stamp was bumped when the
        // club's members were marked stale; reissue their cookie so HasClubCrest disappears now.
        await RefreshAdminCookieAsync(context, userManager, signInManager, cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Reissues the acting admin's authentication cookie so their rebuilt claims (loaded from
    /// the database, not the request principal) take effect without waiting for revalidation.
    /// </summary>
    private static async Task RefreshAdminCookieAsync(
        HttpContext context,
        UserManager<NovaUserEntity> userManager,
        SignInManager<NovaUserEntity> signInManager,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
        {
            return;
        }

        await signInManager.RefreshSignInAsync(user);
    }

    /// <summary>
    /// Selects the blob name for the requested crest size, falling back to the original
    /// when a variant has not been generated. The small variant is a 64px square; the
    /// medium and large variants preserve the source aspect ratio.
    /// </summary>
    /// <param name="crest">The crest entity, or <see langword="null"/> when the club has no crest.</param>
    /// <param name="size">The requested size.</param>
    /// <returns>The blob name to serve, or <see langword="null"/> when unavailable.</returns>
    private static string? SelectBlobName(ClubCrestEntity? crest, ProfilePhotoSize size) => crest is null
        ? null
        : size switch
        {
            ProfilePhotoSize.Small => crest.SmallBlobName ?? crest.OriginalBlobName,
            ProfilePhotoSize.Medium => crest.MediumBlobName ?? crest.OriginalBlobName,
            ProfilePhotoSize.Large => crest.LargeBlobName ?? crest.OriginalBlobName,
            _ => null
        };

    /// <summary>
    /// Handles club search requests.
    /// </summary>
    private static async Task<IResult> SearchClubsHandler(
        [FromQuery] string? q,
        IClubService clubService,
        CancellationToken cancellationToken)
    {
        var result = await clubService.SearchClubsAsync(q, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Handles requests for the current user's pending join request.
    /// </summary>
    private static async Task<IResult> GetPendingRequestHandler(
        IClubJoinRequestService joinRequestService,
        CancellationToken cancellationToken)
    {
        var result = await joinRequestService.GetCurrentUserPendingRequestAsync(cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Handles join request creation for a specific club.
    /// </summary>
    private static async Task<IResult> CreateJoinRequestHandler(
        long clubId,
        IClubJoinRequestService joinRequestService,
        CancellationToken cancellationToken)
    {
        var result = await joinRequestService.CreateJoinRequestAsync(clubId, cancellationToken);
        return result.ToHttpResult(dto => TypedResults.CreatedAtRoute(dto, "GetPendingJoinRequest"));
    }

    /// <summary>
    /// Handles cancellation of a pending join request.
    /// </summary>
    private static async Task<IResult> CancelJoinRequestHandler(
        long requestId,
        IClubJoinRequestService joinRequestService,
        CancellationToken cancellationToken)
    {
        var result = await joinRequestService.CancelJoinRequestAsync(requestId, cancellationToken);
        return result.ToHttpResult(_ => TypedResults.NoContent());
    }

    /// <summary>
    /// Handles listing a club's pending join requests (ClubAdmin only).
    /// </summary>
    private static async Task<IResult> GetClubJoinRequestsHandler(
        long clubId,
        IClubJoinRequestService joinRequestService,
        CancellationToken cancellationToken)
    {
        var result = await joinRequestService.GetClubJoinRequestsAsync(clubId, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Handles GET /api/clubs/members — returns the current user's other club members.
    /// </summary>
    /// <param name="clubMemberService">The club member service.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The HTTP result containing the list of club members.</returns>
    private static async Task<IResult> GetClubMembersHandler(
        IClubMemberService clubMemberService,
        CancellationToken cancellationToken)
    {
        var result = await clubMemberService.GetClubMembersAsync(cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Handles approving a pending join request (ClubAdmin only).
    /// </summary>
    private static async Task<IResult> ApproveJoinRequestHandler(
        long requestId,
        IClubJoinRequestService joinRequestService,
        CancellationToken cancellationToken)
    {
        var result = await joinRequestService.ApproveJoinRequestAsync(requestId, cancellationToken);
        return result.ToHttpResult(_ => TypedResults.NoContent());
    }

    /// <summary>
    /// Handles rejecting a pending join request (ClubAdmin only).
    /// </summary>
    private static async Task<IResult> RejectJoinRequestHandler(
        long requestId,
        IClubJoinRequestService joinRequestService,
        CancellationToken cancellationToken)
    {
        var result = await joinRequestService.RejectJoinRequestAsync(requestId, cancellationToken);
        return result.ToHttpResult(_ => TypedResults.NoContent());
    }

    /// <summary>
    /// Handles promotion of a club member.
    /// </summary>
    private static async Task<IResult> PromoteMemberHandler(
        long memberUserId,
        IClubMemberService clubMemberService,
        CancellationToken cancellationToken)
    {
        var result = await clubMemberService.PromoteMemberAsync(memberUserId, cancellationToken);
        return result.ToHttpResult(_ => TypedResults.NoContent());
    }

    private static async Task<IResult> DemoteMemberHandler(
        long memberUserId,
        IClubMemberService clubMemberService,
        CancellationToken cancellationToken)
        => (await clubMemberService.DemoteMemberAsync(memberUserId, cancellationToken))
            .ToHttpResult(_ => TypedResults.NoContent());

    private static async Task<IResult> RemoveMemberHandler(
        long memberUserId,
        IClubMemberService clubMemberService,
        CancellationToken cancellationToken)
        => (await clubMemberService.RemoveMemberAsync(memberUserId, cancellationToken))
            .ToHttpResult(_ => TypedResults.NoContent());

    private static async Task<IResult> LeaveClubHandler(
        IClubMemberService clubMemberService,
        CancellationToken cancellationToken)
        => (await clubMemberService.LeaveClubAsync(cancellationToken))
            .ToHttpResult(_ => TypedResults.NoContent());

    /// <summary>
    /// Handles the post-onboarding cookie refresh: reissues the auth cookie so the
    /// ClubId claim takes effect, then redirects to the requested local URL.
    /// </summary>
    private static async Task<IResult> CompleteHandler(
        HttpContext context,
        UserManager<NovaUserEntity> userManager,
        SignInManager<NovaUserEntity> signInManager,
        [FromQuery] string? returnUrl,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
        {
            return TypedResults.Challenge();
        }

        await signInManager.RefreshSignInAsync(user);
        var target = !string.IsNullOrEmpty(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
            ? returnUrl.TrimStart('/')
            : "Clubs/Onboarding";
        return TypedResults.LocalRedirect($"~/{target}");
    }
}
