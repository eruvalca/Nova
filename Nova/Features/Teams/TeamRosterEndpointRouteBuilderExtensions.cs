using Nova.Features.Shared;
using Nova.Shared.Features.Teams;
using Nova.Shared.Security;

namespace Nova.Features.Teams;

/// <summary>
/// Maps the team roster query endpoint.
/// </summary>
internal static class TeamRosterEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps the tenant-scoped team roster endpoint.
        /// </summary>
        /// <returns>The endpoint route builder, for chaining.</returns>
        public IEndpointRouteBuilder MapTeamRosterEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            endpoints.MapGroup(TeamRosterEndpoints.GroupPrefix)
                .MapGet(TeamRosterEndpoints.GetRosterRelative, GetTeamRosterHandler)
                .Produces<IReadOnlyList<TeamRosterItem>>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .RequireAuthorization(Policies.RequireClubMember)
                .WithName("GetTeamRoster");

            return endpoints;
        }
    }

    /// <summary>
    /// Handles GET /api/teams.
    /// </summary>
    /// <param name="input">The bound roster filters.</param>
    /// <param name="teamRosterService">The team roster service.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The roster result or a ProblemDetails response.</returns>
    private static async Task<IResult> GetTeamRosterHandler(
        [AsParameters] GetTeamRosterInput input,
        ITeamRosterService teamRosterService,
        CancellationToken cancellationToken)
    {
        var result = await teamRosterService.GetRosterAsync(input, cancellationToken);
        return result.ToHttpResult();
    }
}
