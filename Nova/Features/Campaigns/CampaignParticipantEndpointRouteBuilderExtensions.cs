using Nova.Features.Shared;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Security;

namespace Nova.Features.Campaigns;

/// <summary>
/// Maps campaign participant roster, detail, and graduation-years endpoints.
/// </summary>
internal static class CampaignParticipantEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps campaign participant roster, detail, and graduation-years GET endpoints.
        /// </summary>
        /// <returns>The endpoint route builder for chaining.</returns>
        public IEndpointRouteBuilder MapCampaignParticipantEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints
                .MapGroup(CampaignEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubMember);

            group.MapGet(CampaignEndpoints.GetCampaignParticipantRosterRelative, GetParticipantRosterHandler)
                .Produces<PagedResult<CampaignParticipantRosterItem>>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName(CampaignEndpoints.GetCampaignParticipantRosterRouteName);

            group.MapGet(CampaignEndpoints.GetCampaignParticipantDetailRelative, GetParticipantDetailHandler)
                .Produces<CampaignParticipantDetailDto>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName(CampaignEndpoints.GetCampaignParticipantDetailRouteName);

            group.MapGet(CampaignEndpoints.GetCampaignParticipantGraduationYearsRelative, GetParticipantGraduationYearsHandler)
                .Produces<IReadOnlyList<int>>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName(CampaignEndpoints.GetCampaignParticipantGraduationYearsRouteName);

            return endpoints;
        }
    }

    /// <summary>
    /// Handles the campaign participant roster GET request and converts the service result to an HTTP response.
    /// </summary>
    /// <param name="input">The paged roster query parameters.</param>
    /// <param name="campaignParticipantQueryService">The service that resolves the roster query.</param>
    /// <param name="cancellationToken">Propagates notification that the request should be cancelled.</param>
    /// <returns>The HTTP result for the roster page.</returns>
    private static async Task<IResult> GetParticipantRosterHandler(
        [AsParameters] GetCampaignParticipantRosterInput input,
        ICampaignParticipantQueryService campaignParticipantQueryService,
        CancellationToken cancellationToken)
    {
        var result = await campaignParticipantQueryService.GetParticipantRosterAsync(input, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Handles the campaign participant detail GET request and converts the service result to an HTTP response.
    /// </summary>
    /// <param name="input">The participant detail query parameters.</param>
    /// <param name="campaignParticipantQueryService">The service that resolves the detail query.</param>
    /// <param name="cancellationToken">Propagates notification that the request should be cancelled.</param>
    /// <returns>The HTTP result for the participant detail.</returns>
    private static async Task<IResult> GetParticipantDetailHandler(
        [AsParameters] GetCampaignParticipantDetailInput input,
        ICampaignParticipantQueryService campaignParticipantQueryService,
        CancellationToken cancellationToken)
    {
        var result = await campaignParticipantQueryService.GetParticipantDetailAsync(input, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Handles the campaign participant graduation-years GET request and converts the service result to an HTTP response.
    /// </summary>
    /// <param name="input">The graduation-years query parameters.</param>
    /// <param name="campaignParticipantQueryService">The service that resolves the graduation-years query.</param>
    /// <param name="cancellationToken">Propagates notification that the request should be cancelled.</param>
    /// <returns>The HTTP result for the graduation-years list.</returns>
    private static async Task<IResult> GetParticipantGraduationYearsHandler(
        [AsParameters] GetCampaignParticipantGraduationYearsInput input,
        ICampaignParticipantQueryService campaignParticipantQueryService,
        CancellationToken cancellationToken)
    {
        var result = await campaignParticipantQueryService.GetRosterGraduationYearsAsync(input, cancellationToken);
        return result.ToHttpResult();
    }
}
