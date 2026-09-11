using Microsoft.AspNetCore.Mvc;
using Nova.Features.Common;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;

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

            group.MapPost(CampaignEndpoints.ApplyCampaignTagApplicationRelative, ApplyCampaignTagApplicationHandlerAsync)
                .Produces<CampaignTagApplicationMutationSuccess>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName(CampaignEndpoints.ApplyCampaignTagApplicationRouteName);

            group.MapPost(CampaignEndpoints.CreateAndApplyCampaignTagRelative, CreateAndApplyHandlerAsync)
                .Produces<CampaignTagApplicationMutationSuccess>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName(CampaignEndpoints.CreateAndApplyCampaignTagRouteName);

            group.MapDelete(CampaignEndpoints.RemoveCampaignTagApplicationRelative, RemoveCampaignTagApplicationHandlerAsync)
                .Produces<CampaignTagApplicationMutationSuccess>(StatusCodes.Status200OK)
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
    /// <param name="input">The target participation, tag-definition and replayable operation identifiers.</param>
    /// <param name="service">The campaign tag application service.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>A 201 response containing the application identity, already-applied outcome and immutable receipt, or ProblemDetails.</returns>
    private static async Task<IResult> ApplyCampaignTagApplicationHandlerAsync(
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
    /// <param name="input">The matching application identifier and original operation identity.</param>
    /// <param name="service">The campaign tag application service.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>A 200 response containing the immutable mutation receipt, or ProblemDetails on failure.</returns>
    private static async Task<IResult> RemoveCampaignTagApplicationHandlerAsync(
        long campaignTagApplicationId,
        [FromBody] RemoveCampaignTagApplicationInput input,
        ICampaignTagApplicationService service,
        CancellationToken cancellationToken)
    {
        ServiceResult<CampaignTagApplicationMutationSuccess> result = campaignTagApplicationId != input.CampaignTagApplicationId
            ? ServiceProblem.Validation(nameof(input.CampaignTagApplicationId), "The application must match the route.")
            : await service.RemoveAsync(input, cancellationToken);
        return result.ToHttpResult(TypedResults.Ok);
    }

    /// <summary>Resolves a trait, applies it and returns the original atomic receipt.</summary>
    private static async Task<IResult> CreateAndApplyHandlerAsync(CreateAndApplyCampaignTagInput input,
        ICampaignTagApplicationService service, CancellationToken cancellationToken)
    {
        var result = await service.CreateAndApplyAsync(input, cancellationToken);
        return result.ToHttpResult(success => TypedResults.Created((string?)null, success));
    }
}
