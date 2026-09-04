using Nova.Features.Shared;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Security;
using OneOf;
using OneOf.Types;

namespace Nova.Features.Campaigns;

/// <summary>
/// Maps campaign close and reopen endpoints and converts lifecycle results to HTTP.
/// </summary>
internal static class CampaignLifecycleEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps the campaign close and reopen routes under the shared campaign group.
        /// </summary>
        /// <returns>The endpoint route builder for chaining.</returns>
        public IEndpointRouteBuilder MapCampaignLifecycleEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints
                .MapGroup(CampaignEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubAdmin);

            group.MapPost(CampaignEndpoints.OpenRelative, OpenCampaignHandler)
                .Produces<OpenCampaignResult>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName(CampaignEndpoints.OpenRouteName);

            group.MapDelete(CampaignEndpoints.DeleteDraftRelative, DeleteDraftHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName(CampaignEndpoints.DeleteDraftRouteName);

            group.MapPost(CampaignEndpoints.CloseRelative, CloseCampaignHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName(CampaignEndpoints.CloseRouteName);

            group.MapPost(CampaignEndpoints.ReopenRelative, ReopenCampaignHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName(CampaignEndpoints.ReopenRouteName);

            return endpoints;
        }
    }

    extension(CampaignCloseResult result)
    {
        /// <summary>
        /// Converts a campaign-close result to an ASP.NET Core response.
        /// Success converts to a 204 no-content response; not-found, forbidden, close-blocked, and
        /// conflict cases become the matching ProblemDetails responses with their service-provided
        /// details and, for close-blocked, the condition-keyed error groups.
        /// </summary>
        /// <returns>The HTTP response for the campaign-close result.</returns>
        public IResult ToHttpResult()
        {
            return result.Match(
                _ => TypedResults.NoContent(),
                _ => ServiceProblem.NotFound().ToHttpResult(),
                forbidden => ServiceProblem.Forbidden(forbidden.Detail).ToHttpResult(),
                blocked => ServiceProblem.Conflict(blocked.Detail, blocked.Errors).ToHttpResult(),
                conflict => ServiceProblem.Conflict(conflict.Detail).ToHttpResult());
        }
    }

    extension(OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict> result)
    {
        /// <summary>
        /// Converts a campaign-reopen result to an ASP.NET Core response.
        /// Success converts to a 204 no-content response; not-found, forbidden, and conflict cases
        /// become the matching ProblemDetails responses with their service-provided details.
        /// </summary>
        /// <returns>The HTTP response for the campaign-reopen result.</returns>
        public IResult ToHttpResult()
        {
            return result.Match<IResult>(
                _ => TypedResults.NoContent(),
                _ => ServiceProblem.NotFound().ToHttpResult(),
                forbidden => ServiceProblem.Forbidden(forbidden.Detail).ToHttpResult(),
                conflict => ServiceProblem.Conflict(conflict.Detail).ToHttpResult());
        }
    }

    /// <summary>
    /// Handles POST close requests for a campaign.
    /// </summary>
    /// <param name="campaignId">The campaign identifier to close.</param>
    /// <param name="lifecycleService">The campaign lifecycle service.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A no-content response on success or ProblemDetails on failure.</returns>
    private static async Task<IResult> CloseCampaignHandler(
        long campaignId,
        CampaignLifecycleService lifecycleService,
        CancellationToken cancellationToken)
    {
        var result = await lifecycleService.CloseAsync(campaignId, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Handles an idempotent Draft-open request.
    /// </summary>
    /// <param name="campaignId">The Draft campaign identifier.</param>
    /// <param name="input">The logical opening operation input.</param>
    /// <param name="lifecycleService">The campaign lifecycle service.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The immutable opening receipt or ProblemDetails.</returns>
    private static async Task<IResult> OpenCampaignHandler(
        long campaignId,
        OpenCampaignInput input,
        CampaignLifecycleService lifecycleService,
        CancellationToken cancellationToken)
    {
        var result = await lifecycleService.OpenAsync(campaignId, input, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Handles an idempotent Draft-delete request.
    /// </summary>
    /// <param name="campaignId">The Draft campaign identifier.</param>
    /// <param name="lifecycleService">The campaign lifecycle service.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>No content or ProblemDetails.</returns>
    private static async Task<IResult> DeleteDraftHandler(
        long campaignId,
        CampaignLifecycleService lifecycleService,
        CancellationToken cancellationToken)
    {
        var result = await lifecycleService.DeleteDraftAsync(campaignId, cancellationToken);
        return result.ToHttpResult(_ => TypedResults.NoContent());
    }

    /// <summary>
    /// Handles POST reopen requests for a campaign.
    /// </summary>
    /// <param name="campaignId">The campaign identifier to reopen.</param>
    /// <param name="lifecycleService">The campaign lifecycle service.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A no-content response on success or ProblemDetails on failure.</returns>
    private static async Task<IResult> ReopenCampaignHandler(
        long campaignId,
        CampaignLifecycleService lifecycleService,
        CancellationToken cancellationToken)
    {
        var result = await lifecycleService.ReopenAsync(campaignId, cancellationToken);
        return result.ToHttpResult();
    }
}
