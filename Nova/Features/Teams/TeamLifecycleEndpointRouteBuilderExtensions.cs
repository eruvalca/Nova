using Nova.Features.Shared;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.Shared.Features.Teams;

namespace Nova.Features.Teams;

/// <summary>
/// Maps minimal API endpoints for team lifecycle and graduation-cutoff mutations.
/// </summary>
internal static class TeamLifecycleEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps team lifecycle endpoints under the teams group with club-administrator authorization.
        /// </summary>
        /// <returns>The endpoint route builder for chaining.</returns>
        public IEndpointRouteBuilder MapTeamLifecycleEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints
                .MapGroup(TeamEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubAdmin);

            group.MapPost(TeamEndpoints.ArchiveRelative, ArchiveTeamHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("ArchiveTeam");

            group.MapPost(TeamEndpoints.RestoreRelative, RestoreTeamHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("RestoreTeam");

            return endpoints;
        }
    }

    /// <summary>
    /// Handles POST archive requests for a team.
    /// </summary>
    /// <param name="teamId">The team identifier.</param>
    /// <param name="teamLifecycleService">The team lifecycle service.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A no-content response on success or ProblemDetails on failure.</returns>
    private static async Task<IResult> ArchiveTeamHandler(
        long teamId,
        ITeamLifecycleService teamLifecycleService,
        CancellationToken cancellationToken)
    {
        var result = await teamLifecycleService.ArchiveAsync(teamId, cancellationToken);
        return result.ToHttpResult(_ => TypedResults.NoContent());
    }

    /// <summary>
    /// Handles POST restore requests for a team.
    /// </summary>
    /// <param name="teamId">The team identifier.</param>
    /// <param name="teamLifecycleService">The team lifecycle service.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A no-content response on success or ProblemDetails on failure.</returns>
    private static async Task<IResult> RestoreTeamHandler(
        long teamId,
        ITeamLifecycleService teamLifecycleService,
        CancellationToken cancellationToken)
    {
        var result = await teamLifecycleService.RestoreAsync(teamId, cancellationToken);
        return result.ToHttpResult(_ => TypedResults.NoContent());
    }
}
