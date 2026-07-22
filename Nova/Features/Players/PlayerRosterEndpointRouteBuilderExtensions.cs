using Microsoft.AspNetCore.Mvc;
using Nova.Features.Shared;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Nova.Shared.Security;

namespace Nova.Features.Players;

/// <summary>
/// Maps minimal API endpoints for querying club player rosters.
/// </summary>
internal static class PlayerRosterEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps player-roster query endpoints under the clubs API group.
        /// </summary>
        /// <returns>The endpoint route builder, for chaining.</returns>
        public IEndpointRouteBuilder MapPlayerRosterEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints.MapGroup(GetPlayerRosterEndpoints.GroupPrefix).RequireAuthorization();
            group.MapGet(GetPlayerRosterEndpoints.GetRosterRelative, GetPlayerRosterHandler)
                .Produces<PagedResult<PlayerListItem>>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .RequireAuthorization(Policies.RequireClubMember)
                .WithName("GetPlayerRoster");

            return endpoints;
        }
    }

    /// <summary>
    /// Handles player-roster GET requests.
    /// </summary>
    /// <param name="input">The bound roster input from route and query parameters.</param>
    /// <param name="playerService">The player service.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The HTTP result for the roster query.</returns>
    private static async Task<IResult> GetPlayerRosterHandler(
        [AsParameters] GetPlayerRosterInput input,
        IPlayerService playerService,
        CancellationToken cancellationToken)
    {
        var result = await playerService.GetPlayerRosterAsync(input, cancellationToken);
        return result.ToHttpResult();
    }
}
