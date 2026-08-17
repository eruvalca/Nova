using Nova.Features.Shared;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Security;

namespace Nova.Features.Campaigns;

/// <summary>
/// Maps campaign placement update, roster, and summary endpoints and converts placement results to HTTP.
/// </summary>
internal static class CampaignPlacementEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps the campaign placement update, roster, and summary routes under the shared campaign group.
        /// </summary>
        /// <returns>The endpoint route builder for chaining.</returns>
        public IEndpointRouteBuilder MapCampaignPlacementEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints
                .MapGroup(CampaignEndpoints.GroupPrefix)
                .RequireAuthorization();

            group.MapPut(CampaignEndpoints.UpdateCampaignPlacementRelative, UpdateCampaignPlacementHandler)
                .Produces<PlacementMutationSuccess>(StatusCodes.Status200OK)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .RequireAuthorization(Policies.RequireClubAdmin)
                .WithName(CampaignEndpoints.UpdateCampaignPlacementRouteName);

            group.MapGet(CampaignEndpoints.GetCampaignPlacementRosterRelative, GetPlacementRosterHandler)
                .Produces<PagedResult<CampaignPlacementRosterItem>>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .RequireAuthorization(Policies.RequireClubMember)
                .WithName(CampaignEndpoints.GetCampaignPlacementRosterRouteName);

            group.MapGet(CampaignEndpoints.GetCampaignPlacementSummaryRelative, GetPlacementSummaryHandler)
                .Produces<CampaignPlacementSummaryDto>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .RequireAuthorization(Policies.RequireClubMember)
                .WithName(CampaignEndpoints.GetCampaignPlacementSummaryRouteName);

            return endpoints;
        }
    }

    extension(PlacementUpdateResult result)
    {
        /// <summary>
        /// Converts a placement update result to an ASP.NET Core response.
        /// Success converts to a 200 response containing the new concurrency token; validation errors
        /// become a validation ProblemDetails; not-found, forbidden, and conflict cases become the
        /// matching ProblemDetails responses with their service-provided details.
        /// </summary>
        /// <returns>The HTTP response for the placement update result.</returns>
        public IResult ToHttpResult()
        {
            return result.Match(
                success => TypedResults.Ok(success),
                validation => ServiceProblem.Validation(validation.Value).ToHttpResult(),
                _ => ServiceProblem.NotFound().ToHttpResult(),
                forbidden => ServiceProblem.Forbidden(forbidden.Detail).ToHttpResult(),
                conflict => ServiceProblem.Conflict(conflict.Detail).ToHttpResult());
        }
    }

    /// <summary>
    /// Updates one campaign participant's placement outcome and optional team.
    /// </summary>
    /// <param name="playerCampaignAssignmentId">The campaign participation identifier from the route.</param>
    /// <param name="input">The placement update request body.</param>
    /// <param name="placementService">The campaign placement service.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>A 200 response containing the new concurrency token, or ProblemDetails.</returns>
    private static async Task<IResult> UpdateCampaignPlacementHandler(
        long playerCampaignAssignmentId,
        UpdateCampaignPlacementInput input,
        CampaignPlacementService placementService,
        CancellationToken cancellationToken)
    {
        // Ensure the route parameter and body agree on the target participation.
        if (playerCampaignAssignmentId != input.PlayerCampaignAssignmentId)
        {
            return ServiceProblem.BadRequest(
                    "The player campaign assignment identifier in the route does not match the request body.")
                .ToHttpResult();
        }

        var result = await placementService.UpdatePlacementAsync(input, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Handles the campaign placement roster GET request and converts the service result to an HTTP response.
    /// </summary>
    /// <param name="input">The paged placement roster query parameters.</param>
    /// <param name="campaignPlacementQueryService">The service that resolves the placement roster query.</param>
    /// <param name="cancellationToken">Propagates notification that the request should be cancelled.</param>
    /// <returns>The HTTP result for the placement roster page.</returns>
    private static async Task<IResult> GetPlacementRosterHandler(
        [AsParameters] GetCampaignPlacementRosterInput input,
        ICampaignPlacementQueryService campaignPlacementQueryService,
        CancellationToken cancellationToken)
    {
        var result = await campaignPlacementQueryService.GetPlacementRosterAsync(input, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Handles the campaign placement summary GET request and converts the service result to an HTTP response.
    /// </summary>
    /// <param name="input">The placement summary query parameters.</param>
    /// <param name="campaignPlacementQueryService">The service that resolves the placement summary query.</param>
    /// <param name="cancellationToken">Propagates notification that the request should be cancelled.</param>
    /// <returns>The HTTP result for the placement summary.</returns>
    private static async Task<IResult> GetPlacementSummaryHandler(
        [AsParameters] GetCampaignPlacementSummaryInput input,
        ICampaignPlacementQueryService campaignPlacementQueryService,
        CancellationToken cancellationToken)
    {
        var result = await campaignPlacementQueryService.GetPlacementSummaryAsync(input, cancellationToken);
        return result.ToHttpResult();
    }
}
