using Nova.Features.Shared;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Security;

namespace Nova.Features.Campaigns;

/// <summary>
/// Maps the authorized campaign read endpoints.
/// </summary>
internal static class CampaignQueryEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps campaign list and creation-setup GET endpoints.
        /// </summary>
        /// <returns>The endpoint route builder for chaining.</returns>
        public IEndpointRouteBuilder MapCampaignQueryEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints
                .MapGroup(CampaignEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubMember);

            group.MapGet(CampaignEndpoints.GetCampaignListRelative, GetCampaignListHandler)
                .Produces<CampaignListResult>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName(CampaignEndpoints.GetCampaignListRouteName);

            group.MapGet(CampaignEndpoints.GetCampaignDetailRelative, GetCampaignDetailHandler)
                .Produces<CampaignDetailResult>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName(CampaignEndpoints.GetCampaignDetailRouteName);

            group.MapGet(CampaignEndpoints.GetCreationSetupRelative, GetCreationSetupHandler)
                .Produces<CampaignCreationSetupResult>()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName(CampaignEndpoints.GetCreationSetupRouteName);

            return endpoints;
        }
    }

    /// <summary>
    /// Handles the bounded campaign-list request.
    /// </summary>
    /// <param name="input">The optional campaign-list filters.</param>
    /// <param name="campaignQueryService">The campaign query service.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The campaign list or a ProblemDetails response.</returns>
    private static async Task<IResult> GetCampaignListHandler(
        [AsParameters] GetCampaignListInput input,
        ICampaignQueryService campaignQueryService,
        CancellationToken cancellationToken)
    {
        var result = await campaignQueryService.GetCampaignListAsync(input, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Handles the campaign-detail request.
    /// </summary>
    /// <param name="input">The campaign identifier.</param>
    /// <param name="campaignQueryService">The campaign query service.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The campaign detail or a ProblemDetails response.</returns>
    private static async Task<IResult> GetCampaignDetailHandler(
        [AsParameters] GetCampaignDetailInput input,
        ICampaignQueryService campaignQueryService,
        CancellationToken cancellationToken)
    {
        var result = await campaignQueryService.GetCampaignDetailAsync(input, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Handles the campaign creation-setup request.
    /// </summary>
    /// <param name="campaignQueryService">The campaign query service.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The setup data or a ProblemDetails response.</returns>
    private static async Task<IResult> GetCreationSetupHandler(
        ICampaignQueryService campaignQueryService,
        CancellationToken cancellationToken)
    {
        var result = await campaignQueryService.GetCreationSetupAsync(cancellationToken);
        return result.ToHttpResult();
    }
}
