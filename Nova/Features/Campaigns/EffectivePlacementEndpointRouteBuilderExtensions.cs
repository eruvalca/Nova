using Nova.Features.Common;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Seasons;
using Nova.SharedKernel.Security;

namespace Nova.Features.Campaigns;

/// <summary>Maps bounded member-readable effective and campaign-local roster APIs.</summary>
internal static class EffectivePlacementEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>Registers the placement query routes under their owning feature groups.</summary>
        public IEndpointRouteBuilder MapEffectivePlacementEndpoints()
        {
            var seasons = endpoints.MapGroup(SeasonEndpoints.GroupPrefix).RequireAuthorization(Policies.RequireClubMember);
            Describe<CurrentSeasonRosterResult>(seasons.MapGet(SeasonEndpoints.CurrentRosterRelative, CurrentRosterHandlerAsync))
                .WithName(SeasonEndpoints.CurrentRosterRouteName);
            var campaigns = endpoints.MapGroup(CampaignEndpoints.GroupPrefix).RequireAuthorization(Policies.RequireClubMember);
            Describe<CampaignEffectivePlacementsResult>(campaigns.MapGet(CampaignEndpoints.EffectivePlacementsRelative, WorkingHandlerAsync))
                .ProducesProblem(StatusCodes.Status409Conflict).WithName(CampaignEndpoints.EffectivePlacementsRouteName);
            Describe<ClosedCampaignRosterResult>(campaigns.MapGet(CampaignEndpoints.ClosedRosterRelative, ClosedHandlerAsync))
                .ProducesProblem(StatusCodes.Status409Conflict).WithName(CampaignEndpoints.ClosedRosterRouteName);
            campaigns.MapGet(CampaignEndpoints.ClosedRosterExportRelative, ClosedExportHandlerAsync)
                .Produces(StatusCodes.Status200OK, contentType: ClosedCampaignRosterExportConstraints.CsvContentType)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName(CampaignEndpoints.ClosedRosterExportRouteName);
            return endpoints;
        }
    }

    private static RouteHandlerBuilder Describe<T>(RouteHandlerBuilder route)
        => route.Produces<T>().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status500InternalServerError);

    private static async Task<IResult> CurrentRosterHandlerAsync([AsParameters] GetCurrentSeasonRosterInput input,
        IEffectivePlacementQueryService service, CancellationToken cancellationToken)
        => (await service.GetCurrentSeasonRosterAsync(input, cancellationToken)).ToHttpResult();

    private static async Task<IResult> WorkingHandlerAsync([AsParameters] GetCampaignEffectivePlacementsInput input,
        IEffectivePlacementQueryService service, CancellationToken cancellationToken)
        => (await service.GetCampaignEffectivePlacementsAsync(input, cancellationToken)).ToHttpResult();

    private static async Task<IResult> ClosedHandlerAsync([AsParameters] GetClosedCampaignRosterInput input,
        IEffectivePlacementQueryService service, CancellationToken cancellationToken)
        => (await service.GetClosedCampaignRosterAsync(input, cancellationToken)).ToHttpResult();

    private static async Task<IResult> ClosedExportHandlerAsync([AsParameters] GetClosedCampaignRosterExportInput input,
        IEffectivePlacementQueryService service, CancellationToken cancellationToken)
        => (await service.ExportClosedCampaignRosterAsync(input, cancellationToken)).ToHttpResult(
            export => TypedResults.File(export.Content, export.ContentType, export.FileName));
}
