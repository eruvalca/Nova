using Nova.Features.Common;
using Nova.SharedKernel.Features.Seasons;
using Nova.SharedKernel.Security;

namespace Nova.Features.Seasons;

/// <summary>Maps first-class season command and query endpoints.</summary>
internal static class SeasonEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>Maps the season API under <c>/api/seasons</c>.</summary>
        /// <returns>The endpoint route builder for chaining.</returns>
        public IEndpointRouteBuilder MapSeasonEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);
            var group = endpoints
                .MapGroup(SeasonEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubMember);

            group.MapGet(SeasonEndpoints.CollectionRelative, ListHandlerAsync)
                .Produces<SeasonPageResult>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName("ListSeasons");

            group.MapGet(SeasonEndpoints.DetailRelative, GetHandlerAsync)
                .Produces<SeasonDetailResult>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName(SeasonEndpoints.GetDetailRouteName);

            group.MapPost(SeasonEndpoints.CollectionRelative, CreateHandlerAsync)
                .RequireAuthorization(Policies.RequireClubAdmin)
                .Produces<SeasonSummary>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("CreateSeason");

            group.MapPut(SeasonEndpoints.DetailRelative, UpdateHandlerAsync)
                .RequireAuthorization(Policies.RequireClubAdmin)
                .Produces<SeasonSummary>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("UpdateSeason");

            group.MapPost(SeasonEndpoints.StartNextRelative, StartNextHandlerAsync)
                .RequireAuthorization(Policies.RequireClubAdmin)
                .Produces<StartNextSeasonResult>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("StartNextSeason");

            return endpoints;
        }
    }

    /// <summary>Handles season-list reads.</summary>
    private static async Task<IResult> ListHandlerAsync(
        [AsParameters] GetSeasonListInput input,
        ISeasonQueryService service,
        CancellationToken cancellationToken)
        => (await service.ListAsync(input, cancellationToken)).ToHttpResult();

    /// <summary>Handles season-detail reads.</summary>
    private static async Task<IResult> GetHandlerAsync(
        [AsParameters] GetSeasonDetailInput input,
        ISeasonQueryService service,
        CancellationToken cancellationToken)
        => (await service.GetAsync(input, cancellationToken)).ToHttpResult();

    /// <summary>Handles first-current-season creation.</summary>
    private static async Task<IResult> CreateHandlerAsync(
        CreateSeasonInput input,
        ISeasonCommandService service,
        CancellationToken cancellationToken)
        => (await service.CreateAsync(input, cancellationToken)).ToHttpResult(
            season => TypedResults.CreatedAtRoute(
                season,
                SeasonEndpoints.GetDetailRouteName,
                new { seasonId = season.SeasonId }));

    /// <summary>Handles metadata updates.</summary>
    private static async Task<IResult> UpdateHandlerAsync(
        long seasonId,
        UpdateSeasonInput input,
        ISeasonCommandService service,
        CancellationToken cancellationToken)
        => (await service.UpdateAsync(seasonId, input, cancellationToken)).ToHttpResult();

    /// <summary>Handles atomic season advancement.</summary>
    private static async Task<IResult> StartNextHandlerAsync(
        StartNextSeasonInput input,
        ISeasonCommandService service,
        CancellationToken cancellationToken)
        => (await service.StartNextAsync(input, cancellationToken)).ToHttpResult(
            result => TypedResults.CreatedAtRoute(
                result,
                SeasonEndpoints.GetDetailRouteName,
                new { seasonId = result.CurrentSeason.SeasonId }));
}
