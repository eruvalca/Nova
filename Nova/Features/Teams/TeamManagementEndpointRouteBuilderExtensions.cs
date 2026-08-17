using Nova.Features.Shared;
using Nova.Shared.Features.Teams;
using Nova.Shared.Security;

namespace Nova.Features.Teams;

/// <summary>
/// Maps the minimal API endpoints for team creation and permanent-profile editing.
/// </summary>
internal static class TeamManagementEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps team-management endpoints under the teams group with club-administrator authorization.
        /// </summary>
        /// <returns>The endpoint route builder, for chaining.</returns>
        public IEndpointRouteBuilder MapTeamManagementEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints
                .MapGroup(TeamEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubAdmin);

            group.MapPost(TeamEndpoints.CreateRelative, CreateTeamHandler)
                .Produces<TeamDto>(StatusCodes.Status201Created)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("CreateTeam");

            group.MapPut(TeamEndpoints.UpdateRelative, UpdateTeamHandler)
                .Produces<TeamDto>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("UpdateTeam");

            return endpoints;
        }
    }

    /// <summary>
    /// Handles POST /api/teams.
    /// </summary>
    /// <param name="input">The requested team profile.</param>
    /// <param name="teamManagementService">The team management service.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The created team or a ProblemDetails response.</returns>
    private static async Task<IResult> CreateTeamHandler(
        CreateTeamInput input,
        ITeamManagementService teamManagementService,
        CancellationToken cancellationToken)
    {
        var result = await teamManagementService.CreateAsync(input, cancellationToken);
        return result.ToHttpResult(team => TypedResults.CreatedAtRoute(
            team,
            TeamEndpoints.GetDetailRouteName,
            new { teamId = team.TeamId }));
    }

    /// <summary>
    /// Handles PUT /api/teams/{teamId}.
    /// </summary>
    /// <param name="teamId">The route team identifier.</param>
    /// <param name="input">The requested team profile.</param>
    /// <param name="teamManagementService">The team management service.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The updated team or a ProblemDetails response.</returns>
    private static async Task<IResult> UpdateTeamHandler(
        long teamId,
        UpdateTeamInput input,
        ITeamManagementService teamManagementService,
        CancellationToken cancellationToken)
    {
        if (teamId != input.TeamId)
        {
            return Nova.Shared.Results.ServiceProblem.BadRequest(
                    "The team identifier in the route does not match the request body.")
                .ToHttpResult();
        }

        var result = await teamManagementService.UpdateAsync(input, cancellationToken);
        return result.ToHttpResult();
    }
}
