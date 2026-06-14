using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Shared;
using Nova.Shared.Clubs;
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

            // Create a new club; the current user becomes the club admin.
            group.MapPost(ClubEndpoints.CreateRelative, CreateClubHandler)
                .Produces<ClubDto>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
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

            // Cookie refresh hop after club creation: reissues auth cookie so claims take effect.
            // Mapped at its absolute path, outside the API group.
            endpoints.MapGet(ClubEndpoints.Complete, CompleteHandler)
                .RequireAuthorization()
                .WithName("CompleteClubOnboarding");

            return endpoints;
        }
    }

    /// <summary>
    /// Handles club creation requests.
    /// </summary>
    private static async Task<IResult> CreateClubHandler(
        CreateClubInput input,
        IClubService clubService,
        CancellationToken cancellationToken)
    {
        var result = await clubService.CreateClubAsync(input, cancellationToken);
        // No GET-club-by-id endpoint exists yet, so return 201 without a Location header.
        return result.ToHttpResult(club => TypedResults.Created((string?)null, club));
    }

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
