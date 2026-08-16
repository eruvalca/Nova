using Nova.Features.Shared;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Security;

namespace Nova.Features.Campaigns;

/// <summary>
/// Maps campaign placement roster and summary endpoints.
/// </summary>
internal static class CampaignPlacementEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps campaign placement roster and summary GET endpoints.
        /// </summary>
        /// <returns>The endpoint route builder for chaining.</returns>
        public IEndpointRouteBuilder MapCampaignPlacementEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints
                .MapGroup(CampaignEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubMember);

            group.MapGet(CampaignEndpoints.GetCampaignPlacementRosterRelative, GetPlacementRosterHandler)
                .Produces<PagedResult<CampaignPlacementRosterItem>>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName(CampaignEndpoints.GetCampaignPlacementRosterRouteName);

            group.MapGet(CampaignEndpoints.GetCampaignPlacementSummaryRelative, GetPlacementSummaryHandler)
                .Produces<CampaignPlacementSummaryDto>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName(CampaignEndpoints.GetCampaignPlacementSummaryRouteName);

            return endpoints;
        }
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
