using Nova.Features.Shared;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Security;

namespace Nova.Features.Campaigns;

/// <summary>
/// Maps campaign closeout-readiness and bounded recent-activity query endpoints.
/// </summary>
internal static class CampaignCloseoutEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps the campaign closeout-readiness and recent-activity routes under the shared campaign group.
        /// </summary>
        /// <returns>The endpoint route builder for chaining.</returns>
        public IEndpointRouteBuilder MapCampaignCloseoutEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints
                .MapGroup(CampaignEndpoints.GroupPrefix)
                .RequireAuthorization();

            group.MapGet(CampaignEndpoints.GetCampaignCloseoutReadinessRelative, GetCloseoutReadinessHandler)
                .Produces<CampaignCloseoutReadinessDto>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .RequireAuthorization(Policies.RequireClubMember)
                .WithName(CampaignEndpoints.GetCampaignCloseoutReadinessRouteName);

            group.MapGet(CampaignEndpoints.GetCampaignActivityRelative, GetActivityHandler)
                .Produces<CampaignActivityResult>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .RequireAuthorization(Policies.RequireClubMember)
                .WithName(CampaignEndpoints.GetCampaignActivityRouteName);

            return endpoints;
        }
    }

    /// <summary>
    /// Handles the campaign closeout-readiness GET request and converts the service result to an HTTP response.
    /// </summary>
    /// <param name="input">The closeout-readiness query parameters.</param>
    /// <param name="closeoutQueryService">The service that resolves the closeout-readiness query.</param>
    /// <param name="cancellationToken">Propagates notification that the request should be cancelled.</param>
    /// <returns>The HTTP result for the closeout readiness.</returns>
    private static async Task<IResult> GetCloseoutReadinessHandler(
        [AsParameters] GetCampaignCloseoutReadinessInput input,
        ICampaignCloseoutQueryService closeoutQueryService,
        CancellationToken cancellationToken)
    {
        var result = await closeoutQueryService.GetCloseoutReadinessAsync(input, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Handles the recent campaign activity GET request and converts the service result to an HTTP response.
    /// </summary>
    /// <param name="input">The activity query parameters.</param>
    /// <param name="closeoutQueryService">The service that resolves the activity query.</param>
    /// <param name="cancellationToken">Propagates notification that the request should be cancelled.</param>
    /// <returns>The HTTP result for the recent campaign activity.</returns>
    private static async Task<IResult> GetActivityHandler(
        [AsParameters] GetCampaignActivityInput input,
        ICampaignCloseoutQueryService closeoutQueryService,
        CancellationToken cancellationToken)
    {
        var result = await closeoutQueryService.GetActivityAsync(input, cancellationToken);
        return result.ToHttpResult();
    }
}
