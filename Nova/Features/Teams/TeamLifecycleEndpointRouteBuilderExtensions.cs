using Nova.Features.Shared;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.Shared.Teams;

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

            group.MapPut(TeamEndpoints.UpdateGraduationYearRelative, UpdateGraduationYearHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("UpdateTeamGraduationYear");

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

    /// <summary>
    /// Handles PUT graduation-year cutoff requests for a team.
    /// </summary>
    /// <param name="teamId">The route team identifier.</param>
    /// <param name="input">The requested cutoff.</param>
    /// <param name="teamLifecycleService">The team lifecycle service.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A no-content response on success or ProblemDetails on failure.</returns>
    private static async Task<IResult> UpdateGraduationYearHandler(
        long teamId,
        UpdateTeamGraduationYearInput input,
        ITeamLifecycleService teamLifecycleService,
        CancellationToken cancellationToken)
    {
        if (teamId != input.TeamId)
        {
            return ServiceProblem.BadRequest(
                    "The team identifier in the route does not match the request body.")
                .ToHttpResult();
        }

        var result = await teamLifecycleService.UpdateGraduationYearAsync(input, cancellationToken);
        return result.ToHttpResult(_ => TypedResults.NoContent());
    }
}
