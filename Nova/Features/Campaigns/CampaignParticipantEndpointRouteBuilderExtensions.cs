using Nova.Features.Common;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;

namespace Nova.Features.Campaigns;

/// <summary>
/// Maps campaign participant discovery, detail, and independent evaluation evidence endpoints.
/// </summary>
internal static class CampaignParticipantEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps campaign participant discovery, detail, and evaluation evidence GET endpoints.
        /// </summary>
        /// <returns>The endpoint route builder for chaining.</returns>
        public IEndpointRouteBuilder MapCampaignParticipantEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints
                .MapGroup(CampaignEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubMember);

            group.MapGet(CampaignEndpoints.GetCampaignParticipantRosterRelative, GetParticipantRosterHandlerAsync)
                .Produces<PagedResult<CampaignParticipantRosterItem>>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName(CampaignEndpoints.GetCampaignParticipantRosterRouteName);

            group.MapGet(CampaignEndpoints.GetCampaignParticipantDetailRelative, GetParticipantDetailHandlerAsync)
                .Produces<CampaignParticipantDetailDto>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName(CampaignEndpoints.GetCampaignParticipantDetailRouteName);

            group.MapGet(CampaignEndpoints.GetCampaignParticipantGraduationYearsRelative, GetParticipantGraduationYearsHandlerAsync)
                .Produces<IReadOnlyList<int>>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName(CampaignEndpoints.GetCampaignParticipantGraduationYearsRouteName);

            MapEvidenceReads(group);
            return endpoints;
        }
    }

    /// <summary>Maps independently recoverable evaluation reads with complete HTTP metadata.</summary>
    private static void MapEvidenceReads(RouteGroupBuilder group)
    {
        var notes = group.MapGet(CampaignEndpoints.EvaluationNotesRelative, GetEvaluationNotesHandlerAsync)
            .Produces<EvaluationHistoryPage<CampaignParticipantNoteDto>>().WithName(CampaignEndpoints.EvaluationNotesRouteName);
        var applications = group.MapGet(CampaignEndpoints.EvaluationApplicationsRelative, GetEvaluationApplicationsHandlerAsync)
            .Produces<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>().WithName(CampaignEndpoints.EvaluationApplicationsRouteName);
        var choices = group.MapGet(CampaignEndpoints.EvaluationTagChoicesRelative, GetEvaluationTagChoicesHandlerAsync)
            .Produces<IReadOnlyList<EvaluationTagChoice>>().WithName(CampaignEndpoints.EvaluationTagChoicesRouteName);
        foreach (var endpoint in new[] { notes, applications, choices })
        {
            endpoint.ProducesValidationProblem().ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>Reads a bounded page of shared participant notes.</summary>
    /// <param name="input">The participant identity and optional history cursor.</param>
    /// <param name="service">The authorized evaluation query service.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>The note history page or a structured problem.</returns>
    private static async Task<IResult> GetEvaluationNotesHandlerAsync(
        [AsParameters] GetEvaluationHistoryInput input,
        ICampaignEvaluationQueryService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetNotesAsync(input, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>Reads a bounded page of participant trait applications.</summary>
    /// <param name="input">The participant identity and optional history cursor.</param>
    /// <param name="service">The authorized evaluation query service.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>The application history page or a structured problem.</returns>
    private static async Task<IResult> GetEvaluationApplicationsHandlerAsync(
        [AsParameters] GetEvaluationHistoryInput input,
        ICampaignEvaluationQueryService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetApplicationsAsync(input, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>Reads available trait definitions with participant application status.</summary>
    /// <param name="input">The participant identity.</param>
    /// <param name="service">The authorized evaluation query service.</param>
    /// <param name="cancellationToken">Propagates request cancellation.</param>
    /// <returns>The trait choices or a structured problem.</returns>
    private static async Task<IResult> GetEvaluationTagChoicesHandlerAsync(
        [AsParameters] GetCampaignParticipantDetailInput input,
        ICampaignEvaluationQueryService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetTagChoicesAsync(input, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Handles the campaign participant roster GET request and converts the service result to an HTTP response.
    /// </summary>
    /// <param name="input">The paged roster query parameters.</param>
    /// <param name="campaignParticipantQueryService">The service that resolves the roster query.</param>
    /// <param name="cancellationToken">Propagates notification that the request should be cancelled.</param>
    /// <returns>The HTTP result for the roster page.</returns>
    private static async Task<IResult> GetParticipantRosterHandlerAsync(
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
    private static async Task<IResult> GetParticipantDetailHandlerAsync(
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
    private static async Task<IResult> GetParticipantGraduationYearsHandlerAsync(
        [AsParameters] GetCampaignParticipantGraduationYearsInput input,
        ICampaignParticipantQueryService campaignParticipantQueryService,
        CancellationToken cancellationToken)
    {
        var result = await campaignParticipantQueryService.GetRosterGraduationYearsAsync(input, cancellationToken);
        return result.ToHttpResult();
    }
}
