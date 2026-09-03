using Nova.Features.Shared;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Security;

namespace Nova.Features.Campaigns;

/// <summary>
/// Maps the administrator campaign creation endpoint.
/// </summary>
internal static class CampaignCreationEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps campaign creation under the shared campaign route with ClubAdmin authorization.
        /// </summary>
        /// <returns>The endpoint route builder for chaining.</returns>
        public IEndpointRouteBuilder MapCampaignCreationEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints
                .MapGroup(CampaignEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubAdmin);

            group.MapPost(CampaignEndpoints.CreateRelative, CreateCampaignHandler)
                .Produces<CreateCampaignResult>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName(CampaignEndpoints.CreateRouteName);

            return endpoints;
        }
    }

    /// <summary>
    /// Creates an Active campaign and its initial Active-player participation snapshot.
    /// </summary>
    /// <param name="input">The campaign and season creation request.</param>
    /// <param name="campaignCreationService">The campaign creation service.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>A 201 response containing the committed aggregate, or ProblemDetails.</returns>
    private static async Task<IResult> CreateCampaignHandler(
        CreateCampaignInput input,
        ICampaignCreationService campaignCreationService,
        CancellationToken cancellationToken)
    {
        var result = await campaignCreationService.CreateAsync(input, cancellationToken);
        return result.ToHttpResult(
            campaign => TypedResults.Created((string?)null, campaign));
    }
}
