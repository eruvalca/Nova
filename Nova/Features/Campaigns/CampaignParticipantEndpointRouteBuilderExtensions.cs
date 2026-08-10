using Nova.Features.Shared;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Security;

namespace Nova.Features.Campaigns;

/// <summary>
/// Maps campaign participant roster and detail endpoints.
/// </summary>
internal static class CampaignParticipantEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps campaign participant roster and detail GET endpoints.
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

            return endpoints;
        }
    }

    private static async Task<IResult> GetParticipantRosterHandler(
        [AsParameters] GetCampaignParticipantRosterInput input,
        ICampaignParticipantQueryService campaignParticipantQueryService,
        CancellationToken cancellationToken)
    {
        var result = await campaignParticipantQueryService.GetParticipantRosterAsync(input, cancellationToken);
        return result.ToHttpResult();
    }

    private static async Task<IResult> GetParticipantDetailHandler(
        [AsParameters] GetCampaignParticipantDetailInput input,
        ICampaignParticipantQueryService campaignParticipantQueryService,
        CancellationToken cancellationToken)
    {
        var result = await campaignParticipantQueryService.GetParticipantDetailAsync(input, cancellationToken);
        return result.ToHttpResult();
    }
}
