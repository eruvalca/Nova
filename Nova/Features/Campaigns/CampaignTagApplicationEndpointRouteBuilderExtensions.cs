using Nova.Features.Shared;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Security;

namespace Nova.Features.Campaigns;

/// <summary>
/// Maps the campaign tag application add and remove endpoints.
/// </summary>
internal static class CampaignTagApplicationEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps campaign tag application endpoints under the shared campaign route with club-member authorization.
        /// </summary>
        /// <returns>The endpoint route builder for chaining.</returns>
        public IEndpointRouteBuilder MapCampaignTagApplicationEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints
                .MapGroup(CampaignEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubMember);

            group.MapPost(CampaignEndpoints.ApplyCampaignTagApplicationRelative, ApplyCampaignTagApplicationHandler)
                .Produces<CampaignTagApplicationMutationSuccess>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName(CampaignEndpoints.ApplyCampaignTagApplicationRouteName);

            group.MapDelete(CampaignEndpoints.RemoveCampaignTagApplicationRelative, RemoveCampaignTagApplicationHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName(CampaignEndpoints.RemoveCampaignTagApplicationRouteName);

            return endpoints;
        }
    }

    /// <summary>
    /// Applies one tag definition to one campaign participation.
    /// </summary>
    /// <param name="input">The target participation and tag-definition identifiers.</param>
    /// <param name="service">The campaign tag application service.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>A 201 response containing the created application identifier, or ProblemDetails.</returns>
    private static async Task<IResult> ApplyCampaignTagApplicationHandler(
        ApplyCampaignTagApplicationInput input,
        ICampaignTagApplicationService service,
        CancellationToken cancellationToken)
    {
        var result = await service.ApplyAsync(input, cancellationToken);
        return result.ToHttpResult(success => TypedResults.Created((string?)null, success));
    }

    /// <summary>
    /// Removes one campaign tag application.
    /// </summary>
    /// <param name="campaignTagApplicationId">The campaign tag application identifier to remove.</param>
    /// <param name="service">The campaign tag application service.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>A no-content response on success or ProblemDetails on failure.</returns>
    private static async Task<IResult> RemoveCampaignTagApplicationHandler(
        long campaignTagApplicationId,
        ICampaignTagApplicationService service,
        CancellationToken cancellationToken)
    {
        var input = new RemoveCampaignTagApplicationInput { CampaignTagApplicationId = campaignTagApplicationId };
        var result = await service.RemoveAsync(input, cancellationToken);
        return result.ToHttpResult(_ => TypedResults.NoContent());
    }
}
