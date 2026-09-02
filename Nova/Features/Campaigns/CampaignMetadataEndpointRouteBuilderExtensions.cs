using Nova.Features.Shared;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Security;

namespace Nova.Features.Campaigns;

/// <summary>
/// Maps administrator campaign metadata update endpoints.
/// </summary>
internal static class CampaignMetadataEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps the campaign and season metadata update routes under the shared campaign group.
        /// </summary>
        /// <returns>The endpoint route builder for chaining.</returns>
        public IEndpointRouteBuilder MapCampaignMetadataEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints
                .MapGroup(CampaignEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubAdmin);

            group.MapPut(CampaignEndpoints.UpdateCampaignMetadataRelative, UpdateCampaignMetadataHandler)
                .Produces<UpdateCampaignMetadataResult>(StatusCodes.Status200OK)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery();

            return endpoints;
        }
    }

    /// <summary>
    /// Updates an Active campaign's name, season, start date, and planned end date.
    /// </summary>
    /// <param name="input">The campaign metadata correction request.</param>
    /// <param name="campaignMetadataService">The campaign metadata service.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>A 200 response containing the updated metadata, or ProblemDetails.</returns>
    private static async Task<IResult> UpdateCampaignMetadataHandler(
        UpdateCampaignMetadataInput input,
        ICampaignMetadataService campaignMetadataService,
        CancellationToken cancellationToken)
    {
        var result = await campaignMetadataService.UpdateAsync(input, cancellationToken);
        return result.ToHttpResult();
    }

}
