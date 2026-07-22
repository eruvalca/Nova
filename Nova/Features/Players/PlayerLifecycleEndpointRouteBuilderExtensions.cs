using Nova.Features.Shared;
using Nova.Shared.Players;
using Nova.Shared.Security;

namespace Nova.Features.Players;

/// <summary>
/// Maps minimal API endpoints for player lifecycle archive and restore mutations.
/// </summary>
internal static class PlayerLifecycleEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps the player lifecycle endpoints under the shared player API group.
        /// </summary>
        /// <returns>The endpoint route builder for chaining.</returns>
        public IEndpointRouteBuilder MapPlayerLifecycleEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints
                .MapGroup(PlayerEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubAdmin);

            group.MapPost(PlayerEndpoints.ArchiveRelative, ArchivePlayerHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("ArchivePlayer");

            group.MapPost(PlayerEndpoints.RestoreRelative, RestorePlayerHandler)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status404NotFound)
                .ProducesProblem(StatusCodes.Status409Conflict)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .DisableAntiforgery()
                .WithName("RestorePlayer");

            return endpoints;
        }
    }

    /// <summary>
    /// Handles POST archive requests for a player.
    /// </summary>
    /// <param name="playerId">The player identifier.</param>
    /// <param name="playerLifecycleService">The lifecycle service.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A NoContent response on success or ProblemDetails on failure.</returns>
    private static async Task<IResult> ArchivePlayerHandler(
        long playerId,
        IPlayerLifecycleService playerLifecycleService,
        CancellationToken cancellationToken)
    {
        var result = await playerLifecycleService.ArchiveAsync(playerId, cancellationToken);
        return result.ToHttpResult(_ => TypedResults.NoContent());
    }

    /// <summary>
    /// Handles POST restore requests for a player.
    /// </summary>
    /// <param name="playerId">The player identifier.</param>
    /// <param name="playerLifecycleService">The lifecycle service.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A NoContent response on success or ProblemDetails on failure.</returns>
    private static async Task<IResult> RestorePlayerHandler(
        long playerId,
        IPlayerLifecycleService playerLifecycleService,
        CancellationToken cancellationToken)
    {
        var result = await playerLifecycleService.RestoreAsync(playerId, cancellationToken);
        return result.ToHttpResult(_ => TypedResults.NoContent());
    }
}
